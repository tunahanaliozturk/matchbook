using System.Globalization;
using System.Text;
using Matchbook.Budgets.Domain;

namespace Matchbook.Budgets.UnitTests.Funds;

/// <summary>
/// The funds handlers played over the domain in memory, one budget, one message at a time. Each method makes the
/// same domain calls in the same order as its handler in the application layer; where the handler runs the
/// guarded UPDATE, this asks <see cref="Budget.CanAfford"/> and applies the movement, and where the database's
/// unique index would refuse a second ledger entry for a document and step, this throws. Concurrency is not
/// modelled here: that is the database's half of the guarantee, and the integration tests prove it.
/// </summary>
public sealed class FundsSimulation
{
    private readonly CostCentre costCentre = Some.CostCentre();
    private readonly HashSet<(Guid, LedgerStep)> ledgerKeys = [];
    private readonly List<MatchInvoice> waitingForCommitment = [];

    public FundsSimulation(decimal allotment)
    {
        (Budget, LedgerEntry opening) = Budget.Open(Some.BudgetId, costCentre, 2026, allotment, Some.BudgetAdmin, Some.Now);
        Record(opening);
    }

    public Budget Budget { get; }

    public Dictionary<Guid, RequisitionReservation> Reservations { get; } = [];

    public Dictionary<Guid, OrderCommitment> Orders { get; } = [];

    public List<LedgerEntry> Ledger { get; } = [];

    public static Guid RequisitionId(int number) => new(number, 1, 0, new byte[8]);

    public static Guid OrderId(int number) => new(number, 2, 0, new byte[8]);

    public static Guid InvoiceId(int number) => new(number, 3, 0, new byte[8]);

    public FundsSimulation Deliver(IEnumerable<FundsMessage> messages)
    {
        foreach (FundsMessage message in messages)
        {
            Deliver(message);
        }

        return this;
    }

    public void Deliver(FundsMessage message)
    {
        switch (message)
        {
            case Submit submit:
                Reserve(submit);
                break;
            case Release release:
                ReleaseReservation(RequisitionId(release.Requisition));
                break;
            case RequestCommitment request:
                Commit(request);
                break;
            case MatchInvoice invoice:
                Match(invoice);
                break;
            case CloseOrder close:
                Close(close);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(message), message, "Not a funds message.");
        }
    }

    /// <summary>
    /// Everything that decides what happens next, printed so two runs can be compared and a difference read.
    /// The ledger is left out when <paramref name="withLedger"/> is false, for runs whose entries may split the
    /// same totals differently.
    /// </summary>
    public string State(bool withLedger = true)
    {
        var state = new StringBuilder();
        state.Append(CultureInfo.InvariantCulture, $"budget {Euros(Budget.Allotted)} / {Euros(Budget.Reserved)} / {Euros(Budget.Committed)} / {Euros(Budget.Actual)}\n");
        foreach ((Guid id, RequisitionReservation r) in Reservations.OrderBy(pair => pair.Key))
        {
            state.Append(CultureInfo.InvariantCulture, $"reservation {id} {r.Status} {Euros(r.Held)} {r.Refusal}\n");
        }

        foreach ((Guid id, OrderCommitment o) in Orders.OrderBy(pair => pair.Key))
        {
            state.Append(CultureInfo.InvariantCulture, $"order {id} {o.Status} {Euros(o.Remaining)} of {Euros(o.Amount)} attempt {o.LastAttempt} {o.Refusal}\n");
        }

        if (withLedger)
        {
            foreach (LedgerEntry e in Ledger.OrderBy(e => e.DocumentId).ThenBy(e => e.Step))
            {
                state.Append(CultureInfo.InvariantCulture, $"entry {e.DocumentId} {e.Step} {Euros(e.Allotted)} / {Euros(e.Reserved)} / {Euros(e.Committed)} / {Euros(e.Actual)}\n");
            }
        }

        return state.ToString();
    }

    // RequisitionSubmittedHandler
    private void Reserve(Submit message)
    {
        Guid requisitionId = RequisitionId(message.Requisition);
        Movement movement = Movement.Reserve(message.Amount);
        if (Reservations.ContainsKey(requisitionId))
        {
            return;
        }

        if (FundsCheck.RefusesReservation(costCentre, Budget, out FundsRefusal refusal))
        {
            Reservations.Add(requisitionId, RequisitionReservation.Refuse(requisitionId, message.Amount, refusal));
            return;
        }

        LedgerEntry entry = LedgerEntry.Record(Budget.Id, requisitionId, LedgerStep.Reserve, movement, Some.Now, Some.Now);
        Reservations.Add(
            requisitionId,
            TryApplyWithinAvailable(entry)
                ? RequisitionReservation.Hold(requisitionId, Budget.Id, message.Amount)
                : RequisitionReservation.Refuse(requisitionId, message.Amount, FundsRefusal.InsufficientFunds));
    }

    // ReservationReleaser
    private void ReleaseReservation(Guid requisitionId)
    {
        if (!Reservations.TryGetValue(requisitionId, out RequisitionReservation? reservation))
        {
            Reservations.Add(requisitionId, RequisitionReservation.Tombstone(requisitionId));
            return;
        }

        if (reservation.Release(Some.Now, Some.Now) is { } entry)
        {
            Apply(entry);
        }
    }

    // PurchaseOrderCommitmentRequestedHandler
    private void Commit(RequestCommitment message)
    {
        Guid orderId = OrderId(message.Order);
        Guid requisitionId = RequisitionId(message.Requisition);
        Orders.TryGetValue(orderId, out OrderCommitment? known);
        OrderCommitment order = known ?? OrderCommitment.Track(orderId, requisitionId);
        if (order.Replay(message.Attempt) is not null)
        {
            return;
        }

        if (known is null)
        {
            Orders.Add(orderId, order);
        }

        Reservations.TryGetValue(requisitionId, out RequisitionReservation? reservation);
        if (FundsCheck.RefusesCommitment(reservation, Budget, out FundsRefusal refusal))
        {
            order.Refuse(message.Attempt, refusal);
            return;
        }

        decimal held = reservation?.Held ?? 0m;
        LedgerEntry entry = LedgerEntry.Record(
            Budget.Id, orderId, LedgerStep.Commit, Movement.Commit(held, message.Amount), Some.Now, Some.Now);
        if (!TryApplyWithinAvailable(entry))
        {
            order.Refuse(message.Attempt, FundsRefusal.InsufficientFunds);
            return;
        }

        order.Commit(Budget.Id, message.Attempt, message.Amount);
        reservation?.HandOver();

        // Payables cannot match an invoice before the order is issued, and the order is issued only after this
        // commitment. An invoice the shuffle delivered earlier is delivered now, which is when it could exist.
        MatchInvoice[] ready = [.. waitingForCommitment.Where(invoice => invoice.Order == message.Order)];
        waitingForCommitment.RemoveAll(invoice => invoice.Order == message.Order);
        foreach (MatchInvoice invoice in ready)
        {
            Match(invoice);
        }
    }

    // InvoiceMatchedHandler
    private void Match(MatchInvoice message)
    {
        if (!Orders.TryGetValue(OrderId(message.Order), out OrderCommitment? order) || !order.WasCommitted)
        {
            waitingForCommitment.Add(message);
            return;
        }

        Guid invoiceId = InvoiceId(message.Invoice);
        if (ledgerKeys.Contains((invoiceId, LedgerStep.Invoice)))
        {
            return;
        }

        Apply(order.Invoice(invoiceId, message.Amount, Some.Now, Some.Now));
    }

    // PurchaseOrderClosedHandler
    private void Close(CloseOrder message)
    {
        Guid orderId = OrderId(message.Order);
        if (!Orders.TryGetValue(orderId, out OrderCommitment? order))
        {
            order = OrderCommitment.Track(orderId, RequisitionId(message.Requisition));
            Orders.Add(orderId, order);
        }
        else if (order.Status == CommitmentStatus.Closed)
        {
            return;
        }

        if (order.Close(Some.Now, Some.Now) is { } entry)
        {
            Apply(entry);
        }

        if (!order.WasCommitted)
        {
            ReleaseReservation(order.RequisitionId);
        }
    }

    private bool TryApplyWithinAvailable(LedgerEntry entry)
    {
        if (!Budget.CanAfford(entry.Movement))
        {
            return false;
        }

        Apply(entry);
        return true;
    }

    private void Apply(LedgerEntry entry)
    {
        Budget.Apply(entry.Movement);
        Record(entry);
    }

    private void Record(LedgerEntry entry)
    {
        if (!ledgerKeys.Add((entry.DocumentId, entry.Step)))
        {
            throw new InvalidOperationException($"A second {entry.Step} entry for {entry.DocumentId}: the unique index would refuse it.");
        }

        Ledger.Add(entry);
    }

    private static string Euros(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}

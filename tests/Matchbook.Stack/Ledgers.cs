using Npgsql;

namespace Matchbook.Stack;

/// <summary>
/// The five databases read into memory, in the shape the reconciliation compares. Read once the stack is quiet,
/// so the copies describe one moment. Each service's tables are read through its own connection as the
/// read-only reconciler role; no query joins across services, because no connection could.
/// </summary>
public sealed class Ledgers
{
    public Dictionary<Guid, BudgetRow> Budgets { get; } = [];

    public Dictionary<Guid, (decimal Allotted, decimal Reserved, decimal Committed, decimal Actual)> LedgerSums { get; } = [];

    public List<(Guid Budget, long Sequence, string Step, decimal Available)> OverdrawnGrants { get; } = [];

    public Dictionary<Guid, (string Status, decimal Amount, Guid? Budget)> Reservations { get; } = [];

    public Dictionary<Guid, CommitmentRow> Commitments { get; } = [];

    public Dictionary<Guid, (string Status, decimal Amount)> Requisitions { get; } = [];

    public Dictionary<Guid, (Guid Requisition, string Status, decimal Amount)> Orders { get; } = [];

    public Dictionary<(Guid Order, int Line), (decimal Ordered, decimal Received, decimal Invoiced)> OrderLines { get; } = [];

    public HashSet<Guid> InvoicesPurchasingSawMatched { get; } = [];

    public Dictionary<Guid, InvoiceRow> Invoices { get; } = [];

    public Dictionary<(Guid Order, int Line), decimal> PayablesReceived { get; } = [];

    public Dictionary<(Guid Order, int Line), decimal> PayablesInvoiced { get; } = [];

    public List<PaymentRow> Payments { get; } = [];

    public Dictionary<Guid, List<(int Version, DateTimeOffset ApprovedAt)>> ApprovedAccounts { get; } = [];

    public static async Task<Ledgers> ReadAsync(StackOptions options, CancellationToken cancellationToken)
    {
        var ledgers = new Ledgers();

        await ReadAsync(options, "budgets", cancellationToken,
            ("select id, allotted, reserved, committed, actual from budgets", row =>
                ledgers.Budgets[row.GetGuid(0)] = new BudgetRow(row.GetDecimal(1), row.GetDecimal(2), row.GetDecimal(3), row.GetDecimal(4))),
            ("select budget_id, sum(allotted), sum(reserved), sum(committed), sum(actual) from ledger_entries group by budget_id", row =>
                ledgers.LedgerSums[row.GetGuid(0)] = (row.GetDecimal(1), row.GetDecimal(2), row.GetDecimal(3), row.GetDecimal(4))),
            // Replays each budget's ledger in the order entries were written and keeps every grant that left less
            // than nothing available. Entries on one budget are written under its row lock, so their sequence is
            // their commit order.
            ("""
             select budget_id, sequence, step, available from (
                 select budget_id, sequence, step,
                        sum(allotted - reserved - committed - actual) over (partition by budget_id order by sequence) as available
                 from ledger_entries) replay
             where step in ('Reserve', 'Commit') and available < 0
             """, row => ledgers.OverdrawnGrants.Add((row.GetGuid(0), row.GetInt64(1), row.GetString(2), row.GetDecimal(3)))),
            ("select requisition_id, status, amount, budget_id from requisition_reservations", row =>
                ledgers.Reservations[row.GetGuid(0)] = (row.GetString(1), row.GetDecimal(2), row.IsDBNull(3) ? null : row.GetGuid(3))),
            ("select purchase_order_id, requisition_id, budget_id, status, amount, remaining from order_commitments", row =>
                ledgers.Commitments[row.GetGuid(0)] = new CommitmentRow(
                    row.GetGuid(1), row.IsDBNull(2) ? null : row.GetGuid(2), row.GetString(3), row.GetDecimal(4), row.GetDecimal(5))));

        await ReadAsync(options, "requisitions", cancellationToken,
            ("select id, status, amount from requisitions", row =>
                ledgers.Requisitions[row.GetGuid(0)] = (row.GetString(1), row.GetDecimal(2))));

        await ReadAsync(options, "purchasing", cancellationToken,
            ("select id, requisition_id, status, amount from purchase_orders", row =>
                ledgers.Orders[row.GetGuid(0)] = (row.GetGuid(1), row.GetString(2), row.GetDecimal(3))),
            ("select purchase_order_id, line_number, quantity, received_quantity, invoiced_quantity from purchase_order_lines", row =>
                ledgers.OrderLines[(row.GetGuid(0), row.GetInt32(1))] = (row.GetDecimal(2), row.GetDecimal(3), row.GetDecimal(4))),
            ("select invoice_id from matched_invoices", row => ledgers.InvoicesPurchasingSawMatched.Add(row.GetGuid(0))));

        await ReadAsync(options, "payables", cancellationToken,
            ("select id, purchase_order_id, supplier_id, status, total, matched_at is not null from invoices", row =>
                ledgers.Invoices[row.GetGuid(0)] = new InvoiceRow(
                    row.GetGuid(1), row.GetGuid(2), row.GetString(3), row.GetDecimal(4), row.GetBoolean(5))),
            ("""
             select r.purchase_order_id, l.line_number, sum(l.quantity)
             from receipts r join receipt_lines l on l.receipt_id = r.id
             group by r.purchase_order_id, l.line_number
             """, row => ledgers.PayablesReceived[(row.GetGuid(0), row.GetInt32(1))] = row.GetDecimal(2)),
            ("""
             select i.purchase_order_id, l.line_number, sum(l.quantity)
             from invoices i join invoice_lines l on l.invoice_id = i.id
             where i.matched_at is not null and i.status in ('Payable', 'Scheduled', 'Paid')
             group by i.purchase_order_id, l.line_number
             """, row => ledgers.PayablesInvoiced[(row.GetGuid(0), row.GetInt32(1))] = row.GetDecimal(2)),
            ("""
             select i.invoice_id, i.payment_run_id, i.supplier_id, i.amount, i.status, r.status, r.released_at,
                    c.account_version, c.status
             from payment_run_items i
             join payment_runs r on r.id = i.payment_run_id
             join payment_run_creditors c on c.payment_run_id = i.payment_run_id and c.supplier_id = i.supplier_id
             """, row => ledgers.Payments.Add(new PaymentRow(
                row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetDecimal(3), row.GetString(4), row.GetString(5),
                row.IsDBNull(6) ? null : row.GetFieldValue<DateTimeOffset>(6), row.GetInt32(7), row.GetString(8)))));

        await ReadAsync(options, "suppliers", cancellationToken,
            ("select supplier_id, account_version, decided_at from bank_accounts where status = 'Approved' order by account_version", row =>
            {
                Guid supplier = row.GetGuid(0);

                if (!ledgers.ApprovedAccounts.TryGetValue(supplier, out List<(int, DateTimeOffset)>? accounts))
                {
                    ledgers.ApprovedAccounts[supplier] = accounts = [];
                }

                accounts.Add((row.GetInt32(1), row.GetFieldValue<DateTimeOffset>(2)));
            }
        ));

        return ledgers;
    }

    private static async Task ReadAsync(
        StackOptions options,
        string database,
        CancellationToken cancellationToken,
        params (string Sql, Action<NpgsqlDataReader> Row)[] queries)
    {
        await using var connection = new NpgsqlConnection(options.ReaderConnection(database));
        await connection.OpenAsync(cancellationToken);

        // One snapshot for every query against this database, so a straggling write cannot land between two reads.
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, cancellationToken);

        foreach ((string sql, Action<NpgsqlDataReader> read) in queries)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                read(reader);
            }
        }
    }
}

public sealed record BudgetRow(decimal Allotted, decimal Reserved, decimal Committed, decimal Actual);

public sealed record CommitmentRow(Guid Requisition, Guid? Budget, string Status, decimal Amount, decimal Remaining);

public sealed record InvoiceRow(Guid Order, Guid Supplier, string Status, decimal Total, bool Matched);

public sealed record PaymentRow(
    Guid Invoice,
    Guid Run,
    Guid Supplier,
    decimal Amount,
    string ItemStatus,
    string RunStatus,
    DateTimeOffset? ReleasedAt,
    int AccountVersion,
    string CreditorStatus);

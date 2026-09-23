using System.Globalization;

namespace Matchbook.Budgets.UnitTests.Funds;

/// <summary>
/// The funds events Budgets consumes, reduced to what the decisions depend on. Documents are small numbers so a
/// failing case prints as something a person can follow.
/// </summary>
public abstract record FundsMessage
{
    protected static string Euros(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary><c>RequisitionSubmitted</c>.</summary>
public sealed record Submit(int Requisition, decimal Amount) : FundsMessage
{
    public override string ToString() => $"Submit(R{Requisition}, {Euros(Amount)})";
}

/// <summary><c>RequisitionRejected</c> or <c>RequisitionCancelled</c>; Budgets treats them alike.</summary>
public sealed record Release(int Requisition) : FundsMessage
{
    public override string ToString() => $"Release(R{Requisition})";
}

/// <summary><c>PurchaseOrderCommitmentRequested</c>.</summary>
public sealed record RequestCommitment(int Order, int Requisition, int Attempt, decimal Amount) : FundsMessage
{
    public override string ToString() => $"Commit(O{Order}, R{Requisition}, #{Attempt}, {Euros(Amount)})";
}

/// <summary><c>InvoiceMatched</c>.</summary>
public sealed record MatchInvoice(int Invoice, int Order, decimal Amount) : FundsMessage
{
    public override string ToString() => $"Invoice(I{Invoice}, O{Order}, {Euros(Amount)})";
}

/// <summary><c>PurchaseOrderClosed</c>.</summary>
public sealed record CloseOrder(int Order, int Requisition) : FundsMessage
{
    public override string ToString() => $"Close(O{Order}, R{Requisition})";
}

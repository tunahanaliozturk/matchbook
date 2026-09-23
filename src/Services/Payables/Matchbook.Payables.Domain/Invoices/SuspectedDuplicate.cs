namespace Matchbook.Payables.Domain.Invoices;

/// <summary>An existing invoice as far as the suspected-duplicate rule needs to see it.</summary>
public sealed record DuplicateCandidate(
    Guid InvoiceId,
    Guid SupplierId,
    string NormalisedNumber,
    DateOnly InvoiceDate,
    decimal Total,
    InvoiceStatus Status);

/// <summary>
/// The same supplier and total, invoice dates at most seven days apart, and a different number: the classic way one
/// delivery is billed twice, with the number retyped or reissued. Such an invoice is held for an approver.
/// </summary>
/// <remarks>A rejected invoice is not a claim on anyone, so it neither blocks nor flags a new one.</remarks>
public static class SuspectedDuplicate
{
    public const int WindowDays = 7;

    /// <summary>The invoice the new one looks like, the lowest id first when there are several, or null.</summary>
    public static DuplicateCandidate? FindFor(Invoice invoice, IEnumerable<DuplicateCandidate> existing)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(existing);

        return existing
            .Where(other => other.InvoiceId != invoice.Id
                && other.Status != InvoiceStatus.Rejected
                && other.SupplierId == invoice.SupplierId
                && other.Total == invoice.Total
                && !string.Equals(other.NormalisedNumber, invoice.NormalisedNumber, StringComparison.Ordinal)
                && Math.Abs(other.InvoiceDate.DayNumber - invoice.InvoiceDate.DayNumber) <= WindowDays)
            .OrderBy(other => other.InvoiceId)
            .FirstOrDefault();
    }
}

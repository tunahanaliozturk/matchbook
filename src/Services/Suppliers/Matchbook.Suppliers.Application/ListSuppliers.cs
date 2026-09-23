using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application;

/// <summary>
/// Filters for the supplier list. <see cref="After"/> is the <c>Next</c> of the previous page. An approver's work
/// queue is <c>Status = PendingActivation</c> or <c>HasPendingBankAccount = true</c>.
/// </summary>
public sealed record ListSuppliers(
    SupplierStatus? Status = null, bool? HasPendingBankAccount = null, Guid? After = null, int? Limit = null);

/// <summary>A row of the supplier list. No IBAN at all, not even masked: a list is not where anyone checks one.</summary>
public sealed record SupplierSummary(
    Guid Id,
    string LegalName,
    string TaxId,
    string CountryCode,
    SupplierStatus Status,
    int PaymentTermsDays,
    int AccountVersion,
    bool HasPendingBankAccount);

/// <summary><see cref="Next"/> is null on the last page.</summary>
public sealed record SupplierPage(IReadOnlyList<SupplierSummary> Items, Guid? Next);

/// <summary>Keyset pagination by id, which is a version 7 UUID and so runs oldest first.</summary>
public sealed class ListSuppliersHandler(ISuppliersDb db)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<SupplierPage> HandleAsync(ListSuppliers query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        IQueryable<Supplier> suppliers = db.Suppliers.AsNoTracking();

        if (query.Status is { } status)
        {
            suppliers = suppliers.Where(supplier => supplier.Status == status);
        }

        if (query.HasPendingBankAccount is { } pending)
        {
            suppliers = suppliers.Where(supplier =>
                supplier.BankAccounts.Any(account => account.Status == BankAccountStatus.Pending) == pending);
        }

        if (query.After is { } after)
        {
            suppliers = suppliers.Where(supplier => supplier.Id > after);
        }

        // One row more than asked for says whether there is a next page without a count query.
        List<SupplierSummary> rows = await suppliers
            .OrderBy(static supplier => supplier.Id)
            .Take(limit + 1)
            .Select(static supplier => new SupplierSummary(
                supplier.Id,
                supplier.LegalName,
                supplier.TaxId.Value,
                supplier.Country.Value,
                supplier.Status,
                supplier.PaymentTermsDays,
                supplier.AccountVersion,
                supplier.BankAccounts.Any(account => account.Status == BankAccountStatus.Pending)))
            .ToListAsync(cancellationToken);

        return rows.Count > limit
            ? new SupplierPage(rows[..limit], rows[limit - 1].Id)
            : new SupplierPage(rows, null);
    }
}

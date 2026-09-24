using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Queries.ListSuppliers;

/// <summary>
/// Filters for the supplier list. <see cref="After"/> is the <c>Next</c> of the previous page. An approver's work
/// queue is <c>Status = PendingActivation</c> or <c>HasPendingBankAccount = true</c>.
/// </summary>
public sealed record ListSuppliersQuery(
    SupplierStatus? Status = null, bool? HasPendingBankAccount = null, Guid? After = null, int? Limit = null)
    : IQuery<SupplierPage>;

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

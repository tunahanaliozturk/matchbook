using Matchbook.Requisitions.Application.Common;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListSupplierOptions;

/// <summary>
/// The suppliers a requisition may be raised against, from this service's copy: the active ones, which is what
/// submitting checks. Neither a requester nor an approver may read Suppliers, so the form asks here for its
/// choices and a requisition's page for its supplier's name (ADR 0009). Name order, as many as a list returns at
/// most.
/// </summary>
public sealed class ListSupplierOptionsHandler(IRequisitionsDb db)
    : IQueryHandler<ListSupplierOptionsQuery, IReadOnlyList<SupplierOption>>
{
    public async Task<IReadOnlyList<SupplierOption>> HandleAsync(ListSupplierOptionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ponytail: capped rather than paged, because a select shows them all; a search parameter when there are
        // more suppliers than a person would scroll through.
        return await db.Suppliers
            .AsNoTracking()
            .Where(static supplier => supplier.IsActive)
            .OrderBy(static supplier => supplier.LegalName)
            .ThenBy(static supplier => supplier.Id)
            .Take(Page.MaxLimit)
            .Select(static supplier => new SupplierOption(supplier.Id, supplier.LegalName))
            .ToListAsync(cancellationToken);
    }
}

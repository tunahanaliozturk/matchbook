using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Queries.GetSupplier;

/// <summary>
/// One supplier with its bank account history, as the caller may see it. The query carries the caller because the
/// answer depends on who asks: the full IBAN of a pending proposal goes only to an approver who can decide it.
/// </summary>
public sealed class GetSupplierHandler(ISuppliersDb db) : IQueryHandler<GetSupplierQuery, SupplierView>
{
    public async Task<SupplierView> HandleAsync(GetSupplierQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SupplierView.For(
            await db.Suppliers.AsNoTracking().GetAsync(query.SupplierId, cancellationToken), query.Actor);
    }
}

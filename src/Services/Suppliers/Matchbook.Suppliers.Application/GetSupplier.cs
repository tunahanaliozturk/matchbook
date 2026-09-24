using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application;

/// <summary>
/// One supplier with its bank account history, as the caller may see it. Takes the caller because the answer
/// depends on who asks: the full IBAN of a pending proposal goes only to an approver who can decide it.
/// </summary>
public sealed class GetSupplierHandler(ISuppliersDb db)
{
    public async Task<SupplierView> HandleAsync(Guid supplierId, Actor actor, CancellationToken cancellationToken) =>
        SupplierView.For(await db.Suppliers.AsNoTracking().GetAsync(supplierId, cancellationToken), actor);
}

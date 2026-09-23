using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application;

internal static class SuppliersQueries
{
    /// <summary>One supplier with its whole bank account history, which every rule and every view needs.</summary>
    public static async Task<Supplier> GetAsync(
        this IQueryable<Supplier> suppliers, Guid supplierId, CancellationToken cancellationToken) =>
        await suppliers
            .Include(static supplier => supplier.BankAccounts)
            .SingleOrDefaultAsync(supplier => supplier.Id == supplierId, cancellationToken)
        ?? throw new BusinessRuleException("supplier.not_found", "There is no supplier with this id.", ViolationKind.NotFound);

    /// <summary>
    /// Refuses a tax id another supplier already has, with a code a client can act on. The unique index settles
    /// the race between two requests that both pass this check.
    /// </summary>
    public static async Task EnsureTaxIdIsFreeAsync(
        this ISuppliersDb db, TaxId taxId, Guid supplierId, CancellationToken cancellationToken)
    {
        if (await db.Suppliers.AnyAsync(
                supplier => supplier.TaxId == taxId && supplier.Id != supplierId, cancellationToken))
        {
            throw new BusinessRuleException(
                "supplier.tax_id_taken", "Another supplier already has this tax id.", ViolationKind.Conflict);
        }
    }
}

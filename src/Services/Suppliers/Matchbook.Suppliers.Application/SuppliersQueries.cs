using Matchbook.SharedKernel;
using Matchbook.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Matchbook.Suppliers.Application;

internal static class SuppliersQueries
{
    /// <summary>One supplier with its whole bank account history, which every rule and every view needs.</summary>
    public static Task<Supplier?> FindAsync(
        this IQueryable<Supplier> suppliers, Guid supplierId, CancellationToken cancellationToken) =>
        suppliers
            .Include(static supplier => supplier.BankAccounts)
            .SingleOrDefaultAsync(supplier => supplier.Id == supplierId, cancellationToken);

    public static async Task<Supplier> GetAsync(
        this IQueryable<Supplier> suppliers, Guid supplierId, CancellationToken cancellationToken) =>
        await suppliers.FindAsync(supplierId, cancellationToken)
        ?? throw new BusinessRuleException("supplier.not_found", "There is no supplier with this id.", ViolationKind.NotFound);
}

/// <summary>
/// Creates are idempotent on an id the client chooses, so a client that lost a response can send the same
/// request again. The same id with different content is a mistake worth refusing loudly.
/// </summary>
internal static class ClientIds
{
    public static BusinessRuleException Reused() =>
        new("request.id_reused", "This id was already used for a different request.", ViolationKind.Conflict);
}

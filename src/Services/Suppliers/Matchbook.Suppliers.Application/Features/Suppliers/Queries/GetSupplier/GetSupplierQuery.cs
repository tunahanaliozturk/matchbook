using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Queries.GetSupplier;

public sealed record GetSupplierQuery(Guid SupplierId, Actor Actor) : IQuery<SupplierView>;

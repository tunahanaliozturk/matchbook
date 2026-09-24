using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.UnblockSupplier;

public sealed record UnblockSupplierCommand(Guid SupplierId, Actor Actor) : ICommand<SupplierView>;

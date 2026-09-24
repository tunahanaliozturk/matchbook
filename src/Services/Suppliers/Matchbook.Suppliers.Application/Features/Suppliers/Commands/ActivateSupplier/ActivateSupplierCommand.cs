using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ActivateSupplier;

public sealed record ActivateSupplierCommand(Guid SupplierId, Actor Actor) : ICommand<SupplierView>;

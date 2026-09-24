using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.BlockSupplier;

public sealed record BlockSupplierCommand(Guid SupplierId, string Reason, Actor Actor) : ICommand<SupplierView>;

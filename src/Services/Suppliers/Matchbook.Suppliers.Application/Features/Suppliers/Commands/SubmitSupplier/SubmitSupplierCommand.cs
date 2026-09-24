using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.SubmitSupplier;

public sealed record SubmitSupplierCommand(Guid SupplierId, Actor Actor) : ICommand<SupplierView>;

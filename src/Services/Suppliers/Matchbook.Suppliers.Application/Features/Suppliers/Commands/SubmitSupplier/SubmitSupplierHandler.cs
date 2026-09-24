using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.SubmitSupplier;

/// <summary>A supplier admin sends a draft for activation.</summary>
public sealed class SubmitSupplierHandler(SupplierCommandRunner runner) : ICommandHandler<SubmitSupplierCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(SubmitSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            command.Actor,
            "submitted",
            (supplier, now) => supplier.Submit(command.Actor, now),
            cancellationToken);
    }
}

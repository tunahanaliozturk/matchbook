using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.ActivateSupplier;

/// <summary>A supplier approver other than the submitter activates, which publishes the first snapshot.</summary>
public sealed class ActivateSupplierHandler(SupplierCommandRunner runner)
    : ICommandHandler<ActivateSupplierCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(ActivateSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            command.Actor,
            "activated",
            (supplier, now) => supplier.Activate(command.Actor, now),
            cancellationToken);
    }
}

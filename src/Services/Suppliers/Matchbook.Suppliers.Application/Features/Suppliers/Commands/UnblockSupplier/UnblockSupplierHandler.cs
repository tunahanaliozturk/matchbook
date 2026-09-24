using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.UnblockSupplier;

/// <summary>Only a supplier approver lifts a block.</summary>
public sealed class UnblockSupplierHandler(SupplierCommandRunner runner)
    : ICommandHandler<UnblockSupplierCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(UnblockSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            command.Actor,
            "unblocked",
            (supplier, _) => supplier.Unblock(command.Actor),
            cancellationToken);
    }
}

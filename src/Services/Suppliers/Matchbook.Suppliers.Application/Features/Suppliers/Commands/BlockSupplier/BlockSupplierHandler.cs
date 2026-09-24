using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application.Features.Suppliers.Commands.BlockSupplier;

/// <summary>Either supplier role stops an active supplier being ordered from or paid, with a reason.</summary>
public sealed class BlockSupplierHandler(SupplierCommandRunner runner) : ICommandHandler<BlockSupplierCommand, SupplierView>
{
    public Task<SupplierView> HandleAsync(BlockSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId,
            command.Actor,
            "blocked",
            (supplier, now) => supplier.Block(command.Actor, command.Reason, now),
            cancellationToken);
    }
}

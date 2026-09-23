using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.Application;

// The four lifecycle moves. Each is one domain call; the runner does the loading, publishing and saving.

public sealed record SubmitSupplier(Guid SupplierId);

/// <summary>A supplier admin sends a draft for activation.</summary>
public sealed class SubmitSupplierHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(SubmitSupplier command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(command.SupplierId, actor, (supplier, now) => supplier.Submit(actor, now), cancellationToken);
    }
}

public sealed record ActivateSupplier(Guid SupplierId);

/// <summary>A supplier approver other than the submitter activates, which publishes the first snapshot.</summary>
public sealed class ActivateSupplierHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(ActivateSupplier command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(command.SupplierId, actor, (supplier, now) => supplier.Activate(actor, now), cancellationToken);
    }
}

public sealed record BlockSupplier(Guid SupplierId, string Reason);

/// <summary>Either supplier role stops an active supplier being ordered from or paid, with a reason.</summary>
public sealed class BlockSupplierHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(BlockSupplier command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(
            command.SupplierId, actor, (supplier, now) => supplier.Block(actor, command.Reason, now), cancellationToken);
    }
}

public sealed record UnblockSupplier(Guid SupplierId);

/// <summary>Only a supplier approver lifts a block.</summary>
public sealed class UnblockSupplierHandler(SupplierCommandRunner runner)
{
    public Task<SupplierView> HandleAsync(UnblockSupplier command, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runner.RunAsync(command.SupplierId, actor, (supplier, _) => supplier.Unblock(actor), cancellationToken);
    }
}

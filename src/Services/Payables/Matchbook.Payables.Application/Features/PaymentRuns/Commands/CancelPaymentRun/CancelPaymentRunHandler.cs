using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore.Storage;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Commands.CancelPaymentRun;

/// <summary>A treasurer cancels a draft run. Its invoices become payable again, free for the next run.</summary>
public sealed class CancelPaymentRunHandler(IPayablesDb db, TimeProvider time) : ICommandHandler<CancelPaymentRunCommand, PaymentRunView>
{
    public async Task<PaymentRunView> HandleAsync(CancelPaymentRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using IDbContextTransaction transaction = await db.BeginTransactionAsync(cancellationToken);

        PaymentRun run = await db.PaymentRuns.SingleOrNotFoundAsync(command.PaymentRunId, cancellationToken);
        run.Cancel(command.Treasurer, time.GetUtcNow());

        // Written first, under the run's row version, so a release racing this cancellation cannot also win.
        await db.SaveChangesAsync(cancellationToken);
        await db.ReturnToPayableAsync(run.Id, supplierIds: null, PaymentRunItemStatus.Cancelled, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return PaymentRunView.From(run);
    }
}

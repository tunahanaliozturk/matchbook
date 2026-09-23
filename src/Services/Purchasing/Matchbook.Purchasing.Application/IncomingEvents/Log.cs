using Matchbook.Purchasing.Domain;
using Microsoft.Extensions.Logging;

namespace Matchbook.Purchasing.Application.IncomingEvents;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "{MessageType} for {Key} was already applied; nothing to do")]
    public static partial void AlreadyApplied(this ILogger logger, string messageType, Guid key);

    // Worth seeing at Information: a steady trickle means buyers are cancelling orders that are still waiting
    // for Budgets, or something upstream is redelivering far more than it should.
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Ignored {MessageType} for purchase order {PurchaseOrderId} attempt {Attempt}: the order is {Status} at attempt {CurrentAttempt}")]
    public static partial void CommitmentReplyIgnored(
        this ILogger logger,
        string messageType,
        Guid purchaseOrderId,
        int attempt,
        PurchaseOrderStatus status,
        int currentAttempt);
}

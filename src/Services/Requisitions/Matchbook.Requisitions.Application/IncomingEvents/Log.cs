using Matchbook.Requisitions.Domain;
using Microsoft.Extensions.Logging;

namespace Matchbook.Requisitions.Application.IncomingEvents;

/// <summary>What the event handlers say when a message changes nothing, which is normal and never an error.</summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Cost centre {CostCentreCode} version {Version} ignored: version {HeldVersion} is already held.")]
    public static partial void StaleCostCentre(this ILogger logger, string costCentreCode, long version, long heldVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Supplier {SupplierId} version {Version} ignored: version {HeldVersion} is already held.")]
    public static partial void StaleSupplier(this ILogger logger, Guid supplierId, long version, long heldVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "{EventName} for requisition {Number} ignored: it is {Status}.")]
    public static partial void FactIgnored(this ILogger logger, string eventName, string number, RequisitionStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EventName} for unknown requisition {RequisitionId} ignored.")]
    public static partial void UnknownRequisition(this ILogger logger, string eventName, Guid requisitionId);
}

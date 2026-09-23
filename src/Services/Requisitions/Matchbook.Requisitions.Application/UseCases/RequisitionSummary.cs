using System.Linq.Expressions;
using Matchbook.Requisitions.Domain;

namespace Matchbook.Requisitions.Application.UseCases;

/// <summary>A requisition as a list shows it.</summary>
public sealed record RequisitionSummary(
    Guid Id,
    string Number,
    RequisitionStatus Status,
    Guid RequesterId,
    string RequesterName,
    string CostCentreCode,
    Guid SupplierId,
    decimal Amount,
    DateOnly NeededBy,
    DateTimeOffset CreatedAt)
{
    /// <summary>The summary with the serial it pages on, projected in the query so only these columns are read.</summary>
    internal static readonly Expression<Func<Requisition, Keyed<RequisitionSummary>>> KeyedBySerial =
        requisition => new Keyed<RequisitionSummary>(
            requisition.Serial,
            new RequisitionSummary(
                requisition.Id,
                requisition.Number,
                requisition.Status,
                requisition.RequesterId,
                requisition.RequesterName,
                requisition.CostCentreCode,
                requisition.SupplierId,
                requisition.Amount,
                requisition.NeededBy,
                requisition.CreatedAt));
}

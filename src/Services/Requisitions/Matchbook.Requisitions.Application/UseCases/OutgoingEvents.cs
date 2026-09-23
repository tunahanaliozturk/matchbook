using Matchbook.Contracts.Requisitions;
using Matchbook.Requisitions.Domain;
using ContractLine = Matchbook.Contracts.Requisitions.RequisitionLine;

namespace Matchbook.Requisitions.Application.UseCases;

/// <summary>Translates a requisition into the integration events other services read.</summary>
internal static class OutgoingEvents
{
    public static RequisitionSubmitted Submitted(Requisition requisition, DateTimeOffset now) => new(
        requisition.Id,
        requisition.Number,
        requisition.RequesterId,
        requisition.CostCentreCode,
        FiscalYearOf(requisition),
        requisition.SupplierId,
        requisition.Amount,
        now);

    public static RequisitionApproved Approved(Requisition requisition, DateTimeOffset now) => new(
        requisition.Id,
        requisition.Number,
        requisition.RequesterId,
        requisition.CostCentreCode,
        FiscalYearOf(requisition),
        requisition.SupplierId,
        [.. requisition.Lines
            .OrderBy(static line => line.LineNumber)
            .Select(static line => new ContractLine(
                line.LineNumber, line.Description, line.Quantity, line.UnitOfMeasure, line.UnitPrice, line.Amount))],
        requisition.Amount,
        [.. requisition.Steps
            .OrderBy(static step => step.Sequence)
            .Where(static step => step.DecidedBy.HasValue)
            .Select(static step => step.DecidedBy!.Value)],
        now);

    private static int FiscalYearOf(Requisition requisition) =>
        requisition.FiscalYear
        ?? throw new InvalidOperationException($"Requisition {requisition.Number} has no fiscal year; it was never submitted.");
}

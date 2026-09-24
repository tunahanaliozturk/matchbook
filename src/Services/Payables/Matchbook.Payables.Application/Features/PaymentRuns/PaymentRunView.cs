using System.Linq.Expressions;
using Matchbook.Payables.Domain;
using Matchbook.Payables.Domain.PaymentRuns;

namespace Matchbook.Payables.Application.Features.PaymentRuns;

/// <summary>A payment run with a line per supplier. Account numbers are masked; the bank file is where they appear in full.</summary>
public sealed record PaymentRunView(
    Guid Id,
    DateOnly ExecutionDate,
    PaymentRunStatus Status,
    Guid DraftedBy,
    DateTimeOffset DraftedAt,
    Guid? ReleasedBy,
    DateTimeOffset? ReleasedAt,
    Guid? CancelledBy,
    DateTimeOffset? CancelledAt,
    int ItemCount,
    decimal Total,
    int PaidCount,
    decimal PaidTotal,
    IReadOnlyList<PaymentRunCreditorView> Creditors)
{
    public static PaymentRunView From(PaymentRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new PaymentRunView(
            run.Id,
            run.ExecutionDate,
            run.Status,
            run.DraftedBy,
            run.DraftedAt,
            run.ReleasedBy,
            run.ReleasedAt,
            run.CancelledBy,
            run.CancelledAt,
            run.ItemCount,
            run.Total,
            run.PaidCount,
            run.PaidTotal,
            [.. run.Creditors.Select(creditor => new PaymentRunCreditorView(
                creditor.SupplierId,
                creditor.AccountHolder,
                Iban.Mask(creditor.IbanLastFour),
                creditor.Bic.Value,
                creditor.AccountVersion,
                creditor.ItemCount,
                creditor.Total,
                creditor.Status,
                creditor.DropReason))]);
    }
}

/// <summary>A payment run as a list shows it: its figures, without the suppliers.</summary>
public sealed record PaymentRunSummary(
    Guid Id,
    DateOnly ExecutionDate,
    PaymentRunStatus Status,
    Guid DraftedBy,
    DateTimeOffset DraftedAt,
    Guid? ReleasedBy,
    DateTimeOffset? ReleasedAt,
    int ItemCount,
    decimal Total,
    int PaidCount,
    decimal PaidTotal)
{
    internal static readonly Expression<Func<PaymentRun, PaymentRunSummary>> Projection = run => new PaymentRunSummary(
        run.Id,
        run.ExecutionDate,
        run.Status,
        run.DraftedBy,
        run.DraftedAt,
        run.ReleasedBy,
        run.ReleasedAt,
        run.ItemCount,
        run.Total,
        run.PaidCount,
        run.PaidTotal);
}

public sealed record PaymentRunCreditorView(
    Guid SupplierId,
    string AccountHolder,
    string MaskedIban,
    string Bic,
    int AccountVersion,
    int ItemCount,
    decimal Total,
    CreditorStatus Status,
    CreditorDropReason? DropReason);

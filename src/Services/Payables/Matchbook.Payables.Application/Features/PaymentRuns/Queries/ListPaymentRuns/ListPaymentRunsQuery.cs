using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.PaymentRuns;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Queries.ListPaymentRuns;

/// <summary>A page of payment runs, newest first, optionally in one status.</summary>
public sealed record ListPaymentRunsQuery(PaymentRunStatus? Status, Guid? After, int Limit) : IQuery<Page<PaymentRunSummary>>;

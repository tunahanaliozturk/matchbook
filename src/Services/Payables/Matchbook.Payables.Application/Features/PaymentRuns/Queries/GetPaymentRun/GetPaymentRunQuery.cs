using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Queries.GetPaymentRun;

public sealed record GetPaymentRunQuery(Guid PaymentRunId) : IQuery<PaymentRunView>;

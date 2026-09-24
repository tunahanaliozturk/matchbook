using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Commands.CancelPaymentRun;

public sealed record CancelPaymentRunCommand(Guid PaymentRunId, Actor Treasurer) : ICommand<PaymentRunView>;

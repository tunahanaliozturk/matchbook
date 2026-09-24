using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Commands.ReleasePaymentRun;

public sealed record ReleasePaymentRunCommand(Guid PaymentRunId, Actor Treasurer) : ICommand<PaymentRunView>;

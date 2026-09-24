using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Commands.DraftPaymentRun;

/// <param name="Id">Optional, chosen by the client so a retry returns the run instead of drafting a second one.</param>
public sealed record DraftPaymentRunCommand(Guid? Id, DateOnly ExecutionDate, Actor Treasurer) : ICommand<PaymentRunView>;

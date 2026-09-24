using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Commands.AcceptPriceVariance;

public sealed record AcceptPriceVarianceCommand(Guid InvoiceId, string Reason, Actor Approver) : ICommand<InvoiceView>;

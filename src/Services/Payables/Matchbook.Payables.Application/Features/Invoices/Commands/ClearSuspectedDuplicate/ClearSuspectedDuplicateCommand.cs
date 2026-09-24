using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Commands.ClearSuspectedDuplicate;

public sealed record ClearSuspectedDuplicateCommand(Guid InvoiceId, Actor Approver) : ICommand<InvoiceView>;

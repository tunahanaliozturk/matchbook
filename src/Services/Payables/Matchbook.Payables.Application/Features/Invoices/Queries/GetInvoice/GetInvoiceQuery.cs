using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.GetInvoice;

public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceView>;

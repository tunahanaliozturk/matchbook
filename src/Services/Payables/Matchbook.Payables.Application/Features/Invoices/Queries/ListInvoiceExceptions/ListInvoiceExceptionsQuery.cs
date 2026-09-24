using Matchbook.Payables.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListInvoiceExceptions;

public sealed record ListInvoiceExceptionsQuery(Guid? After, int Limit) : IQuery<Page<InvoiceSummary>>;

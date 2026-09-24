using Matchbook.Payables.Application.Common;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListInvoices;

/// <summary>A page of invoices, newest first, optionally in one status.</summary>
public sealed record ListInvoicesQuery(InvoiceStatus? Status, Guid? After, int Limit) : IQuery<Page<InvoiceSummary>>;

using Matchbook.Payables.Application.Common;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.Invoices.Queries.ListBillableSuppliers;

/// <summary>A page of the suppliers an invoice can be captured for, newest first.</summary>
public sealed record ListBillableSuppliersQuery(Guid? After, int Limit) : IQuery<Page<BillableSupplier>>;

/// <summary>A supplier with at least one order an invoice can still bill, from Payables' local copy.</summary>
public sealed record BillableSupplier(Guid Id, string LegalName, bool IsActive, int PaymentTermsDays);

using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderForRequisition;

public sealed record GetPurchaseOrderForRequisitionQuery(Guid RequisitionId) : IQuery<PurchaseOrderView>;

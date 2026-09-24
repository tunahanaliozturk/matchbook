using Matchbook.Budgets.Application.Budgets;
using Matchbook.Budgets.Application.CostCentres;
using Matchbook.Budgets.Application.Funds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Budgets.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The handlers, scoped like the DbContext they share. <see cref="IBudgetsDb"/> and <c>IEventPublisher</c>
    /// come from Infrastructure and the messaging setup.
    /// </summary>
    public static IServiceCollection AddBudgetsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<CreateCostCentreHandler>();
        services.AddScoped<ChangeCostCentreHandler>();
        services.AddScoped<GetCostCentreHandler>();
        services.AddScoped<ListCostCentresHandler>();

        services.AddScoped<OpenBudgetHandler>();
        services.AddScoped<ChangeAllotmentHandler>();
        services.AddScoped<GetBudgetHandler>();
        services.AddScoped<ListBudgetsHandler>();
        services.AddScoped<ListLedgerHandler>();
        services.AddScoped<OverspendReportHandler>();

        services.AddScoped<ReservationReleaser>();
        services.AddScoped<RequisitionSubmittedHandler>();
        services.AddScoped<RequisitionRejectedHandler>();
        services.AddScoped<RequisitionCancelledHandler>();
        services.AddScoped<PurchaseOrderCommitmentRequestedHandler>();
        services.AddScoped<InvoiceMatchedHandler>();
        services.AddScoped<PurchaseOrderClosedHandler>();
        return services;
    }
}

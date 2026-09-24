using Matchbook.Requisitions.Application.IncomingEvents;
using Matchbook.Requisitions.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Matchbook.Requisitions.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers every handler, scoped like the DbContext they share. The host registers
    /// <see cref="IRequisitionsDb"/> and the event publisher.
    /// </summary>
    public static IServiceCollection AddRequisitionsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<CreateRequisitionHandler>();
        services.AddScoped<EditRequisitionHandler>();
        services.AddScoped<SubmitRequisitionHandler>();
        services.AddScoped<CancelRequisitionHandler>();
        services.AddScoped<ApproveRequisitionHandler>();
        services.AddScoped<RejectRequisitionHandler>();
        services.AddScoped<GetRequisitionHandler>();
        services.AddScoped<ListMyRequisitionsHandler>();
        services.AddScoped<ListMyApprovalsHandler>();

        services.AddScoped<CostCentreChangedHandler>();
        services.AddScoped<SupplierChangedHandler>();
        services.AddScoped<FundsReservedHandler>();
        services.AddScoped<FundsReservationRejectedHandler>();
        services.AddScoped<PurchaseOrderIssuedHandler>();
        services.AddScoped<PurchaseOrderClosedHandler>();

        return services;
    }
}

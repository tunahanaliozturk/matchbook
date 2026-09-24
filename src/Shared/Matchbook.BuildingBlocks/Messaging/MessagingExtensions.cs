using System.Data;
using MassTransit;
using Matchbook.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.BuildingBlocks.Messaging;

public static class MessagingExtensions
{
    /// <summary>
    /// MassTransit on RabbitMQ with the Entity Framework outbox and inbox on <typeparamref name="TDbContext"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Outbox.</b> A message published outside a consumer (from an API handler) is written to the outbox table
    /// by the same <c>SaveChangesAsync</c> that commits the change, and a background sender delivers it. A message
    /// published inside a consumer is held until the consumer finishes and committed with its changes. Either
    /// way an event leaves if and only if its cause committed.
    /// </para>
    /// <para>
    /// <b>Inbox.</b> Each consumer records the message ids it has processed, in the same transaction, and skips a
    /// redelivery within the deduplication window. Consumers are also idempotent on their own natural keys, because
    /// the window is finite.
    /// </para>
    /// <para>
    /// <b>Retries.</b> In memory, outside the outbox, so each attempt gets a fresh transaction and a fresh
    /// DbContext. After the last one the message goes to the endpoint's <c>_error</c> queue. A business refusal is
    /// normally an event and the consumer succeeds; a <see cref="BusinessRuleException"/> that escapes a consumer
    /// is a rule that will say no again, so it goes to the error queue at once instead of being retried.
    /// </para>
    /// <para>
    /// <b>Isolation.</b> Read committed, not MassTransit's default of repeatable read. Correctness here rests on
    /// conditional updates, row versions and unique constraints, which read committed honours. Under repeatable
    /// read, two consumers granting from one budget at once do not wait and re-check: one fails with a
    /// serialization error and burns a retry, and under load that becomes a storm.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMatchbookMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator> consumers)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(consumers);

        Uri broker = new(configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not configured."));

        services.AddMassTransit(bus =>
        {
            // Queues are named for the service and the consumer (budgets-requisition-submitted), so two services
            // consuming the same event each get their own copy.
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false));

            consumers(bus);

            bus.AddEntityFrameworkOutbox<TDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.IsolationLevel = IsolationLevel.ReadCommitted;
                outbox.QueryDelay = TimeSpan.FromMilliseconds(100);
                outbox.DuplicateDetectionWindow = TimeSpan.FromHours(1);
            });

            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(retry =>
                {
                    retry.Ignore<BusinessRuleException>();
                    retry.Intervals(
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(500),
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(3),
                        TimeSpan.FromSeconds(10));
                });
                endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
                endpoint.ConcurrentMessageLimit = 16;
            });

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(broker);
                rabbit.PrefetchCount = 32;
                rabbit.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        return services;
    }

    /// <summary>
    /// Publishes through whatever <see cref="IPublishEndpoint"/> the scope has: the consume context inside a
    /// consumer, the bus outbox in a web request. Both write to the outbox, never straight to the broker.
    /// </summary>
    private sealed class OutboxEventPublisher(IPublishEndpoint endpoint) : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken)
            where TEvent : class =>
            endpoint.Publish(message, cancellationToken);
    }
}

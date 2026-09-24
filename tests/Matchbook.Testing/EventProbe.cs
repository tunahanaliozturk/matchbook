using System.Collections.Concurrent;
using MassTransit;

namespace Matchbook.Testing;

/// <summary>
/// A second bus on a service's virtual host, standing in for the other four services: it publishes the events a
/// service consumes and records the events it publishes, over real RabbitMQ.
/// </summary>
/// <example>
/// <code>
/// await using var probe = await EventProbe.StartAsync(host.Broker, listen => listen
///     .For&lt;FundsReserved&gt;()
///     .For&lt;FundsReservationRejected&gt;());
/// await probe.PublishAsync(new RequisitionSubmitted(...));
/// FundsReserved reserved = await probe.WaitForAsync&lt;FundsReserved&gt;(message => message.RequisitionId == id);
/// </code>
/// </example>
public sealed class EventProbe : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<object> _received = new();
    private readonly ConcurrentDictionary<Guid, byte> _seen = new();
    private IBusControl _bus = null!;

    private EventProbe()
    {
    }

    /// <summary>
    /// Starts the probe with a temporary queue bound to every event type named in <paramref name="listen"/>. Only
    /// events published after this returns are seen.
    /// </summary>
    public static async Task<EventProbe> StartAsync(Uri broker, Action<Subscriptions> listen)
    {
        ArgumentNullException.ThrowIfNull(listen);

        var probe = new EventProbe();
        var subscriptions = new Subscriptions(probe);
        listen(subscriptions);

        probe._bus = Bus.Factory.CreateUsingRabbitMq(rabbit =>
        {
            rabbit.Host(broker);
            rabbit.ReceiveEndpoint($"probe-{Guid.NewGuid():N}", endpoint =>
            {
                endpoint.AutoDelete = true;
                endpoint.Durable = false;

                foreach (Action<IReceiveEndpointConfigurator> subscribe in subscriptions.Handlers)
                {
                    subscribe(endpoint);
                }
            });
        });

        await probe._bus.StartAsync();
        return probe;
    }

    public Task PublishAsync<T>(T message)
        where T : class =>
        _bus.Publish(message);

    /// <summary>
    /// Publishes with a chosen message id. Publishing the same message twice under one id is exactly what a
    /// redelivery looks like to a consumer.
    /// </summary>
    public Task PublishAsync<T>(T message, Guid messageId)
        where T : class =>
        _bus.Publish(message, context => context.MessageId = messageId);

    /// <summary>Every <typeparamref name="T"/> received so far, once per message id.</summary>
    public IReadOnlyList<T> Received<T>() => [.. _received.OfType<T>()];

    /// <summary>Waits for a <typeparamref name="T"/> matching <paramref name="predicate"/>, and returns it.</summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool> predicate, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var deadline = new CancellationTokenSource(timeout ?? DefaultTimeout);

        while (true)
        {
            if (_received.OfType<T>().FirstOrDefault(predicate) is { } found)
            {
                return found;
            }

            try
            {
                await Task.Delay(25, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"No {typeof(T).Name} matching the condition arrived within {timeout ?? DefaultTimeout}.");
            }
        }
    }

    /// <summary>
    /// Asserts that no <typeparamref name="T"/> matching <paramref name="predicate"/> arrives within
    /// <paramref name="within"/>. A negative can only be shown by waiting, so keep the window short and the
    /// predicate specific.
    /// </summary>
    public async Task ShouldNotReceiveAsync<T>(Func<T, bool> predicate, TimeSpan within)
    {
        await Task.Delay(within);

        if (_received.OfType<T>().Any(predicate))
        {
            throw new InvalidOperationException($"A {typeof(T).Name} matching the condition was published.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_bus is not null)
        {
            await _bus.StopAsync();
        }
    }

    /// <summary>The event types a probe listens for.</summary>
    public sealed class Subscriptions
    {
        private readonly EventProbe _probe;

        internal Subscriptions(EventProbe probe) => _probe = probe;

        internal List<Action<IReceiveEndpointConfigurator>> Handlers { get; } = [];

        public Subscriptions For<T>()
            where T : class
        {
            Handlers.Add(endpoint => endpoint.Handler<T>(context =>
            {
                // The outbox delivers at least once, and a second delivery carries the same message id: seen when
                // two copies of one incoming message were consumed at the same moment. Every real consumer's inbox
                // drops that copy, so the probe does too. A handler that published twice would have used two ids,
                // and both are still recorded.
                if (context.MessageId is not { } id || _probe._seen.TryAdd(id, 0))
                {
                    _probe._received.Enqueue(context.Message);
                }

                return Task.CompletedTask;
            }));

            return this;
        }
    }
}

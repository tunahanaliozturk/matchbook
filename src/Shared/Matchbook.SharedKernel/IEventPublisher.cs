namespace Matchbook.SharedKernel;

/// <summary>
/// Publishes an integration event as part of the unit of work in progress. The implementation writes to the
/// service's outbox, so the event leaves if and only if the change that caused it commits.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken)
        where TEvent : class;
}

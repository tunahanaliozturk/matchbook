namespace Matchbook.SharedKernel;

// Commands and queries without a mediator. MediatR moved to a commercial licence at version 13, and what it would
// add here, dispatch by type, is one constructor parameter at each endpoint. The interfaces earn their place by
// what they let the rest of the code base check: handlers are registered by scanning for them, and the
// architecture tests hold every one to its folder, its name and its side of the split (ADR 0008).

/// <summary>A request to change state, answered with the state it left behind.</summary>
/// <typeparam name="TResult">What the caller gets back: the changed resource, as every mutating endpoint returns it.</typeparam>
public interface ICommand<TResult>;

/// <summary>A request to read state. Answering it changes nothing and publishes nothing.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to an event another service published. The MassTransit consumer in Infrastructure is a thin adapter that
/// calls this; the rules, and the tolerance for events arriving in any order (ADR 0002), live here.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The rule reserves the suffix for .NET event delegates. This handles integration events, and " +
        "the name says so; \"consumer\" is taken by the MassTransit adapters that call it.")]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

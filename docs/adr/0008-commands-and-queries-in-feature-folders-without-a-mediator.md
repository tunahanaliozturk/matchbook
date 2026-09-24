# 8. Commands and queries in feature folders, without a mediator

Status: accepted

## Context

Five services were built at the same time by five people working to one design, and their Application projects came
out in five shapes: one flat folder in Suppliers, `UseCases/` and `IncomingEvents/` in Requisitions, `Events/` in
Payables, `Funds/` next to `Budgets/` in Budgets. Each was reasonable on its own. Together they meant a reader who
had learned one service had learned nothing about the next, and "where does capturing an invoice live" had five
different answers depending on the service.

The use cases were already one handler class each (ADR 0003), but nothing said which of them change state and which
only read, and nothing checked where a new one went.

## Decision

**Every use case is a command or a query in a folder named for it, under the resource it belongs to**, and the
architecture tests enforce the shape:

```
Matchbook.<Service>.Application/
  Features/<Area>/Commands/<UseCase>/<UseCase>Command.cs, <UseCase>Handler.cs
  Features/<Area>/Queries/<UseCase>/<UseCase>Query.cs, <UseCase>Handler.cs
  Features/<Area>/                 views and helpers used only by that area
  IntegrationEvents/<Event>Handler.cs, and helpers shared by the event handlers
  Common/                          what every area uses: paging, metrics, the clock
  I<Service>Db.cs, DependencyInjection.cs

Matchbook.<Service>.Api/
  Features/<Area>/<Area>Endpoints.cs, <Area>Requests.cs
```

Event handlers get no folder each. A folder, and so a namespace, named `GoodsReceived` would hide the
`GoodsReceived` contract from the handler inside it, and each would hold one file anyway.

An area is a resource the API exposes: the path groups in `docs/design.md`. The Api project has the same areas as
Application, so a feature is found at the same path in both.

Commands implement `ICommand<TResult>` and queries `IQuery<TResult>`, from the shared kernel, and their handlers
`ICommandHandler<,>` and `IQueryHandler<,>`. A command carries the person acting as a property, so the request is
the whole of what the handler needs. Handlers of other services' events implement `IIntegrationEventHandler<>`.
`AddHandlersFrom` registers all three kinds by scanning, and endpoints ask for the handler by its interface.

There is no mediator. An endpoint takes the handler it calls as a parameter.

## Consequences

- One answer to "where is it" across five services, and the answer is checked: `FeatureLayoutTests` fails a
  handler in the wrong folder, a handler named for something other than its request, a request without exactly one
  handler, a query handler that can publish or call a command, an endpoint class outside `Api/Features/<Area>`,
  and an Api area Application does not have.
- Reads and writes are told apart by type. A query handler cannot take `IEventPublisher`, so a read that publishes
  is a failing test, not a review comment.
- Registration cannot be forgotten, because it is not written. The cost is that it cannot be seen either: a handler
  is registered because it is in the assembly. The layout tests are what make that safe.
- More files and deeper paths. A use case is two files in two folders down, where some were one file before.
- Nothing sits between an endpoint and its handler, so there is no pipeline to hang logging, validation or retries
  on. Where a cross-cutting step is needed it is written out: Payables' `OrderRaceRetry` wraps the three commands
  that claim an order, by name.

## Alternatives considered

**MediatR.** The usual way to write this in .NET, and it would give a pipeline for cross-cutting behaviour. It
moved to a commercial licence at version 13, which the licence audit refuses, and the pipeline would have one
use today. Would win if several behaviours applied to every handler and the licence were not a question.

**Wolverine.** MIT licensed and does dispatch, messaging and an outbox. It would replace MassTransit too, which is
a much bigger decision than folder layout and would reopen ADR 0001 for no gain in correctness.

**Folders by kind (`Handlers/`, `Views/`, `Commands/`).** Easy to scan by technical role, but a change to one feature
touches four folders, and the folders grow without bound. Lost on locality.

**One file per use case (request and handler together).** Fewest files. Lost because the larger handlers (drafting
a payment run, the three-way match) are long, and the request is what a reader, and the endpoint, looks for first.

**Endpoints in the same folder as their handler (full vertical slices).** Better locality still, but Application
cannot reference ASP.NET Core, and the layering is checked against compiled references (ADR 0003). The mirrored
`Features/<Area>` in Api keeps the locality without breaking the layers.

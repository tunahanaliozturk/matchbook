# 3. Application uses EF Core directly, behind one interface per service

Status: accepted

## Context

Each service is four projects: Domain, Application, Infrastructure and Api. Application holds the use cases, and
use cases read and write data: list the approvals waiting for a user, lock a budget row and reserve against it,
find the invoices due by a date. The house rule for this portfolio is that relational data goes through an EF Core
DbContext used directly, with no repository layer over it.

Application cannot reference Infrastructure, where the DbContext, its mappings and its migrations live, without
inverting the layering.

## Decision

Each service's Application project declares one interface, `I<Service>Db`, exposing the `DbSet`s it needs and
`SaveChangesAsync`, plus a narrowly named method where a use case needs a statement LINQ cannot express (the next
document number from a sequence). The DbContext in Infrastructure implements it. Handlers query and write through
it with ordinary EF Core: LINQ, `AsNoTracking`, `ExecuteUpdateAsync`.

Application may reference EF Core, which is provider neutral. It may not reference Npgsql, MassTransit or ASP.NET
Core. `tests/Matchbook.ArchitectureTests` reads every compiled assembly's references and fails the build on any
other dependency, and on any reference from one service to another.

## Consequences

- A query is written once, where it is used, and reads as a query. There is no `GetInvoicesDueByAsync` on a
  repository interface whose only caller is one handler.
- Application tests that touch data run against real Postgres, in the integration suites. The domain, where the
  rules are, is tested without a database.
- Provider-specific behaviour (row locks, sequences, partial unique indexes) lives in Infrastructure: in the
  migrations, and behind the few named methods on the interface.

## Alternatives considered

**Repositories per aggregate.** The textbook answer, and against the house rule. Every query becomes a method on
an interface with a single implementation and usually a single caller, and the interesting query moves away from
the use case that needs it.

**Handlers in Infrastructure, next to the DbContext.** No interface at all, but Application would be left with
nothing to do, and the use cases would sit next to migrations and message plumbing.

**The DbContext in Application.** Moves the mappings, and with them Postgres details, into the layer that should
not know the database is Postgres.

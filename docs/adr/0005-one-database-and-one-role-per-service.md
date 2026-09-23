# 5. One database and one role per service, on one Postgres

Status: accepted

## Context

Services that share tables are one service with five deployments. Each service here has to own its data outright,
so that the only way to learn about another service's state is an event. The demo also has to start on a laptop.

## Decision

One Postgres 18 server in compose, with a database per service, each owned by a login role of the same name.
`deploy/postgres/init.sql` revokes every privilege on each database from `public`, so the `budgets` role cannot even
connect to the `payables` database. A service's connection string names its own role, and nothing else.

A sixth role, `reconciler`, can connect to all five and read, never write. Only the system tests use it, to check
the invariants that span services (docs/design.md, "What must hold across services"), the way a finance team
reconciles subledgers at month end.

Each service applies its own EF Core migrations on start. That is fine for one replica per service; with several,
migrations become a deployment job (docs/operations.md).

## Consequences

- The boundary is enforced by the database, not by convention. A query that joins across services cannot be
  written, even by accident, because the connection cannot reach the other database.
- One server is one point of failure and one set of resources. In production each service would have its own
  server, or at least its own cluster; the code does not change, only connection strings.
- Resource usage is honest for a laptop: five small databases share one buffer cache.

## Alternatives considered

**A Postgres container per service.** The truer picture of production, and five times the memory and start-up time
for a demo whose isolation is already enforced by roles.

**Schemas in one database.** Weaker: one login can be granted everything with a single mistake, and cross-schema
joins are one qualified name away.

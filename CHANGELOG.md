# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-24

First release. Everything below is covered by the test suites or by a published measurement.

### The process

- A purchase from requisition to payment across five services: Suppliers, Budgets, Requisitions, Purchasing and
  Payables, each with its own Postgres database, talking only through integration events on RabbitMQ.
- Encumbrance accounting in Budgets: every budget carries allotted, reserved, committed and actual, kept as an
  append-only ledger, with available never taken below zero by a grant under any concurrency.
- An approval route fixed when funds are reserved: the cost centre's manager, then finance over 10,000, then
  the CFO over 100,000.
- Purchase orders drafted from approved requisitions and issued only once Budgets commits the funds; receipts
  per line, never more than ordered.
- A three-way match of order, receipt and invoice per line, with a price tolerance of 2% or 100.00, whichever is
  smaller; invoices that arrive before their order or their goods wait and match on their own.
- Duplicate invoices refused by normalised number, and suspected duplicates held for an approver.
- Payment runs drafted by one treasurer and released by another, producing an ISO 20022 `pain.001.001.09`
  file; an invoice is paid at most once, and never to an account that changed after the run was drafted.

### Controls

- Separation of duties and four eyes as domain rules with tests: nobody approves their own requisition, the
  receiver is never the buyer who issued the order, and bank account changes, supplier activations and payment
  runs each need a second person.
- Bank account numbers encrypted with AES-256-GCM at rest and in flight, masked in every response except to the
  approver reviewing a change.
- Tokens from Keycloak, checked at the gateway and again in every service; RFC 9457 problem details with a
  stable `code` for every refusal, 401 and 403 included.

### Reliability

- Transactional outbox and inbox on every service, and consumers idempotent by natural keys and correct under
  any arrival order.
- Creates idempotent on a client-chosen id, so a client that lost a response can retry without buying twice.
- System tests that run a purchase end to end, then thirty more through a broker restart and two services killed
  with SIGKILL, and reconcile five databases on eight invariants afterwards
  (`docs/measurements/chaos.md`).
- An invoice captured at the moment Payables applies a change to its order retries on a fresh copy of the order,
  up to three times, instead of answering 409 to a clerk who did nothing wrong.
- Numbers in every contract are JSON numbers and nothing else: a quoted amount is refused with 400, where it was
  once quietly accepted, so a generated client can type amounts as numbers.

### Operations

- OpenTelemetry traces, metrics and logs from every service to the Aspire dashboard, with a business meter per
  service under the `matchbook.` prefix.
- `docs/operations.md`: configuration, what to watch, a runbook per anticipated failure, known limitations.
- A licence audit in CI that fails on any commercially licensed package at any depth.

### Console

- A Vue 3 console served by nginx on port 5301, one screen per job: suppliers and bank accounts, cost centres and
  budgets, requisitions and approvals, purchase orders and receipts, invoices and their exceptions, and payment
  runs with the bank file. Each person's inbox gathers the decisions waiting on their roles.
- Sign-in through Keycloak with PKCE; the access token is kept in memory and never written to storage.
- A client generated from the services' OpenAPI documents that validates every response with Zod, and a CI step
  that regenerates it against the running stack and fails on any difference.
- A Content Security Policy with no inline script and no `eval`, and five browser journeys that fail on any
  violation of it and on any serious WCAG 2 AA finding in the light or the dark appearance.
- Read endpoints the console needed, each answered from the service's own copies so no role gains access to
  another service: cost centres and suppliers for the requisition form, billable suppliers and orders for invoice
  capture, supplier names on orders and invoices, a list of payment runs, a status filter on requisitions and an
  `awaitingGoods` filter on purchase orders. All are additive; no existing field changed.

### Code layout

- Every service's application layer is organised by feature, with each command and query in its own folder beside
  its one handler, and no mediator (ADR 0008). An architecture test enforces the layout, one handler per request,
  and that no query handler can publish an event or run a command.

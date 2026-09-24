# Security

## Reporting a vulnerability

Open a [private security advisory](https://github.com/tunahanaliozturk/matchbook/security/advisories/new)
rather than a public issue. I will acknowledge within a few days and keep you updated.

## What this project is, and is not

Matchbook is a reference implementation of a purchase-to-pay system. It is written as services you could
deploy, and there are things a deployment has to add.

**The compose file and the Keycloak realm are for development.** Every password is `matchbook` or the service's
own name, the encryption key for bank accounts is committed in `compose.yaml` and in two `appsettings.json`
files, and the `matchbook-cli` client allows the password grant so the `.http` files and the system tests can
sign in without a browser. All of that is public on purpose, bound to localhost, and not a finding. A
deployment supplies its own keys, turns `Auth__RequireHttpsMetadata` on and uses a realm without the password
grant.

**Migrations run when a service starts.** Convenient here; a deployment runs them as a job with a role that
the service itself does not hold.

## What is in scope

**Anything that lets one person do what the design says takes two.** A bank account change, a supplier
activation and a payment run each need a second person who is not the first, and those rules live in the
domain with tests, not in a UI. So do the separation of duties rules: nobody approves their own requisition or
two steps of one, the manager step belongs to the manager on record, the receiver cannot be the buyer who
issued the order, and an AP approver cannot accept a variance on an invoice they captured. A path around any of
these is a real vulnerability.

**Paying an invoice twice, or to the wrong account.** An invoice is paid at most once under any concurrency,
and a run released after the supplier's bank account changed drops that supplier's invoices rather than paying
the account it was drafted against. A way to break either is in scope.

**Spending money a budget does not have.** Reservations and commitments are granted by one conditional update
per grant. A path that reserves or commits beyond available is in scope.

**An IBAN in plain text anywhere it should not be.** Bank account numbers are encrypted with AES-256-GCM at
rest in Suppliers and Payables, and travel encrypted in `SupplierChanged`, so neither the outbox tables nor the
broker ever hold one in the clear (ADR 0007). Responses mask them to the last four characters, except for the
supplier approver reviewing a pending change. An IBAN in a log line, a trace, an outbox row, a message, or a
response to anyone else is in scope.

**Reaching a service without a valid token, or with the wrong role.** The gateway checks the token and every
service checks it again with its own authorization policies (ADR 0004). An endpoint that answers without a
token, or a role reaching an endpoint its policy does not admit, is in scope. The `auditor` role reads
everything and changes nothing; a write it gets through is in scope.

## What is not

**Denial of service by volume.** The gateway limits each caller to 200 requests a second with bursts of 400,
which is a fairness control rather than a defence. There is no protection against a distributed flood.

**What the reconciler cannot see.** The eight invariants in `tests/Matchbook.Stack` are checked by the system
tests, not continuously in a deployment. A state that breaks one after the tests pass is a correctness bug
worth reporting, but the reconciler not running in production is a documented limitation, not a finding.

**Two suppliers sharing one IBAN.** A classic sign of vendor fraud, and not flagged: the column is encrypted under
a random nonce and cannot be indexed. Documented in `docs/services/suppliers.md`.

## Dependencies

`tools/Matchbook.LicenseAudit` reads the licence terms of every package in the restored tree, at every depth,
and fails the build on anything that is not permissively licensed. It is why MassTransit is pinned to 8.5. It
runs in CI on every push, and it is a supply chain control as much as a licensing one: a dependency nobody chose
and nobody read is the usual shape of both problems.

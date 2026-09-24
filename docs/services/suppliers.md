# Suppliers

Suppliers owns the supplier master: who Matchbook buys from, on what payment terms, and which bank account
their money goes to. It publishes one event, `SupplierChanged`, and consumes none. Requisitions, Purchasing and
Payables each keep a copy built from that event.

The rules that matter here are about fraud. Someone who changes a supplier's bank account can redirect every
payment that follows, so no single person can create a payable supplier or change where it is paid. Test names
below are in `tests/Services/Suppliers`: the domain in `Matchbook.Suppliers.UnitTests`, the running service
against real Postgres and RabbitMQ in `Matchbook.Suppliers.IntegrationTests`.

## States

```
Draft ──submit──▶ PendingActivation ──activate──▶ Active ◀──unblock── Blocked
                                                    └──────block───────▶
```

| Move | Who | Also needs |
|---|---|---|
| create, change details, submit | `supplier-admin` | submit only from `Draft` |
| activate | `supplier-approver` who did not submit it | an approved bank account |
| block | either role | a reason, up to 500 characters |
| unblock | `supplier-approver` | |

These rules live in the domain, not in endpoint policies, so they can be tested without an HTTP request and hold
for whoever calls. The person holding both roles is the case the tests use, in the domain
(`The_person_who_submitted_cannot_activate_even_holding_the_approver_role`) and over HTTP
(`Holding_both_roles_does_not_let_anyone_approve_their_own_account_or_activate_their_own_submission`).

## Bank accounts

A supplier admin proposes an account (IBAN, BIC, account holder). A supplier approver who did not propose it
approves it. Until then the account in force stays in force, so a payment run drafted before the change still
pays the account it was drafted against. Each approval raises `AccountVersion`, which Payables compares at
release. Every proposal stays as a row with who proposed it, who decided, when, and why if rejected.

- **IBAN.** Spaces stripped, upper-cased, then checked for the country's length and ISO 7064 mod 97-10. The
  properties in `IbanTests` build IBANs for every accepted country with check digits from a `BigInteger`
  oracle, and show that any single changed digit, any swap of two adjacent digits, and any character added or
  dropped is refused.
- **BIC.** 8 or 11 characters: four letters, an assigned country code, two letters or digits, and an optional
  branch of three.
- **Account holder.** Required, at most 70 characters, because the creditor name in a pain.001 credit transfer
  is `Max70Text`.

**Who sees a full IBAN.** Only a supplier approver who could decide a pending proposal sees that proposal's
IBAN in full: they have to compare it with the supplier's letter. Everyone else, the proposer and the auditor
included, sees every IBAN masked to the last four characters, and the supplier list carries none at all
(`Only_the_approver_reviewing_a_pending_proposal_sees_its_iban_in_full`, `The_supplier_list_carries_no_iban_at_all`).
The rule lives in `BankAccount.MayRevealIbanTo`, and `GetSupplierQuery` carries the caller so its handler can
apply it.

## Where an IBAN is plain text

Only in the memory of this process, while a request handles it. Everywhere else it is ciphertext under the
payment-data key (`Encryption:*`, AES-256-GCM through `ColumnProtector`), which only Suppliers and Payables hold
(ADR 0007).

- **At rest.** `IbanConverter` composes the value object's mapping with `ProtectedStringConverter`, so the
  `bank_accounts.iban` column holds `v1.{keyId}.{nonce, tag and ciphertext}` and a decrypted value is parsed
  again before the domain sees it. `Every_stored_iban_is_ciphertext_under_the_payment_data_key` reads the column
  with Npgsql, not through the service, finds no account number in it, and decrypts it with the test key.
- **In flight.** `SupplierChanged.BankAccount` carries `ProtectedIban` and `IbanLastFour`, never the IBAN. The
  outbox deletes a row once the broker has it, so a test reading the table afterwards could pass on an empty
  table; the integration fixture puts a trigger on `outbox_message` that keeps a copy of every body written.
  `No_outbox_message_ever_carries_the_iban_and_the_event_decrypts_to_it_with_the_shared_key` checks those copies
  and decrypts the event the probe received.
- **In logs.** `Iban.ToString()` returns the masked form, and no validation message repeats the input
  (`An_error_message_never_repeats_the_iban`). EF logs parameters as `?`.

## SupplierChanged

A snapshot: status, legal name, country, payment terms and the verified account with its `AccountVersion`.
`Supplier.Version` is 0 until the first activation, which publishes version 1. After that it rises by exactly
one on block, unblock, a newly approved account, or a change of name, country or terms, and not otherwise.
`Any_sequence_of_operations_moves_the_versions_exactly_as_the_model_says` runs random operation sequences
against an active supplier and a small model, and compares status, version, account version and both accounts
after every step, including the steps the domain refuses. Over HTTP,
`A_supplier_goes_live_only_through_a_second_person_and_each_change_is_published_as_the_next_version` sees
versions 1, 2 and 3 arrive for activation, a block and an approved account change, and nothing before activation.

The handler compares the version before and after the domain call and publishes when it moved, before the one
`SaveChangesAsync` that also writes the change, so the outbox row and the change commit together. Every
`OccurredAt` is UTC.

## HTTP API

Under `/suppliers`, behind the gateway at `/api/suppliers`. `http/suppliers.http` walks through all of it with
real Keycloak tokens, and the OpenAPI document is at `/openapi/v1.json` (`/openapi/suppliers.json` at the
gateway).

| Method and path | Who the domain lets through | Answers |
|---|---|---|
| `GET /suppliers?status=&hasPendingBankAccount=&after=&limit=` | either supplier role, auditor | 200, a page (keyset on id, limit 50, at most 200) |
| `GET /suppliers/{id}` | either supplier role, auditor | 200, 404 |
| `POST /suppliers` | `supplier-admin` | 201, 400, 409, 422 |
| `PUT /suppliers/{id}` | `supplier-admin` | 200, 400, 404, 409, 422 |
| `POST /suppliers/{id}/submit` | `supplier-admin` | 200, 404, 409 |
| `POST /suppliers/{id}/activate` | `supplier-approver`, not the submitter | 200, 404, 409 |
| `POST /suppliers/{id}/block` | either supplier role | 200, 400, 404, 409, 422 |
| `POST /suppliers/{id}/unblock` | `supplier-approver` | 200, 404, 409 |
| `POST /suppliers/{id}/bank-accounts` | `supplier-admin` | 201, 400, 404, 409, 422 |
| `POST /suppliers/{id}/bank-accounts/{accountId}/approve` | `supplier-approver`, not the proposer | 200, 404, 409 |
| `POST /suppliers/{id}/bank-accounts/{accountId}/reject` | either supplier role | 200, 400, 404, 409, 422 |

Every endpoint can also answer 401 and 403. Every mutating one returns the supplier as it is after the change.
`The_document_describes_every_endpoint_with_its_body_and_its_problems` holds the OpenAPI document to that table.

**Creates are idempotent.** `POST /suppliers` and `POST .../bank-accounts` take an optional `id`. The same
request again with the same id answers 201 with the same supplier and writes nothing; the same id with different
content, or from another person, is 409 `request.id_reused`
(`Repeating_a_create_with_its_id_answers_as_the_first_did_and_creates_nothing`).

**Races.** A unique index is the rule, and each one maps to the code a client sees: `ux_suppliers_tax_id` to
`supplier.tax_id_taken`, `ux_bank_accounts_one_pending_per_supplier` to `supplier.bank_account_pending`, the
primary keys to `request.id_reused`. Two approvals of one proposal at once meet on `xmin`, and the loser gets
409 `concurrency.conflict`. `Two_approvals_of_one_proposal_at_once_put_it_in_force_once_and_the_loser_gets_a_conflict`
does not hope for the race: it holds a lock on the supplier row, waits until `pg_stat_activity` shows both
requests blocked on it, then lets go, and checks that the account version rose once and one event left.

## Metrics

On the `Matchbook.Suppliers` meter, exported with the rest over OTLP:

| Instrument | Tag | Counts |
|---|---|---|
| `matchbook.suppliers.changes` | `change`: `created`, `details_changed`, `submitted`, `activated`, `blocked`, `unblocked`, `bank_account_proposed`, `bank_account_approved`, `bank_account_rejected` | changes saved; a retried request that changed nothing is not counted |
| `matchbook.suppliers.refusals` | `code`: the rule's code, or `concurrency.conflict` | commands refused by a rule or a lost race |

A rise in `bank_account_approved`, or any `supplier.self_approval`, is where a fraud review starts. Refusals by a
unique index surface as 409s in the HTTP server metrics rather than here, because the handler never sees them.
`Saved_changes_and_refusals_are_counted_on_the_suppliers_meter` listens to this host's meter.

## What the database enforces

Every rule a stray `UPDATE` could break is repeated as a constraint.

| Constraint | Rule |
|---|---|
| `ux_suppliers_tax_id` | one supplier per normalised tax id |
| `ck_suppliers_tax_id_normalised` | the stored tax id is already normalised, so the unique index compares like with like |
| `ck_suppliers_activated_by_second_person` | the activator is not the submitter |
| `ck_suppliers_activated_with_verified_account` | an active or blocked supplier has `account_version > 0` |
| `ck_suppliers_block_reason` | a reason exactly when blocked |
| `ck_suppliers_payment_terms_days`, `ck_suppliers_country`, `ck_suppliers_status` | ranges and shapes |
| `ux_bank_accounts_one_pending_per_supplier` | at most one pending proposal (partial unique index) |
| `ux_bank_accounts_account_version` | one approved account per version |
| `ck_bank_accounts_approved_by_second_person` | the approver is not the proposer |
| `ck_bank_accounts_decision`, `ck_bank_accounts_version_when_approved`, `ck_bank_accounts_reason_when_rejected` | a decision is complete or absent |
| `ck_bank_accounts_bic`, `ck_bank_accounts_status` | shapes |

The unique indexes and primary keys are exercised by `RaceTests` and `IdempotencyTests`. The check constraints
are not yet: a test should insert violating rows directly and expect each named constraint to refuse them.

Both tables carry Postgres's `xmin` as a concurrency token. `bank_accounts` needs its own: approving and
rejecting the same proposal at once both write that row, and without a token the later write would win
silently. The foreign key from `bank_accounts` is `RESTRICT`, because deleting a supplier would delete its
payment history; nothing deletes one. There is no check on the IBAN column, because it holds ciphertext.

## Decisions the design left open

### The domain

- **IBANs from SEPA countries only**: the 41 countries on the EPC's 2025 list plus Gibraltar, the one SEPA
  territory with its own IBAN prefix (Guernsey, Jersey and the Isle of Man use GB). Payables pays by SEPA credit
  transfer, which cannot reach a Turkish or US account, so accepting one would move the failure from the
  moment someone types it to the moment a payment run is released. The full SWIFT registry lost for that
  reason; it wins the day Payables can pay another way.
- **Check digits 02 to 98 only.** 99 satisfies mod 97 wherever 02 does, but no correct IBAN has it
  (`Check_digits_outside_02_to_98_are_refused_even_where_mod_97_holds`).
- **Tax ids are ASCII.** Separators are dropped, but a non-ASCII letter or digit is refused rather than dropped:
  dropping it could make two different ids collide, and keeping it would let a Cyrillic look-alike past the
  unique index. Only the normalised form is stored; keeping the typed spelling for display lost because nothing
  reads it and two spellings of one id would show differently.
- **Countries are the 249 assigned ISO 3166 codes**, not any two letters, so `UK` and `EU` are refused before
  three other services copy them. BICs are checked against the same list.
- **Details are editable in every state by one supplier admin.** The design puts only the account and
  activation under four eyes. Freezing details while pending activation lost: the approver reviews the current
  details when activating, and an active supplier's details change under one person anyway.
- **A country change publishes a version**, alongside name and terms. The snapshot carries the country, and a
  copy that is not told goes stale.
- **One pending proposal at a time.** A second is refused until the first is approved or rejected. Letting a
  newer proposal replace the pending one lost because the history would no longer say who turned the first one
  down.
- **Proposing the account already in force is refused.** Approving it would raise `AccountVersion` for nothing,
  and every payment run drafted against the old version would drop that supplier's invoices at release.
- **Either role may reject a proposal, the proposer included.** Refusing a change leaves the account in force,
  so it needs no second person, and it is how an admin withdraws a typing mistake.
- **The bank account is checked at activation, not at submission.** An approver can then approve a new
  supplier's account and activate it in one sitting.
- **Unblocking clears the block.** Only bank account history is kept; who blocked a supplier and why is on the
  supplier while it is blocked.
- **Commands on an existing supplier share one runner** (`Features/Suppliers/SupplierCommandRunner`): load, one
  domain call, publish if the version moved, save, count. Each use case keeps its own command and handler, in a
  folder of its own under `Features/Suppliers/Commands` (ADR 0008), so the API maps one endpoint to one handler,
  but eight handlers do not repeat the same fifteen lines.

### The service

- **Two coarse policies, and the domain decides the rest.** `suppliers.read` admits both supplier roles and the
  auditor; `suppliers.write` admits the two supplier roles. Which of them may activate or approve is the domain's
  rule, so every refusal a supplier user meets carries a code (`supplier.role_required`,
  `supplier.self_approval`). A policy per endpoint mirroring the domain lost: two sources for one rule, and a bare
  403 where the client could have had a code. The policies still stop everyone else before a handler runs, and
  `An_auditor_reads_everything_and_is_stopped_before_any_change` tells the two refusals apart by the missing code.
- **The unique index is the tax id rule.** The phase 1 handler checked for a taken tax id before inserting and
  left races to the index. With the index mapped to `supplier.tax_id_taken`, the check only added a query and a
  second path that nothing but a race could reach, so it went.
- **A retried create is compared with the supplier as it is now**, and with who created it. After someone has
  changed the supplier, a late retry no longer matches and gets `request.id_reused`; the client should read it.
  Storing each request's hash to answer a retry exactly lost: a table and an expiry policy for a rare case. Two
  copies of one create arriving at the same moment both miss the lookup; the primary key refuses the second
  with `request.id_reused` rather than replaying.
- **Timestamps are cut to the microsecond** when taken, because Postgres keeps no more. Otherwise a response
  built from memory and a later read of the same row differ in the seventh decimal, and a retried create would
  not answer byte for byte as the first.
- **The meter lives in Application** (`Common/SupplierMetrics`), where an outcome is known. Putting it in Infrastructure would need an
  interface with one implementation for the handlers to call; `IMeterFactory` is in the base library, so the
  architecture test is satisfied.
- **Validation attributes check shape only.** A missing field is 400 with the field named. Whether a value is
  acceptable, an IBAN or 0 to 120 days, is the domain's answer, 422 with a code. Duplicating ranges in attributes
  lost: the client would get a 400 without a code for a rule that has one.
- **Response records mirror the views**, with enums of their own. The HTTP contract is what a generated client
  depends on, so a renamed domain state should break the build here rather than change the document silently.
  The OpenAPI document types the `code` extension on `ProblemDetails`, since it is the one field a client
  branches on.
- **`Location` is relative** (`suppliers/{id}`), so it resolves to `/api/suppliers/{id}` behind the gateway and
  to `/suppliers/{id}` here. An absolute path would be wrong behind the gateway, and honouring
  `X-Forwarded-Prefix` needs the gateway to send it. A proposal answers 201 without `Location`; it has no address
  of its own.
- **The unique-violation mapping sits in `AddSuppliersInfrastructure`**, beside the configurations that name the
  indexes (`SupplierIndexes`), rather than in `Program.cs`, so renaming an index and its code are one change.
- **EF's own error logs are off** (`Database.Command` and `Update` at `Critical`). EF logs every failed save as an
  error twice, including the unique violations and lost races answered with a 409 on purpose. A failure nobody
  expected still reaches the log once, as the unhandled exception with the Postgres error and constraint name.
- **The model holds the first protector.** EF builds the model once per process, and the IBAN converter keeps
  the `ColumnProtector` of the first context. There is one per process, built from configuration at start.
- **No new migration.** The IBAN column was already `text`; encryption changed what goes into it, not the
  schema. `dotnet ef migrations has-pending-model-changes` reports none.

## Error codes

Rule codes start with `supplier.`.

| Codes | Status |
|---|---|
| `legal_name_invalid`, `tax_id_invalid`, `country_invalid`, `payment_terms_invalid`, `email_invalid`, `iban_invalid`, `bic_invalid`, `account_holder_invalid`, `reason_invalid` | 422 |
| `role_required`, `self_approval` | 403 |
| `invalid_transition`, `no_verified_account`, `tax_id_taken`, `bank_account_pending`, `bank_account_unchanged`, `bank_account_not_pending` | 409 |
| `not_found`, `bank_account_not_found` | 404 |

Shared codes: `request.id_reused` and `concurrency.conflict` (409), `request.malformed` (400). A 400 from
validation names the failing fields and has no code.

## Known limitations

- An approver cannot decline an activation. A supplier they will not activate stays pending, or the admin fixes
  it and they activate then. A return-to-draft move is the fix when the queue needs emptying.
- Nothing flags two suppliers sharing one IBAN, a classic sign of vendor fraud. With the column encrypted under
  a random nonce it cannot be indexed; a keyed hash of the IBAN in its own column would make it possible.
- Rows written before encryption at rest would not decrypt. None exist: the service had not run anywhere with
  plain-text IBANs. A deployment that had them would need a one-off rewrite before this version starts.
- The check constraints are not covered by a test yet (see above).

# Suppliers

Suppliers owns the supplier master: who Matchbook buys from, on what payment terms, and which bank account
their money goes to. It publishes one event, `SupplierChanged`, and consumes none. Requisitions, Purchasing and
Payables each keep a copy built from that event.

The rules that matter here are about fraud. Someone who changes a supplier's bank account can redirect every
payment that follows, so no single person can create a payable supplier or change where it is paid. Test names
below are in `tests/Services/Suppliers/Matchbook.Suppliers.UnitTests`.

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

The domain checks the role as well as the API. Separation of duties is only a rule if the code that enforces
it can be tested without an HTTP request, so the person holding both roles is the case the tests use:
`The_person_who_submitted_cannot_activate_even_holding_the_approver_role`.

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

`Iban.ToString()` returns the masked form (`****3000`), and no validation message repeats the input
(`An_error_message_never_repeats_the_iban`). An IBAN that slips into a log line by accident shows four
characters.

**Who sees a full IBAN.** Only a supplier approver who could decide a pending proposal sees that proposal's
IBAN in full: they have to compare it with the supplier's letter. Everyone else, the proposer included, sees
every IBAN masked, and the supplier list carries none at all
(`The_approver_reviewing_a_proposal_sees_that_iban_in_full_and_every_other_masked`). The rule lives in
`BankAccount.MayRevealIbanTo`, and the query handlers take the caller so they can apply it.

## SupplierChanged

A snapshot: status, legal name, country, payment terms and the verified account with its `AccountVersion`.
`Supplier.Version` is 0 until the first activation, which publishes version 1. After that it rises by exactly
one on block, unblock, a newly approved account, or a change of name, country or terms, and not otherwise.
`Any_sequence_of_operations_moves_the_versions_exactly_as_the_model_says` runs random operation sequences
against an active supplier and a small model, and compares status, version, account version and both accounts
after every step, including the steps the domain refuses.

The handler compares the version before and after the domain call and publishes when it moved, before the one
`SaveChangesAsync` that also writes the change, so the outbox row and the change commit together.

## What the database enforces

Every rule a stray `UPDATE` could break is repeated as a constraint. None is exercised by a test yet: the
integration suite should insert violating rows directly and expect each named constraint to refuse them.

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

Both tables carry Postgres's `xmin` as a concurrency token. `bank_accounts` needs its own: approving and
rejecting the same proposal at once both write only that row, and without a token the later write would win
silently. The foreign key from `bank_accounts` is `RESTRICT`, because deleting a supplier would delete its
payment history; nothing deletes one. There is no check on the IBAN column, because it is where the ciphertext
goes once IBANs are encrypted at rest; `IbanConverter` is the one mapping to change for that.

## Decisions the design left open

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
- **Commands on an existing supplier share one runner** (`SupplierCommandRunner`): load, one domain call,
  publish if the version moved, save. Each use case keeps its own handler and command, so the API maps one
  endpoint to one handler, but eight handlers do not repeat the same ten lines.

## Error codes

Every code starts with `supplier.`.

| Codes | Status |
|---|---|
| `legal_name_invalid`, `tax_id_invalid`, `country_invalid`, `payment_terms_invalid`, `email_invalid`, `iban_invalid`, `bic_invalid`, `account_holder_invalid`, `reason_invalid` | 422 |
| `role_required`, `self_approval` | 403 |
| `invalid_transition`, `no_verified_account`, `tax_id_taken`, `bank_account_pending`, `bank_account_unchanged`, `bank_account_not_pending` | 409 |
| `not_found`, `bank_account_not_found` | 404 |

## Known limitations

- An approver cannot decline an activation. A supplier they will not activate stays pending, or the admin fixes
  it and they activate then. A return-to-draft move is the fix when the queue needs emptying.
- Two requests creating the same tax id at once both pass the pre-check, and the second fails on
  `ux_suppliers_tax_id` rather than with `supplier.tax_id_taken`, unless the API maps that index name to the
  code. The same holds for `ux_bank_accounts_one_pending_per_supplier` and `supplier.bank_account_pending`.
- Nothing flags two suppliers sharing one IBAN, a classic sign of vendor fraud. With the column encrypted under
  a random nonce it cannot be indexed; a keyed hash of the IBAN in its own column would make it possible.
- `SupplierChanged` carries the full IBAN, so it sits in plain text in the outbox table until delivered and in
  the broker while queued.

# 7. Bank accounts travel encrypted, and rest encrypted

Status: accepted

## Context

A supplier's bank account is the most attacked field in accounts payable. Changing it to an attacker's account
before a payment run, the "supplier impersonation" fraud, is how most payment fraud works, and a leaked list of
supplier accounts is the raw material for it. Two services need the account: Suppliers, which keeps it under
four-eyes control, and Payables, which writes it into the payment file.

The first version of the contract carried the IBAN in plain text in `SupplierChanged`. Every service stored it
encrypted, but the event sat in Suppliers' outbox table until delivered, crossed RabbitMQ, and landed in the queues
of Requisitions and Purchasing, which have no use for it. The Suppliers engineer flagged that as a contradiction of
"encrypted at rest", and it was.

## Decision

- **A payment-data key** (`Encryption:*`, AES-256-GCM through `ColumnProtector`) is configured in Suppliers and
  Payables and nowhere else.
- **`SupplierChanged` carries `ProtectedIban`**, the IBAN encrypted under that key, and `IbanLastFour` for display.
  Requisitions and Purchasing receive a value they cannot read and have no reason to.
- **At rest**, both services keep the IBAN encrypted in their databases with the same `ColumnProtector`.
  Payables decrypts only to validate an incoming account and to write a payment file.
- **In responses**, IBANs are masked to the last four characters, except for the supplier approver reviewing a
  pending change, who has to compare the full number with what the supplier sent.
- **Never in logs.** The `Iban` value objects render masked from `ToString()`, so a structured log of one shows
  four characters.

The inner layers see the key only as `IFieldProtector` from the shared kernel.

## Consequences

- The account number is plain text only in the memory of two services, and only while it is being checked or
  written into a file.
- Rotating the key is a configuration change in two services at once: add the new key to both, then make it
  active in both. Values under the old key keep decrypting, because each carries its key id.
- An attacker with the broker, or with a copy of the Requisitions or Purchasing database, gets no accounts.
- The pain.001 file itself contains accounts in the clear, as the bank requires. It is produced on request for
  treasurers only and never stored outside the Payables database.

## Alternatives considered

**MassTransit's message encryption.** Encrypts whole messages for every consumer, so every consumer needs the key,
which is the opposite of the goal.

**Payables asks Suppliers for the account at payment time, over HTTP.** Keeps the account in one service, and makes
every payment run depend on Suppliers being up, with a synchronous call this design otherwise avoids.

**Plain text on an internal network.** The usual choice, and the one a fraud investigation ends up regretting.

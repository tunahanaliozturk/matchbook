# Changing an integration event

The records in `src/Shared/Matchbook.Contracts` are read by services that deploy on their own schedule, so a
message published by a new version of one service is consumed by an old version of another, and the other way
round. RabbitMQ does not care about the shape of a message; MassTransit maps it by the type's full name, and
System.Text.Json fills what it can.

That gives three rules:

1. **Add, never rename or remove.** A new member goes at the end, nullable or with a default the old publisher
   implies. An old consumer ignores it; a new consumer reading an old message gets the default.
2. **Never change a member's meaning or type.** `Amount` stays euros to two places. If it has to mean something
   else, it is a new member.
3. **Anything else is a new type.** `PurchaseOrderIssuedV2`, published alongside the old one until every
   consumer has moved, then the old one retired.

A type's full name is its address on the broker, so moving a record to another namespace is a breaking change
too. `tests/Matchbook.ArchitectureTests` pins every contract's name and members, and fails when one changes, so
a change to this project is always a deliberate one.

Values that look like enums (`SupplierStatus`, `FundsRejectionReason`, `PurchaseOrderCloseReason`) are strings
on the wire. A consumer that meets a value it does not know treats it as the most cautious one it does: an
unknown supplier status is not `Active`.

Every `OccurredAt` is UTC, with a zero offset. Postgres `timestamptz` columns written through Npgsql accept
nothing else, and a consumer should never have to guess.

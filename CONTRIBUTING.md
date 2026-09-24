# Contributing

## Getting set up

You need the .NET 10 SDK and Docker. Nothing else.

```bash
dotnet build Matchbook.slnx
dotnet run --project tests/Services/Budgets/Matchbook.Budgets.UnitTests
dotnet run --project tests/Services/Budgets/Matchbook.Budgets.IntegrationTests   # needs Docker running
```

Every service has the same two suites under `tests/Services/<Service>`. Test projects are executables on
Microsoft.Testing.Platform, so they run with `dotnet run`, not `dotnet test`. For the whole stack:

```bash
docker compose up -d --build --wait
dotnet run --project tests/Matchbook.SystemTests
```

## Ground rules

**`docs/design.md` is the contract.** It says which service owns what, which events exist, and which rules hold
across services. A change that moves ownership, adds an event or changes a cross-service rule starts there, and
usually needs an ADR in `docs/adr/`.

**No service references another service's projects.** What one service knows about another arrives as an event
from `src/Shared/Matchbook.Contracts`. `tests/Matchbook.ArchitectureTests` checks the compiled references, and
the same suite checks the layering inside each service.

**Events are a published contract.** Changing an event's name or members breaks every consumer that is already
deployed, and a test pins each one. Add a new event, or add members with defaults; do not rename or remove.

**Every consumer must be correct under any arrival order and any number of deliveries.** A consumer stores what
it was told and acts once the rest is present; it never throws because something has not arrived yet (ADR
0002). A new consumer needs a test that delivers its message twice and one that delivers it before the event it
depends on.

**A change to a guarantee needs a test that would fail without it.** The concurrency, four-eyes and payment
claims in the README are the point of the project. If you touch a conditional update, a unique index, a
separation of duties rule or the payment run, the pull request includes the test that proves the new
behaviour, and a system test run that still reconciles.

**Warnings are errors.** `TreatWarningsAsErrors` is on for every project, and CI runs
`dotnet format whitespace` and `dotnet format style` with `--verify-no-changes`. Run `dotnet format` before you
push.

**Nothing commercially licensed, at any depth.** `tools/Matchbook.LicenseAudit` fails the build on anything
that is not permissively licensed. It is why MassTransit is pinned to 8.5. If the audit fails, downgrade or
replace rather than adding an exception.

**Packages are centrally managed.** Versions live in `Directory.Packages.props` and nowhere else.

## Commits

Conventional commits (`feat`, `fix`, `test`, `docs`, `refactor`, `chore`), with the service as the scope when
the change is inside one: `fix(payables): ...`. The subject says what changed for someone reading the log; the
body says why.

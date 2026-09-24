# Matchbook console

The screen for the fifteen people in the realm: a Vue 3 single page application that signs in against Keycloak and
talks to the five services through the gateway. The decisions behind it are in `docs/adr/0009-*`.

## Running it

With the stack up from the repository root (`docker compose up -d --build --wait`), the console is at
http://localhost:5301. To work on it with hot reload instead:

```
cd web
npm ci
npm run dev
```

Sign in as any seeded person (`docs/design.md`, "Seeded identities"); every password is `matchbook`. The dev server
proxies `/api` to the gateway on `:5300`. The realm also allows ports 5302 to 5305, so several dev servers can run
side by side with `npx vite --port 5302`.

## Where things are

```
src/app/            the window (AppShell), the router, the source list's destinations, config
src/auth/           sign-in (session.ts), roles, and the realm's people for naming who did what
src/shared/api/     generated/<service>: types, Zod schemas and SDK from the OpenAPI documents; problem.ts
src/shared/ui/      the design system: tokens are in src/styles, components here
src/features/<area> one folder per API area, as in the services (ADR 0008)
e2e/                journeys against the running stack, one file per area
```

A feature folder holds `data.ts` (its queries and mutations), its views, its sheets, `tones.ts` (status to tone),
`routes.ts`, and any inbox queues. Features do not import each other; what two need moves to `src/shared`.

## The contract

`npm run contract` regenerates `src/shared/api/generated` from the running gateway. The generated files are
committed, CI regenerates them from the stack it starts, and any difference fails the build: a server change the
console has not caught up with breaks the pipeline, not a screen. Never edit a generated file.

Every SDK call parses its response through the generated Zod schema. A refusal arrives as an `ApiProblem` carrying
the status, the problem `code` and the server's own sentence (`detail`), which is what the screen shows.

## How screens are built

- **Data**: one `data.ts` per feature, TanStack Query only. Query keys live in one object there. A command's answer is
  the resource's new state (`docs/design.md`, "API"), so a mutation writes it into the cache with `setQueryData` and
  only invalidates lists. Follow `src/features/suppliers/data.ts`.
- **Creates** get their id from `uuidv7()` when the sheet opens, so a double submit or a retry creates one thing.
- **Forms** live in a `UiSheet`. `FormField` wires label, hint and errors; a 400's field errors come from
  `ApiProblem.errorsFor(field)`, anything else is an `InlineNotice` at the top of the sheet.
- **Decisions that need a reason** (reject, block, accept a variance) use `ReasonSheet`.
- **Rules about who may act** (four eyes, separation of duties) are said out loud with an `InlineNotice` where they
  apply: a disabled button with the reason next to it, not a button that silently is not there. The server enforces
  the rule either way; the screen explains it.
- **Asynchronous steps** (Submitted until Budgets answers, CommitmentPending until funds are committed) show their
  transitional status and refetch with `refetchInterval` while it lasts.
- **Names**: the services store ids; `nameOf(id, me)` from `src/auth/people.ts` turns them into "you" or a name.
- **Inbox**: each role's waiting work is a `WorkQueue` component registered in `src/features/inbox/queues.ts`.
- **Routes** are registered in `src/features/routes.ts`; `meta.roles` is the read policy of the service behind them,
  so the console never shows a page that would answer 403.

## The design system

In the manner of a macOS application, and deliberately not a component library's look.

- Colour, type, spacing, radius and motion are custom properties in `src/styles/tokens.css`, with a dark value for
  each. Components use tokens only, never a literal colour.
- Components: `PageHeader` (large title, back link, toolbar), `GroupedSection` with `ListRow` or `DetailRow` (inset
  grouped lists), `UiTable` (lines and ledgers), `StatusPill` with a tone, `MoneyText`, `LedgerCapsule` (a budget as
  one capsule, spent, ordered, requested, available), `UiButton` (primary, secondary, destructive, plain),
  `SegmentedControl` (filters), `UiSheet`, `ReasonSheet`, `FormField`, `InlineNotice`, `EmptyState`, `UiIcon`.
- One primary button per surface, the action the person most likely came for. Actions that need more input before
  they happen end with an ellipsis: "Reject…".
- Copy is sentence case, plain verbs, no exclamation marks, no emoji, no capitals for emphasis. An empty list says
  why it is empty in one sentence and offers the action that would change it.
- Money is always `MoneyText` (euros, ADR 0006); any figure that may line up in a column uses tabular numerals.

## Checks

```
npm run type-check     vue-tsc, strict
npm run lint           ESLint with the Vue accessibility rules, no warnings allowed
npm run format:check   Prettier
npm test               Vitest
npm run build && npm run budget    the first load must stay under 170 kB compressed
npm run licences       every npm package permissively licensed
npm run test:e2e       Playwright journeys against the running stack
```

Every journey calls `expectAccessible(page)` on each screen it visits, which runs axe in the light and the dark
appearance and fails on any serious or critical WCAG 2 AA violation.

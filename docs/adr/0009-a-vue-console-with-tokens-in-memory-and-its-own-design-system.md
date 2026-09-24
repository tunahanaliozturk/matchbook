# 9. A Vue console, with tokens in memory and a design system of its own

Status: accepted

## Context

Matchbook had an API and no screen. Fifteen seeded people hold thirteen roles, and each does a different part of
one purchase: rita raises a requisition, mark approves it, bruno orders, rosa receives, alice captures the invoice,
aaron clears its exceptions, tess and trevor pay it, sam and sofia keep the supplier, bob keeps the budget, audrey
reads everything. A reviewer should be able to sign in as any of them and do that person's whole job.

The realm already had a public client for it (`matchbook-console`, PKCE, `http://localhost:5301`), and the gateway
already validates bearer tokens. The house standard for a frontend asks for strict TypeScript, server data in a
query cache, every response parsed at the boundary, types generated from the server's contract with a CI job that
fails on drift, accessibility and bundle size as build gates, and no token where a script can read it for longer
than it has to.

## Decision

**A Vue 3 single page application in `web/`, served by nginx at `:5301`, which proxies `/api` to the gateway.**

- **Sign-in**: authorization code with PKCE through `oidc-client-ts`. The access token and the refresh token live in
  memory only. A reload loses them, and the router sends the browser back through Keycloak, whose own session answers
  at once.
- **Contract**: the five OpenAPI documents are the source. `@hey-api/openapi-ts` generates the types, a Zod schema per
  model, and a client that parses every response through its schema. A CI job regenerates from the running stack and
  fails on any difference.
- **Server state** in TanStack Query. The session is the only application-wide state, a composable, no store.
- **Look**: a design system of our own, written as CSS custom properties and scoped component styles, in the manner
  of a macOS application: a translucent source list on the left, a large title and a toolbar over the content,
  grouped lists with hairline separators, sheets for forms, the system font, one accent colour, light and dark.
  Reka UI supplies unstyled primitives only where accessibility is hard to get right by hand (the sheet's focus
  trap, the segmented control's keyboard model). Confirmations are a plain `aria-live` region: Reka's toast adds
  hidden focusable proxies that axe reports, for a feature that never needs focus.
- **Features** follow the API's areas, as the services do (ADR 0008): `src/features/<area>`.
- **Accessibility and weight are gates**: every journey runs axe over each screen in the light and the dark
  appearance, and the first load is held under 170 kB compressed.

## Consequences

- No backend change to sign in, and no session state on any server. Bearer tokens in a header cannot be forged by
  another site, so there is no CSRF surface. The cost: an XSS could read the token for as long as the page is open.
  The page's Content Security Policy allows scripts from its own origin only, and the token lives fifteen minutes.
- Same origin for the API, so the browser sends no preflight and the CSP's `connect-src` names only itself and
  Keycloak.
- A change to a response on the server fails the frontend's type-check in CI, and a change the types cannot see
  fails the Zod parse at the fetch site with the path and the field.
- The UI is ours to maintain: there is no component library to upgrade, and none to lean on either.
- Reference data a role cannot read from its owner (a requester cannot list suppliers) is served by the service
  that holds a local copy for that use case, as lookup queries in that service's own API.
- The services record who acted by id. The console names people from a copy of the realm's user list
  (`src/auth/people.ts`), because a person's own token cannot list users; a deployment would read the identity
  provider's directory instead.
- Two house rules gave way to the generated client: `exactOptionalPropertyTypes` is off, because the generated code
  does not compile with it, and `.ts` import paths are allowed, because the client imports its runtime config so.
- The system colours were adjusted where Apple's own fail WCAG AA at the console's text sizes: secondary text is
  `#636366` rather than `#6E6E73`, and in dark mode the filled accent is `#0A6FDB` rather than `#0A84FF`.

## Alternatives considered

**A backend for frontend.** The gateway would sign in on the browser's behalf and hold the tokens, and the browser
would carry only an `HttpOnly` cookie. It is the stronger answer to XSS and the one the OAuth browser-app guidance
leans towards. It lost here on cost: session state in the gateway (one instance, or a shared store), anti-forgery
tokens on every write, and a gateway redesigned around cookies. It would win the moment the console handled
anything a stolen fifteen-minute token could do lasting harm with, and a payment run release is close to that; it
is listed in the operations guide as the first thing to change for production.

**Nuxt.** Nothing here needs rendering on a server: every page is behind a sign-in and nothing is indexed. Nuxt would
add a Node process to run and patch, and hydration as a kind of bug.

**A component library (PrimeVue, Naive UI, Vuetify).** Fastest to a working screen, and every screen would look like
the library's demo. PrimeVue 5 also moved to a licence with revenue conditions, which the licence audit refuses.

**Hand-written Zod schemas next to generated types.** The sibling project `pitchwire` does this for a handful of
payloads. With five services and several dozen models, two definitions of each would drift, and generating both
from one document is the point of having the document.

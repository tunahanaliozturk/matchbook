# 4. Identity from Keycloak, checked at the gateway and again in every service

Status: accepted

## Context

Almost every rule in purchasing is about who: a requester may not approve their own request, the person who
ordered may not receive, the clerk who captured an invoice may not accept its variance, one treasurer drafts a
payment run and another releases it. The services need a trustworthy answer to "who is this and what may they
do", and no service should have to trust another to have asked.

## Decision

- **Keycloak issues the tokens.** One realm, one role per job (`Matchbook.SharedKernel.Roles`), realm roles in the
  access token, an audience mapper that stamps `matchbook` into it. The compose stack imports a realm with one
  user per role (`deploy/keycloak`), for development only.
- **The gateway (YARP) is the only public entry point.** It validates the token, rejects anything
  unauthenticated, limits each caller to a token bucket (200 requests a second, bursts to 400) keyed by the token's
  subject rather than the client address, caps request bodies at 1 MB, and forwards the token unchanged.
- **Every service validates the token again**, with the same code (`AddMatchbookAuthentication`): issuer,
  audience, signature, lifetime. A fallback policy requires an authenticated caller on every endpoint, so an
  endpoint that forgets its policy fails closed. Nothing a service does depends on having been called through the
  gateway.
- **The domain receives an `Actor`** (id, name, roles), built from the token by one extension method, and the
  separation-of-duties rules are domain rules with tests, never checks in a controller.

Tests use the same authentication code with a signing key of their own (`tests/Matchbook.Testing/TestIdentity`),
and tokens shaped exactly like Keycloak's, `realm_access` and all, so the claims transformation is exercised too.

## Consequences

- A request pays for signature validation twice. It is microseconds against an RSA public key the service has
  cached, and it buys a service that is safe on its own network.
- The compose stack runs Keycloak, which takes about twenty seconds to start and is the slowest container to
  become healthy.
- Tokens name `localhost:8080` as their issuer while services fetch signing keys over the compose network
  (`KC_HOSTNAME` with a dynamic back channel). A real deployment has one public hostname and none of this.

## Alternatives considered

**Validation at the gateway only, with services trusting a forwarded header.** Common, and it turns any service
reachable from inside the network into one that believes whoever calls it.

**A hand-written token service.** An identity provider is a product of its own; the portfolio has one already
(`keyward`), and here the point is what the services do with an identity, not how it is issued.

**API keys per role.** Would make separation of duties meaningless: the rules are about people, not about roles.

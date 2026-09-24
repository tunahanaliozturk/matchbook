// Where the console signs in and what it assumes about money. The defaults are the compose stack's; a build
// for anywhere else sets VITE_AUTHORITY.
export const config = {
    authority: import.meta.env.VITE_AUTHORITY ?? "http://localhost:8080/realms/matchbook",
    clientId: "matchbook-console",
    apiBase: "/api",
    // One currency for the whole system (ADR 0006), so it is a constant here rather than a field on every amount.
    currency: "EUR",
    locale: "en-GB",
} as const;

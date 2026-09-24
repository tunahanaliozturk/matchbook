import { config } from "@/app/config";
import { accessToken } from "@/auth/session";

// Read by each generated client before it is created (openapi-ts.config.ts, runtimeConfigPath). Every service is
// reached through the gateway under one prefix on the console's own origin, and every request carries the token
// of whoever is signed in.
export const createClientConfig = <T extends object>(generated?: T) => ({
    ...generated,
    baseUrl: config.apiBase,
    auth: () => accessToken(),
});

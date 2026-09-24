import { defineConfig, devices } from "@playwright/test";

// Journeys against the real stack: Keycloak signs each person in, the gateway and the five services answer. One
// worker, because journeys hand work from one person to the next and share the stack's state.
export default defineConfig({
    testDir: "./e2e",
    testMatch: "*.pw.ts",
    timeout: 60_000,
    expect: { timeout: 15_000 },
    workers: 1,
    retries: process.env.CI ? 1 : 0,
    reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
    use: {
        ...devices["Desktop Chrome"],
        baseURL: process.env.MATCHBOOK_CONSOLE_URL ?? "http://localhost:5301",
        screenshot: "only-on-failure",
        trace: "retain-on-failure",
    },
});

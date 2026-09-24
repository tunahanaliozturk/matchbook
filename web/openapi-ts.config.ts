import { defineConfig } from "@hey-api/openapi-ts";

// The five OpenAPI documents, as the running gateway serves them, are the only definition of the contract. From
// each one this generates the types, a Zod schema per model, and an SDK that parses every response through its
// schema before a component sees it. CI regenerates from the stack it just started and fails on any difference,
// so a server change the console has not caught up with breaks the build instead of a screen.
const gateway = process.env.MATCHBOOK_GATEWAY ?? "http://localhost:5300";
const services = ["suppliers", "budgets", "requisitions", "purchasing", "payables"] as const;

export default defineConfig({
    input: services.map((service) => `${gateway}/openapi/${service}.json`),
    output: services.map((service) => `src/shared/api/generated/${service}`),
    plugins: [
        // Throwing on a refusal lets the query cache treat it as a failure, and types every response as present.
        {
            name: "@hey-api/client-fetch",
            runtimeConfigPath: "./src/shared/api/runtime.ts",
            throwOnError: true,
        },
        "@hey-api/typescript",
        // .NET writes a DateTimeOffset with its offset ("+00:00"), which is ISO 8601; Zod's default wants a "Z".
        { name: "zod", dates: { offset: true } },
        { name: "@hey-api/sdk", validator: true },
    ],
});

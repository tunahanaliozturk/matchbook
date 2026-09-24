// Every `import ... from "zod"` in the console, the generated schemas included, resolves here (vite.config.ts), so
// Zod is configured before any schema exists. Zod 4 probes for its JIT with Function("") as each object schema is
// created, which the Content Security Policy forbids (script-src 'self') and the browser reports as a violation.
// Configuring it from main.ts was not enough: the bundler places schemas used by the entry in a chunk that runs
// before the entry's own code, and the journey's CSP check caught the probe firing first.
import { z } from "zod/v4";

z.config({ jitless: true });

export * from "zod/v4";
export { z };

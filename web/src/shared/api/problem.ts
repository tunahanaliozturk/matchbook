import { z } from "zod";

// Every refusal from every service is an RFC 9457 problem with a stable code (ADR 0004, docs/design.md "API").
// The console branches on the code and shows the detail, which the services write as a sentence for a person.
const problemBody = z.object({
    title: z.string().optional(),
    status: z.number().optional(),
    detail: z.string().optional(),
    code: z.string().optional(),
    errors: z.record(z.string(), z.array(z.string())).optional(),
});

export class ApiProblem extends Error {
    readonly status: number;
    readonly code: string | undefined;
    readonly detail: string;
    readonly fieldErrors: Readonly<Record<string, readonly string[]>>;

    constructor(status: number, body: unknown) {
        const parsed = problemBody.safeParse(body);
        const problem = parsed.success ? parsed.data : {};
        const detail = problem.detail ?? problem.title ?? fallback(status);

        super(detail);
        this.name = "ApiProblem";
        this.status = status;
        this.code = problem.code;
        this.detail = detail;
        this.fieldErrors = problem.errors ?? {};
    }

    /** The server's messages for one field of a form, matched without regard to case. */
    errorsFor(field: string): readonly string[] {
        const key = Object.keys(this.fieldErrors).find(
            (name) => name.toLowerCase() === field.toLowerCase(),
        );
        return key === undefined ? [] : (this.fieldErrors[key] ?? []);
    }
}

function fallback(status: number): string {
    if (status === 401) return "Your session has ended. Sign in again to continue.";
    if (status === 403) return "Your roles do not allow this.";
    if (status === 404) return "This no longer exists, or never did.";
    if (status === 429) return "Too many requests at once. Wait a moment and try again.";
    if (status >= 500) return "The service could not answer. Try again in a moment.";
    return "The request was refused.";
}

/** Whether trying again could help: a refusal will be refused again, an outage might not last. */
export const isTransient = (error: unknown): boolean =>
    !(error instanceof ApiProblem) || error.status >= 500 || error.status === 429;

/** A sentence for any error that reaches the screen, whatever threw it. */
export const describe = (error: unknown): string =>
    error instanceof ApiProblem
        ? error.detail
        : error instanceof Error && error.name === "ZodError"
          ? "The service answered in a shape this console does not understand. It may need updating."
          : "Something went wrong between the console and the service. Try again.";

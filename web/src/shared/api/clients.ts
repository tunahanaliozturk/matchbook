import { client as budgets } from "./generated/budgets/client.gen";
import { client as payables } from "./generated/payables/client.gen";
import { client as purchasing } from "./generated/purchasing/client.gen";
import { client as requisitions } from "./generated/requisitions/client.gen";
import { client as suppliers } from "./generated/suppliers/client.gen";
import { ApiProblem } from "./problem";

// A refusal from any service becomes one error type the screens understand, carrying the status, the problem code
// and the server's own sentence. A response that fails its schema, or a request that never got an answer, keeps
// its own error: those are not refusals, and describe() words them differently.
for (const client of [suppliers, budgets, requisitions, purchasing, payables]) {
    client.interceptors.error.use((error, response) =>
        response !== undefined && !response.ok ? new ApiProblem(response.status, error) : error,
    );
}

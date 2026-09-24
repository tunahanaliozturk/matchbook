// The preconditions a journey needs but does not test, set up the way another service's user would: straight through
// the gateway with the person's own token. A journey about paying an invoice should not also click through creating
// the supplier, the budget and the requisition behind it; those have journeys of their own.
const gateway = process.env.MATCHBOOK_GATEWAY_URL ?? "http://localhost:5300/api";
const tokenEndpoint =
    process.env.MATCHBOOK_TOKEN_URL ??
    "http://localhost:8080/realms/matchbook/protocol/openid-connect/token";

const tokens = new Map<string, string>();

/** A token for a seeded person, from the password grant the matchbook-cli client allows for exactly this. */
async function tokenFor(username: string): Promise<string> {
    const cached = tokens.get(username);
    if (cached) return cached;

    const response = await fetch(tokenEndpoint, {
        method: "POST",
        headers: { "content-type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({
            grant_type: "password",
            client_id: "matchbook-cli",
            username,
            password: "matchbook",
        }),
    });

    if (!response.ok) {
        throw new Error(
            `Keycloak refused ${username}: ${response.status} ${await response.text()}`,
        );
    }

    const { access_token: token } = (await response.json()) as { access_token: string };
    tokens.set(username, token);
    return token;
}

/** Calls the gateway as <username>; throws with the problem body on anything but success. */
export async function api<T = unknown>(
    username: string,
    method: "GET" | "POST" | "PUT",
    path: string,
    body?: unknown,
): Promise<T> {
    const response = await fetch(`${gateway}${path}`, {
        method,
        headers: {
            authorization: `Bearer ${await tokenFor(username)}`,
            ...(body === undefined ? {} : { "content-type": "application/json" }),
        },
        body: body === undefined ? null : JSON.stringify(body),
    });

    if (!response.ok) {
        throw new Error(
            `${method} ${path} as ${username}: ${response.status} ${await response.text()}`,
        );
    }

    return (response.status === 204 ? undefined : await response.json()) as T;
}

/** Polls <read> until <done> holds: events between services take a moment to arrive. */
export async function eventually<T>(
    read: () => Promise<T>,
    done: (value: T) => boolean,
    timeoutMs = 30_000,
): Promise<T> {
    const deadline = Date.now() + timeoutMs;
    let last = await read();

    while (!done(last)) {
        if (Date.now() > deadline) {
            throw new Error(
                `Still waiting after ${timeoutMs} ms; last seen ${JSON.stringify(last)}`,
            );
        }

        await new Promise((resolve) => setTimeout(resolve, 250));
        last = await read();
    }

    return last;
}

const run = () => Date.now().toString(36).toUpperCase();

/** An active supplier with an approved account: sam creates and submits, sofia approves and activates. */
export async function activeSupplier(
    paymentTermsDays = 30,
): Promise<{ id: string; legalName: string }> {
    const legalName = `Journey Supplier ${run()} GmbH`;
    const supplier = await api<{ id: string }>("sam", "POST", "/suppliers", {
        legalName,
        taxId: `DE${String(Date.now()).slice(-9)}`,
        countryCode: "DE",
        paymentTermsDays,
        contactEmail: "ap@journey.example",
    });
    const proposed = await api<{ bankAccounts: { id: string }[] }>(
        "sam",
        "POST",
        `/suppliers/${supplier.id}/bank-accounts`,
        { iban: "DE89370400440532013000", bic: "DEUTDEFF", accountHolder: legalName },
    );
    await api(
        "sofia",
        "POST",
        `/suppliers/${supplier.id}/bank-accounts/${proposed.bankAccounts[0]!.id}/approve`,
    );
    await api("sam", "POST", `/suppliers/${supplier.id}/submit`);
    await api("sofia", "POST", `/suppliers/${supplier.id}/activate`);
    return { id: supplier.id, legalName };
}

/** A cost centre managed by mark, with a budget for this year, created by bob. */
export async function costCentreWithBudget(
    allotted = 100_000,
): Promise<{ code: string; budgetId: string }> {
    const code = `JRN-${run()}`;
    await api("bob", "POST", "/cost-centres", {
        code,
        name: `Journey ${code}`,
        managerId: "a0000000-0000-4000-8000-000000000002",
    });
    const budget = await api<{ id: string }>("bob", "POST", "/budgets", {
        costCentreCode: code,
        fiscalYear: new Date().getUTCFullYear(),
        allotted,
    });
    return { code, budgetId: budget.id };
}

/**
 * A requisition rita raised and mark approved, under the finance threshold so one approval is enough. Requisitions
 * learns of the supplier and cost centre by events, so creating it is retried until it has.
 */
export async function approvedRequisition(
    supplierId: string,
    costCentreCode: string,
    lines = [{ description: "Widget", quantity: 10, unitOfMeasure: "EA", unitPrice: 12.5 }],
): Promise<{ id: string }> {
    const neededBy = new Date(Date.now() + 90 * 86_400_000).toISOString().slice(0, 10);
    const requisition = await eventually(
        () =>
            api<{ id: string }>("rita", "POST", "/requisitions", {
                costCentreCode,
                supplierId,
                justification: "Journey purchase",
                neededBy,
                lines,
            }).catch(() => null),
        (created) => created !== null,
    );

    await api("rita", "POST", `/requisitions/${requisition!.id}/submit`);
    await eventually(
        () => api<{ status: string }>("rita", "GET", `/requisitions/${requisition!.id}`),
        (current) => current.status === "PendingApproval",
    );
    await api("mark", "POST", `/requisitions/${requisition!.id}/approve`);
    return { id: requisition!.id };
}

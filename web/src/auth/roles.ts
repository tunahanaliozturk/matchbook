import { z } from "zod";

// The realm roles, as Matchbook.SharedKernel.Roles names them. The server decides what each may do; the console
// only uses them to show a person the work that is theirs.
export const roles = [
    "requester",
    "approver",
    "finance-approver",
    "cfo",
    "buyer",
    "receiver",
    "ap-clerk",
    "ap-approver",
    "treasurer",
    "supplier-admin",
    "supplier-approver",
    "budget-admin",
    "auditor",
] as const;

export type Role = (typeof roles)[number];

export const roleNames: Record<Role, string> = {
    requester: "Requester",
    approver: "Approver",
    "finance-approver": "Finance approver",
    cfo: "CFO",
    buyer: "Buyer",
    receiver: "Receiver",
    "ap-clerk": "AP clerk",
    "ap-approver": "AP approver",
    treasurer: "Treasurer",
    "supplier-admin": "Supplier admin",
    "supplier-approver": "Supplier approver",
    "budget-admin": "Budget admin",
    auditor: "Auditor",
};

const isRole = (value: string): value is Role => (roles as readonly string[]).includes(value);

// What the console reads from an access token. Keycloak nests realm roles; anything that is not one of ours (the
// default-roles and offline_access entries) is ignored rather than trusted.
const claims = z.object({
    sub: z.string(),
    preferred_username: z.string(),
    realm_access: z.object({ roles: z.array(z.string()) }).optional(),
});

export interface Person {
    id: string;
    name: string;
    roles: ReadonlySet<Role>;
}

/** Reads who a token belongs to. The signature is the server's business; this is for display only. */
export function personFrom(accessToken: string): Person {
    const payload = accessToken.split(".")[1];

    if (payload === undefined) {
        throw new Error("The access token is not a JWT.");
    }

    const json = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
    const parsed = claims.parse(JSON.parse(json));

    return {
        id: parsed.sub,
        name: parsed.preferred_username,
        roles: new Set((parsed.realm_access?.roles ?? []).filter(isRole)),
    };
}

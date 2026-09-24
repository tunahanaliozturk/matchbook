import type { Role } from "./roles";

export interface Colleague {
    id: string;
    name: string;
    roles: readonly Role[];
}

// The people in the realm (deploy/keycloak), so a screen can say "approved by Mark" where the services store only
// an id, and a budget admin can pick a cost centre's manager by name. The console cannot list users from Keycloak
// with a person's own token; a production deployment would read this from the identity provider's directory.
export const colleagues: readonly Colleague[] = [
    { id: "a0000000-0000-4000-8000-000000000001", name: "Rita", roles: ["requester"] },
    { id: "a0000000-0000-4000-8000-000000000002", name: "Mark", roles: ["approver"] },
    { id: "a0000000-0000-4000-8000-000000000003", name: "Maya", roles: ["approver"] },
    { id: "a0000000-0000-4000-8000-000000000004", name: "Fiona", roles: ["finance-approver"] },
    { id: "a0000000-0000-4000-8000-000000000005", name: "Carl", roles: ["cfo"] },
    { id: "a0000000-0000-4000-8000-000000000006", name: "Bruno", roles: ["buyer"] },
    { id: "a0000000-0000-4000-8000-000000000007", name: "Rosa", roles: ["receiver"] },
    { id: "a0000000-0000-4000-8000-000000000008", name: "Alice", roles: ["ap-clerk"] },
    { id: "a0000000-0000-4000-8000-000000000009", name: "Aaron", roles: ["ap-approver"] },
    { id: "a0000000-0000-4000-8000-000000000010", name: "Tess", roles: ["treasurer"] },
    { id: "a0000000-0000-4000-8000-000000000011", name: "Trevor", roles: ["treasurer"] },
    { id: "a0000000-0000-4000-8000-000000000012", name: "Sam", roles: ["supplier-admin"] },
    { id: "a0000000-0000-4000-8000-000000000013", name: "Sofia", roles: ["supplier-approver"] },
    { id: "a0000000-0000-4000-8000-000000000014", name: "Bob", roles: ["budget-admin"] },
    { id: "a0000000-0000-4000-8000-000000000015", name: "Audrey", roles: ["auditor"] },
];

const byId = new Map(colleagues.map((colleague) => [colleague.id, colleague]));

/** "you", a colleague's name, or "someone" for an id the directory does not know. */
export function nameOf(id: string | null | undefined, me: string | undefined): string {
    if (!id) return "someone";
    if (id === me) return "you";
    return byId.get(id)?.name ?? "someone";
}

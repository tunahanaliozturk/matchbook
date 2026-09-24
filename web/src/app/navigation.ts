import type { Role } from "@/auth/roles";
import type { IconName } from "@/shared/ui/icons";

export interface Destination {
    route: string;
    label: string;
    icon: IconName;
    /** Who sees it in the source list. The same roles the service's read policy admits, so nobody is shown a door
     *  that answers 403. */
    roles: readonly Role[];
}

export interface Section {
    label: string | null;
    destinations: readonly Destination[];
}

const approvers = ["approver", "finance-approver", "cfo"] as const satisfies readonly Role[];

export const sections: readonly Section[] = [
    {
        label: null,
        destinations: [
            {
                route: "inbox",
                label: "Inbox",
                icon: "inbox",
                roles: [
                    "requester",
                    ...approvers,
                    "buyer",
                    "receiver",
                    "ap-clerk",
                    "ap-approver",
                    "treasurer",
                    "supplier-admin",
                    "supplier-approver",
                    "budget-admin",
                    "auditor",
                ],
            },
        ],
    },
    {
        label: "Buying",
        destinations: [
            {
                route: "requisitions",
                label: "Requisitions",
                icon: "requisition",
                roles: ["requester", ...approvers, "auditor"],
            },
            { route: "approvals", label: "Approvals", icon: "approval", roles: approvers },
            {
                route: "purchase-orders",
                label: "Purchase orders",
                icon: "order",
                roles: ["buyer", "receiver", "auditor"],
            },
        ],
    },
    {
        label: "Paying",
        destinations: [
            {
                route: "invoices",
                label: "Invoices",
                icon: "invoice",
                roles: ["ap-clerk", "ap-approver", "treasurer", "auditor"],
            },
            {
                route: "invoice-exceptions",
                label: "Exceptions",
                icon: "exception",
                roles: ["ap-approver"],
            },
            {
                route: "payment-runs",
                label: "Payment runs",
                icon: "payment",
                roles: ["treasurer", "auditor"],
            },
        ],
    },
    {
        label: "Master data",
        destinations: [
            {
                route: "suppliers",
                label: "Suppliers",
                icon: "supplier",
                roles: ["supplier-admin", "supplier-approver", "auditor"],
            },
            {
                route: "cost-centres",
                label: "Cost centres",
                icon: "costCentre",
                roles: ["budget-admin", ...approvers, "auditor"],
            },
            {
                route: "budgets",
                label: "Budgets",
                icon: "budget",
                roles: ["budget-admin", ...approvers, "auditor"],
            },
        ],
    },
];

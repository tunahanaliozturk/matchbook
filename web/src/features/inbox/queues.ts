import { defineAsyncComponent, type Component } from "vue";

import type { Role } from "@/auth/roles";

export interface Queue {
    key: string;
    /** Whose work it is. A person sees the queues of every role they hold. */
    roles: readonly Role[];
    component: Component;
}

// The work waiting for someone, one queue per kind of decision, contributed by the feature that owns it. Each queue
// is its own component with its own query, so a slow service delays only its own section of the inbox.
export const queues: readonly Queue[] = [
    {
        key: "suppliers-to-activate",
        roles: ["supplier-approver"],
        component: defineAsyncComponent(() => import("@/features/suppliers/ActivationQueue.vue")),
    },
    {
        key: "bank-accounts-to-review",
        roles: ["supplier-approver"],
        component: defineAsyncComponent(() => import("@/features/suppliers/BankAccountQueue.vue")),
    },
    {
        key: "supplier-drafts",
        roles: ["supplier-admin"],
        component: defineAsyncComponent(() => import("@/features/suppliers/DraftQueue.vue")),
    },
    {
        key: "orders-to-issue",
        roles: ["buyer"],
        component: defineAsyncComponent(() => import("@/features/purchase-orders/IssueQueue.vue")),
    },
    {
        key: "orders-waiting-for-goods",
        roles: ["receiver"],
        component: defineAsyncComponent(
            () => import("@/features/purchase-orders/ReceiveQueue.vue"),
        ),
    },
];

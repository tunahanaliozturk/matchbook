import type { RouteRecordRaw } from "vue-router";

// GET /approvals is behind the policy that decides, not the one that reads: the auditor sees every requisition
// but has no queue.
const deciders = ["approver", "finance-approver", "cfo"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "approvals",
        name: "approvals",
        component: () => import("./ApprovalsView.vue"),
        meta: { title: "Approvals", roles: deciders },
    },
];

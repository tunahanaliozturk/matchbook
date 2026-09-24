import type { RouteRecordRaw } from "vue-router";

// Any signed-in person may read cost centres from the service, since a requester picks one; the page is for the
// people who read budgets, the same as the source list shows it to.
const readers = ["budget-admin", "auditor", "approver", "finance-approver", "cfo"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "cost-centres",
        name: "cost-centres",
        component: () => import("./CostCentresView.vue"),
        meta: { title: "Cost centres", roles: readers },
    },
];

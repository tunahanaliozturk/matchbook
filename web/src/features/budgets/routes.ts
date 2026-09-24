import type { RouteRecordRaw } from "vue-router";

// Budgets are read by their admins, the auditor, and the people who approve spending against them.
const readers = ["budget-admin", "auditor", "approver", "finance-approver", "cfo"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "budgets",
        name: "budgets",
        component: () => import("./BudgetsView.vue"),
        meta: { title: "Budgets", roles: readers },
    },
    {
        path: "budgets/:budgetId",
        name: "budget",
        component: () => import("./BudgetView.vue"),
        props: true,
        meta: { title: "Budget", roles: readers },
    },
];

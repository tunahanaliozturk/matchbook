import type { RouteRecordRaw } from "vue-router";

const readers = ["treasurer", "auditor"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "payment-runs",
        name: "payment-runs",
        component: () => import("./PaymentRunsView.vue"),
        meta: { title: "Payment runs", roles: readers },
    },
    {
        path: "payment-runs/:paymentRunId",
        name: "payment-run",
        component: () => import("./PaymentRunView.vue"),
        props: true,
        meta: { title: "Payment run", roles: readers },
    },
];

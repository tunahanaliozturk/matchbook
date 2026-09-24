import type { RouteRecordRaw } from "vue-router";

// Purchasing's read policy: buyers and receivers work with orders, auditors read them.
const readers = ["buyer", "receiver", "auditor"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "purchase-orders",
        name: "purchase-orders",
        component: () => import("./OrdersView.vue"),
        meta: { title: "Purchase orders", roles: readers },
    },
    {
        path: "purchase-orders/:purchaseOrderId",
        name: "purchase-order",
        component: () => import("./OrderView.vue"),
        props: true,
        meta: { title: "Purchase order", roles: readers },
    },
];

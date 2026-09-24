import type { RouteRecordRaw } from "vue-router";

const readers = ["requester", "approver", "finance-approver", "cfo", "auditor"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "requisitions",
        name: "requisitions",
        component: () => import("./RequisitionsView.vue"),
        meta: { title: "Requisitions", roles: readers },
    },
    {
        path: "requisitions/:requisitionId",
        name: "requisition",
        component: () => import("./RequisitionView.vue"),
        props: true,
        meta: { title: "Requisition", roles: readers },
    },
];

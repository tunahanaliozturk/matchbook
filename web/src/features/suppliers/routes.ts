import type { RouteRecordRaw } from "vue-router";

const readers = ["supplier-admin", "supplier-approver", "auditor"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "suppliers",
        name: "suppliers",
        component: () => import("./SuppliersView.vue"),
        meta: { title: "Suppliers", roles: readers },
    },
    {
        path: "suppliers/:supplierId",
        name: "supplier",
        component: () => import("./SupplierView.vue"),
        props: true,
        meta: { title: "Supplier", roles: readers },
    },
];

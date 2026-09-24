import type { RouteRecordRaw } from "vue-router";

// Payables' invoice read policy. The exceptions page is in the source list for approvers only, but anyone who may
// read invoices may open it.
const readers = ["ap-clerk", "ap-approver", "treasurer", "auditor"] as const;

export const routes: RouteRecordRaw[] = [
    {
        path: "invoices",
        name: "invoices",
        component: () => import("./InvoicesView.vue"),
        meta: { title: "Invoices", roles: readers },
    },
    {
        path: "invoices/exceptions",
        name: "invoice-exceptions",
        component: () => import("./ExceptionsView.vue"),
        meta: { title: "Exceptions", roles: readers },
    },
    {
        path: "invoices/:invoiceId",
        name: "invoice",
        component: () => import("./InvoiceView.vue"),
        props: true,
        meta: { title: "Invoice", roles: readers },
    },
];

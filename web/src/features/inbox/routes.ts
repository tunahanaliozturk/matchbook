import type { RouteRecordRaw } from "vue-router";

// The inbox has its own address rather than the root, so the source list does not show it as the current page on
// every page beneath it.
export const routes: RouteRecordRaw[] = [
    { path: "", redirect: { name: "inbox" } },
    {
        path: "inbox",
        name: "inbox",
        component: () => import("./InboxView.vue"),
        meta: { title: "Inbox" },
    },
];

import { nextTick } from "vue";
import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

import type { Role } from "@/auth/roles";
import { signIn, useSession } from "@/auth/session";
import { routes as featureRoutes } from "@/features/routes";

import AppShell from "./AppShell.vue";

declare module "vue-router" {
    interface RouteMeta {
        /** Reachable without signing in: only the page Keycloak redirects back to. */
        public?: boolean;
        /** The roles whose read policy admits this page. Anyone else sees why instead of a 403 from the service. */
        roles?: readonly Role[];
        title?: string;
    }
}

const routes: RouteRecordRaw[] = [
    {
        path: "/signed-in",
        name: "signed-in",
        component: () => import("./SignedInView.vue"),
        meta: { public: true, title: "Signing in" },
    },
    {
        path: "/",
        component: AppShell,
        children: [
            ...featureRoutes,
            {
                path: ":unknown(.*)*",
                name: "not-found",
                component: () => import("./NotFoundView.vue"),
                meta: { title: "Not found" },
            },
        ],
    },
];

export const router = createRouter({ history: createWebHistory(), routes });

const session = useSession();

router.beforeEach(async (to) => {
    if (to.meta.public || session.signedIn.value) {
        return true;
    }

    // No token in memory: a first visit, or a reload. Keycloak's own session usually answers without a prompt.
    await signIn(to.fullPath);
    return false;
});

router.afterEach(async (to) => {
    document.title = to.meta.title ? `${to.meta.title} - Matchbook` : "Matchbook";

    // A new page is announced by moving focus to its title; otherwise a screen reader stays on the old one.
    await nextTick();
    document.querySelector<HTMLElement>("main h1")?.focus();
});

<script setup lang="ts">
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";

import { roleNames } from "@/auth/roles";
import { signOut, useSession } from "@/auth/session";
import EmptyState from "@/shared/ui/EmptyState.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import UiIcon from "@/shared/ui/UiIcon.vue";

import { sections } from "./navigation";

// The window: a source list on the left with only the places this person's roles open, and the page on the right.
// The list is the one translucent surface, so the content always reads as the thing in front.
const { person, hasAny } = useSession();
const router = useRouter();

// A destination whose feature has no page yet is left out rather than linked, since resolving a link to a route
// that does not exist throws and takes the whole window down with it.
const visible = computed(() =>
    sections
        .map((section) => ({
            ...section,
            destinations: section.destinations.filter(
                (destination) => hasAny(destination.roles) && router.hasRoute(destination.route),
            ),
        }))
        .filter((section) => section.destinations.length > 0),
);

const roleList = computed(() =>
    person.value ? [...person.value.roles].map((role) => roleNames[role]).join(", ") : "",
);

// A page whose service would answer 403 says so here, with the reason, before any request is made.
const route = useRoute();
const allowed = computed(() => !route.meta.roles || hasAny(route.meta.roles));
</script>

<template>
    <div class="window">
        <a class="skip" href="#content">Skip to content</a>
        <nav class="sidebar" aria-label="Main">
            <p class="product">Matchbook</p>
            <div v-for="section in visible" :key="section.label ?? 'top'" class="group">
                <h2 v-if="section.label" class="section">{{ section.label }}</h2>
                <ul>
                    <li v-for="destination in section.destinations" :key="destination.route">
                        <RouterLink :to="{ name: destination.route }" class="destination">
                            <UiIcon :name="destination.icon" />
                            <span>{{ destination.label }}</span>
                        </RouterLink>
                    </li>
                </ul>
            </div>
            <div v-if="person" class="account">
                <p class="name">{{ person.name }}</p>
                <p class="roles">{{ roleList }}</p>
                <button type="button" class="sign-out" @click="signOut">Sign out</button>
            </div>
        </nav>
        <main id="content" class="content">
            <div class="page">
                <RouterView v-if="allowed" />
                <template v-else>
                    <PageHeader :title="route.meta.title ?? 'Not available'" />
                    <EmptyState
                        :message="`This page is for ${(route.meta.roles ?? []).map((role) => roleNames[role]).join(', ')}. You are signed in as ${person?.name ?? 'someone'} (${roleList}).`"
                    >
                        <RouterLink :to="{ name: 'inbox' }">Go to your inbox</RouterLink>
                    </EmptyState>
                </template>
            </div>
        </main>
    </div>
</template>

<style scoped>
.window {
    display: grid;
    grid-template-columns: var(--sidebar-width) minmax(0, 1fr);
    min-height: 100vh;
}

.skip {
    position: absolute;
    top: var(--space-2);
    left: var(--space-2);
    z-index: 40;
    padding: var(--space-2) var(--space-3);
    background: var(--content);
    border-radius: var(--radius-control);
    transform: translateY(-200%);
}

.skip:focus {
    transform: none;
}

.sidebar {
    position: sticky;
    top: 0;
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
    height: 100vh;
    padding: var(--space-5) var(--space-3) var(--space-4);
    overflow-y: auto;
    background: var(--sidebar);
    border-right: 0.5px solid var(--separator);
    backdrop-filter: saturate(180%) blur(24px);
    -webkit-backdrop-filter: saturate(180%) blur(24px);
}

@supports not (backdrop-filter: blur(1px)) {
    .sidebar {
        background: var(--sidebar-solid);
    }
}

.product {
    padding: 0 var(--space-2);
    font: var(--text-headline);
}

.group {
    display: grid;
    gap: 2px;
}

.section {
    padding: 0 var(--space-2);
    margin-bottom: 2px;
    font: var(--text-caption);
    font-weight: 600;
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
}

.destination {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    min-height: 1.75rem;
    padding: 0 var(--space-2);
    border-radius: var(--radius-control);
    color: var(--label);
    text-decoration: none;
}

.destination:hover {
    background: var(--hover);
    text-decoration: none;
}

.destination :deep(.icon) {
    color: var(--accent);
}

/* The current place, selected the way a source list selects: a filled row in the accent colour. */
.destination.router-link-active {
    background: var(--accent);
    color: var(--label-on-accent);
}

.destination.router-link-active :deep(.icon) {
    color: var(--label-on-accent);
}

.account {
    display: grid;
    gap: 2px;
    padding: var(--space-3) var(--space-2) 0;
    margin-top: auto;
    border-top: 0.5px solid var(--separator);
}

.name {
    font: var(--text-headline);
}

.roles {
    font: var(--text-caption);
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
}

.sign-out {
    justify-self: start;
    padding: 0;
    margin-top: var(--space-1);
    background: none;
    border: 0;
    font: var(--text-callout);
    color: var(--accent-text);
    cursor: pointer;
}

.content {
    min-width: 0;
    background: var(--window);
}

.page {
    max-width: var(--content-max);
    padding: var(--space-8) var(--space-8) var(--space-10);
}

@media (max-width: 48rem) {
    .window {
        grid-template-columns: 1fr;
    }

    .sidebar {
        position: static;
        height: auto;
    }

    .page {
        padding: var(--space-6) var(--space-4);
    }
}
</style>

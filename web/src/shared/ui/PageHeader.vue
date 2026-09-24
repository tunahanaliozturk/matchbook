<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";

// The large title every page opens with, the way a macOS window names what it shows. The toolbar holds the page's
// actions on the right. Focus lands on the title after every navigation (router.ts), so a screen reader starts here.
defineProps<{
    title: string;
    back?: { to: RouteLocationRaw; label: string };
}>();
</script>

<template>
    <header class="header">
        <RouterLink v-if="back" :to="back.to" class="back">{{ back.label }}</RouterLink>
        <div class="row">
            <div class="titles">
                <h1 tabindex="-1" class="title">{{ title }}</h1>
                <div v-if="$slots.subtitle" class="subtitle"><slot name="subtitle" /></div>
            </div>
            <div v-if="$slots.default" class="toolbar"><slot /></div>
        </div>
    </header>
</template>

<style scoped>
.header {
    display: grid;
    gap: var(--space-1);
    margin-bottom: var(--space-6);
}

.back {
    justify-self: start;
    font: var(--text-callout);
}

.back::before {
    content: "‹ ";
}

.row {
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    gap: var(--space-4);
    flex-wrap: wrap;
}

.titles {
    display: grid;
    gap: var(--space-1);
    min-width: 0;
}

.title {
    font: var(--text-large-title);
    letter-spacing: var(--tracking-large-title);
    outline: none;
}

.subtitle {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    flex-wrap: wrap;
    color: var(--label-secondary);
}

.toolbar {
    display: flex;
    gap: var(--space-2);
    flex-wrap: wrap;
}
</style>

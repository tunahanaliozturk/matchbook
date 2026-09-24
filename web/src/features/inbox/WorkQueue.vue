<script setup lang="ts" generic="T">
import type { RouteLocationRaw } from "vue-router";

import { describe } from "@/shared/api/problem";
import GroupedSection from "@/shared/ui/GroupedSection.vue";

// One section of the inbox: a title saying what the decision is, up to a handful of items, a way to see all of
// them, and a plain sentence when there is nothing to do. Every feature's queue renders through this, so the inbox
// reads as one list of work however many services feed it.
defineProps<{
    title: string;
    items: readonly T[];
    loading: boolean;
    error: unknown;
    empty: string;
    keyOf: (item: T) => string;
    more?: RouteLocationRaw;
}>();
</script>

<template>
    <GroupedSection :title="title">
        <template v-if="more && items.length > 0" #actions>
            <RouterLink :to="more" class="more">Show all</RouterLink>
        </template>
        <p v-if="loading" class="note" role="status">Loading…</p>
        <p v-else-if="error" class="note error" role="alert">{{ describe(error) }}</p>
        <p v-else-if="items.length === 0" class="note">{{ empty }}</p>
        <template v-for="item in items" v-else :key="keyOf(item)">
            <slot :item="item" />
        </template>
    </GroupedSection>
</template>

<style scoped>
.note {
    padding: var(--space-3) var(--space-4);
    color: var(--label-secondary);
}

.error {
    color: var(--negative);
}

.more {
    font: var(--text-callout);
}
</style>

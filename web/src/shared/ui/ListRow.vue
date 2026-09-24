<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";

// One row of a grouped list. With a destination it is a link, and the whole row is the target; without one it is
// a plain row. Leading and trailing slots hold what a person scans: the title and detail on the left, the amount and
// status on the right.
defineProps<{ to?: RouteLocationRaw }>();
</script>

<template>
    <RouterLink v-if="to" :to="to" class="row link">
        <span class="main">
            <span class="title"><slot /></span>
            <span v-if="$slots.detail" class="detail"><slot name="detail" /></span>
        </span>
        <span v-if="$slots.trailing" class="trailing"><slot name="trailing" /></span>
        <span class="chevron" aria-hidden="true" />
    </RouterLink>
    <div v-else class="row">
        <span class="main">
            <span class="title"><slot /></span>
            <span v-if="$slots.detail" class="detail"><slot name="detail" /></span>
        </span>
        <span v-if="$slots.trailing" class="trailing"><slot name="trailing" /></span>
    </div>
</template>

<style scoped>
.row {
    position: relative;
    display: flex;
    align-items: center;
    gap: var(--space-3);
    min-height: 2.75rem;
    padding: var(--space-2) var(--space-4);
    color: var(--label);
}

/* Hairlines between rows, inset from the leading edge as grouped lists draw them. */
.row:not(:first-child)::before {
    content: "";
    position: absolute;
    top: 0;
    right: 0;
    left: var(--space-4);
    height: 0.5px;
    background: var(--separator);
}

.link {
    text-decoration: none;
}

.link:hover {
    background: var(--hover);
    text-decoration: none;
}

.link:active {
    background: var(--pressed);
}

.link:focus-visible {
    outline-offset: -3px;
}

.main {
    display: grid;
    flex: 1;
    min-width: 0;
}

.title {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.detail {
    overflow: hidden;
    font: var(--text-callout);
    color: var(--label-secondary);
    text-overflow: ellipsis;
    white-space: nowrap;
}

.trailing {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    font-variant-numeric: tabular-nums;
}

.chevron {
    width: 0.4375rem;
    height: 0.4375rem;
    border-top: 1.5px solid var(--label-tertiary);
    border-right: 1.5px solid var(--label-tertiary);
    transform: rotate(45deg);
}
</style>

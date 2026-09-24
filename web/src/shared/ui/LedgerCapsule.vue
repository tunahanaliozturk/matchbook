<script setup lang="ts">
import { computed } from "vue";

import { formatMoney } from "@/shared/format";

// A budget the way macOS shows a disk: one capsule, filled from the left by what is spent, then what is promised to
// orders, then what is set aside for requisitions, with what is left as the empty track. The four figures and
// available = allotted - reserved - committed - actual are the design's own (docs/design.md, "Budgets"). An overspend
// pushes past the end of the track and turns that part red, because an overspend is reported, never hidden.
const props = withDefaults(
    defineProps<{
        allotted: number;
        reserved: number;
        committed: number;
        actual: number;
        compact?: boolean;
    }>(),
    { compact: false },
);

const consumed = computed(() => props.actual + props.committed + props.reserved);
const available = computed(() => props.allotted - consumed.value);
const scale = computed(() => Math.max(props.allotted, consumed.value, 1));

const share = (amount: number) => `${(Math.max(amount, 0) / scale.value) * 100}%`;

const segments = computed(() => [
    { key: "actual", label: "Spent", amount: props.actual },
    { key: "committed", label: "Ordered", amount: props.committed },
    { key: "reserved", label: "Requested", amount: props.reserved },
]);

const summary = computed(
    () =>
        `${formatMoney(props.actual)} spent, ${formatMoney(props.committed)} ordered, ` +
        `${formatMoney(props.reserved)} requested of ${formatMoney(props.allotted)}; ` +
        (available.value >= 0
            ? `${formatMoney(available.value)} available.`
            : `${formatMoney(-available.value)} over budget.`),
);
</script>

<template>
    <figure class="ledger" :class="{ compact }">
        <div class="capsule" role="img" :aria-label="summary">
            <span
                v-for="segment in segments"
                :key="segment.key"
                class="segment"
                :class="segment.key"
                :style="{ width: share(segment.amount) }"
            />
            <span
                v-if="available < 0"
                class="allotment-edge"
                :style="{ left: share(allotted) }"
                aria-hidden="true"
            />
        </div>
        <figcaption v-if="!compact" class="legend">
            <span v-for="segment in segments" :key="segment.key" class="entry">
                <span class="swatch" :class="segment.key" aria-hidden="true" />
                <span class="name">{{ segment.label }}</span>
                <span class="amount">{{ formatMoney(segment.amount) }}</span>
            </span>
            <span class="entry">
                <span class="swatch track" aria-hidden="true" />
                <span class="name">{{ available >= 0 ? "Available" : "Over budget" }}</span>
                <span class="amount" :class="{ over: available < 0 }">{{
                    formatMoney(Math.abs(available))
                }}</span>
            </span>
        </figcaption>
    </figure>
</template>

<style scoped>
.ledger {
    display: grid;
    gap: var(--space-3);
}

.capsule {
    position: relative;
    display: flex;
    height: 0.75rem;
    overflow: hidden;
    background: var(--ledger-track);
    border-radius: var(--radius-capsule);
}

.compact .capsule {
    height: 0.375rem;
}

.segment {
    height: 100%;
    transition: width var(--duration) var(--ease);
}

/* A hairline of the track between segments, so adjacent colours stay distinguishable without relying on hue. */
.segment + .segment {
    box-shadow: inset 1.5px 0 0 var(--grouped);
}

.actual {
    background: var(--ledger-actual);
}

.committed {
    background: var(--ledger-committed);
}

.reserved {
    background: var(--ledger-reserved);
}

.allotment-edge {
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    background: repeating-linear-gradient(
        -45deg,
        var(--ledger-over) 0 3px,
        color-mix(in srgb, var(--ledger-over), transparent 45%) 3px 6px
    );
}

.legend {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2) var(--space-6);
    font: var(--text-callout);
}

.entry {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
}

.swatch {
    width: 0.625rem;
    height: 0.625rem;
    border-radius: 3px;
}

.swatch.track {
    background: var(--ledger-track);
}

.name {
    color: var(--label-secondary);
}

.amount {
    font-variant-numeric: tabular-nums;
    font-weight: 500;
}

.amount.over {
    color: var(--negative);
}
</style>

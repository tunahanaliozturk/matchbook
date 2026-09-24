<script setup lang="ts">
// A label and its value on one line, the way a settings pane lists facts. Used inside a GroupedSection whose
// content is a <dl>, so assistive technology reads term and definition as a pair.
defineProps<{ label: string }>();
</script>

<template>
    <div class="pair">
        <dt class="label">{{ label }}</dt>
        <dd class="value"><slot /></dd>
    </div>
</template>

<style scoped>
.pair {
    position: relative;
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--space-4);
    min-height: 2.75rem;
    padding: var(--space-3) var(--space-4);
}

.pair:not(:first-child)::before {
    content: "";
    position: absolute;
    top: 0;
    right: 0;
    left: var(--space-4);
    height: 0.5px;
    background: var(--separator);
}

.label {
    flex-shrink: 0;
    color: var(--label);
}

.value {
    min-width: 0;
    color: var(--label-secondary);
    text-align: right;
    overflow-wrap: anywhere;
    font-variant-numeric: tabular-nums;
}
</style>

<script setup lang="ts" generic="T extends string">
import { ToggleGroupItem, ToggleGroupRoot } from "reka-ui";

// The platform's way to switch between a few views of one list, a status filter above invoices for instance. One
// segment is always selected; choosing the selected one again does nothing rather than clearing the filter.
const model = defineModel<T>({ required: true });

defineProps<{ label: string; options: readonly { value: T; label: string }[] }>();

const choose = (value: unknown) => {
    if (typeof value === "string" && value !== "") {
        model.value = value as T;
    }
};
</script>

<template>
    <ToggleGroupRoot
        :model-value="model"
        type="single"
        class="segments"
        :aria-label="label"
        @update:model-value="choose"
    >
        <ToggleGroupItem
            v-for="option in options"
            :key="option.value"
            :value="option.value"
            class="segment"
        >
            {{ option.label }}
        </ToggleGroupItem>
    </ToggleGroupRoot>
</template>

<style scoped>
.segments {
    display: inline-flex;
    gap: 2px;
    padding: 2px;
    background: var(--neutral-tint);
    border-radius: calc(var(--radius-control) + 2px);
}

.segment {
    min-height: 1.5rem;
    padding: 0 var(--space-3);
    background: transparent;
    border: 0;
    border-radius: var(--radius-control);
    font: var(--text-callout);
    font-weight: 500;
    color: var(--label);
    cursor: default;
}

.segment[data-state="on"] {
    background: var(--grouped);
    box-shadow:
        var(--shadow-control),
        0 0 0 0.5px var(--separator);
}
</style>

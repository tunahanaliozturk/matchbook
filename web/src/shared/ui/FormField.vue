<script setup lang="ts">
import { computed, useId } from "vue";

// A label, the control it names, an optional hint, and whatever the server said was wrong with the value. The
// control is the default slot and receives the ids to wire up, so the label and the messages are associated with
// it however it is built (input, select, textarea).
const props = defineProps<{ label: string; hint?: string; errors?: readonly string[] }>();

const id = useId();
const hintId = `${id}-hint`;
const errorId = `${id}-error`;

const describedBy = computed(
    () =>
        [props.hint ? hintId : null, props.errors?.length ? errorId : null]
            .filter(Boolean)
            .join(" ") || undefined,
);
const invalid = computed(() => (props.errors?.length ?? 0) > 0);
</script>

<template>
    <div class="field">
        <label :for="id" class="label">{{ label }}</label>
        <slot :id="id" :described-by="describedBy" :invalid="invalid" />
        <p v-if="hint" :id="hintId" class="hint">{{ hint }}</p>
        <p v-if="invalid" :id="errorId" class="error">{{ errors?.join(" ") }}</p>
    </div>
</template>

<style scoped>
.field {
    display: grid;
    gap: var(--space-1);
}

.label {
    font: var(--text-callout);
    font-weight: 500;
}

.hint {
    font: var(--text-caption);
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
}

.error {
    font: var(--text-caption);
    letter-spacing: var(--tracking-caption);
    color: var(--negative);
}

/* Controls placed in the slot share one look. They are styled here, not per page, so every form agrees. */
.field :slotted(input),
.field :slotted(select),
.field :slotted(textarea) {
    width: 100%;
    min-height: var(--control-height);
    padding: var(--space-1) var(--space-2);
    background: var(--grouped);
    border: 0;
    border-radius: var(--radius-control);
    box-shadow: inset 0 0 0 0.5px var(--field-border);
    font: var(--text-body);
}

.field :slotted(textarea) {
    min-height: 4.5rem;
    resize: vertical;
}

.field :slotted([aria-invalid="true"]) {
    box-shadow: inset 0 0 0 1px var(--negative);
}

.field :slotted(input:focus-visible),
.field :slotted(select:focus-visible),
.field :slotted(textarea:focus-visible) {
    outline: 3px solid var(--focus-ring);
    outline-offset: 0;
}
</style>

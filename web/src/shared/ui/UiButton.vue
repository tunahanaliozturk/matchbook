<script setup lang="ts">
// A macOS push button. One primary per surface: the action the person most likely came to take. Actions that need
// more input before they happen end in an ellipsis in their label ("Reject…"), the platform's own convention.
withDefaults(
    defineProps<{
        variant?: "primary" | "secondary" | "destructive" | "plain";
        type?: "button" | "submit";
        busy?: boolean;
        disabled?: boolean;
    }>(),
    { variant: "secondary", type: "button", busy: false, disabled: false },
);
</script>

<template>
    <button
        :type="type"
        class="button"
        :class="variant"
        :disabled="disabled || busy"
        :aria-busy="busy || undefined"
    >
        <span class="label" :class="{ hidden: busy }"><slot /></span>
        <span v-if="busy" class="spinner" aria-hidden="true" />
    </button>
</template>

<style scoped>
.button {
    position: relative;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-height: var(--control-height);
    padding: 0 var(--space-3);
    border: 0;
    border-radius: var(--radius-control);
    font: var(--text-body);
    font-weight: 500;
    white-space: nowrap;
    cursor: default;
    transition:
        background-color var(--duration) var(--ease),
        opacity var(--duration) var(--ease);
}

.primary {
    background: var(--accent);
    color: var(--label-on-accent);
}

.primary:hover:not(:disabled) {
    background: var(--accent-hover);
}

.primary:active:not(:disabled) {
    background: var(--accent-pressed);
}

.secondary {
    background: var(--control);
    color: var(--label);
    box-shadow:
        var(--shadow-control),
        inset 0 0 0 0.5px var(--field-border);
}

.secondary:hover:not(:disabled) {
    background: var(--control-hover);
}

.destructive {
    background: var(--control);
    color: var(--destructive);
    box-shadow:
        var(--shadow-control),
        inset 0 0 0 0.5px var(--field-border);
}

.plain {
    background: transparent;
    color: var(--accent-text);
    padding: 0 var(--space-2);
}

.plain:hover:not(:disabled) {
    background: var(--hover);
}

.button:disabled {
    opacity: 0.45;
}

.hidden {
    visibility: hidden;
}

.spinner {
    position: absolute;
    width: 0.875rem;
    height: 0.875rem;
    border: 2px solid currentcolor;
    border-right-color: transparent;
    border-radius: 50%;
    animation: turn 0.8s linear infinite;
}

@keyframes turn {
    to {
        transform: rotate(360deg);
    }
}
</style>

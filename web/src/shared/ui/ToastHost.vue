<script setup lang="ts">
import { useToasts } from "./toasts";

// A live region that is always in the document, so a screen reader hears each confirmation as it is added. Nothing
// in it takes focus: a confirmation is news, not a question.
const { toasts } = useToasts();
</script>

<template>
    <div class="viewport" role="status" aria-live="polite">
        <TransitionGroup name="toast">
            <p v-for="toast in toasts" :key="toast.id" class="toast">{{ toast.message }}</p>
        </TransitionGroup>
    </div>
</template>

<style scoped>
.viewport {
    position: fixed;
    bottom: var(--space-6);
    left: 50%;
    z-index: 30;
    display: grid;
    justify-items: center;
    gap: var(--space-2);
    transform: translateX(-50%);
    pointer-events: none;
}

.toast {
    padding: var(--space-2) var(--space-4);
    background: var(--label);
    color: var(--window);
    border-radius: var(--radius-capsule);
    font: var(--text-callout);
    font-weight: 500;
    white-space: nowrap;
}

.toast-enter-active,
.toast-leave-active {
    transition:
        opacity var(--duration) var(--ease),
        transform var(--duration) var(--ease);
}

.toast-enter-from,
.toast-leave-to {
    opacity: 0;
    transform: translateY(0.5rem);
}
</style>

<script setup lang="ts">
import {
    DialogContent,
    DialogDescription,
    DialogOverlay,
    DialogPortal,
    DialogRoot,
    DialogTitle,
} from "reka-ui";

// A macOS sheet: a modal that drops from the top of the window for one task, a form or a decision. Reka's dialog
// does the parts that are easy to get wrong: focus moves in, stays in, returns to the trigger, and Escape closes.
// The footer holds the actions, primary last, as the platform orders them.
const open = defineModel<boolean>("open", { required: true });

defineProps<{ title: string; description?: string; wide?: boolean }>();
</script>

<template>
    <DialogRoot v-model:open="open">
        <DialogPortal>
            <DialogOverlay class="scrim" />
            <DialogContent class="sheet" :class="{ wide }">
                <DialogTitle class="title">{{ title }}</DialogTitle>
                <DialogDescription v-if="description" class="description">{{
                    description
                }}</DialogDescription>
                <div class="body"><slot /></div>
                <div v-if="$slots.footer" class="footer"><slot name="footer" /></div>
            </DialogContent>
        </DialogPortal>
    </DialogRoot>
</template>

<style scoped>
.scrim {
    position: fixed;
    inset: 0;
    z-index: 20;
    background: var(--scrim);
    animation: fade var(--duration) var(--ease);
}

.sheet {
    position: fixed;
    top: var(--space-10);
    left: 50%;
    z-index: 21;
    display: grid;
    gap: var(--space-4);
    width: min(32rem, calc(100vw - 2 * var(--space-4)));
    max-height: calc(100vh - 2 * var(--space-10));
    padding: var(--space-6);
    overflow: auto;
    background: var(--content);
    border-radius: var(--radius-sheet);
    box-shadow: var(--shadow-sheet);
    transform: translateX(-50%);
    animation: drop var(--duration) var(--ease);
}

.sheet.wide {
    width: min(48rem, calc(100vw - 2 * var(--space-4)));
}

.sheet:focus-visible {
    outline: none;
}

.title {
    font: var(--text-title);
    letter-spacing: var(--tracking-title);
}

.description {
    margin-top: calc(-1 * var(--space-2));
    color: var(--label-secondary);
}

.body {
    display: grid;
    gap: var(--space-4);
}

.footer {
    display: flex;
    justify-content: flex-end;
    gap: var(--space-2);
    padding-top: var(--space-2);
}

@keyframes fade {
    from {
        opacity: 0;
    }
}

@keyframes drop {
    from {
        opacity: 0;
        transform: translate(-50%, -0.75rem);
    }
}
</style>

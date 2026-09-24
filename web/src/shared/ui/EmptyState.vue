<script setup lang="ts">
// What a list says when it has nothing: one plain sentence about why, and the action that would change that.
defineProps<{ message: string }>();
</script>

<template>
    <div class="empty" role="status">
        <p class="message">{{ message }}</p>
        <div v-if="$slots.default" class="action"><slot /></div>
    </div>
</template>

<style scoped>
.empty {
    display: grid;
    justify-items: center;
    gap: var(--space-3);
    padding: var(--space-10) var(--space-6);
    text-align: center;
}

.message {
    max-width: 32rem;
    color: var(--label-secondary);
}
</style>

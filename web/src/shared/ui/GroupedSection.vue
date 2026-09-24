<script setup lang="ts">
// An inset grouped list: rows on one rounded surface, a small title above, a note below. Grouping is carried by the
// surface and the hairlines between rows, not by shadows or boxes around each item.
defineProps<{ title?: string; footer?: string }>();
</script>

<template>
    <section class="section">
        <div v-if="title || $slots.actions" class="heading">
            <h2 v-if="title" class="title">{{ title }}</h2>
            <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
        </div>
        <div class="group"><slot /></div>
        <p v-if="footer" class="footer">{{ footer }}</p>
    </section>
</template>

<style scoped>
.section {
    display: grid;
    gap: var(--space-2);
}

.section + .section {
    margin-top: var(--space-6);
}

.heading {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--space-4);
    padding: 0 var(--space-4);
}

.title {
    font: var(--text-headline);
    color: var(--label-secondary);
}

.actions {
    display: flex;
    gap: var(--space-1);
}

.group {
    overflow: hidden;
    background: var(--grouped);
    border-radius: var(--radius-group);
    box-shadow: 0 0 0 0.5px var(--separator);
}

.footer {
    padding: 0 var(--space-4);
    font: var(--text-caption);
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
}
</style>

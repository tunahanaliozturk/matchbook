<script setup lang="ts">
// A sentence of context above the content it concerns: why an action is not offered to this person (four eyes,
// separation of duties), or what went wrong loading the page. Caution and error are announced as they appear.
withDefaults(defineProps<{ tone?: "info" | "caution" | "error" }>(), { tone: "info" });
</script>

<template>
    <div class="notice" :class="tone" :role="tone === 'error' ? 'alert' : 'status'">
        <slot />
    </div>
</template>

<style scoped>
.notice {
    padding: var(--space-3) var(--space-4);
    margin-bottom: var(--space-4);
    border-radius: var(--radius-group);
    font: var(--text-callout);
}

.info {
    background: var(--neutral-tint);
    color: var(--label);
}

.caution {
    background: var(--caution-tint);
    color: var(--label);
}

.error {
    background: var(--negative-tint);
    color: var(--label);
}
</style>

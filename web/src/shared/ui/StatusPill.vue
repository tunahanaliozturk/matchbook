<script setup lang="ts">
import { computed } from "vue";

import { humanise } from "@/shared/format";

import type { Tone } from "./tones";

// A document's state, as a tinted capsule. The tone says whether anybody has to act (caution), whether it is
// moving (progress), done well (positive), done badly (negative), or at rest (neutral); the words say which state.
const props = defineProps<{ status: string; tone: Tone }>();

const text = computed(() => humanise(props.status));
</script>

<template>
    <span class="pill" :class="tone">{{ text }}</span>
</template>

<style scoped>
.pill {
    display: inline-flex;
    align-items: center;
    height: 1.375rem;
    padding: 0 var(--space-2);
    border-radius: var(--radius-capsule);
    font: var(--text-caption);
    font-weight: 500;
    letter-spacing: var(--tracking-caption);
    white-space: nowrap;
}

.positive {
    background: var(--positive-tint);
    color: var(--positive);
}

.caution {
    background: var(--caution-tint);
    color: var(--caution);
}

.negative {
    background: var(--negative-tint);
    color: var(--negative);
}

.neutral {
    background: var(--neutral-tint);
    color: var(--neutral);
}

.progress {
    background: var(--progress-tint);
    color: var(--progress);
}
</style>

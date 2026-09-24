<script setup lang="ts">
import { computed } from "vue";

import { paths, type IconName } from "./icons";

// Decoration next to a text label, so hidden from assistive technology.

const props = defineProps<{ name: IconName }>();

const d = computed(() => paths[props.name]);
</script>

<template>
    <svg class="icon" viewBox="0 0 20 20" aria-hidden="true" focusable="false">
        <path :d="d" />
    </svg>
</template>

<style scoped>
.icon {
    width: 1.125rem;
    height: 1.125rem;
    flex-shrink: 0;
    fill: none;
    stroke: currentcolor;
    stroke-width: 1.5;
    stroke-linecap: round;
    stroke-linejoin: round;
}
</style>

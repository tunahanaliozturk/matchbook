<script setup lang="ts">
import { computed } from "vue";

import { formatMoney } from "@/shared/format";

const props = defineProps<{ amount: number }>();

const text = computed(() => formatMoney(props.amount));
</script>

<template>
    <span class="money">{{ text }}</span>
</template>

<style scoped>
.money {
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
}
</style>

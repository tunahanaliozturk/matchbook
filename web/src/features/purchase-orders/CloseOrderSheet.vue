<script setup lang="ts">
import { computed } from "vue";

import { describe } from "@/shared/api/problem";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";

import type { Closing } from "./orders";

// Ending an order early cannot be undone, so it is confirmed in a sheet that says what follows: Budgets lets go of
// the money either way, and a short-close still pays for what already arrived.
const props = defineProps<{ closing: Closing | null; busy: boolean; error: unknown }>();
const emit = defineEmits<{ dismiss: []; confirm: [kind: Closing] }>();

const texts = {
    cancel: {
        title: "Cancel order",
        description:
            "Nothing is bought from the supplier, and Budgets releases what it holds for this order. A cancelled order cannot be reopened.",
        action: "Cancel order",
        keep: "Keep order",
    },
    "short-close": {
        title: "Short-close order",
        description:
            "Nothing more will be received. Invoices for what already arrived are still paid, and Budgets releases the rest of the commitment.",
        action: "Short-close",
        keep: "Cancel",
    },
} as const;

const text = computed(() => texts[props.closing ?? "cancel"]);
</script>

<template>
    <UiSheet
        :open="closing !== null"
        :title="text.title"
        :description="text.description"
        @update:open="(open) => !open && emit('dismiss')"
    >
        <InlineNotice v-if="error" tone="error">{{ describe(error) }}</InlineNotice>
        <template #footer>
            <UiButton @click="emit('dismiss')">{{ text.keep }}</UiButton>
            <UiButton
                variant="destructive"
                :busy="busy"
                @click="closing && emit('confirm', closing)"
                >{{ text.action }}</UiButton
            >
        </template>
    </UiSheet>
</template>

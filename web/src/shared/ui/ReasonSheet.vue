<script setup lang="ts">
import { ref, watch } from "vue";

import { describe } from "@/shared/api/problem";

import FormField from "./FormField.vue";
import InlineNotice from "./InlineNotice.vue";
import UiButton from "./UiButton.vue";
import UiSheet from "./UiSheet.vue";

// A decision that needs its reason written down: rejecting, blocking, accepting a variance. The reason is required
// by the service, and the person who reads it later is the one it has to make sense to.
const open = defineModel<boolean>("open", { required: true });

const props = defineProps<{
    title: string;
    description: string;
    label: string;
    action: string;
    destructive?: boolean;
    busy: boolean;
    error: unknown;
}>();

const emit = defineEmits<{ confirm: [reason: string] }>();

const reason = ref("");

watch(open, (isOpen) => {
    if (isOpen) {
        reason.value = "";
    }
});

const submit = () => {
    if (reason.value.trim() !== "") {
        emit("confirm", reason.value.trim());
    }
};
</script>

<template>
    <UiSheet v-model:open="open" :title="title" :description="description">
        <form id="reason-form" class="form" @submit.prevent="submit">
            <InlineNotice v-if="props.error" tone="error">{{ describe(props.error) }}</InlineNotice>
            <FormField v-slot="{ id, describedBy }" :label="label">
                <textarea
                    :id="id"
                    v-model="reason"
                    :aria-describedby="describedBy"
                    required
                    maxlength="500"
                />
            </FormField>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="reason-form"
                :variant="destructive ? 'destructive' : 'primary'"
                :busy="busy"
                :disabled="reason.trim() === ''"
            >
                {{ action }}
            </UiButton>
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}
</style>

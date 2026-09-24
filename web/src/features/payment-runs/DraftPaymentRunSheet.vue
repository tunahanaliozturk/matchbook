<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRouter } from "vue-router";

import { ApiProblem, describe } from "@/shared/api/problem";
import { isoDay } from "@/shared/format";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useDraftPaymentRun } from "./data";

// Drafts a run of everything payable and due by the execution date. The id is chosen when the sheet opens, so
// pressing Draft twice, or again after a timeout, drafts one run.
const open = defineModel<boolean>("open", { required: true });

const router = useRouter();
const { confirm } = useToasts();
const draft = useDraftPaymentRun();
const executionDate = ref("");
const id = ref(uuidv7());
// Execution dates are calendar days in UTC, like every date the services keep.
const today = computed(() => isoDay(new Date()));

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    draft.reset();
    executionDate.value = today.value;
});

const problem = computed(() => draft.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];

async function save() {
    const drafted = await draft.mutateAsync({
        id: id.value,
        executionDate: executionDate.value === "" ? null : executionDate.value,
    });
    confirm("Payment run drafted");
    open.value = false;
    await router.push({ name: "payment-run", params: { paymentRunId: drafted.id } });
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        title="Draft payment run"
        description="The run takes every payable invoice due by the execution date, for suppliers that are active with a verified bank account. Another treasurer releases it."
    >
        <form
            id="payment-run-form"
            class="form"
            novalidate
            @submit.prevent="save().catch(() => undefined)"
        >
            <InlineNotice
                v-if="problem && !(problem instanceof ApiProblem && problem.status === 400)"
                tone="error"
            >
                {{ describe(problem) }}
            </InlineNotice>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="Execution date"
                hint="The day the bank pays. Today or later."
                :errors="errorsFor('executionDate')"
            >
                <input
                    :id="fieldId"
                    v-model="executionDate"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    type="date"
                    :min="today"
                    required
                />
            </FormField>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="payment-run-form"
                variant="primary"
                :busy="draft.isPending.value"
                >Draft</UiButton
            >
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}
</style>

<script setup lang="ts">
import { computed, reactive, watch } from "vue";

import { ApiProblem, describe } from "@/shared/api/problem";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useAmendLine, type OrderLineView } from "./data";
import { asNumber } from "./orders";

// Changes what a draft buys on one line. The supplier and the description came from the approved requisition and
// stay as they are; the service recomputes the line's amount with the one rounding rule (ADR 0006).
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ orderId: string; line: OrderLineView | null }>();

const { confirm } = useToasts();
const amend = useAmendLine(() => props.orderId);
const form = reactive<{ quantity: number | string; unitPrice: number | string }>({
    quantity: 0,
    unitPrice: 0,
});

watch(open, (isOpen) => {
    if (!isOpen || !props.line) return;
    amend.reset();
    Object.assign(form, { quantity: props.line.quantity, unitPrice: props.line.unitPrice });
});

const problem = computed(() => amend.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];

async function save() {
    if (!props.line) return;
    // An emptied field goes as null, which the service names as missing, rather than as a zero nobody typed.
    await amend.mutateAsync({
        lineNumber: props.line.lineNumber,
        quantity: asNumber(form.quantity),
        unitPrice: asNumber(form.unitPrice),
    });
    confirm(`Line ${props.line.lineNumber} changed`);
    open.value = false;
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        :title="`Change line ${line?.lineNumber ?? ''}`"
        :description="line?.description"
    >
        <form
            id="line-form"
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
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    :label="`Quantity (${line?.unitOfMeasure ?? ''})`"
                    hint="Up to three decimal places. Zero keeps the line but buys none."
                    :errors="errorsFor('quantity')"
                >
                    <input
                        :id="fieldId"
                        v-model.number="form.quantity"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="number"
                        min="0"
                        step="0.001"
                        inputmode="decimal"
                        required
                    />
                </FormField>
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Unit price (EUR)"
                    hint="Up to four decimal places."
                    :errors="errorsFor('unitPrice')"
                >
                    <input
                        :id="fieldId"
                        v-model.number="form.unitPrice"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="number"
                        min="0"
                        step="0.0001"
                        inputmode="decimal"
                        required
                    />
                </FormField>
            </div>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton type="submit" form="line-form" variant="primary" :busy="amend.isPending.value"
                >Save</UiButton
            >
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}

.pair {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-4);
}
</style>

<script setup lang="ts">
import { computed, ref, watch } from "vue";

import { describe } from "@/shared/api/problem";
import { formatQuantity } from "@/shared/format";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useRecordReceipt, type PurchaseOrderView } from "./data";
import { asNumber, outstanding, receiptProblem } from "./orders";

// Records one delivery. Each line starts at what is still to arrive, since a delivery usually completes the order,
// and the receiver lowers what did not come. The receipt id is chosen when the sheet opens, so pressing Record
// twice, or again after a timeout, records the delivery once.
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ order: PurchaseOrderView }>();

const { confirm } = useToasts();
const record = useRecordReceipt(() => props.order.id);
const id = ref(uuidv7());
const entered = ref<Record<number, number | string>>({});

// A line received in full has nothing left to record against it.
const remaining = computed(() => props.order.lines.filter((line) => outstanding(line) > 0));

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    record.reset();
    entered.value = Object.fromEntries(
        remaining.value.map((line) => [line.lineNumber, outstanding(line)]),
    );
});

const problems = computed(() =>
    Object.fromEntries(
        remaining.value.map((line) => [
            line.lineNumber,
            receiptProblem(line, asNumber(entered.value[line.lineNumber])),
        ]),
    ),
);

const receiptLines = computed(() =>
    remaining.value
        .map((line) => ({
            lineNumber: line.lineNumber,
            quantity: asNumber(entered.value[line.lineNumber]) ?? 0,
        }))
        .filter((line) => line.quantity > 0),
);

const ready = computed(
    () =>
        receiptLines.value.length > 0 &&
        Object.values(problems.value).every((problem) => problem === null),
);

async function save() {
    if (!ready.value) return;
    await record.mutateAsync({ id: id.value, lines: receiptLines.value });
    confirm("Receipt recorded");
    open.value = false;
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        :title="`Record receipt for ${order.number}`"
        description="What arrived, line by line, up to what is still to arrive. Enter 0 for a line that did not come. The buyer who issued an order cannot record its receipts."
    >
        <form
            id="receipt-form"
            class="form"
            novalidate
            @submit.prevent="save().catch(() => undefined)"
        >
            <InlineNotice v-if="record.error.value" tone="error">{{
                describe(record.error.value)
            }}</InlineNotice>
            <FormField
                v-for="line in remaining"
                :key="line.lineNumber"
                v-slot="{ id: fieldId, describedBy, invalid }"
                :label="`Line ${line.lineNumber}, ${line.description} (${line.unitOfMeasure})`"
                :hint="`Ordered ${formatQuantity(line.quantity)}, ${formatQuantity(outstanding(line))} still to arrive.`"
                :errors="problems[line.lineNumber] ? [problems[line.lineNumber]!] : []"
            >
                <input
                    :id="fieldId"
                    v-model.number="entered[line.lineNumber]"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    type="number"
                    min="0"
                    :max="outstanding(line)"
                    step="0.001"
                    inputmode="decimal"
                    required
                />
            </FormField>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="receipt-form"
                variant="primary"
                :disabled="!ready"
                :busy="record.isPending.value"
                >Record</UiButton
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

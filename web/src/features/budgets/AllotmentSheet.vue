<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";

import { ApiProblem, describe } from "@/shared/api/problem";
import { formatMoney } from "@/shared/format";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import SegmentedControl from "@/shared/ui/SegmentedControl.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useChangeAllotment, type BudgetView } from "./data";

// Raises or lowers an allotment by an amount, not to a new total, so two admins raising the same budget at once
// both get their raise (docs/services/budgets.md). Lowering stops at what is already consumed; the service checks
// that under the budget's row lock and its refusal is shown as it words it. The change's id is chosen when the sheet
// opens, so a retry after a timeout applies it once.
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ budget: BudgetView }>();

type Direction = "raise" | "lower";

const directions: readonly { value: Direction; label: string }[] = [
    { value: "raise", label: "Raise" },
    { value: "lower", label: "Lower" },
];

const { confirm } = useToasts();
const change = useChangeAllotment(() => props.budget.id);
const form = reactive({ direction: "raise" as Direction, amount: null as number | null });
const id = ref(uuidv7());

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    change.reset();
    Object.assign(form, { direction: "raise", amount: null });
});

// An empty field, or one emptied to "", is no change: the service says so, rather than the client refusing to send.
const signed = computed(() => (Number(form.amount) || 0) * (form.direction === "raise" ? 1 : -1));

const problem = computed(() => change.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];

async function save() {
    await change.mutateAsync({ id: id.value, change: signed.value });
    confirm(form.direction === "raise" ? "Allotment raised" : "Allotment lowered");
    open.value = false;
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        title="Change allotment"
        :description="`${budget.costCentreCode} ${budget.fiscalYear} has ${formatMoney(budget.allotted)} allotted. The change is written to the ledger with your name.`"
    >
        <form
            id="allotment-form"
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
            <SegmentedControl
                v-model="form.direction"
                class="direction"
                label="Change"
                :options="directions"
            />
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                :label="form.direction === 'raise' ? 'Raise by' : 'Lower by'"
                :hint="
                    form.direction === 'raise'
                        ? undefined
                        : `At most ${formatMoney(Math.max(budget.available, 0))}: what is requested, ordered and spent stays covered.`
                "
                :errors="errorsFor('change')"
            >
                <input
                    :id="fieldId"
                    v-model.number="form.amount"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    type="number"
                    min="0"
                    step="0.01"
                    inputmode="decimal"
                    required
                />
            </FormField>
            <p class="outcome" aria-live="polite">
                The allotment becomes
                <strong>{{ formatMoney(budget.allotted + signed) }}</strong
                >, leaving <strong>{{ formatMoney(budget.available + signed) }}</strong> available.
            </p>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="allotment-form"
                variant="primary"
                :busy="change.isPending.value"
            >
                {{ form.direction === "raise" ? "Raise allotment" : "Lower allotment" }}
            </UiButton>
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}

/* Its own width, not the form's: a control this small stretched across a sheet reads as a tab bar. */
.direction {
    justify-self: start;
}

.outcome {
    font: var(--text-callout);
    color: var(--label-secondary);
}

.outcome strong {
    font-weight: 600;
    font-variant-numeric: tabular-nums;
    color: var(--label);
}
</style>

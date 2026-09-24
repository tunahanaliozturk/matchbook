<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { useRouter } from "vue-router";

import { ApiProblem, describe } from "@/shared/api/problem";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useCostCentreChoices, useOpenBudget } from "./data";

// Opens a cost centre's budget for a year. There is one per cost centre and year; a second is refused with the
// service's sentence saying to change the allotment instead. The id is chosen when the sheet opens, so pressing
// Open twice opens one budget.
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ fiscalYear: number }>();

const router = useRouter();
const { confirm } = useToasts();
const create = useOpenBudget();
const choices = useCostCentreChoices();

const form = reactive({
    costCentreCode: "",
    fiscalYear: props.fiscalYear,
    allotted: null as number | null,
});
const id = ref(uuidv7());

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    create.reset();
    Object.assign(form, { costCentreCode: "", fiscalYear: props.fiscalYear, allotted: null });
});

const problem = computed(() => create.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];

async function save() {
    const opened = await create.mutateAsync({
        id: id.value,
        costCentreCode: form.costCentreCode,
        fiscalYear: Number(form.fiscalYear) || 0,
        allotted: Number(form.allotted) || 0,
    });
    confirm("Budget opened");
    open.value = false;
    await router.push({ name: "budget", params: { budgetId: opened.id } });
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        title="Open budget"
        description="Requisitions on the cost centre can set money aside from the budget once it is open. The allotment can be raised or lowered later."
    >
        <form
            id="open-budget-form"
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
            <InlineNotice v-if="choices.isError.value" tone="error">{{
                describe(choices.error.value)
            }}</InlineNotice>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="Cost centre"
                hint="Active cost centres only."
                :errors="errorsFor('costCentreCode')"
            >
                <select
                    :id="fieldId"
                    v-model="form.costCentreCode"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    required
                >
                    <option value="" disabled>
                        {{ choices.isPending.value ? "Loading…" : "Choose a cost centre" }}
                    </option>
                    <option
                        v-for="costCentre in choices.data.value ?? []"
                        :key="costCentre.code"
                        :value="costCentre.code"
                    >
                        {{ costCentre.code }}, {{ costCentre.name }}
                    </option>
                </select>
            </FormField>
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Fiscal year"
                    hint="The calendar year, in UTC."
                    :errors="errorsFor('fiscalYear')"
                >
                    <input
                        :id="fieldId"
                        v-model.number="form.fiscalYear"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="number"
                        min="2000"
                        max="2100"
                        required
                    />
                </FormField>
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Allotted (EUR)"
                    :errors="errorsFor('allotted')"
                >
                    <input
                        :id="fieldId"
                        v-model.number="form.allotted"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="number"
                        min="0"
                        step="0.01"
                        inputmode="decimal"
                        required
                    />
                </FormField>
            </div>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="open-budget-form"
                variant="primary"
                :busy="create.isPending.value"
                >Open</UiButton
            >
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}

/* Top-aligned, so the field with a hint does not stretch its neighbour's input to the same height. */
.pair {
    display: grid;
    grid-template-columns: 1fr 1fr;
    align-items: start;
    gap: var(--space-4);
}
</style>

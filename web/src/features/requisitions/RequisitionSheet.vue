<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { useRouter } from "vue-router";

import { ApiProblem, describe } from "@/shared/api/problem";
import { formatMoney, isoDay } from "@/shared/format";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import {
    useCostCentreOptions,
    useCreateRequisition,
    useEditRequisition,
    useSupplierOptions,
    type RequisitionView,
} from "./data";
import { estimatedTotal, lineAmount, type LineDraft } from "./lines";
import { routeFor } from "./rules";

// Drafts a requisition, or rewrites a draft. The id for a new one is chosen when the sheet opens, so pressing Create
// twice, or again after a timeout, makes one requisition.
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ requisition?: RequisitionView }>();

const maxLines = 50;

const router = useRouter();
const { confirm } = useToasts();
const create = useCreateRequisition();
const edit = useEditRequisition(() => props.requisition?.id ?? "");
const mutation = computed(() => (props.requisition ? edit : create));
const costCentres = useCostCentreOptions(open);
const suppliers = useSupplierOptions(open);

type Line = LineDraft & { key: number };

let nextKey = 0;
const blankLine = (): Line => ({
    key: nextKey++,
    description: "",
    quantity: 1,
    unitOfMeasure: "EA",
    unitPrice: "",
});

const form = reactive({ costCentreCode: "", supplierId: "", justification: "", neededBy: "" });
const lines = ref<Line[]>([]);
const id = ref(uuidv7());
const attempted = ref(false);

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    attempted.value = false;
    create.reset();
    edit.reset();
    Object.assign(form, {
        costCentreCode: props.requisition?.costCentreCode ?? "",
        supplierId: props.requisition?.supplierId ?? "",
        justification: props.requisition?.justification ?? "",
        neededBy: props.requisition?.neededBy ?? "",
    });
    lines.value = props.requisition
        ? props.requisition.lines.map((line) => ({
              key: nextKey++,
              description: line.description,
              quantity: line.quantity,
              unitOfMeasure: line.unitOfMeasure,
              unitPrice: line.unitPrice,
          }))
        : [blankLine()];
});

const today = isoDay(new Date());
const total = computed(() => estimatedTotal(lines.value));

// The amount decides the route, so the form says whose desk it will cross before anyone submits it.
const approvers = {
    Manager: "the cost centre's manager",
    Finance: "a finance approver",
    Cfo: "the CFO",
};
const route = computed(() =>
    routeFor(total.value)
        .map((kind) => approvers[kind])
        .join(", then "),
);

// The generated client checks a body against the contract before sending it, and a missing supplier or date would
// fail that check with a message about the service. These three are said here instead, next to their fields.
const unchosen = computed<Record<string, string>>(() => ({
    ...(form.costCentreCode ? {} : { costCentreCode: "Choose a cost centre." }),
    ...(form.supplierId ? {} : { supplierId: "Choose a supplier." }),
    ...(form.neededBy ? {} : { neededBy: "Choose the date it is needed by." }),
}));

const problem = computed(() => mutation.value.error.value);
const lookupProblem = computed(() => costCentres.error.value ?? suppliers.error.value);
const errorsFor = (field: string): readonly string[] => {
    const local = attempted.value ? unchosen.value[field] : undefined;
    if (local) return [local];
    return problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];
};

async function save() {
    attempted.value = true;
    if (Object.keys(unchosen.value).length > 0) return;

    const details = {
        ...form,
        lines: lines.value.map((line) => ({
            description: line.description,
            quantity: Number(line.quantity),
            unitOfMeasure: line.unitOfMeasure,
            unitPrice: Number(line.unitPrice),
        })),
    };

    if (props.requisition) {
        await edit.mutateAsync(details);
        confirm("Draft saved");
        open.value = false;
        return;
    }

    const created = await create.mutateAsync({ id: id.value, ...details });
    confirm("Requisition saved as a draft");
    open.value = false;
    await router.push({ name: "requisition", params: { requisitionId: created.id } });
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        wide
        :title="requisition ? 'Edit draft' : 'New requisition'"
        :description="
            requisition
                ? 'Only a draft can change. The lines you save replace the ones it has.'
                : 'It starts as a draft only you can see. Submitting it asks Budgets to hold the money, then the approvers decide.'
        "
    >
        <form
            id="requisition-form"
            class="form"
            novalidate
            @submit.prevent="save().catch(() => undefined)"
        >
            <InlineNotice v-if="lookupProblem" tone="error">{{
                describe(lookupProblem)
            }}</InlineNotice>
            <InlineNotice
                v-if="problem && !(problem instanceof ApiProblem && problem.status === 400)"
                tone="error"
            >
                {{ describe(problem) }}
            </InlineNotice>
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Cost centre"
                    hint="Active cost centres you do not manage."
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
                            {{ costCentres.isPending.value ? "Loading…" : "Choose…" }}
                        </option>
                        <option
                            v-for="option in costCentres.data.value ?? []"
                            :key="option.code"
                            :value="option.code"
                        >
                            {{ option.code }}, {{ option.name }}
                        </option>
                    </select>
                </FormField>
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Supplier"
                    hint="Active suppliers. A supplier admin adds one that is missing."
                    :errors="errorsFor('supplierId')"
                >
                    <select
                        :id="fieldId"
                        v-model="form.supplierId"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        required
                    >
                        <option value="" disabled>
                            {{ suppliers.isPending.value ? "Loading…" : "Choose…" }}
                        </option>
                        <option
                            v-for="option in suppliers.data.value ?? []"
                            :key="option.id"
                            :value="option.id"
                        >
                            {{ option.legalName }}
                        </option>
                    </select>
                </FormField>
            </div>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="Justification"
                hint="What it is for, for the people who approve it."
                :errors="errorsFor('justification')"
            >
                <textarea
                    :id="fieldId"
                    v-model="form.justification"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    maxlength="2000"
                    required
                />
            </FormField>
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Needed by"
                    :errors="errorsFor('neededBy')"
                >
                    <input
                        :id="fieldId"
                        v-model="form.neededBy"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="date"
                        :min="today"
                        required
                    />
                </FormField>
            </div>

            <fieldset class="lines">
                <legend class="legend">Lines</legend>
                <table class="table">
                    <thead>
                        <tr>
                            <th scope="col">Description</th>
                            <th scope="col" class="numeric quantity">Quantity</th>
                            <th scope="col" class="unit">Unit</th>
                            <th scope="col" class="numeric price">Unit price</th>
                            <th scope="col" class="numeric amount">Amount</th>
                            <th scope="col" class="remove">
                                <span class="visually-hidden">Remove</span>
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="(line, index) in lines" :key="line.key">
                            <td>
                                <input
                                    v-model="line.description"
                                    class="cell"
                                    :aria-label="`Line ${index + 1} description`"
                                    maxlength="500"
                                    required
                                />
                            </td>
                            <td>
                                <input
                                    v-model.number="line.quantity"
                                    class="cell numeric"
                                    :aria-label="`Line ${index + 1} quantity`"
                                    type="number"
                                    min="0"
                                    step="any"
                                    inputmode="decimal"
                                    required
                                />
                            </td>
                            <td>
                                <input
                                    v-model="line.unitOfMeasure"
                                    class="cell"
                                    :aria-label="`Line ${index + 1} unit of measure`"
                                    maxlength="16"
                                    required
                                />
                            </td>
                            <td>
                                <input
                                    v-model.number="line.unitPrice"
                                    class="cell numeric"
                                    :aria-label="`Line ${index + 1} unit price`"
                                    type="number"
                                    min="0"
                                    step="any"
                                    inputmode="decimal"
                                    required
                                />
                            </td>
                            <td class="numeric">
                                <MoneyText :amount="lineAmount(line.quantity, line.unitPrice)" />
                            </td>
                            <td class="remove">
                                <UiButton
                                    variant="plain"
                                    :aria-label="`Remove line ${index + 1}`"
                                    :disabled="lines.length === 1"
                                    @click="lines.splice(index, 1)"
                                    >Remove</UiButton
                                >
                            </td>
                        </tr>
                    </tbody>
                    <tfoot>
                        <tr>
                            <td colspan="3">
                                <UiButton
                                    variant="plain"
                                    :disabled="lines.length >= maxLines"
                                    @click="lines.push(blankLine())"
                                    >Add line</UiButton
                                >
                            </td>
                            <th scope="row" class="numeric">Estimated total</th>
                            <td class="numeric total"><MoneyText :amount="total" /></td>
                            <td />
                        </tr>
                    </tfoot>
                </table>
                <p class="note">
                    Unit prices are estimates, in euros, on one to {{ maxLines }} lines. At
                    {{ formatMoney(total) }} it is approved by {{ route }}.
                </p>
            </fieldset>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="requisition-form"
                variant="primary"
                :busy="mutation.isPending.value"
            >
                {{ requisition ? "Save" : "Create" }}
            </UiButton>
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

.lines {
    display: grid;
    gap: var(--space-2);
    min-width: 0;
    padding: 0;
    margin: 0;
    border: 0;
}

.legend {
    padding: 0;
    margin-bottom: var(--space-1);
    font: var(--text-callout);
    font-weight: 500;
}

.table {
    width: 100%;
    border-collapse: collapse;
    table-layout: fixed;
}

th,
td {
    padding: var(--space-1);
    text-align: left;
    vertical-align: middle;
}

th:first-child,
td:first-child {
    padding-left: 0;
}

th:last-child,
td:last-child {
    padding-right: 0;
}

thead th {
    font: var(--text-caption);
    font-weight: 600;
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
    white-space: nowrap;
}

tfoot th,
tfoot td {
    padding-top: var(--space-2);
    font-weight: 600;
}

.quantity {
    width: 5.5rem;
}

.unit {
    width: 4.5rem;
}

.price {
    width: 7rem;
}

.amount {
    width: 7.5rem;
}

.remove {
    width: 5rem;
    text-align: right;
}

.numeric {
    font-variant-numeric: tabular-nums;
    text-align: right;
    white-space: nowrap;
}

/* The same look FormField gives its controls, for inputs that are cells of a table rather than fields. */
.cell {
    width: 100%;
    min-height: var(--control-height);
    padding: var(--space-1) var(--space-2);
    background: var(--grouped);
    border: 0;
    border-radius: var(--radius-control);
    box-shadow: inset 0 0 0 0.5px var(--field-border);
    font: var(--text-body);
}

.cell:focus-visible {
    outline: 3px solid var(--focus-ring);
    outline-offset: 0;
}

.note {
    font: var(--text-caption);
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
}
</style>

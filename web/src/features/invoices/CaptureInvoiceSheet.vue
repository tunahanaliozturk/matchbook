<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { useRouter } from "vue-router";

import { ApiProblem, describe } from "@/shared/api/problem";
import { formatDay, formatMoney, isoDay } from "@/shared/format";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import type { Column } from "@/shared/ui/table";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import UiTable from "@/shared/ui/UiTable.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useBillableOrders, useBillableSuppliers, useCaptureInvoice } from "./data";
import { lineCents, linesFromOrder, linesTotal, type DraftLine } from "./lines";

// Keys in a supplier's invoice against the order it bills. The lines start as the order has them, since most
// invoices bill their order exactly; the clerk corrects what the paper says differently. The id is chosen when the
// sheet opens, so pressing Capture twice, or again after a timeout, captures one invoice.
const open = defineModel<boolean>("open", { required: true });

const router = useRouter();
const { confirm } = useToasts();
const capture = useCaptureInvoice();

const form = reactive({
    supplierId: "",
    purchaseOrderId: "",
    supplierInvoiceNumber: "",
    invoiceDate: "",
    total: 0,
});
const lines = ref<DraftLine[]>([]);
const id = ref(uuidv7());
// The total follows the lines until the clerk types the one printed on the invoice.
const totalTyped = ref(false);

const suppliers = useBillableSuppliers(open);
const orders = useBillableOrders(() => form.supplierId);

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    capture.reset();
    Object.assign(form, {
        supplierId: "",
        purchaseOrderId: "",
        supplierInvoiceNumber: "",
        invoiceDate: isoDay(new Date()),
        total: 0,
    });
    lines.value = [];
    totalTyped.value = false;
});

const sum = computed(() => linesTotal(lines.value));

watch(sum, (next) => {
    if (!totalTyped.value) form.total = next;
});

function chooseSupplier() {
    form.purchaseOrderId = "";
    lines.value = [];
}

function chooseOrder() {
    const order = orders.data.value?.find((candidate) => candidate.id === form.purchaseOrderId);
    lines.value = order ? linesFromOrder(order) : [];
    totalTyped.value = false;
    form.total = sum.value;
}

const columns: readonly Column[] = [
    { key: "billed", label: "Bill" },
    { key: "lineNumber", label: "Line", numeric: true },
    { key: "quantity", label: "Quantity", numeric: true },
    { key: "unitPrice", label: "Unit price", numeric: true },
    { key: "amount", label: "Amount", numeric: true },
];

const problem = computed(() => capture.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];

// Said as the clerk types, before the service refuses it for the same reason.
const totalErrors = computed(() => [
    ...errorsFor("total"),
    ...(lines.value.length > 0 && form.total !== sum.value
        ? [`The billed lines add up to ${formatMoney(sum.value)}.`]
        : []),
]);

// An empty field goes as null, so the service names it in a 400 rather than the client failing on a blank.
const numberOrNull = (value: unknown) =>
    typeof value === "number" && Number.isFinite(value) ? value : null;
const textOrNull = (value: string) => (value.trim() === "" ? null : value.trim());

async function save() {
    const created = await capture.mutateAsync({
        id: id.value,
        supplierId: textOrNull(form.supplierId),
        supplierInvoiceNumber: textOrNull(form.supplierInvoiceNumber),
        invoiceDate: textOrNull(form.invoiceDate),
        purchaseOrderId: textOrNull(form.purchaseOrderId),
        lines: lines.value
            .filter((line) => line.billed)
            .map((line) => ({
                lineNumber: line.lineNumber,
                quantity: numberOrNull(line.quantity),
                unitPrice: numberOrNull(line.unitPrice),
            })),
        total: numberOrNull(form.total),
    });
    confirm("Invoice captured");
    open.value = false;
    await router.push({ name: "invoice", params: { invoiceId: created.id } });
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        wide
        title="Capture invoice"
        description="Key it in as the supplier wrote it. It is matched against the order and the goods received as soon as it is captured."
    >
        <form
            id="capture-invoice-form"
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
            <InlineNotice v-if="suppliers.isError.value" tone="error">{{
                describe(suppliers.error.value)
            }}</InlineNotice>
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Supplier"
                    :hint="
                        suppliers.data.value?.length === 0
                            ? 'No supplier has an order open to invoices.'
                            : undefined
                    "
                    :errors="errorsFor('supplierId')"
                >
                    <select
                        :id="fieldId"
                        v-model="form.supplierId"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        required
                        @change="chooseSupplier"
                    >
                        <option value="" disabled>
                            {{ suppliers.isPending.value ? "Loading…" : "Choose a supplier" }}
                        </option>
                        <option
                            v-for="supplier in suppliers.data.value ?? []"
                            :key="supplier.id"
                            :value="supplier.id"
                        >
                            {{ supplier.legalName }}{{ supplier.isActive ? "" : " (not active)" }}
                        </option>
                    </select>
                </FormField>
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Purchase order"
                    :errors="errorsFor('purchaseOrderId')"
                >
                    <select
                        :id="fieldId"
                        v-model="form.purchaseOrderId"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        :disabled="form.supplierId === ''"
                        required
                        @change="chooseOrder"
                    >
                        <option value="" disabled>
                            {{
                                form.supplierId !== "" && orders.isPending.value
                                    ? "Loading…"
                                    : "Choose an order"
                            }}
                        </option>
                        <option
                            v-for="order in orders.data.value ?? []"
                            :key="order.id"
                            :value="order.id"
                        >
                            {{ order.number }}, issued {{ formatDay(order.issuedAt) }}
                        </option>
                    </select>
                </FormField>
            </div>
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Invoice number"
                    hint="As printed. INV-0042 and inv42 count as the same number."
                    :errors="errorsFor('supplierInvoiceNumber')"
                >
                    <input
                        :id="fieldId"
                        v-model="form.supplierInvoiceNumber"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        autocomplete="off"
                        spellcheck="false"
                        required
                    />
                </FormField>
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Invoice date"
                    hint="The supplier's payment terms run from this date."
                    :errors="errorsFor('invoiceDate')"
                >
                    <input
                        :id="fieldId"
                        v-model="form.invoiceDate"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="date"
                        required
                    />
                </FormField>
            </div>

            <div class="lines">
                <p class="label">Lines</p>
                <p v-if="lines.length === 0" class="placeholder">
                    Choose the purchase order and its lines appear here, as ordered.
                </p>
                <UiTable
                    v-else
                    caption="Lines to bill"
                    :columns="columns"
                    :rows="lines"
                    :key-of="(line) => String(line.lineNumber)"
                >
                    <template #cell-billed="{ row }">
                        <input
                            v-model="row.billed"
                            type="checkbox"
                            :aria-label="`Bill line ${row.lineNumber}`"
                        />
                    </template>
                    <template #cell-quantity="{ row }">
                        <input
                            v-model.number="row.quantity"
                            class="figure"
                            type="number"
                            min="0"
                            step="any"
                            inputmode="decimal"
                            :disabled="!row.billed"
                            :aria-label="`Quantity, line ${row.lineNumber}`"
                        />
                    </template>
                    <template #cell-unitPrice="{ row }">
                        <input
                            v-model.number="row.unitPrice"
                            class="figure"
                            type="number"
                            min="0"
                            step="any"
                            inputmode="decimal"
                            :disabled="!row.billed"
                            :aria-label="`Unit price, line ${row.lineNumber}`"
                        />
                    </template>
                    <template #cell-amount="{ row }">
                        <MoneyText
                            v-if="row.billed"
                            :amount="lineCents(row.quantity, row.unitPrice) / 100"
                        />
                        <span v-else class="unbilled">Not billed</span>
                    </template>
                    <template #footer>
                        <tr>
                            <td colspan="4">Sum of the billed lines</td>
                            <td class="numeric"><MoneyText :amount="sum" /></td>
                        </tr>
                    </template>
                </UiTable>
                <p v-if="errorsFor('lines').length > 0" class="error">
                    {{ errorsFor("lines").join(" ") }}
                </p>
            </div>

            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Total"
                    hint="The total printed on the invoice. It has to equal the sum of the billed lines."
                    :errors="totalErrors"
                >
                    <input
                        :id="fieldId"
                        v-model.number="form.total"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        class="figure"
                        type="number"
                        min="0"
                        step="0.01"
                        inputmode="decimal"
                        required
                        @input="totalTyped = true"
                    />
                </FormField>
            </div>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="capture-invoice-form"
                variant="primary"
                :busy="capture.isPending.value"
                >Capture</UiButton
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

.lines {
    display: grid;
    gap: var(--space-1);
}

.label {
    font: var(--text-callout);
    font-weight: 500;
}

.placeholder {
    padding: var(--space-4);
    border-radius: var(--radius-group);
    background: var(--neutral-tint);
    font: var(--text-callout);
    color: var(--label-secondary);
}

.error {
    font: var(--text-caption);
    letter-spacing: var(--tracking-caption);
    color: var(--negative);
}

.unbilled {
    color: var(--label-secondary);
}

/* Inputs in the lines table look like the form's own fields, narrowed to a figure and aligned as one. */
.lines input.figure {
    width: 7rem;
    min-height: var(--control-height);
    padding: var(--space-1) var(--space-2);
    background: var(--grouped);
    border: 0;
    border-radius: var(--radius-control);
    box-shadow: inset 0 0 0 0.5px var(--field-border);
    font: var(--text-body);
    font-variant-numeric: tabular-nums;
    text-align: right;
}

.lines input.figure:disabled {
    opacity: 0.45;
}

.lines input:focus-visible {
    outline: 3px solid var(--focus-ring);
    outline-offset: 0;
}

/* The total is a figure too, so it reads from the right like the amounts above it. */
.form input.figure {
    font-variant-numeric: tabular-nums;
    text-align: right;
}
</style>

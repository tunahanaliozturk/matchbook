<script setup lang="ts">
import { computed, ref } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatDay, formatMoment, formatQuantity } from "@/shared/format";
import DetailRow from "@/shared/ui/DetailRow.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import ReasonSheet from "@/shared/ui/ReasonSheet.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import type { Column } from "@/shared/ui/table";
import UiButton from "@/shared/ui/UiButton.vue";
import UiTable from "@/shared/ui/UiTable.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useAcceptPriceVariance, useClearSuspectedDuplicate, useInvoice } from "./data";
import { formatUnitPrice } from "./lines";
import { invoiceTone, needsAttention } from "./tones";

const props = defineProps<{ invoiceId: string }>();

const { person, has } = useSession();
const { confirm } = useToasts();
const invoice = useInvoice(() => props.invoiceId);
const current = computed(() => invoice.data.value);
// The invoice this one resembles, read only to name it.
const original = useInvoice(() => current.value?.suspectedDuplicateOf);

const accept = useAcceptPriceVariance(() => props.invoiceId);
const clear = useClearSuspectedDuplicate(() => props.invoiceId);
const accepting = ref(false);

const who = (id: string | null | undefined) => nameOf(id, person.value?.id);
const isApprover = computed(() => has("ap-approver"));
const capturedByMe = computed(() => current.value?.capturedBy === person.value?.id);
const awaitsApprover = computed(
    () =>
        current.value?.status === "PriceVariance" || current.value?.status === "SuspectedDuplicate",
);

// Separation of duties, said out loud where it applies, rather than a button that is simply missing.
const dutyNote = computed(() =>
    isApprover.value && awaitsApprover.value && capturedByMe.value
        ? "You captured this invoice, so another AP approver has to decide."
        : null,
);

const columns: readonly Column[] = [
    { key: "lineNumber", label: "Order line", numeric: true },
    { key: "quantity", label: "Quantity", numeric: true },
    { key: "unitPrice", label: "Unit price", numeric: true },
    { key: "amount", label: "Amount", numeric: true },
];

async function confirmAccept(reason: string) {
    await accept.mutateAsync(reason);
    accepting.value = false;
    confirm("Price variance accepted");
}

async function confirmClear() {
    await clear.mutateAsync();
    confirm("Suspected duplicate cleared");
}
</script>

<template>
    <InlineNotice v-if="invoice.isError.value" tone="error">{{
        describe(invoice.error.value)
    }}</InlineNotice>
    <p v-else-if="!current" class="loading" role="status">Loading invoice…</p>

    <template v-else>
        <PageHeader
            :title="current.supplierInvoiceNumber"
            :back="{ to: { name: 'invoices' }, label: 'Invoices' }"
        >
            <template #subtitle>
                <StatusPill :status="current.status" :tone="invoiceTone[current.status]" />
                <MoneyText :amount="current.total" />
            </template>

            <UiButton
                v-if="isApprover && current.status === 'PriceVariance'"
                variant="primary"
                :disabled="capturedByMe"
                @click="accepting = true"
                >Accept price variance…</UiButton
            >
            <UiButton
                v-if="isApprover && current.status === 'SuspectedDuplicate'"
                variant="primary"
                :disabled="capturedByMe"
                :busy="clear.isPending.value"
                @click="confirmClear().catch(() => undefined)"
            >
                Clear suspected duplicate
            </UiButton>
        </PageHeader>

        <InlineNotice v-if="clear.error.value" tone="error">{{
            describe(clear.error.value)
        }}</InlineNotice>
        <InlineNotice v-if="dutyNote" tone="caution">{{ dutyNote }}</InlineNotice>

        <InlineNotice
            v-if="current.status === 'SuspectedDuplicate'"
            :tone="needsAttention(current.status) ? 'caution' : 'info'"
        >
            Held as a possible duplicate of
            <RouterLink
                v-if="current.suspectedDuplicateOf"
                :to="{ name: 'invoice', params: { invoiceId: current.suspectedDuplicateOf } }"
                >{{
                    original.data.value?.supplierInvoiceNumber ?? "an earlier invoice"
                }}</RouterLink
            >: the same supplier and total, dated within seven days, under another number. An AP
            approver who did not capture it decides whether it is genuine.
        </InlineNotice>
        <InlineNotice
            v-else-if="current.reasonDetail"
            :tone="needsAttention(current.status) ? 'caution' : 'info'"
        >
            {{ current.reasonDetail }}
        </InlineNotice>

        <GroupedSection title="Details">
            <dl>
                <DetailRow label="Supplier">{{
                    current.supplierName ?? "Not known to Payables yet"
                }}</DetailRow>
                <DetailRow label="Purchase order">{{
                    current.purchaseOrderNumber ?? "Not known to Payables yet"
                }}</DetailRow>
                <DetailRow label="Invoice date">{{ formatDay(current.invoiceDate) }}</DetailRow>
                <DetailRow label="Due">{{
                    current.dueDate ? formatDay(current.dueDate) : "Once it is matched"
                }}</DetailRow>
                <DetailRow v-if="current.paidAt" label="Paid">{{
                    formatMoment(current.paidAt)
                }}</DetailRow>
            </dl>
        </GroupedSection>

        <GroupedSection title="Lines">
            <UiTable
                caption="Invoice lines"
                :columns="columns"
                :rows="current.lines"
                :key-of="(line) => String(line.lineNumber)"
            >
                <template #cell-quantity="{ row }">{{ formatQuantity(row.quantity) }}</template>
                <template #cell-unitPrice="{ row }">{{ formatUnitPrice(row.unitPrice) }}</template>
                <template #cell-amount="{ row }"><MoneyText :amount="row.amount" /></template>
                <template #footer>
                    <tr>
                        <td colspan="3">Total</td>
                        <td class="numeric"><MoneyText :amount="current.total" /></td>
                    </tr>
                </template>
            </UiTable>
        </GroupedSection>

        <GroupedSection title="History">
            <dl>
                <DetailRow label="Captured"
                    >{{ formatMoment(current.capturedAt) }} by
                    {{ who(current.capturedBy) }}</DetailRow
                >
                <DetailRow v-if="current.duplicateClearedBy" label="Duplicate cleared">
                    by {{ who(current.duplicateClearedBy) }}
                </DetailRow>
                <DetailRow v-if="current.varianceAcceptedBy" label="Variance accepted">
                    by {{ who(current.varianceAcceptedBy) }}: {{ current.varianceAcceptanceReason }}
                </DetailRow>
                <DetailRow v-if="current.matchedAt" label="Matched">{{
                    formatMoment(current.matchedAt)
                }}</DetailRow>
            </dl>
        </GroupedSection>

        <ReasonSheet
            v-model:open="accepting"
            title="Accept price variance"
            description="The invoice is matched at the prices it states and becomes payable. The reason is kept with the invoice."
            label="Reason"
            action="Accept"
            :busy="accept.isPending.value"
            :error="accept.error.value"
            @confirm="(reason) => confirmAccept(reason).catch(() => undefined)"
        />
    </template>
</template>

<style scoped>
.loading {
    padding: var(--space-4);
    color: var(--label-secondary);
}
</style>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRouter } from "vue-router";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatMoment, formatQuantity } from "@/shared/format";
import DetailRow from "@/shared/ui/DetailRow.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiTable from "@/shared/ui/UiTable.vue";
import type { Column } from "@/shared/ui/table";
import { useToasts } from "@/shared/ui/toasts";

import CloseOrderSheet from "./CloseOrderSheet.vue";
import {
    useCancelOrder,
    useGoodsReceipts,
    useIssueOrder,
    usePurchaseOrder,
    useShortCloseOrder,
    type GoodsReceiptView,
    type OrderLineView,
} from "./data";
import LineSheet from "./LineSheet.vue";
import {
    anythingReceived,
    formatUnitPrice,
    outstanding,
    rejectionSentence,
    statusLabel,
    type Closing,
} from "./orders";
import ReceiptSheet from "./ReceiptSheet.vue";
import { orderTone } from "./tones";

const props = defineProps<{ purchaseOrderId: string }>();

const router = useRouter();
const { person, has, hasAny } = useSession();
const { confirm } = useToasts();
const order = usePurchaseOrder(() => props.purchaseOrderId);
const current = computed(() => order.data.value);
const receipts = useGoodsReceipts(() => props.purchaseOrderId);

const issue = useIssueOrder(() => props.purchaseOrderId);
const shortClose = useShortCloseOrder(() => props.purchaseOrderId);
const cancel = useCancelOrder(() => props.purchaseOrderId);

const isBuyer = computed(() => has("buyer"));
const isReceiver = computed(() => has("receiver"));
const who = (id: string | null | undefined) => nameOf(id, person.value?.id);

const status = computed(() => current.value?.status);
const lines = computed(() => current.value?.lines ?? []);
const editable = computed(() => isBuyer.value && status.value === "Draft");
const stillToArrive = computed(() => lines.value.some((line) => outstanding(line) > 0));
const issuedByMe = computed(
    () => !!current.value?.issuedBy && current.value.issuedBy === person.value?.id,
);

// Goods on the shelf are a fact the order has to keep, so once anything has arrived it can only be short-closed.
const canCancel = computed(
    () =>
        isBuyer.value &&
        (status.value === "Draft" ||
            status.value === "CommitmentPending" ||
            (status.value === "Issued" && !anythingReceived(lines.value))),
);

// Separation of duties, said out loud where it applies, rather than a button that is simply missing.
const receiptNote = computed(() =>
    status.value === "Issued" && isReceiver.value && issuedByMe.value
        ? "You issued this order, so another receiver has to record what arrives."
        : null,
);

// A requisition is linked only when this console has its page and the person may read it; a buyer may not.
const requisition = computed(() => {
    if (!current.value) return null;
    const target = router.resolve(`/requisitions/${current.value.requisitionId}`);
    const readable = !target.meta.roles || hasAny(target.meta.roles);
    return target.name !== "not-found" && readable ? target : null;
});

const columns = computed<Column[]>(() => [
    { key: "lineNumber", label: "Line" },
    { key: "description", label: "Description" },
    { key: "quantity", label: "Ordered", numeric: true },
    { key: "receivedQuantity", label: "Received", numeric: true },
    { key: "invoicedQuantity", label: "Invoiced", numeric: true },
    { key: "unitPrice", label: "Unit price", numeric: true },
    { key: "amount", label: "Amount", numeric: true },
    ...(editable.value ? [{ key: "change", label: "" }] : []),
]);

const unitOf = (lineNumber: number) =>
    lines.value.find((line) => line.lineNumber === lineNumber)?.unitOfMeasure ?? "";
const receiptLines = (receipt: GoodsReceiptView) =>
    receipt.lines
        .map(
            (line) =>
                `Line ${line.lineNumber}: ${formatQuantity(line.quantity)} ${unitOf(line.lineNumber)}`,
        )
        .join(", ");

const changing = ref<OrderLineView | null>(null);
const receiving = ref(false);
const closing = ref<Closing | null>(null);

// Budgets answers an issue a moment later. The notice on the page says it is waiting, and this says when the
// answer is yes; a no puts the reason on the page.
watch(status, (now, before) => {
    if (before === "CommitmentPending" && now === "Issued")
        confirm("Funds committed, order issued");
});

function startClosing(kind: Closing) {
    cancel.reset();
    shortClose.reset();
    closing.value = kind;
}

async function close(kind: Closing) {
    await (kind === "cancel" ? cancel : shortClose).mutateAsync();
    closing.value = null;
    confirm(kind === "cancel" ? "Order cancelled" : "Order short-closed");
}
</script>

<template>
    <InlineNotice v-if="order.isError.value" tone="error">{{
        describe(order.error.value)
    }}</InlineNotice>
    <p v-else-if="!current" class="loading" role="status">Loading purchase order…</p>

    <template v-else>
        <PageHeader
            :title="current.number"
            :back="{ to: { name: 'purchase-orders' }, label: 'Purchase orders' }"
        >
            <template #subtitle>
                <StatusPill
                    :status="statusLabel[current.status]"
                    :tone="orderTone[current.status]"
                />
                <MoneyText :amount="current.amount" />
            </template>

            <UiButton v-if="canCancel" variant="destructive" @click="startClosing('cancel')"
                >Cancel order…</UiButton
            >
            <UiButton
                v-if="isBuyer && current.status === 'Issued'"
                @click="startClosing('short-close')"
                >Short-close…</UiButton
            >
            <UiButton
                v-if="editable"
                variant="primary"
                :busy="issue.isPending.value"
                @click="issue.mutateAsync().catch(() => undefined)"
            >
                Issue
            </UiButton>
            <UiButton
                v-if="isReceiver && current.status === 'Issued' && stillToArrive"
                variant="primary"
                :disabled="issuedByMe"
                @click="receiving = true"
            >
                Record receipt…
            </UiButton>
        </PageHeader>

        <InlineNotice v-if="issue.error.value" tone="error">{{
            describe(issue.error.value)
        }}</InlineNotice>
        <InlineNotice v-if="current.status === 'CommitmentPending'">
            Waiting for Budgets to commit the funds. This page updates by itself when it answers.
        </InlineNotice>
        <InlineNotice
            v-if="current.status === 'Draft' && current.commitmentRejectionReason"
            tone="caution"
        >
            {{ rejectionSentence(current.commitmentRejectionReason) }} Change the lines, or have the
            budget raised, and issue it again.
        </InlineNotice>
        <InlineNotice v-if="receiptNote" tone="caution">{{ receiptNote }}</InlineNotice>
        <InlineNotice v-if="current.status === 'Issued' && !stillToArrive">
            Everything ordered has arrived. The order completes when its invoices are matched.
        </InlineNotice>
        <InlineNotice v-else-if="isBuyer && current.status === 'Issued' && !canCancel">
            Goods have arrived against this order, so it can be short-closed but no longer
            cancelled.
        </InlineNotice>

        <GroupedSection title="Details">
            <dl>
                <DetailRow label="Supplier">{{
                    current.supplierName ?? "Not known to Purchasing yet"
                }}</DetailRow>
                <DetailRow label="Cost centre"
                    >{{ current.costCentreCode }}, fiscal year {{ current.fiscalYear }}</DetailRow
                >
                <DetailRow label="Requisition">
                    <RouterLink v-if="requisition" :to="requisition">Open requisition</RouterLink>
                    <span v-else class="figures">{{ current.requisitionId }}</span>
                </DetailRow>
                <DetailRow label="Drafted">{{ formatMoment(current.draftedAt) }}</DetailRow>
                <DetailRow v-if="current.status === 'CommitmentPending'" label="Sent for funds">
                    by {{ who(current.issuedBy) }}
                </DetailRow>
                <DetailRow v-if="current.issuedAt" label="Issued">
                    {{ formatMoment(current.issuedAt) }} by {{ who(current.issuedBy) }}
                </DetailRow>
                <DetailRow v-if="current.closedAt" :label="statusLabel[current.status]">
                    {{ formatMoment(current.closedAt) }}
                    <template v-if="current.closedBy">by {{ who(current.closedBy) }}</template>
                </DetailRow>
            </dl>
        </GroupedSection>

        <GroupedSection
            title="Lines"
            :footer="
                editable
                    ? 'A line changed to zero stays on the order and is not bought.'
                    : undefined
            "
        >
            <UiTable
                caption="Order lines"
                :columns="columns"
                :rows="lines"
                :key-of="(line) => String(line.lineNumber)"
            >
                <template #cell-quantity="{ row }"
                    >{{ formatQuantity(row.quantity) }} {{ row.unitOfMeasure }}</template
                >
                <template #cell-receivedQuantity="{ row }">{{
                    formatQuantity(row.receivedQuantity)
                }}</template>
                <template #cell-invoicedQuantity="{ row }">{{
                    formatQuantity(row.invoicedQuantity)
                }}</template>
                <template #cell-unitPrice="{ row }">{{ formatUnitPrice(row.unitPrice) }}</template>
                <template #cell-amount="{ row }"><MoneyText :amount="row.amount" /></template>
                <template #cell-change="{ row }">
                    <UiButton variant="plain" @click="changing = row"
                        >Change…<span class="visually-hidden">
                            line {{ row.lineNumber }}</span
                        ></UiButton
                    >
                </template>
                <template #footer>
                    <tr class="total">
                        <th scope="row" colspan="6">Total</th>
                        <td class="numeric"><MoneyText :amount="current.amount" /></td>
                        <td v-if="editable" />
                    </tr>
                </template>
            </UiTable>
        </GroupedSection>

        <GroupedSection title="Receipts">
            <p v-if="receipts.isPending.value" class="none" role="status">Loading receipts…</p>
            <p v-else-if="receipts.isError.value" class="none" role="alert">
                {{ describe(receipts.error.value) }}
            </p>
            <p v-else-if="(receipts.data.value ?? []).length === 0" class="none">
                Nothing has been received against this order.
            </p>
            <ListRow v-for="receipt in receipts.data.value" :key="receipt.id">
                {{ formatMoment(receipt.receivedAt) }}, recorded by {{ who(receipt.receivedBy) }}
                <template #detail>{{ receiptLines(receipt) }}</template>
            </ListRow>
        </GroupedSection>

        <LineSheet
            :open="changing !== null"
            :order-id="current.id"
            :line="changing"
            @update:open="(open) => !open && (changing = null)"
        />
        <ReceiptSheet v-model:open="receiving" :order="current" />
        <CloseOrderSheet
            :closing="closing"
            :busy="cancel.isPending.value || shortClose.isPending.value"
            :error="closing === 'cancel' ? cancel.error.value : shortClose.error.value"
            @dismiss="closing = null"
            @confirm="(kind) => close(kind).catch(() => undefined)"
        />
    </template>
</template>

<style scoped>
.loading,
.none {
    padding: var(--space-4);
    color: var(--label-secondary);
}

.total th,
.total td {
    font-weight: 600;
}
</style>

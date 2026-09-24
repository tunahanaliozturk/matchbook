<script setup lang="ts">
import { computed, ref } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatDay, formatMoment } from "@/shared/format";
import DetailRow from "@/shared/ui/DetailRow.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import type { Column } from "@/shared/ui/table";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import UiTable from "@/shared/ui/UiTable.vue";
import { useToasts } from "@/shared/ui/toasts";

import {
    useCancelPaymentRun,
    useDownloadPaymentFile,
    usePaymentRun,
    useReleasePaymentRun,
} from "./data";
import { creditorTone, paymentRunTone } from "./tones";

const props = defineProps<{ paymentRunId: string }>();

const { person, has } = useSession();
const { confirm } = useToasts();
const run = usePaymentRun(() => props.paymentRunId);
const current = computed(() => run.data.value);

const release = useReleasePaymentRun(() => props.paymentRunId);
const cancel = useCancelPaymentRun(() => props.paymentRunId);
const download = useDownloadPaymentFile(() => props.paymentRunId);
const cancelling = ref(false);

// The last thing that failed, whichever button it was, stays on the page until the next attempt.
const failure = computed(() => release.error.value ?? download.error.value ?? null);

const isTreasurer = computed(() => has("treasurer"));
const draftedByMe = computed(() => current.value?.draftedBy === person.value?.id);
const who = (id: string | null | undefined) => nameOf(id, person.value?.id);
const invoices = (count: number) => (count === 1 ? "1 invoice" : `${count} invoices`);

// Four eyes, said out loud where it applies, rather than a button that is simply missing.
const releaseNote = computed(() =>
    current.value?.status === "Draft" && isTreasurer.value && draftedByMe.value
        ? "You drafted this run, so another treasurer has to release it."
        : null,
);

const dropped = computed(
    () => current.value?.creditors.filter((creditor) => creditor.status === "Dropped") ?? [],
);

// The drop reason only earns a column when some supplier was dropped.
const columns = computed<readonly Column[]>(() => [
    { key: "accountHolder", label: "Account holder" },
    { key: "maskedIban", label: "IBAN" },
    { key: "bic", label: "BIC" },
    { key: "accountVersion", label: "Account", numeric: true },
    { key: "itemCount", label: "Invoices", numeric: true },
    { key: "total", label: "Total", numeric: true },
    { key: "status", label: "Status" },
    ...(dropped.value.length > 0 ? [{ key: "dropReason", label: "Dropped because" }] : []),
]);

const dropReasons = {
    SupplierNotActive: "Supplier no longer active",
    AccountChanged: "Bank account changed since the draft",
} as const;

async function confirmRelease() {
    await release.mutateAsync();
    confirm("Payment run released");
}

async function confirmCancel() {
    await cancel.mutateAsync();
    cancelling.value = false;
    confirm("Payment run cancelled");
}

async function saveFile() {
    await download.mutateAsync();
    confirm("Bank file downloaded");
}
</script>

<template>
    <InlineNotice v-if="run.isError.value" tone="error">{{
        describe(run.error.value)
    }}</InlineNotice>
    <p v-else-if="!current" class="loading" role="status">Loading payment run…</p>

    <template v-else>
        <PageHeader
            :title="`Run for ${formatDay(current.executionDate)}`"
            :back="{ to: { name: 'payment-runs' }, label: 'Payment runs' }"
        >
            <template #subtitle>
                <StatusPill :status="current.status" :tone="paymentRunTone[current.status]" />
                <MoneyText :amount="current.total" />
            </template>

            <UiButton
                v-if="isTreasurer && current.status === 'Draft'"
                variant="destructive"
                @click="cancelling = true"
                >Cancel…</UiButton
            >
            <UiButton
                v-if="isTreasurer && current.status === 'Draft'"
                variant="primary"
                :disabled="draftedByMe"
                :busy="release.isPending.value"
                @click="confirmRelease().catch(() => undefined)"
                >Release</UiButton
            >
            <UiButton
                v-if="isTreasurer && current.status === 'Released'"
                variant="primary"
                :busy="download.isPending.value"
                @click="saveFile().catch(() => undefined)"
                >Download bank file</UiButton
            >
        </PageHeader>

        <InlineNotice v-if="failure" tone="error">{{ describe(failure) }}</InlineNotice>
        <InlineNotice v-if="releaseNote" tone="caution">{{ releaseNote }}</InlineNotice>
        <InlineNotice v-if="current.status === 'Released' && dropped.length > 0" tone="caution">
            {{ dropped.length === 1 ? "One supplier was" : `${dropped.length} suppliers were` }}
            dropped at release. Their invoices are payable again and go into the next run.
        </InlineNotice>

        <GroupedSection title="Summary">
            <dl>
                <DetailRow label="Execution date">{{ formatDay(current.executionDate) }}</DetailRow>
                <DetailRow label="In the run"
                    >{{ invoices(current.itemCount) }}, <MoneyText :amount="current.total"
                /></DetailRow>
                <DetailRow v-if="current.status === 'Released'" label="Paid"
                    >{{ invoices(current.paidCount) }}, <MoneyText :amount="current.paidTotal"
                /></DetailRow>
            </dl>
        </GroupedSection>

        <GroupedSection
            title="Suppliers"
            footer="Paid into the verified account on record at the draft. Account numbers appear in full only in the bank file."
        >
            <UiTable
                caption="Suppliers in the run"
                :columns="columns"
                :rows="current.creditors"
                :key-of="(creditor) => creditor.supplierId"
            >
                <template #cell-maskedIban="{ row }"
                    >Ending {{ row.maskedIban.replace(/\*/g, "") }}</template
                >
                <template #cell-accountVersion="{ row }">Version {{ row.accountVersion }}</template>
                <template #cell-total="{ row }"><MoneyText :amount="row.total" /></template>
                <template #cell-status="{ row }">
                    <StatusPill :status="row.status" :tone="creditorTone[row.status]" />
                </template>
                <template #cell-dropReason="{ row }">{{
                    row.dropReason ? dropReasons[row.dropReason] : ""
                }}</template>
            </UiTable>
        </GroupedSection>

        <GroupedSection title="History">
            <dl>
                <DetailRow label="Drafted"
                    >{{ formatMoment(current.draftedAt) }} by
                    {{ who(current.draftedBy) }}</DetailRow
                >
                <DetailRow v-if="current.releasedAt" label="Released">
                    {{ formatMoment(current.releasedAt) }} by {{ who(current.releasedBy) }}
                </DetailRow>
                <DetailRow v-if="current.cancelledAt" label="Cancelled">
                    {{ formatMoment(current.cancelledAt) }} by {{ who(current.cancelledBy) }}
                </DetailRow>
            </dl>
        </GroupedSection>

        <UiSheet
            v-model:open="cancelling"
            title="Cancel payment run"
            :description="
                current.itemCount === 1
                    ? 'Nothing has been paid. Its invoice becomes payable again and can go into a new run.'
                    : `Nothing has been paid. Its ${current.itemCount} invoices become payable again and can go into a new run.`
            "
        >
            <InlineNotice v-if="cancel.error.value" tone="error">{{
                describe(cancel.error.value)
            }}</InlineNotice>
            <template #footer>
                <UiButton @click="cancelling = false">Keep draft</UiButton>
                <UiButton
                    variant="destructive"
                    :busy="cancel.isPending.value"
                    @click="confirmCancel().catch(() => undefined)"
                    >Cancel run</UiButton
                >
            </template>
        </UiSheet>
    </template>
</template>

<style scoped>
.loading {
    padding: var(--space-4);
    color: var(--label-secondary);
}
</style>

<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";

import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatDay } from "@/shared/format";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import SegmentedControl from "@/shared/ui/SegmentedControl.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import CaptureInvoiceSheet from "./CaptureInvoiceSheet.vue";
import { useInvoiceList, type InvoiceFilter } from "./data";
import { invoiceTone } from "./tones";

// The filter lives in the address, so a filtered list survives a reload and can be sent to a colleague. The two
// exceptions have a page of their own, in the order an approver works through them.
type View = "all" | "order" | "goods" | "payable" | "scheduled" | "paid" | "rejected";

const views: readonly { value: View; label: string }[] = [
    { value: "all", label: "All" },
    { value: "order", label: "Waiting for order" },
    { value: "goods", label: "Waiting for goods" },
    { value: "payable", label: "Payable" },
    { value: "scheduled", label: "Scheduled" },
    { value: "paid", label: "Paid" },
    { value: "rejected", label: "Rejected" },
];

const filters: Record<View, InvoiceFilter> = {
    all: {},
    order: { status: "AwaitingPurchaseOrder" },
    goods: { status: "AwaitingReceipt" },
    payable: { status: "Payable" },
    scheduled: { status: "Scheduled" },
    paid: { status: "Paid" },
    rejected: { status: "Rejected" },
};

const route = useRoute();
const router = useRouter();
const { has } = useSession();

const view = computed<View>({
    get: () => {
        const requested = route.query.view;
        return typeof requested === "string" && requested in filters ? (requested as View) : "all";
    },
    set: (next) => void router.replace({ query: next === "all" ? {} : { view: next } }),
});

const list = useInvoiceList(() => filters[view.value]);
const invoices = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
const capturing = ref(false);
</script>

<template>
    <PageHeader title="Invoices">
        <UiButton v-if="has('ap-clerk')" variant="primary" @click="capturing = true"
            >Capture invoice…</UiButton
        >
    </PageHeader>

    <div class="filters">
        <SegmentedControl v-model="view" label="Show" :options="views" />
    </div>

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection v-else>
        <p v-if="list.isPending.value" class="loading" role="status">Loading invoices…</p>
        <EmptyState v-else-if="invoices.length === 0" message="No invoices match this view." />
        <ListRow
            v-for="invoice in invoices"
            :key="invoice.id"
            :to="{ name: 'invoice', params: { invoiceId: invoice.id } }"
        >
            {{ invoice.supplierInvoiceNumber }}
            <template #detail
                >{{ invoice.supplierName ?? "Supplier not known yet" }},
                {{
                    invoice.dueDate
                        ? `due ${formatDay(invoice.dueDate)}`
                        : `dated ${formatDay(invoice.invoiceDate)}`
                }}</template
            >
            <template #trailing>
                <MoneyText :amount="invoice.total" />
                <StatusPill :status="invoice.status" :tone="invoiceTone[invoice.status]" />
            </template>
        </ListRow>
    </GroupedSection>

    <div v-if="list.hasNextPage.value" class="more">
        <UiButton :busy="list.isFetchingNextPage.value" @click="list.fetchNextPage()"
            >Show more</UiButton
        >
    </div>

    <CaptureInvoiceSheet v-model:open="capturing" />
</template>

<style scoped>
.filters {
    margin-bottom: var(--space-4);
}

.loading {
    padding: var(--space-6) var(--space-4);
    color: var(--label-secondary);
}

.more {
    display: flex;
    justify-content: center;
    margin-top: var(--space-4);
}
</style>

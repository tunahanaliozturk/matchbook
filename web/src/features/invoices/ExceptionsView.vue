<script setup lang="ts">
import { computed } from "vue";

import { describe } from "@/shared/api/problem";
import { formatMoment } from "@/shared/format";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import { useInvoiceExceptions } from "./data";
import { invoiceTone } from "./tones";

// The AP approvers' work, oldest first, so the invoice that has waited longest is decided first.
const list = useInvoiceExceptions();
const invoices = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
</script>

<template>
    <PageHeader title="Exceptions" />

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection
        v-else
        footer="Suspected duplicates and price variances, oldest first. Invoices waiting for an order or for goods move on by themselves when those arrive."
    >
        <p v-if="list.isPending.value" class="loading" role="status">Loading exceptions…</p>
        <EmptyState
            v-else-if="invoices.length === 0"
            message="No invoice is waiting for an approver's decision."
        />
        <ListRow
            v-for="invoice in invoices"
            :key="invoice.id"
            :to="{ name: 'invoice', params: { invoiceId: invoice.id } }"
        >
            {{ invoice.supplierInvoiceNumber }}
            <template #detail
                >{{ invoice.supplierName ?? "Supplier not known yet" }}, captured
                {{ formatMoment(invoice.capturedAt) }}</template
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
</template>

<style scoped>
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

<script setup lang="ts">
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";

import { describe } from "@/shared/api/problem";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import SegmentedControl from "@/shared/ui/SegmentedControl.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import { usePurchaseOrderList, type PurchaseOrderFilter } from "./data";
import { statusLabel } from "./orders";
import { orderTone } from "./tones";

// The filter lives in the address, so a filtered list survives a reload and can be sent to a colleague.
type View = "all" | "draft" | "pending" | "issued" | "completed" | "short-closed" | "cancelled";

const views: readonly { value: View; label: string }[] = [
    { value: "all", label: "All" },
    { value: "draft", label: "Drafts" },
    { value: "pending", label: "Waiting for funds" },
    { value: "issued", label: "Issued" },
    { value: "completed", label: "Completed" },
    { value: "short-closed", label: "Short closed" },
    { value: "cancelled", label: "Cancelled" },
];

const filters: Record<View, PurchaseOrderFilter> = {
    all: {},
    draft: { status: "Draft" },
    pending: { status: "CommitmentPending" },
    issued: { status: "Issued" },
    completed: { status: "Completed" },
    "short-closed": { status: "ShortClosed" },
    cancelled: { status: "Cancelled" },
};

const route = useRoute();
const router = useRouter();

const view = computed<View>({
    get: () => {
        const requested = route.query.view;
        return typeof requested === "string" && requested in filters ? (requested as View) : "all";
    },
    set: (next) => void router.replace({ query: next === "all" ? {} : { view: next } }),
});

const list = usePurchaseOrderList(() => filters[view.value]);
const orders = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
</script>

<template>
    <PageHeader title="Purchase orders" />

    <div class="filters">
        <SegmentedControl v-model="view" label="Show" :options="views" />
    </div>

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection v-else>
        <p v-if="list.isPending.value" class="loading" role="status">Loading purchase orders…</p>
        <EmptyState
            v-else-if="orders.length === 0"
            message="No purchase orders match this view. An order is drafted when a requisition is approved."
        />
        <ListRow
            v-for="order in orders"
            :key="order.id"
            :to="{ name: 'purchase-order', params: { purchaseOrderId: order.id } }"
        >
            <span class="figures">{{ order.number }}</span>
            <template #detail
                >{{ order.supplierName ?? "Supplier name not known yet" }}, cost centre
                {{ order.costCentreCode }}</template
            >
            <template #trailing>
                <MoneyText :amount="order.amount" />
                <StatusPill :status="statusLabel[order.status]" :tone="orderTone[order.status]" />
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

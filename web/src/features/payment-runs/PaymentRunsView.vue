<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatDay, formatMoment } from "@/shared/format";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import SegmentedControl from "@/shared/ui/SegmentedControl.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import { usePaymentRunList, type PaymentRunFilter } from "./data";
import DraftPaymentRunSheet from "./DraftPaymentRunSheet.vue";
import { paymentRunTone } from "./tones";

// The filter lives in the address, so a filtered list survives a reload and can be sent to a colleague.
type View = "all" | "draft" | "released" | "cancelled";

const views: readonly { value: View; label: string }[] = [
    { value: "all", label: "All" },
    { value: "draft", label: "Drafts" },
    { value: "released", label: "Released" },
    { value: "cancelled", label: "Cancelled" },
];

const filters: Record<View, PaymentRunFilter> = {
    all: {},
    draft: { status: "Draft" },
    released: { status: "Released" },
    cancelled: { status: "Cancelled" },
};

const route = useRoute();
const router = useRouter();
const { person, has } = useSession();

const view = computed<View>({
    get: () => {
        const requested = route.query.view;
        return typeof requested === "string" && requested in filters ? (requested as View) : "all";
    },
    set: (next) => void router.replace({ query: next === "all" ? {} : { view: next } }),
});

const list = usePaymentRunList(() => filters[view.value]);
const runs = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
const drafting = ref(false);
const who = (id: string | null | undefined) => nameOf(id, person.value?.id);
const invoices = (count: number) => (count === 1 ? "1 invoice" : `${count} invoices`);
</script>

<template>
    <PageHeader title="Payment runs">
        <UiButton v-if="has('treasurer')" variant="primary" @click="drafting = true"
            >Draft payment run…</UiButton
        >
    </PageHeader>

    <div class="filters">
        <SegmentedControl v-model="view" label="Show" :options="views" />
    </div>

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection v-else>
        <p v-if="list.isPending.value" class="loading" role="status">Loading payment runs…</p>
        <EmptyState v-else-if="runs.length === 0" message="No payment runs match this view." />
        <ListRow
            v-for="run in runs"
            :key="run.id"
            :to="{ name: 'payment-run', params: { paymentRunId: run.id } }"
        >
            Run for {{ formatDay(run.executionDate) }}
            <template #detail
                >{{ invoices(run.itemCount) }}, drafted {{ formatMoment(run.draftedAt) }} by
                {{ who(run.draftedBy) }}</template
            >
            <template #trailing>
                <MoneyText :amount="run.status === 'Released' ? run.paidTotal : run.total" />
                <StatusPill :status="run.status" :tone="paymentRunTone[run.status]" />
            </template>
        </ListRow>
    </GroupedSection>

    <div v-if="list.hasNextPage.value" class="more">
        <UiButton :busy="list.isFetchingNextPage.value" @click="list.fetchNextPage()"
            >Show more</UiButton
        >
    </div>

    <DraftPaymentRunSheet v-model:open="drafting" />
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

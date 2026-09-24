<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";

import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatMoney } from "@/shared/format";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import LedgerCapsule from "@/shared/ui/LedgerCapsule.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import SegmentedControl from "@/shared/ui/SegmentedControl.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import { useBudgetList } from "./data";
import { fiscalYearFrom } from "./ledger";
import OpenBudgetSheet from "./OpenBudgetSheet.vue";

// The year and the view live in the address, so a list survives a reload and can be sent to a colleague.
type View = "all" | "overspent";

const views: readonly { value: View; label: string }[] = [
    { value: "all", label: "All" },
    { value: "overspent", label: "Over allotment" },
];

const route = useRoute();
const router = useRouter();
const { has } = useSession();

const year = computed(() => fiscalYearFrom(route.query.year, new Date()));

const view = computed<View>({
    get: () => (route.query.view === "overspent" ? "overspent" : "all"),
    set: (next) =>
        void router.replace({
            query: { ...route.query, view: next === "all" ? undefined : next },
        }),
});

const yearLink = (to: number) => ({ query: { ...route.query, year: String(to) } });

const list = useBudgetList(() => ({
    fiscalYear: year.value,
    overspent: view.value === "overspent",
}));
const budgets = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
const opening = ref(false);

const empty = computed(() =>
    view.value === "overspent"
        ? `No budget has gone past its allotment in ${year.value}.`
        : `No budget is open for ${year.value}. Requisitions on a cost centre without one are refused.`,
);
</script>

<template>
    <PageHeader title="Budgets">
        <UiButton v-if="has('budget-admin')" variant="primary" @click="opening = true"
            >Open budget…</UiButton
        >
    </PageHeader>

    <div class="filters">
        <nav class="years" aria-label="Fiscal year">
            <RouterLink
                :to="yearLink(year - 1)"
                class="step"
                :aria-label="`Fiscal year ${year - 1}`"
                >‹</RouterLink
            >
            <span class="year figures" aria-current="page">{{ year }}</span>
            <RouterLink
                :to="yearLink(year + 1)"
                class="step"
                :aria-label="`Fiscal year ${year + 1}`"
                >›</RouterLink
            >
        </nav>
        <SegmentedControl v-model="view" label="Show" :options="views" />
    </div>

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection
        v-else
        footer="Each bar is spent, then ordered, then requested, against what is left of the allotment."
    >
        <p v-if="list.isPending.value" class="loading" role="status">Loading budgets…</p>
        <EmptyState v-else-if="budgets.length === 0" :message="empty">
            <UiButton v-if="has('budget-admin') && view === 'all'" @click="opening = true"
                >Open budget…</UiButton
            >
        </EmptyState>
        <ListRow
            v-for="budget in budgets"
            :key="budget.id"
            :to="{ name: 'budget', params: { budgetId: budget.id } }"
        >
            <span class="figures">{{ budget.costCentreCode }}</span>
            <template #detail>{{ formatMoney(budget.allotted) }} allotted</template>
            <template #trailing>
                <StatusPill v-if="budget.overspend > 0" status="Overspent" tone="negative" />
                <span class="meter">
                    <LedgerCapsule
                        :allotted="budget.allotted"
                        :reserved="budget.reserved"
                        :committed="budget.committed"
                        :actual="budget.actual"
                        compact
                    />
                </span>
                <span class="left" :class="{ over: budget.overspend > 0 }">
                    {{
                        budget.overspend > 0
                            ? `${formatMoney(budget.overspend)} over`
                            : `${formatMoney(budget.available)} left`
                    }}
                </span>
            </template>
        </ListRow>
    </GroupedSection>

    <div v-if="list.hasNextPage.value" class="more">
        <UiButton :busy="list.isFetchingNextPage.value" @click="list.fetchNextPage()"
            >Show more</UiButton
        >
    </div>

    <OpenBudgetSheet v-model:open="opening" :fiscal-year="year" />
</template>

<style scoped>
.filters {
    display: flex;
    align-items: center;
    gap: var(--space-4);
    flex-wrap: wrap;
    margin-bottom: var(--space-4);
}

/* A stepper through the years, drawn as the segmented control is, since the two sit side by side. */
.years {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    padding: 2px;
    background: var(--neutral-tint);
    border-radius: calc(var(--radius-control) + 2px);
}

.step {
    display: inline-grid;
    place-items: center;
    width: 1.75rem;
    min-height: 1.5rem;
    border-radius: var(--radius-control);
    font: var(--text-body);
    font-weight: 600;
    color: var(--label);
    text-decoration: none;
}

.step:hover {
    background: var(--hover);
    text-decoration: none;
}

.year {
    display: inline-grid;
    place-items: center;
    min-height: 1.5rem;
    padding: 0 var(--space-3);
    background: var(--grouped);
    border-radius: var(--radius-control);
    box-shadow:
        var(--shadow-control),
        0 0 0 0.5px var(--separator);
    font: var(--text-callout);
    font-weight: 600;
}

.loading {
    padding: var(--space-6) var(--space-4);
    color: var(--label-secondary);
}

.meter {
    width: 9rem;
}

.left {
    min-width: 8.5rem;
    font: var(--text-callout);
    color: var(--label-secondary);
    text-align: right;
}

.left.over {
    color: var(--negative);
    font-weight: 500;
}

.more {
    display: flex;
    justify-content: center;
    margin-top: var(--space-4);
}
</style>

<script setup lang="ts">
import { computed, ref } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatMoment, formatMoney } from "@/shared/format";
import DetailRow from "@/shared/ui/DetailRow.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import LedgerCapsule from "@/shared/ui/LedgerCapsule.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import type { Column } from "@/shared/ui/table";
import UiButton from "@/shared/ui/UiButton.vue";
import UiTable from "@/shared/ui/UiTable.vue";

import AllotmentSheet from "./AllotmentSheet.vue";
import { useBudget, useCostCentreOf, useLedger, type LedgerEntryView } from "./data";
import { documentOf, formatMovement, stepName } from "./ledger";

const props = defineProps<{ budgetId: string }>();

const { person, has } = useSession();
const budget = useBudget(() => props.budgetId);
const current = computed(() => budget.data.value);
const costCentre = useCostCentreOf(() => current.value?.costCentreCode);
const ledger = useLedger(() => props.budgetId);
const entries = computed(() => ledger.data.value?.pages.flatMap((page) => page.items) ?? []);

const who = (id: string | null | undefined) => nameOf(id, person.value?.id);
const over = computed(() => (current.value?.overspend ?? 0) > 0);
const changing = ref(false);

// The ledger's columns are the capsule's figures, so a column's sum is the figure above it. They run in the order
// money moves (requested, ordered, spent), so a commitment reads as one amount leaving a column for the next.
const columns: readonly Column[] = [
    { key: "occurredAt", label: "When" },
    { key: "step", label: "Step" },
    { key: "documentId", label: "Document" },
    { key: "allotted", label: "Allotted", numeric: true },
    { key: "reserved", label: "Requested", numeric: true },
    { key: "committed", label: "Ordered", numeric: true },
    { key: "actual", label: "Spent", numeric: true },
];

const figures = ["allotted", "reserved", "committed", "actual"] as const;
</script>

<template>
    <InlineNotice v-if="budget.isError.value" tone="error">{{
        describe(budget.error.value)
    }}</InlineNotice>
    <p v-else-if="!current" class="loading" role="status">Loading budget…</p>

    <template v-else>
        <PageHeader
            :title="`${current.costCentreCode} ${current.fiscalYear}`"
            :back="{
                to: { name: 'budgets', query: { year: String(current.fiscalYear) } },
                label: 'Budgets',
            }"
        >
            <template #subtitle>
                <StatusPill v-if="over" status="Overspent" tone="negative" />
                <span v-if="costCentre.data.value"
                    >{{ costCentre.data.value.name }}, managed by
                    {{ who(costCentre.data.value.managerId) }}</span
                >
            </template>

            <UiButton v-if="has('budget-admin')" variant="primary" @click="changing = true"
                >Change allotment…</UiButton
            >
        </PageHeader>

        <section class="storage" aria-labelledby="budget-headline">
            <p id="budget-headline" class="headline">
                <template v-if="over">
                    <span class="figure over">{{ formatMoney(current.overspend) }}</span>
                    over an allotment of {{ formatMoney(current.allotted) }}
                </template>
                <template v-else>
                    <span class="figure">{{ formatMoney(current.available) }}</span>
                    available of {{ formatMoney(current.allotted) }}
                </template>
            </p>
            <LedgerCapsule
                :allotted="current.allotted"
                :reserved="current.reserved"
                :committed="current.committed"
                :actual="current.actual"
            />
        </section>

        <GroupedSection
            title="Figures"
            footer="Available is the allotment less what is requested, ordered and spent. Requisitions and orders are refused past it; an invoice is not, and shows here as an overspend."
        >
            <dl>
                <DetailRow label="Allotted"><MoneyText :amount="current.allotted" /></DetailRow>
                <DetailRow label="Spent"><MoneyText :amount="current.actual" /></DetailRow>
                <DetailRow label="Ordered"><MoneyText :amount="current.committed" /></DetailRow>
                <DetailRow label="Requested"><MoneyText :amount="current.reserved" /></DetailRow>
                <DetailRow v-if="over" label="Over budget">
                    <span class="over"><MoneyText :amount="current.overspend" /></span>
                </DetailRow>
                <DetailRow v-else label="Available"
                    ><MoneyText :amount="current.available"
                /></DetailRow>
            </dl>
        </GroupedSection>

        <GroupedSection
            title="Ledger"
            footer="Every movement on the budget, in the order it was written. Nothing is edited or removed."
        >
            <InlineNotice v-if="ledger.isError.value" tone="error">{{
                describe(ledger.error.value)
            }}</InlineNotice>
            <p v-else-if="ledger.isPending.value" class="loading" role="status">
                Loading the ledger…
            </p>
            <UiTable
                v-else
                caption="Ledger"
                :columns="columns"
                :rows="entries"
                :key-of="(entry: LedgerEntryView) => String(entry.sequence)"
            >
                <template #cell-occurredAt="{ row }">{{ formatMoment(row.occurredAt) }}</template>
                <template #cell-step="{ row }">
                    {{ stepName(row.step)
                    }}<template v-if="row.actorId"> by {{ who(row.actorId) }}</template>
                </template>
                <template #cell-documentId="{ row }">
                    <span class="document">{{ documentOf(row.step, row.documentId) }}</span>
                </template>
                <template v-for="figure in figures" :key="figure" #[`cell-${figure}`]="{ row }">
                    <span :class="{ out: row[figure] < 0 }">{{ formatMovement(row[figure]) }}</span>
                </template>
            </UiTable>
        </GroupedSection>

        <div v-if="ledger.hasNextPage.value" class="more">
            <UiButton :busy="ledger.isFetchingNextPage.value" @click="ledger.fetchNextPage()"
                >Show more</UiButton
            >
        </div>

        <AllotmentSheet v-model:open="changing" :budget="current" />
    </template>
</template>

<style scoped>
.loading {
    padding: var(--space-4);
    color: var(--label-secondary);
}

/* The page's centrepiece: what is left, in large figures, over the capsule, the way a disk's capacity reads. */
.storage {
    display: grid;
    gap: var(--space-4);
    padding: var(--space-5) var(--space-4) var(--space-4);
    margin-bottom: var(--space-6);
    background: var(--grouped);
    border-radius: var(--radius-group);
    box-shadow: 0 0 0 0.5px var(--separator);
}

.headline {
    color: var(--label-secondary);
}

.figure {
    margin-right: var(--space-1);
    font: var(--text-figure);
    font-variant-numeric: tabular-nums;
    color: var(--label);
}

.over,
.figure.over {
    color: var(--negative);
}

.document {
    color: var(--label-secondary);
    white-space: nowrap;
}

.out {
    color: var(--label-secondary);
}

.more {
    display: flex;
    justify-content: center;
    margin-top: var(--space-4);
}
</style>

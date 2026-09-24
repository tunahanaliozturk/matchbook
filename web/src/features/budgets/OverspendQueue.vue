<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import { formatMoney } from "@/shared/format";
import LedgerCapsule from "@/shared/ui/LedgerCapsule.vue";
import ListRow from "@/shared/ui/ListRow.vue";

import { useBudgetList, type BudgetFilter } from "./data";

// An overspend is allowed, since an invoice that passed its match is a fact, but someone has to decide what to do
// about it: raise the allotment, or take it up with whoever spent it. This year's only; last year's are history.
const filter: BudgetFilter = { fiscalYear: new Date().getUTCFullYear(), overspent: true };
const list = useBudgetList(filter, 5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Budgets over their allotment"
        :items="items"
        :key-of="(budget) => budget.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No budget has gone past its allotment this year."
        :more="{ name: 'budgets', query: { view: 'overspent' } }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'budget', params: { budgetId: item.id } }">
                <span class="figures">{{ item.costCentreCode }} {{ item.fiscalYear }}</span>
                <template #detail
                    >{{ formatMoney(item.overspend) }} over an allotment of
                    {{ formatMoney(item.allotted) }}</template
                >
                <template #trailing>
                    <span class="meter">
                        <LedgerCapsule
                            :allotted="item.allotted"
                            :reserved="item.reserved"
                            :committed="item.committed"
                            :actual="item.actual"
                            compact
                        />
                    </span>
                </template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

<style scoped>
.meter {
    width: 9rem;
}
</style>

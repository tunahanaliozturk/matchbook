<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";

import { useRequisitionList, type RequisitionStatus } from "./data";
import { waitsOnRequester } from "./rules";
import { requisitionTone } from "./tones";

// The list page's first page, from the same cache. ponytail: filtered from the newest fifty, because GET
// /requisitions takes no status filter; a status parameter there if drafts older than that go unnoticed.
const list = useRequisitionList();
const items = computed(() =>
    (list.data.value?.pages[0]?.items ?? [])
        .filter((requisition) => waitsOnRequester(requisition.status))
        .slice(0, 5),
);

const why: Partial<Record<RequisitionStatus, string>> = {
    Draft: "A draft, not submitted yet",
    BudgetRejected: "Budgets could not hold the money",
    Rejected: "An approver rejected it",
};
</script>

<template>
    <WorkQueue
        title="Your requisitions to follow up"
        :items="items"
        :key-of="(requisition) => requisition.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="None of your recent requisitions is a draft or was turned down."
        :more="{ name: 'requisitions' }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'requisition', params: { requisitionId: item.id } }">
                <span class="figures">{{ item.number }}</span>
                <template #detail>{{ why[item.status] }}</template>
                <template #trailing>
                    <MoneyText :amount="item.amount" />
                    <StatusPill :status="item.status" :tone="requisitionTone[item.status]" />
                </template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";

import { useRequisitionList, type RequisitionStatus } from "./data";
import { waitingOnRequester } from "./rules";
import { requisitionTone } from "./tones";

const list = useRequisitionList({ status: [...waitingOnRequester] }, 5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);

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
        empty="None of your requisitions is a draft or was turned down."
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

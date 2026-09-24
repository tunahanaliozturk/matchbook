<script setup lang="ts">
import { computed } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";

import { useApprovalList } from "./data";

const { person } = useSession();
const list = useApprovalList(5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Requisitions to approve"
        :items="items"
        :key-of="(requisition) => requisition.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="Nothing is waiting for your decision."
        :more="{ name: 'approvals' }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'requisition', params: { requisitionId: item.id } }">
                <span class="figures">{{ item.number }}</span>
                <template #detail
                    >{{ item.costCentreCode }}, raised by
                    {{ nameOf(item.requesterId, person?.id) }}</template
                >
                <template #trailing><MoneyText :amount="item.amount" /></template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

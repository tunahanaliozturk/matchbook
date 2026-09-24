<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";

import { usePurchaseOrderList, type PurchaseOrderFilter } from "./data";

const filter: PurchaseOrderFilter = { status: "Draft" };
const list = usePurchaseOrderList(filter, 5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Orders to issue"
        :items="items"
        :key-of="(order) => order.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No draft order is waiting to be issued."
        :more="{ name: 'purchase-orders', query: { view: 'draft' } }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'purchase-order', params: { purchaseOrderId: item.id } }">
                <span class="figures">{{ item.number }}</span>
                <template #detail>{{
                    item.supplierName ?? "Supplier name not known yet"
                }}</template>
                <template #trailing><MoneyText :amount="item.amount" /></template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

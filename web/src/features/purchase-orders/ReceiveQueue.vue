<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";

import { usePurchaseOrderList, type PurchaseOrderFilter } from "./data";

// Issued orders with a line not yet received in full. One received in full still waits for its invoices, but not
// for the receiver.
const filter: PurchaseOrderFilter = { status: "Issued", awaitingGoods: true };
const list = usePurchaseOrderList(filter, 5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Orders waiting for goods"
        :items="items"
        :key-of="(order) => order.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No issued order is waiting for goods."
        :more="{ name: 'purchase-orders', query: { view: 'issued' } }"
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

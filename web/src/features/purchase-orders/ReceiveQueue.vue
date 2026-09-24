<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";

import { usePurchaseOrderList, type PurchaseOrderFilter } from "./data";

// Every issued order, including one received in full that still waits for its invoices: the list does not carry
// line totals, and opening such an order says plainly that everything has arrived.
const filter: PurchaseOrderFilter = { status: "Issued" };
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

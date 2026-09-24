<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";

import { useInvoiceList, type InvoiceFilter } from "./data";

// What a clerk captured that cannot be paid yet because the order or the goods have not reached Payables. These move
// on by themselves; the queue is here so a clerk can chase an order or a receipt that is overdue.
const noOrder: InvoiceFilter = { status: "AwaitingPurchaseOrder" };
const noGoods: InvoiceFilter = { status: "AwaitingReceipt" };
const orders = useInvoiceList(noOrder, 5);
const goods = useInvoiceList(noGoods, 5);

// Ids are version 7, so the larger id is the later capture, and the two lists merge newest first.
const items = computed(() =>
    [...(orders.data.value?.pages[0]?.items ?? []), ...(goods.data.value?.pages[0]?.items ?? [])]
        .sort((a, b) => (a.id < b.id ? 1 : -1))
        .slice(0, 5),
);
</script>

<template>
    <WorkQueue
        title="Invoices waiting for an order or goods"
        :items="items"
        :key-of="(invoice) => invoice.id"
        :loading="orders.isPending.value || goods.isPending.value"
        :error="orders.error.value ?? goods.error.value"
        empty="No captured invoice is waiting for its order or its goods."
        :more="{ name: 'invoices', query: { view: 'goods' } }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'invoice', params: { invoiceId: item.id } }">
                {{ item.supplierInvoiceNumber }}
                <template #detail
                    >{{
                        item.status === "AwaitingPurchaseOrder"
                            ? "Waiting for the order"
                            : "Waiting for goods"
                    }}, {{ item.supplierName ?? "supplier not known yet" }}</template
                >
                <template #trailing><MoneyText :amount="item.total" /></template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

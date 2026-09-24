<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import { humanise } from "@/shared/format";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";

import { useInvoiceExceptions } from "./data";

const list = useInvoiceExceptions(5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Invoice exceptions to decide"
        :items="items"
        :key-of="(invoice) => invoice.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No suspected duplicate or price variance is waiting for a decision."
        :more="{ name: 'invoice-exceptions' }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'invoice', params: { invoiceId: item.id } }">
                {{ item.supplierInvoiceNumber }}
                <template #detail
                    >{{ humanise(item.status) }},
                    {{ item.supplierName ?? "supplier not known yet" }}</template
                >
                <template #trailing><MoneyText :amount="item.total" /></template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

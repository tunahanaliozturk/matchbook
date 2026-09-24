<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";

import { useSupplierList, type SupplierFilter } from "./data";

const filter: SupplierFilter = { status: "PendingActivation" };
const list = useSupplierList(filter, 5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Suppliers to activate"
        :items="items"
        :key-of="(supplier) => supplier.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No supplier is waiting for activation."
        :more="{ name: 'suppliers', query: { view: 'pending' } }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'supplier', params: { supplierId: item.id } }">
                {{ item.legalName }}
                <template #detail>{{ item.taxId }}, {{ item.countryCode }}</template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";

import { useSupplierList, type SupplierFilter } from "./data";

const filter: SupplierFilter = { status: "Draft" };
const list = useSupplierList(filter, 5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Supplier drafts to finish"
        :items="items"
        :key-of="(supplier) => supplier.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No supplier draft is waiting to be submitted."
        :more="{ name: 'suppliers', query: { view: 'draft' } }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'supplier', params: { supplierId: item.id } }">
                {{ item.legalName }}
                <template #detail>{{
                    item.hasPendingBankAccount
                        ? "Bank account awaiting approval"
                        : "Needs an approved bank account, then submitting"
                }}</template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import ListRow from "@/shared/ui/ListRow.vue";

import { useSupplierList, type SupplierFilter } from "./data";

const filter: SupplierFilter = { hasPendingBankAccount: true };
const list = useSupplierList(filter, 5);
const items = computed(() => list.data.value?.pages[0]?.items ?? []);
</script>

<template>
    <WorkQueue
        title="Bank account changes to review"
        :items="items"
        :key-of="(supplier) => supplier.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No bank account change is waiting for review."
        :more="{ name: 'suppliers', query: { view: 'bank' } }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'supplier', params: { supplierId: item.id } }">
                {{ item.legalName }}
                <template #detail
                    >Account in force:
                    {{
                        item.accountVersion === 0 ? "none yet" : `version ${item.accountVersion}`
                    }}</template
                >
            </ListRow>
        </template>
    </WorkQueue>
</template>

<script setup lang="ts">
import { computed } from "vue";

import WorkQueue from "@/features/inbox/WorkQueue.vue";
import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { formatDay, formatMoment } from "@/shared/format";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";

import { usePaymentRunList, type PaymentRunFilter } from "./data";

// Drafts another treasurer made. A treasurer's own drafts are not work for them: someone else has to release them.
// Drafts are few, so the first page holds them all and the filter by drafter can happen here.
const filter: PaymentRunFilter = { status: "Draft" };
const { person } = useSession();
const list = usePaymentRunList(filter);
const items = computed(() =>
    (list.data.value?.pages[0]?.items ?? [])
        .filter((run) => run.draftedBy !== person.value?.id)
        .slice(0, 5),
);
</script>

<template>
    <WorkQueue
        title="Payment runs to release"
        :items="items"
        :key-of="(run) => run.id"
        :loading="list.isPending.value"
        :error="list.error.value"
        empty="No payment run drafted by another treasurer is waiting for release."
        :more="{ name: 'payment-runs', query: { view: 'draft' } }"
    >
        <template #default="{ item }">
            <ListRow :to="{ name: 'payment-run', params: { paymentRunId: item.id } }">
                Run for {{ formatDay(item.executionDate) }}
                <template #detail
                    >{{ item.itemCount === 1 ? "1 invoice" : `${item.itemCount} invoices` }},
                    drafted {{ formatMoment(item.draftedAt) }} by
                    {{ nameOf(item.draftedBy, person?.id) }}</template
                >
                <template #trailing><MoneyText :amount="item.total" /></template>
            </ListRow>
        </template>
    </WorkQueue>
</template>

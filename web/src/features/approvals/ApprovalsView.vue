<script setup lang="ts">
import { computed } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatDay } from "@/shared/format";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import { useApprovalList } from "./data";

// Only what the caller can decide now, by the same three rules the approval itself checks, so every row here opens
// on a requisition whose Approve button works for this person.
const { person } = useSession();

const list = useApprovalList();
const requisitions = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
</script>

<template>
    <PageHeader title="Approvals" />

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection
        v-else
        footer="Longest waiting first. A requisition is here while its current step is yours to take."
    >
        <p v-if="list.isPending.value" class="loading" role="status">Loading approvals…</p>
        <EmptyState
            v-else-if="requisitions.length === 0"
            message="Nothing is waiting for your decision. A requisition appears here when its current step is yours."
        />
        <ListRow
            v-for="requisition in requisitions"
            :key="requisition.id"
            :to="{ name: 'requisition', params: { requisitionId: requisition.id } }"
        >
            <span class="figures">{{ requisition.number }}</span>
            <template #detail
                >{{ requisition.costCentreCode }}, raised by
                {{ nameOf(requisition.requesterId, person?.id) }}, needed by
                {{ formatDay(requisition.neededBy) }}</template
            >
            <template #trailing>
                <MoneyText :amount="requisition.amount" />
            </template>
        </ListRow>
    </GroupedSection>

    <div v-if="list.hasNextPage.value" class="more">
        <UiButton :busy="list.isFetchingNextPage.value" @click="list.fetchNextPage()"
            >Show more</UiButton
        >
    </div>
</template>

<style scoped>
.loading {
    padding: var(--space-6) var(--space-4);
    color: var(--label-secondary);
}

.more {
    display: flex;
    justify-content: center;
    margin-top: var(--space-4);
}
</style>

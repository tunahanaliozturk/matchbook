<script setup lang="ts">
import { computed, ref } from "vue";

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
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import { useRequisitionList } from "./data";
import RequisitionSheet from "./RequisitionSheet.vue";
import { requisitionTone } from "./tones";

// There is no status filter: GET /requisitions takes none, and filtering one loaded page would hide the rest.
const { person, has, hasAny } = useSession();

const list = useRequisitionList();
const requisitions = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
const creating = ref(false);

// A requester sees only their own, so saying who raised each one would say "you" on every row.
const seesEveryone = computed(() => hasAny(["approver", "finance-approver", "cfo", "auditor"]));
</script>

<template>
    <PageHeader title="Requisitions">
        <UiButton v-if="has('requester')" variant="primary" @click="creating = true"
            >New requisition…</UiButton
        >
    </PageHeader>

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection v-else>
        <p v-if="list.isPending.value" class="loading" role="status">Loading requisitions…</p>
        <EmptyState
            v-else-if="requisitions.length === 0"
            :message="
                has('requester')
                    ? 'You have not raised a requisition yet. Start one with New requisition.'
                    : 'Nobody has raised a requisition yet.'
            "
        />
        <ListRow
            v-for="requisition in requisitions"
            :key="requisition.id"
            :to="{ name: 'requisition', params: { requisitionId: requisition.id } }"
        >
            <span class="figures">{{ requisition.number }}</span>
            <template #detail
                >{{ requisition.costCentreCode
                }}<template v-if="seesEveryone"
                    >, raised by {{ nameOf(requisition.requesterId, person?.id) }}</template
                >, needed by {{ formatDay(requisition.neededBy) }}</template
            >
            <template #trailing>
                <MoneyText :amount="requisition.amount" />
                <StatusPill
                    :status="requisition.status"
                    :tone="requisitionTone[requisition.status]"
                />
            </template>
        </ListRow>
    </GroupedSection>

    <div v-if="list.hasNextPage.value" class="more">
        <UiButton :busy="list.isFetchingNextPage.value" @click="list.fetchNextPage()"
            >Show more</UiButton
        >
    </div>

    <RequisitionSheet v-model:open="creating" />
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

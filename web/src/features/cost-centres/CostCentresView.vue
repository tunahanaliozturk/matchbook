<script setup lang="ts">
import { computed, ref } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import CostCentreSheet from "./CostCentreSheet.vue";
import { useCostCentreList, type CostCentreView } from "./data";
import { costCentreTone } from "./tones";

const { person, has } = useSession();

const list = useCostCentreList();
const costCentres = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
const isAdmin = computed(() => has("budget-admin"));

// One sheet for both: no cost centre means a new one.
const sheetOpen = ref(false);
const editing = ref<CostCentreView>();

function edit(costCentre?: CostCentreView) {
    editing.value = costCentre;
    sheetOpen.value = true;
}
</script>

<template>
    <PageHeader title="Cost centres">
        <UiButton v-if="isAdmin" variant="primary" @click="edit()">New cost centre…</UiButton>
    </PageHeader>

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection
        v-else
        footer="The manager gives every requisition on the cost centre its first approval."
    >
        <p v-if="list.isPending.value" class="loading" role="status">Loading cost centres…</p>
        <EmptyState
            v-else-if="costCentres.length === 0"
            message="No cost centre exists yet. A budget and every requisition hang off one."
        >
            <UiButton v-if="isAdmin" @click="edit()">New cost centre…</UiButton>
        </EmptyState>
        <ListRow v-for="costCentre in costCentres" :key="costCentre.code">
            <span class="figures">{{ costCentre.code }}</span>
            <template #detail
                >{{ costCentre.name }}, managed by
                {{ nameOf(costCentre.managerId, person?.id) }}</template
            >
            <template #trailing>
                <StatusPill
                    :status="costCentre.isActive ? 'Active' : 'Inactive'"
                    :tone="costCentreTone[costCentre.isActive ? 'Active' : 'Inactive']"
                />
                <UiButton
                    v-if="isAdmin"
                    variant="plain"
                    :aria-label="`Edit ${costCentre.code}…`"
                    @click="edit(costCentre)"
                    >Edit…</UiButton
                >
            </template>
        </ListRow>
    </GroupedSection>

    <div v-if="list.hasNextPage.value" class="more">
        <UiButton :busy="list.isFetchingNextPage.value" @click="list.fetchNextPage()"
            >Show more</UiButton
        >
    </div>

    <CostCentreSheet v-model:open="sheetOpen" :cost-centre="editing" />
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

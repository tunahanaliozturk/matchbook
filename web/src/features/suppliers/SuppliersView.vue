<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";

import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import EmptyState from "@/shared/ui/EmptyState.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import SegmentedControl from "@/shared/ui/SegmentedControl.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";

import { useSupplierList, type SupplierFilter } from "./data";
import SupplierSheet from "./SupplierSheet.vue";
import { supplierTone } from "./tones";

// The filter lives in the address, so a filtered list survives a reload and can be sent to a colleague.
type View = "all" | "draft" | "pending" | "active" | "blocked" | "bank";

const views: readonly { value: View; label: string }[] = [
    { value: "all", label: "All" },
    { value: "draft", label: "Drafts" },
    { value: "pending", label: "To activate" },
    { value: "active", label: "Active" },
    { value: "blocked", label: "Blocked" },
    { value: "bank", label: "Bank changes" },
];

const filters: Record<View, SupplierFilter> = {
    all: {},
    draft: { status: "Draft" },
    pending: { status: "PendingActivation" },
    active: { status: "Active" },
    blocked: { status: "Blocked" },
    bank: { hasPendingBankAccount: true },
};

const route = useRoute();
const router = useRouter();
const { has } = useSession();

const view = computed<View>({
    get: () => {
        const requested = route.query.view;
        return typeof requested === "string" && requested in filters ? (requested as View) : "all";
    },
    set: (next) => void router.replace({ query: next === "all" ? {} : { view: next } }),
});

const list = useSupplierList(() => filters[view.value]);
const suppliers = computed(() => list.data.value?.pages.flatMap((page) => page.items) ?? []);
const creating = ref(false);
</script>

<template>
    <PageHeader title="Suppliers">
        <UiButton v-if="has('supplier-admin')" variant="primary" @click="creating = true"
            >New supplier…</UiButton
        >
    </PageHeader>

    <div class="filters">
        <SegmentedControl v-model="view" label="Show" :options="views" />
    </div>

    <InlineNotice v-if="list.isError.value" tone="error">{{
        describe(list.error.value)
    }}</InlineNotice>

    <GroupedSection v-else>
        <p v-if="list.isPending.value" class="loading" role="status">Loading suppliers…</p>
        <EmptyState v-else-if="suppliers.length === 0" message="No suppliers match this view." />
        <ListRow
            v-for="supplier in suppliers"
            :key="supplier.id"
            :to="{ name: 'supplier', params: { supplierId: supplier.id } }"
        >
            {{ supplier.legalName }}
            <template #detail
                >{{ supplier.taxId }}, {{ supplier.countryCode }},
                {{ supplier.paymentTermsDays }} days</template
            >
            <template #trailing>
                <StatusPill
                    v-if="supplier.hasPendingBankAccount"
                    status="Bank change"
                    tone="caution"
                />
                <StatusPill :status="supplier.status" :tone="supplierTone[supplier.status]" />
            </template>
        </ListRow>
    </GroupedSection>

    <div v-if="list.hasNextPage.value" class="more">
        <UiButton :busy="list.isFetchingNextPage.value" @click="list.fetchNextPage()"
            >Show more</UiButton
        >
    </div>

    <SupplierSheet v-model:open="creating" />
</template>

<style scoped>
.filters {
    margin-bottom: var(--space-4);
}

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

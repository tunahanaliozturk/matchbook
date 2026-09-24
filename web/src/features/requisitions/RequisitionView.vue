<script setup lang="ts">
import { computed, ref } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatDay, formatMoment, formatQuantity, humanise } from "@/shared/format";
import DetailRow from "@/shared/ui/DetailRow.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import MoneyText from "@/shared/ui/MoneyText.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import ReasonSheet from "@/shared/ui/ReasonSheet.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import UiTable from "@/shared/ui/UiTable.vue";
import type { Column } from "@/shared/ui/table";
import { useToasts } from "@/shared/ui/toasts";

import {
    useApproveRequisition,
    useCancelRequisition,
    useRejectRequisition,
    useRequisition,
    useSubmitRequisition,
    useSupplierOptions,
} from "./data";
import { formatUnitPrice } from "./lines";
import RequisitionSheet from "./RequisitionSheet.vue";
import { actorOf, budgetRefusal, currentStep, decisionFor, stepNames, stepPeople } from "./rules";
import { requisitionTone, stepTone } from "./tones";

const props = defineProps<{ requisitionId: string }>();

const { person, has } = useSession();
const { confirm } = useToasts();
const requisition = useRequisition(() => props.requisitionId);
const current = computed(() => requisition.data.value);
const me = computed(() => person.value?.id);

const submit = useSubmitRequisition(() => props.requisitionId);
const cancel = useCancelRequisition(() => props.requisitionId);
const approve = useApproveRequisition(() => props.requisitionId);
const reject = useRejectRequisition(() => props.requisitionId);

// The last thing that failed on the page itself stays until the next attempt; the sheets show their own.
const failure = computed(() => submit.error.value ?? approve.error.value ?? null);

// Only the requisition's own requester changes it, and only before approval (docs/services/requisitions.md).
const isMine = computed(() => has("requester") && current.value?.requesterId === me.value);
const canEdit = computed(() => isMine.value && current.value?.status === "Draft");
const canCancel = computed(
    () =>
        isMine.value &&
        ["Draft", "Submitted", "PendingApproval"].includes(current.value?.status ?? ""),
);
const decision = computed(() => (current.value ? decisionFor(current.value, person.value) : null));
const waiting = computed(() => (current.value ? currentStep(current.value) : undefined));
const rejectedBy = computed(
    () => current.value?.steps.find((step) => step.decision === "Rejected")?.decidedBy,
);

// Nobody who reads requisitions may read Suppliers, so the name comes from Requisitions' own copy of the active
// ones. A supplier blocked since is no longer in it, and shows as the id the requisition holds.
const suppliers = useSupplierOptions(true);
const supplierName = computed(
    () =>
        suppliers.data.value?.find((supplier) => supplier.id === current.value?.supplierId)
            ?.legalName,
);

const columns: readonly Column[] = [
    { key: "description", label: "Description" },
    { key: "quantity", label: "Quantity", numeric: true },
    { key: "unitOfMeasure", label: "Unit" },
    { key: "unitPrice", label: "Unit price", numeric: true },
    { key: "amount", label: "Amount", numeric: true },
];

const editing = ref(false);
const cancelling = ref(false);
const rejecting = ref(false);

async function run(action: { mutateAsync: () => Promise<unknown> }, done: string) {
    await action.mutateAsync();
    confirm(done);
}

async function confirmCancel() {
    await cancel.mutateAsync();
    cancelling.value = false;
    confirm("Requisition cancelled");
}

async function confirmReject(reason: string) {
    await reject.mutateAsync(reason);
    rejecting.value = false;
    confirm("Requisition rejected");
}
</script>

<template>
    <InlineNotice v-if="requisition.isError.value" tone="error">{{
        describe(requisition.error.value)
    }}</InlineNotice>
    <p v-else-if="!current" class="loading" role="status">Loading requisition…</p>

    <template v-else>
        <PageHeader
            :title="current.number"
            :back="{ to: { name: 'requisitions' }, label: 'Requisitions' }"
        >
            <template #subtitle>
                <StatusPill :status="current.status" :tone="requisitionTone[current.status]" />
                <MoneyText :amount="current.amount" />
            </template>

            <UiButton v-if="canEdit" @click="editing = true">Edit…</UiButton>
            <UiButton v-if="canCancel" variant="destructive" @click="cancelling = true"
                >Cancel…</UiButton
            >
            <UiButton
                v-if="canEdit"
                variant="primary"
                :busy="submit.isPending.value"
                @click="run(submit, 'Submitted for approval').catch(() => undefined)"
            >
                Submit for approval
            </UiButton>
            <template v-if="decision?.shown">
                <UiButton :disabled="!decision.allowed" @click="rejecting = true">Reject…</UiButton>
                <UiButton
                    variant="primary"
                    :disabled="!decision.allowed"
                    :busy="approve.isPending.value"
                    @click="run(approve, 'Requisition approved').catch(() => undefined)"
                >
                    Approve
                </UiButton>
            </template>
        </PageHeader>

        <InlineNotice v-if="failure" tone="error">{{ describe(failure) }}</InlineNotice>
        <InlineNotice v-if="decision?.reason" tone="caution">{{ decision.reason }}</InlineNotice>
        <InlineNotice v-if="current.status === 'Draft' && isMine">
            Only you can see this draft. Submitting it asks Budgets to hold the amount against
            {{ current.costCentreCode }}, and then the approvers decide.
        </InlineNotice>
        <InlineNotice v-if="current.status === 'Submitted'">
            Budgets is checking {{ current.costCentreCode }} for the money. This page updates when
            it answers.
        </InlineNotice>
        <InlineNotice v-if="current.status === 'BudgetRejected'" tone="caution">
            Budgets could not hold the money.
            {{ budgetRefusal(current.rejectionReason, current.costCentreCode) }}
        </InlineNotice>
        <InlineNotice v-if="current.status === 'Rejected'" tone="caution">
            Rejected by {{ nameOf(rejectedBy, me) }}: {{ current.rejectionReason }}
        </InlineNotice>

        <GroupedSection title="Details">
            <dl>
                <DetailRow label="Cost centre">{{ current.costCentreCode }}</DetailRow>
                <DetailRow label="Supplier">{{ supplierName ?? current.supplierId }}</DetailRow>
                <DetailRow label="Needed by">{{ formatDay(current.neededBy) }}</DetailRow>
                <DetailRow label="Raised by"
                    >{{ nameOf(current.requesterId, me) }},
                    {{ formatMoment(current.createdAt) }}</DetailRow
                >
                <DetailRow v-if="current.fiscalYear" label="Budget year">{{
                    current.fiscalYear
                }}</DetailRow>
                <DetailRow v-if="current.purchaseOrderNumber" label="Purchase order">
                    <span class="figures">{{ current.purchaseOrderNumber }}</span>
                </DetailRow>
            </dl>
        </GroupedSection>

        <GroupedSection title="Justification">
            <p class="prose">{{ current.justification }}</p>
        </GroupedSection>

        <section class="section">
            <h2 class="heading">Lines</h2>
            <UiTable
                caption="Lines"
                :columns="columns"
                :rows="current.lines"
                :key-of="(line) => String(line.lineNumber)"
            >
                <template #cell-quantity="{ row }">{{ formatQuantity(row.quantity) }}</template>
                <template #cell-unitPrice="{ row }">{{ formatUnitPrice(row.unitPrice) }}</template>
                <template #cell-amount="{ row }"><MoneyText :amount="row.amount" /></template>
                <template #footer>
                    <tr>
                        <th scope="row" colspan="4" class="total-label">Total</th>
                        <td class="numeric"><MoneyText :amount="current.amount" /></td>
                    </tr>
                </template>
            </UiTable>
        </section>

        <GroupedSection
            title="Approval route"
            footer="Steps are taken in order. Nobody decides their own requisition, or two steps of one."
        >
            <p v-if="current.steps.length === 0" class="none">
                {{
                    current.status === "Draft" || current.status === "Submitted"
                        ? "The route is fixed once Budgets holds the money: the cost centre's manager, then a finance approver over €10,000.00, then the CFO over €100,000.00."
                        : "This requisition did not reach approval."
                }}
            </p>
            <ListRow v-for="step in current.steps" :key="step.sequence">
                {{ step.sequence }}. {{ stepNames[step.kind] }}
                <template #detail
                    >{{ stepPeople(step, me)
                    }}<template v-if="step.decidedAt"
                        >, {{ formatMoment(step.decidedAt) }}</template
                    ></template
                >
                <template #trailing>
                    <StatusPill
                        :status="
                            step.decision !== 'Pending'
                                ? step.decision
                                : step === waiting
                                  ? 'Waiting'
                                  : 'Later'
                        "
                        :tone="stepTone(step, step === waiting)"
                    />
                </template>
            </ListRow>
        </GroupedSection>

        <GroupedSection title="Timeline">
            <ListRow v-for="entry in current.timeline" :key="entry.sequence">
                {{ humanise(entry.action) }}
                <template #detail
                    >{{ actorOf(entry, me) }}, {{ formatMoment(entry.at)
                    }}<template v-if="entry.detail"
                        >.
                        {{
                            entry.action === "FundsRefused"
                                ? budgetRefusal(entry.detail, current.costCentreCode)
                                : entry.detail
                        }}</template
                    ></template
                >
            </ListRow>
        </GroupedSection>

        <RequisitionSheet v-model:open="editing" :requisition="current" />
        <UiSheet
            v-model:open="cancelling"
            title="Cancel requisition"
            :description="
                current.status === 'Draft'
                    ? 'The draft is withdrawn. Nobody else has seen it.'
                    : 'It is withdrawn from approval, and Budgets releases the money it holds for it.'
            "
        >
            <InlineNotice v-if="cancel.error.value" tone="error">{{
                describe(cancel.error.value)
            }}</InlineNotice>
            <template #footer>
                <UiButton @click="cancelling = false">Keep it</UiButton>
                <UiButton
                    variant="destructive"
                    :busy="cancel.isPending.value"
                    @click="confirmCancel().catch(() => undefined)"
                    >Cancel requisition</UiButton
                >
            </template>
        </UiSheet>
        <ReasonSheet
            v-model:open="rejecting"
            title="Reject requisition"
            description="The requester reads the reason. Budgets releases the money it holds."
            label="Reason"
            action="Reject"
            destructive
            :busy="reject.isPending.value"
            :error="reject.error.value"
            @confirm="(reason) => confirmReject(reason).catch(() => undefined)"
        />
    </template>
</template>

<style scoped>
.loading,
.none {
    padding: var(--space-4);
    color: var(--label-secondary);
}

.prose {
    padding: var(--space-3) var(--space-4);
    white-space: pre-line;
    overflow-wrap: anywhere;
}

/* The lines are a table with a surface of its own, under a heading that matches a grouped section's. */
.section {
    display: grid;
    gap: var(--space-2);
    margin-top: var(--space-6);
}

.heading {
    padding: 0 var(--space-4);
    font: var(--text-headline);
    color: var(--label-secondary);
}

.total-label {
    text-align: left;
}
</style>

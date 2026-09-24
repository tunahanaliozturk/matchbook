<script setup lang="ts">
import { computed, ref } from "vue";

import { nameOf } from "@/auth/people";
import { useSession } from "@/auth/session";
import { describe } from "@/shared/api/problem";
import { formatMoment } from "@/shared/format";
import DetailRow from "@/shared/ui/DetailRow.vue";
import GroupedSection from "@/shared/ui/GroupedSection.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import ListRow from "@/shared/ui/ListRow.vue";
import PageHeader from "@/shared/ui/PageHeader.vue";
import ReasonSheet from "@/shared/ui/ReasonSheet.vue";
import StatusPill from "@/shared/ui/StatusPill.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import { useToasts } from "@/shared/ui/toasts";

import BankAccountSheet from "./BankAccountSheet.vue";
import {
    useActivateSupplier,
    useApproveBankAccount,
    useBlockSupplier,
    useRejectBankAccount,
    useSubmitSupplier,
    useSupplier,
    useUnblockSupplier,
} from "./data";
import SupplierSheet from "./SupplierSheet.vue";
import { bankAccountTone, supplierTone } from "./tones";

const props = defineProps<{ supplierId: string }>();

const { person, has } = useSession();
const { confirm } = useToasts();
const supplier = useSupplier(() => props.supplierId);
const current = computed(() => supplier.data.value);

const submit = useSubmitSupplier(() => props.supplierId);
const activate = useActivateSupplier(() => props.supplierId);
const block = useBlockSupplier(() => props.supplierId);
const unblock = useUnblockSupplier(() => props.supplierId);
const approve = useApproveBankAccount(() => props.supplierId);
const reject = useRejectBankAccount(() => props.supplierId);

// The last thing that failed, whichever button it was, stays on the page until the next attempt.
const failure = computed(
    () =>
        submit.error.value ??
        activate.error.value ??
        unblock.error.value ??
        approve.error.value ??
        null,
);

const isAdmin = computed(() => has("supplier-admin"));
const isApprover = computed(() => has("supplier-approver"));
const isMe = (id: string | null | undefined) =>
    id !== null && id !== undefined && id === person.value?.id;
const who = (id: string | null | undefined) => nameOf(id, person.value?.id);

const editing = ref(false);
const proposing = ref(false);
const blocking = ref(false);
const rejecting = ref<string | null>(null);

const accounts = computed(() => current.value?.bankAccounts ?? []);
const hasApprovedAccount = computed(() =>
    accounts.value.some((account) => account.status === "Approved"),
);

// Four eyes, said out loud where it applies, rather than a button that is simply missing.
const activationNote = computed(() =>
    current.value?.status === "PendingActivation" &&
    isApprover.value &&
    isMe(current.value.submittedBy)
        ? "You submitted this supplier, so another supplier approver has to activate it."
        : null,
);

const formatIban = (iban: string, masked: boolean) =>
    masked ? `Ending ${iban.replace(/\*/g, "")}` : iban.replace(/(.{4})/g, "$1 ").trim();

async function run(action: { mutateAsync: () => Promise<unknown> }, done: string) {
    await action.mutateAsync();
    confirm(done);
}

async function confirmBlock(reason: string) {
    await block.mutateAsync(reason);
    blocking.value = false;
    confirm("Supplier blocked");
}

async function confirmReject(reason: string) {
    if (rejecting.value === null) return;
    await reject.mutateAsync({ accountId: rejecting.value, reason });
    rejecting.value = null;
    confirm("Bank account rejected");
}
</script>

<template>
    <InlineNotice v-if="supplier.isError.value" tone="error">{{
        describe(supplier.error.value)
    }}</InlineNotice>
    <p v-else-if="!current" class="loading" role="status">Loading supplier…</p>

    <template v-else>
        <PageHeader
            :title="current.legalName"
            :back="{ to: { name: 'suppliers' }, label: 'Suppliers' }"
        >
            <template #subtitle>
                <StatusPill :status="current.status" :tone="supplierTone[current.status]" />
                <span>{{ current.taxId }}</span>
            </template>

            <UiButton v-if="isAdmin && current.status !== 'Blocked'" @click="editing = true"
                >Edit details…</UiButton
            >
            <UiButton v-if="isAdmin" @click="proposing = true">Propose bank account…</UiButton>
            <UiButton
                v-if="(isAdmin || isApprover) && current.status === 'Active'"
                variant="destructive"
                @click="blocking = true"
            >
                Block…
            </UiButton>
            <UiButton
                v-if="isApprover && current.status === 'Blocked'"
                variant="primary"
                :busy="unblock.isPending.value"
                @click="run(unblock, 'Supplier unblocked').catch(() => undefined)"
            >
                Unblock
            </UiButton>
            <UiButton
                v-if="isAdmin && current.status === 'Draft'"
                variant="primary"
                :disabled="!hasApprovedAccount"
                :busy="submit.isPending.value"
                @click="run(submit, 'Submitted for activation').catch(() => undefined)"
            >
                Submit for activation
            </UiButton>
            <UiButton
                v-if="isApprover && current.status === 'PendingActivation'"
                variant="primary"
                :disabled="isMe(current.submittedBy)"
                :busy="activate.isPending.value"
                @click="run(activate, 'Supplier activated').catch(() => undefined)"
            >
                Activate
            </UiButton>
        </PageHeader>

        <InlineNotice v-if="failure" tone="error">{{ describe(failure) }}</InlineNotice>
        <InlineNotice v-if="activationNote" tone="caution">{{ activationNote }}</InlineNotice>
        <InlineNotice v-if="isAdmin && current.status === 'Draft' && !hasApprovedAccount">
            A supplier is activated with a bank account already approved. Propose one, and have a
            supplier approver approve it, before submitting.
        </InlineNotice>
        <InlineNotice v-if="current.status === 'Blocked' && current.blockReason" tone="caution">
            Blocked by {{ who(current.blockedBy) }}: {{ current.blockReason }}
        </InlineNotice>

        <GroupedSection title="Details">
            <dl>
                <DetailRow label="Tax ID">{{ current.taxId }}</DetailRow>
                <DetailRow label="Country">{{ current.countryCode }}</DetailRow>
                <DetailRow label="Payment terms"
                    >{{ current.paymentTermsDays }} days from the invoice date</DetailRow
                >
                <DetailRow label="Contact">{{ current.contactEmail }}</DetailRow>
                <DetailRow label="Account in force">
                    {{
                        current.accountVersion === 0
                            ? "None approved yet"
                            : `Version ${current.accountVersion}`
                    }}
                </DetailRow>
            </dl>
        </GroupedSection>

        <GroupedSection
            title="Bank accounts"
            footer="Account numbers are shown in full only to a supplier approver reviewing a pending change."
        >
            <p v-if="accounts.length === 0" class="none">No bank account has been proposed.</p>
            <ListRow v-for="account in accounts" :key="account.id">
                <span class="figures">{{ formatIban(account.iban, account.ibanMasked) }}</span>
                <template #detail>
                    {{ account.bic }}, {{ account.accountHolder }}, proposed by
                    {{ who(account.proposedBy) }}
                    {{ formatMoment(account.proposedAt) }}
                    <template v-if="account.rejectionReason"
                        >. Rejected: {{ account.rejectionReason }}</template
                    >
                </template>
                <template #trailing>
                    <template
                        v-if="
                            account.status === 'Pending' && isApprover && !isMe(account.proposedBy)
                        "
                    >
                        <UiButton @click="rejecting = account.id">Reject…</UiButton>
                        <UiButton
                            variant="primary"
                            :busy="approve.isPending.value"
                            @click="
                                approve
                                    .mutateAsync(account.id)
                                    .then(() => confirm('Bank account approved'))
                                    .catch(() => undefined)
                            "
                        >
                            Approve
                        </UiButton>
                    </template>
                    <span v-else-if="account.accountVersion" class="version"
                        >Version {{ account.accountVersion }}</span
                    >
                    <StatusPill :status="account.status" :tone="bankAccountTone[account.status]" />
                </template>
            </ListRow>
        </GroupedSection>

        <GroupedSection title="History">
            <dl>
                <DetailRow label="Created"
                    >{{ formatMoment(current.createdAt) }} by
                    {{ who(current.createdBy) }}</DetailRow
                >
                <DetailRow v-if="current.submittedAt" label="Submitted">
                    {{ formatMoment(current.submittedAt) }} by {{ who(current.submittedBy) }}
                </DetailRow>
                <DetailRow v-if="current.activatedAt" label="Activated">
                    {{ formatMoment(current.activatedAt) }} by {{ who(current.activatedBy) }}
                </DetailRow>
                <DetailRow v-if="current.blockedAt" label="Blocked">
                    {{ formatMoment(current.blockedAt) }} by {{ who(current.blockedBy) }}
                </DetailRow>
            </dl>
        </GroupedSection>

        <SupplierSheet v-model:open="editing" :supplier="current" />
        <BankAccountSheet
            v-model:open="proposing"
            :supplier-id="current.id"
            :holder="current.legalName"
        />
        <ReasonSheet
            v-model:open="blocking"
            title="Block supplier"
            description="Nothing is ordered from or paid to a blocked supplier until a supplier approver unblocks it."
            label="Reason"
            action="Block"
            destructive
            :busy="block.isPending.value"
            :error="block.error.value"
            @confirm="(reason) => confirmBlock(reason).catch(() => undefined)"
        />
        <ReasonSheet
            :open="rejecting !== null"
            title="Reject bank account"
            description="The account in force stays in force. The reason is kept with the proposal."
            label="Reason"
            action="Reject"
            destructive
            :busy="reject.isPending.value"
            :error="reject.error.value"
            @update:open="(open) => !open && (rejecting = null)"
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

.version {
    font: var(--text-callout);
    color: var(--label-secondary);
}
</style>

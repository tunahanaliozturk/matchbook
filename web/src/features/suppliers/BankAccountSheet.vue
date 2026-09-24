<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";

import { ApiProblem, describe } from "@/shared/api/problem";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useProposeBankAccount } from "./data";

// Proposes a new bank account. It is not used for payments until a supplier approver other than the proposer
// approves it; until then the account already in force stays in force.
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ supplierId: string; holder: string }>();

const { confirm } = useToasts();
const propose = useProposeBankAccount(() => props.supplierId);
const form = reactive({ iban: "", bic: "", accountHolder: "" });
const id = ref(uuidv7());

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    propose.reset();
    Object.assign(form, { iban: "", bic: "", accountHolder: props.holder });
});

const problem = computed(() => propose.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];

async function save() {
    await propose.mutateAsync({ id: id.value, ...form });
    confirm("Bank account proposed");
    open.value = false;
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        title="Propose bank account"
        description="A second supplier approver checks it against the supplier's letter before any payment uses it."
    >
        <form
            id="bank-account-form"
            class="form"
            novalidate
            @submit.prevent="save().catch(() => undefined)"
        >
            <InlineNotice
                v-if="problem && !(problem instanceof ApiProblem && problem.status === 400)"
                tone="error"
            >
                {{ describe(problem) }}
            </InlineNotice>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="IBAN"
                hint="Spaces are fine. Checked by length for the country and by its check digits."
                :errors="errorsFor('iban')"
            >
                <input
                    :id="fieldId"
                    v-model="form.iban"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    autocomplete="off"
                    spellcheck="false"
                    required
                />
            </FormField>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="BIC"
                :errors="errorsFor('bic')"
            >
                <input
                    :id="fieldId"
                    v-model="form.bic"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    autocomplete="off"
                    spellcheck="false"
                    required
                />
            </FormField>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="Account holder"
                :errors="errorsFor('accountHolder')"
            >
                <input
                    :id="fieldId"
                    v-model="form.accountHolder"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    required
                />
            </FormField>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="bank-account-form"
                variant="primary"
                :busy="propose.isPending.value"
                >Propose</UiButton
            >
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}
</style>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { useRouter } from "vue-router";

import { ApiProblem, describe } from "@/shared/api/problem";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import { useChangeSupplierDetails, useCreateSupplier, type SupplierResponse } from "./data";

// Creates a supplier as a draft, or corrects one's details. The id for a new supplier is chosen when the sheet
// opens, so pressing Create twice, or again after a timeout, makes one supplier.
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ supplier?: SupplierResponse }>();

const router = useRouter();
const { confirm } = useToasts();
const create = useCreateSupplier();
const change = useChangeSupplierDetails(() => props.supplier?.id ?? "");
const mutation = computed(() => (props.supplier ? change : create));

const form = reactive({
    legalName: "",
    taxId: "",
    countryCode: "",
    paymentTermsDays: 30,
    contactEmail: "",
});
const id = ref(uuidv7());

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    create.reset();
    change.reset();
    Object.assign(form, {
        legalName: props.supplier?.legalName ?? "",
        taxId: props.supplier?.taxId ?? "",
        countryCode: props.supplier?.countryCode ?? "",
        paymentTermsDays: props.supplier?.paymentTermsDays ?? 30,
        contactEmail: props.supplier?.contactEmail ?? "",
    });
});

const problem = computed(() => mutation.value.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];

async function save() {
    const details = { ...form, countryCode: form.countryCode.toUpperCase() };

    if (props.supplier) {
        await change.mutateAsync(details);
        confirm("Details saved");
        open.value = false;
        return;
    }

    const created = await create.mutateAsync({ id: id.value, ...details });
    confirm("Supplier created as a draft");
    open.value = false;
    await router.push({ name: "supplier", params: { supplierId: created.id } });
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        :title="supplier ? 'Edit details' : 'New supplier'"
        :description="
            supplier
                ? 'Changes to the name, country or terms of an active supplier are sent to every service that pays it.'
                : 'The supplier starts as a draft. It needs an approved bank account and a second person to activate it.'
        "
    >
        <form
            id="supplier-form"
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
                label="Legal name"
                :errors="errorsFor('legalName')"
            >
                <input
                    :id="fieldId"
                    v-model="form.legalName"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    autocomplete="organization"
                    required
                />
            </FormField>
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Tax ID"
                    hint="Checked for duplicates without spaces or punctuation."
                    :errors="errorsFor('taxId')"
                >
                    <input
                        :id="fieldId"
                        v-model="form.taxId"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        required
                    />
                </FormField>
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Country"
                    hint="Two letters, as in DE or GB."
                    :errors="errorsFor('countryCode')"
                >
                    <input
                        :id="fieldId"
                        v-model="form.countryCode"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        maxlength="2"
                        autocomplete="country"
                        required
                    />
                </FormField>
            </div>
            <div class="pair">
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Payment terms (days)"
                    hint="From the invoice date, 0 to 120."
                    :errors="errorsFor('paymentTermsDays')"
                >
                    <input
                        :id="fieldId"
                        v-model.number="form.paymentTermsDays"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="number"
                        min="0"
                        max="120"
                        required
                    />
                </FormField>
                <FormField
                    v-slot="{ id: fieldId, describedBy, invalid }"
                    label="Contact email"
                    :errors="errorsFor('contactEmail')"
                >
                    <input
                        :id="fieldId"
                        v-model="form.contactEmail"
                        :aria-describedby="describedBy"
                        :aria-invalid="invalid"
                        type="email"
                        autocomplete="email"
                        required
                    />
                </FormField>
            </div>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="supplier-form"
                variant="primary"
                :busy="mutation.isPending.value"
            >
                {{ supplier ? "Save" : "Create" }}
            </UiButton>
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}

.pair {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-4);
}
</style>

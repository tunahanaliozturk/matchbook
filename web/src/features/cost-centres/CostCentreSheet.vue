<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";

import { colleagues } from "@/auth/people";
import type { Role } from "@/auth/roles";
import { ApiProblem, describe } from "@/shared/api/problem";
import { uuidv7 } from "@/shared/ids";
import FormField from "@/shared/ui/FormField.vue";
import InlineNotice from "@/shared/ui/InlineNotice.vue";
import UiButton from "@/shared/ui/UiButton.vue";
import UiSheet from "@/shared/ui/UiSheet.vue";
import { useToasts } from "@/shared/ui/toasts";

import {
    useChangeCostCentre,
    useCreateCostCentre,
    useReloadCostCentre,
    type CostCentreView,
} from "./data";

// Creates a cost centre, or changes one's name, manager or state. An edit is made against the version the editor
// saw; if someone saved in between, the service refuses it rather than overwrite their change, and the sheet offers
// the current version to start again from. A new cost centre's id is chosen when the sheet opens, so a double
// submit creates one.
const open = defineModel<boolean>("open", { required: true });
const props = defineProps<{ costCentre?: CostCentreView }>();

const { confirm } = useToasts();
const create = useCreateCostCentre();
const reload = useReloadCostCentre();

// What the edit is made against: the row that was clicked, or what a reload found.
const base = ref<CostCentreView>();
const change = useChangeCostCentre(() => base.value?.code ?? "");
const mutation = computed(() => (base.value ? change : create));

// The manager takes a requisition's first approval, so only people who hold an approving role are offered.
const approving: readonly Role[] = ["approver", "finance-approver", "cfo"];
const managers = colleagues.filter((colleague) =>
    colleague.roles.some((role) => approving.includes(role)),
);

const form = reactive({ code: "", name: "", managerId: "", isActive: true });
const id = ref(uuidv7());

function fill(from: CostCentreView | undefined) {
    base.value = from;
    Object.assign(form, {
        code: from?.code ?? "",
        name: from?.name ?? "",
        managerId: from?.managerId ?? "",
        isActive: from?.isActive ?? true,
    });
}

watch(open, (isOpen) => {
    if (!isOpen) return;
    id.value = uuidv7();
    create.reset();
    change.reset();
    fill(props.costCentre);
});

const problem = computed(() => mutation.value.error.value);
const errorsFor = (field: string) =>
    problem.value instanceof ApiProblem ? problem.value.errorsFor(field) : [];
const conflict = computed(
    () => problem.value instanceof ApiProblem && problem.value.code === "concurrency.conflict",
);

const reloading = ref(false);
const reloadError = ref<unknown>();

async function startAgain() {
    if (!base.value) return;
    reloading.value = true;
    reloadError.value = undefined;
    try {
        fill(await reload(base.value.code));
        change.reset();
    } catch (error) {
        reloadError.value = error;
    } finally {
        reloading.value = false;
    }
}

async function save() {
    if (base.value) {
        await change.mutateAsync({
            version: base.value.version,
            name: form.name,
            managerId: form.managerId,
            isActive: form.isActive,
        });
        confirm("Cost centre saved");
        open.value = false;
        return;
    }

    await create.mutateAsync({
        id: id.value,
        code: form.code.trim().toUpperCase(),
        name: form.name,
        managerId: form.managerId,
    });
    confirm("Cost centre created");
    open.value = false;
}
</script>

<template>
    <UiSheet
        v-model:open="open"
        :title="base ? `Edit ${base.code}` : 'New cost centre'"
        :description="
            base
                ? 'Requisitions learns of the change and sends new requisitions on this cost centre to the manager chosen here.'
                : 'A cost centre starts active. Open a budget for it before anyone can requisition against it.'
        "
    >
        <form
            id="cost-centre-form"
            class="form"
            novalidate
            @submit.prevent="save().catch(() => undefined)"
        >
            <InlineNotice v-if="conflict" tone="caution">
                <p>
                    Someone else changed this cost centre after you opened it. Reload it to see
                    their change, then make yours again.
                </p>
                <div class="notice-action">
                    <UiButton :busy="reloading" @click="startAgain()">Reload</UiButton>
                </div>
            </InlineNotice>
            <InlineNotice
                v-else-if="problem && !(problem instanceof ApiProblem && problem.status === 400)"
                tone="error"
            >
                {{ describe(problem) }}
            </InlineNotice>
            <InlineNotice v-if="reloadError" tone="error">{{ describe(reloadError) }}</InlineNotice>

            <FormField
                v-if="!base"
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="Code"
                hint="Two to five capital letters, a hyphen, then two to twelve capital letters or digits, as in ENG-PLATFORM. It cannot be changed later."
                :errors="errorsFor('code')"
            >
                <input
                    :id="fieldId"
                    v-model="form.code"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    class="code"
                    autocomplete="off"
                    spellcheck="false"
                    maxlength="18"
                    required
                />
            </FormField>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="Name"
                :errors="errorsFor('name')"
            >
                <input
                    :id="fieldId"
                    v-model="form.name"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    autocomplete="off"
                    required
                />
            </FormField>
            <FormField
                v-slot="{ id: fieldId, describedBy, invalid }"
                label="Manager"
                hint="Gives every requisition on this cost centre its first approval."
                :errors="errorsFor('managerId')"
            >
                <select
                    :id="fieldId"
                    v-model="form.managerId"
                    :aria-describedby="describedBy"
                    :aria-invalid="invalid"
                    required
                >
                    <option value="" disabled>Choose a manager</option>
                    <option v-for="manager in managers" :key="manager.id" :value="manager.id">
                        {{ manager.name }}
                    </option>
                </select>
            </FormField>
            <div v-if="base" class="switch-field">
                <label class="switch">
                    <input
                        v-model="form.isActive"
                        type="checkbox"
                        role="switch"
                        aria-describedby="cost-centre-active-hint"
                    />
                    Active
                </label>
                <p id="cost-centre-active-hint" class="hint">
                    An inactive cost centre refuses new requisitions and cannot be given a budget.
                    What is already reserved or ordered on it stands.
                </p>
            </div>
        </form>
        <template #footer>
            <UiButton @click="open = false">Cancel</UiButton>
            <UiButton
                type="submit"
                form="cost-centre-form"
                variant="primary"
                :disabled="conflict"
                :busy="mutation.isPending.value"
            >
                {{ base ? "Save" : "Create" }}
            </UiButton>
        </template>
    </UiSheet>
</template>

<style scoped>
.form {
    display: grid;
    gap: var(--space-4);
}

.notice-action {
    margin-top: var(--space-2);
}

.code {
    font-variant-numeric: tabular-nums;
    text-transform: uppercase;
}

.switch-field {
    display: grid;
    gap: var(--space-1);
}

.switch {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
    justify-self: start;
    font: var(--text-callout);
    font-weight: 500;
}

/* The platform's switch, drawn over a real checkbox so it keeps the checkbox's keyboard and form behaviour. */
.switch input {
    position: relative;
    width: 2.375rem;
    height: 1.375rem;
    margin: 0;
    background: var(--neutral-tint);
    border-radius: var(--radius-capsule);
    box-shadow: inset 0 0 0 0.5px var(--field-border);
    appearance: none;
    transition: background-color var(--duration) var(--ease);
}

.switch input::after {
    content: "";
    position: absolute;
    top: 2px;
    left: 2px;
    width: calc(1.375rem - 4px);
    height: calc(1.375rem - 4px);
    background: var(--control);
    border-radius: 50%;
    box-shadow: var(--shadow-control);
    transition: transform var(--duration) var(--ease);
}

.switch input:checked {
    background: var(--accent);
}

.switch input:checked::after {
    background: var(--label-on-accent);
    transform: translateX(1rem);
}

.hint {
    font: var(--text-caption);
    letter-spacing: var(--tracking-caption);
    color: var(--label-secondary);
}
</style>

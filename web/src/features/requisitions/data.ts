import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { computed, toValue, type MaybeRefOrGetter } from "vue";

import {
    approveRequisition,
    cancelRequisition,
    createRequisition,
    editRequisition,
    getRequisition,
    listCostCentreOptions,
    listRequisitions,
    listSupplierOptions,
    rejectRequisition,
    submitRequisition,
} from "@/shared/api/generated/requisitions/sdk.gen";
import type {
    ApprovalDecision,
    ApprovalStepKind,
    CreateRequisitionRequest,
    EditRequisitionRequest,
    RequisitionStatus,
    RequisitionSummary,
    RequisitionView,
    StepView,
    TimelineEntryView,
} from "@/shared/api/generated/requisitions/types.gen";

export type {
    ApprovalDecision,
    ApprovalStepKind,
    RequisitionStatus,
    RequisitionSummary,
    RequisitionView,
    StepView,
    TimelineEntryView,
};

// Query keys, in one place, so a mutation can say exactly what it made stale.
export const requisitionKeys = {
    all: ["requisitions"] as const,
    list: (filter: RequisitionFilter, limit: number) =>
        ["requisitions", "list", filter, limit] as const,
    one: (id: string) => ["requisitions", "one", id] as const,
    costCentres: ["requisitions", "cost-centres"] as const,
    suppliers: ["requisitions", "suppliers"] as const,
};

/** The requisitions the caller may read, newest first, a page at a time along the service's keyset cursor. */
/** Only requisitions in one of these statuses; empty for all of them. */
export interface RequisitionFilter {
    status?: RequisitionStatus[];
}

export function useRequisitionList(filter: MaybeRefOrGetter<RequisitionFilter> = {}, limit = 50) {
    return useInfiniteQuery({
        queryKey: computed(() => requisitionKeys.list(toValue(filter), limit)),
        initialPageParam: undefined as number | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listRequisitions({
                    query: {
                        ...toValue(filter),
                        limit,
                        ...(pageParam === undefined ? {} : { after: pageParam }),
                    },
                    signal,
                })
            ).data,
        getNextPageParam: (page) => page.nextCursor ?? undefined,
    });
}

/** A submitted requisition waits on Budgets, so the page asks again until the answer has arrived. */
export function useRequisition(id: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => requisitionKeys.one(toValue(id))),
        queryFn: async ({ signal }) =>
            (await getRequisition({ path: { id: toValue(id) }, signal })).data,
        refetchInterval: (query) => (query.state.data?.status === "Submitted" ? 2_000 : false),
    });
}

/** The form's choices, from Requisitions' own copies: a requester may read neither Budgets nor Suppliers. */
export const useCostCentreOptions = (enabled: MaybeRefOrGetter<boolean>) =>
    useQuery({
        queryKey: requisitionKeys.costCentres,
        queryFn: async ({ signal }) => (await listCostCentreOptions({ signal })).data,
        enabled: computed(() => toValue(enabled)),
    });

export const useSupplierOptions = (enabled: MaybeRefOrGetter<boolean>) =>
    useQuery({
        queryKey: requisitionKeys.suppliers,
        queryFn: async ({ signal }) => (await listSupplierOptions({ signal })).data,
        enabled: computed(() => toValue(enabled)),
    });

/**
 * Every command answers with the requisition as it now is (docs/design.md, "API"), so a success replaces the cached
 * requisition directly and only the lists, whose membership may have changed, are fetched again.
 */
function useRequisitionCommand<TInput>(run: (input: TInput) => Promise<RequisitionView>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: run,
        onSuccess: (requisition) => {
            queries.setQueryData(requisitionKeys.one(requisition.id), requisition);
            void queries.invalidateQueries({ queryKey: [...requisitionKeys.all, "list"] });
            // A decision changes what the approvals queue offers. That list belongs to the approvals feature, which
            // this one does not import, so it is named by its key.
            void queries.invalidateQueries({ queryKey: ["approvals"] });
        },
    });
}

export const useCreateRequisition = () =>
    useRequisitionCommand(
        async (body: CreateRequisitionRequest) => (await createRequisition({ body })).data,
    );

export const useEditRequisition = (id: MaybeRefOrGetter<string>) =>
    useRequisitionCommand(
        async (body: EditRequisitionRequest) =>
            (await editRequisition({ path: { id: toValue(id) }, body })).data,
    );

export const useSubmitRequisition = (id: MaybeRefOrGetter<string>) =>
    useRequisitionCommand(
        async () => (await submitRequisition({ path: { id: toValue(id) } })).data,
    );

export const useCancelRequisition = (id: MaybeRefOrGetter<string>) =>
    useRequisitionCommand(
        async () => (await cancelRequisition({ path: { id: toValue(id) } })).data,
    );

export const useApproveRequisition = (id: MaybeRefOrGetter<string>) =>
    useRequisitionCommand(
        async () => (await approveRequisition({ path: { id: toValue(id) } })).data,
    );

export const useRejectRequisition = (id: MaybeRefOrGetter<string>) =>
    useRequisitionCommand(
        async (reason: string) =>
            (await rejectRequisition({ path: { id: toValue(id) }, body: { reason } })).data,
    );

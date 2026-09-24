import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { computed, toValue, type MaybeRefOrGetter } from "vue";

import {
    activateSupplier,
    approveBankAccount,
    blockSupplier,
    changeSupplierDetails,
    createSupplier,
    getSupplier,
    listSuppliers,
    proposeBankAccount,
    rejectBankAccount,
    submitSupplier,
    unblockSupplier,
} from "@/shared/api/generated/suppliers/sdk.gen";
import type {
    ChangeSupplierDetailsRequest,
    CreateSupplierRequest,
    ProposeBankAccountRequest,
    SupplierResponse,
    SupplierStatus,
} from "@/shared/api/generated/suppliers/types.gen";

export type { SupplierResponse, SupplierStatus };

export interface SupplierFilter {
    status?: SupplierStatus;
    hasPendingBankAccount?: boolean;
}

// Query keys, in one place, so a mutation can say exactly what it made stale.
export const supplierKeys = {
    all: ["suppliers"] as const,
    list: (filter: SupplierFilter) => ["suppliers", "list", filter] as const,
    one: (id: string) => ["suppliers", "one", id] as const,
};

/** Suppliers a page at a time, following the keyset cursor the service returns. */
export function useSupplierList(filter: MaybeRefOrGetter<SupplierFilter>, limit = 50) {
    return useInfiniteQuery({
        queryKey: computed(() => supplierKeys.list(toValue(filter))),
        initialPageParam: undefined as string | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listSuppliers({
                    query: {
                        ...toValue(filter),
                        limit,
                        ...(pageParam ? { after: pageParam } : {}),
                    },
                    signal,
                })
            ).data,
        getNextPageParam: (page) => page.next ?? undefined,
    });
}

export function useSupplier(id: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => supplierKeys.one(toValue(id))),
        queryFn: async ({ signal }) =>
            (await getSupplier({ path: { supplierId: toValue(id) }, signal })).data,
    });
}

/**
 * Every command answers with the supplier as it now is (docs/design.md, "API"), so a success replaces the cached
 * supplier directly and only the lists, whose membership may have changed, are fetched again.
 */
function useSupplierCommand<TInput>(run: (input: TInput) => Promise<SupplierResponse>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: run,
        onSuccess: (supplier) => {
            queries.setQueryData(supplierKeys.one(supplier.id), supplier);
            void queries.invalidateQueries({ queryKey: [...supplierKeys.all, "list"] });
        },
    });
}

export const useCreateSupplier = () =>
    useSupplierCommand(
        async (body: CreateSupplierRequest) => (await createSupplier({ body })).data,
    );

export const useChangeSupplierDetails = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async (body: ChangeSupplierDetailsRequest) =>
            (await changeSupplierDetails({ path: { supplierId: toValue(id) }, body })).data,
    );

export const useProposeBankAccount = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async (body: ProposeBankAccountRequest) =>
            (await proposeBankAccount({ path: { supplierId: toValue(id) }, body })).data,
    );

export const useApproveBankAccount = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async (accountId: string) =>
            (
                await approveBankAccount({
                    path: { supplierId: toValue(id), bankAccountId: accountId },
                })
            ).data,
    );

export const useRejectBankAccount = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async ({ accountId, reason }: { accountId: string; reason: string }) =>
            (
                await rejectBankAccount({
                    path: { supplierId: toValue(id), bankAccountId: accountId },
                    body: { reason },
                })
            ).data,
    );

export const useSubmitSupplier = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async () => (await submitSupplier({ path: { supplierId: toValue(id) } })).data,
    );

export const useActivateSupplier = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async () => (await activateSupplier({ path: { supplierId: toValue(id) } })).data,
    );

export const useBlockSupplier = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async (reason: string) =>
            (await blockSupplier({ path: { supplierId: toValue(id) }, body: { reason } })).data,
    );

export const useUnblockSupplier = (id: MaybeRefOrGetter<string>) =>
    useSupplierCommand(
        async () => (await unblockSupplier({ path: { supplierId: toValue(id) } })).data,
    );

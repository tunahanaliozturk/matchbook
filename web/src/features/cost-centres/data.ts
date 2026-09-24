import { useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/vue-query";
import { toValue, type MaybeRefOrGetter } from "vue";

import {
    changeCostCentre,
    createCostCentre,
    getCostCentre,
    listCostCentres,
} from "@/shared/api/generated/budgets/sdk.gen";
import type {
    ChangeCostCentreRequest,
    CostCentreView,
    CreateCostCentreRequest,
} from "@/shared/api/generated/budgets/types.gen";

export type { CostCentreView };

// Query keys, in one place, so a mutation can say exactly what it made stale.
export const costCentreKeys = {
    all: ["cost-centres"] as const,
    list: () => ["cost-centres", "list"] as const,
    one: (code: string) => ["cost-centres", "one", code] as const,
};

/** Cost centres in code order, a page at a time, following the keyset cursor the service returns. */
export function useCostCentreList(limit = 50) {
    return useInfiniteQuery({
        queryKey: costCentreKeys.list(),
        initialPageParam: undefined as string | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listCostCentres({
                    query: { limit, ...(pageParam ? { after: pageParam } : {}) },
                    signal,
                })
            ).data,
        getNextPageParam: (page) => page.next ?? undefined,
    });
}

/**
 * Reads a cost centre afresh, for an edit that lost a race to someone else's. The list is fetched again too,
 * since its row shows the version that just lost.
 */
export function useReloadCostCentre() {
    const queries = useQueryClient();

    return async (code: string) => {
        const current = await queries.fetchQuery({
            queryKey: costCentreKeys.one(code),
            queryFn: async ({ signal }) => (await getCostCentre({ path: { code }, signal })).data,
            staleTime: 0,
        });
        void queries.invalidateQueries({ queryKey: costCentreKeys.list() });
        return current;
    };
}

/** A command answers with the cost centre as it now is, so it replaces the cached one and the list is refetched. */
function useCostCentreCommand<TInput>(run: (input: TInput) => Promise<CostCentreView>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: run,
        onSuccess: (costCentre) => {
            queries.setQueryData(costCentreKeys.one(costCentre.code), costCentre);
            void queries.invalidateQueries({ queryKey: costCentreKeys.list() });
        },
    });
}

export const useCreateCostCentre = () =>
    useCostCentreCommand(
        async (body: CreateCostCentreRequest) => (await createCostCentre({ body })).data,
    );

export const useChangeCostCentre = (code: MaybeRefOrGetter<string>) =>
    useCostCentreCommand(
        async (body: ChangeCostCentreRequest) =>
            (await changeCostCentre({ path: { code: toValue(code) }, body })).data,
    );

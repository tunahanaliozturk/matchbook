import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { computed, toValue, type MaybeRefOrGetter } from "vue";

import {
    changeAllotment,
    getBudget,
    getCostCentre,
    listBudgets,
    listCostCentres,
    listLedger,
    listOverspentBudgets,
    openBudget,
} from "@/shared/api/generated/budgets/sdk.gen";
import type {
    BudgetView,
    LedgerEntryView,
    OpenBudgetRequest,
} from "@/shared/api/generated/budgets/types.gen";

export type { BudgetView, LedgerEntryView };

export interface BudgetFilter {
    fiscalYear: number;
    /** Only budgets consumed past their allotment: the service's overspend report. */
    overspent?: boolean;
}

// Query keys, in one place, so a mutation can say exactly what it made stale. The cost centre keys are the cost
// centres feature's own, so a budget page and the cost centre list share one cached copy of a cost centre.
export const budgetKeys = {
    all: ["budgets"] as const,
    list: (filter: BudgetFilter) => ["budgets", "list", filter] as const,
    one: (id: string) => ["budgets", "one", id] as const,
    ledger: (id: string) => ["budgets", "ledger", id] as const,
    costCentre: (code: string) => ["cost-centres", "one", code] as const,
    costCentreChoices: ["cost-centres", "choices"] as const,
};

/** One year's budgets in cost centre order, a page at a time, following the keyset cursor. */
export function useBudgetList(filter: MaybeRefOrGetter<BudgetFilter>, limit = 50) {
    return useInfiniteQuery({
        queryKey: computed(() => budgetKeys.list(toValue(filter))),
        initialPageParam: undefined as string | undefined,
        queryFn: async ({ pageParam, signal }) => {
            const { fiscalYear, overspent } = toValue(filter);
            const query = { fiscalYear, limit, ...(pageParam ? { after: pageParam } : {}) };
            return overspent
                ? (await listOverspentBudgets({ query, signal })).data
                : (await listBudgets({ query, signal })).data;
        },
        getNextPageParam: (page) => page.next ?? undefined,
    });
}

export function useBudget(id: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => budgetKeys.one(toValue(id))),
        queryFn: async ({ signal }) =>
            (await getBudget({ path: { id: toValue(id) }, signal })).data,
    });
}

/** A budget's ledger in the order it was written. The cursor is the last sequence seen, a number on the way in. */
export function useLedger(id: MaybeRefOrGetter<string>, limit = 25) {
    return useInfiniteQuery({
        queryKey: computed(() => budgetKeys.ledger(toValue(id))),
        initialPageParam: undefined as number | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listLedger({
                    path: { id: toValue(id) },
                    query: { limit, ...(pageParam === undefined ? {} : { after: pageParam }) },
                    signal,
                })
            ).data,
        getNextPageParam: (page) => (page.next === null ? undefined : Number(page.next)),
    });
}

/** The cost centre a budget belongs to, for its name and manager; the budget itself carries only the code. */
export function useCostCentreOf(code: MaybeRefOrGetter<string | undefined>) {
    return useQuery({
        queryKey: computed(() => budgetKeys.costCentre(toValue(code) ?? "")),
        queryFn: async ({ signal }) =>
            (await getCostCentre({ path: { code: toValue(code) ?? "" }, signal })).data,
        enabled: computed(() => Boolean(toValue(code))),
    });
}

/**
 * The cost centres a budget can be opened for: active ones only, since the service refuses the rest. One page of
 * the service's largest, 200, which is every cost centre here; past that the picker needs a search instead.
 */
export function useCostCentreChoices() {
    return useQuery({
        queryKey: budgetKeys.costCentreChoices,
        queryFn: async ({ signal }) =>
            (await listCostCentres({ query: { limit: 200 }, signal })).data.items.filter(
                (costCentre) => costCentre.isActive,
            ),
    });
}

/** Opening answers with the new budget, which goes straight into the cache; the lists are fetched again. */
export function useOpenBudget() {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: async (body: OpenBudgetRequest) => (await openBudget({ body })).data,
        onSuccess: (budget) => {
            queries.setQueryData(budgetKeys.one(budget.id), budget);
            void queries.invalidateQueries({ queryKey: [...budgetKeys.all, "list"] });
        },
    });
}

/**
 * A change answers with the balance right after it, read under the budget's row lock, so that replaces the cached
 * budget; the ledger has a new entry and the lists new figures, so those are fetched again.
 */
export function useChangeAllotment(id: MaybeRefOrGetter<string>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: async (body: { id: string; change: number }) =>
            (await changeAllotment({ path: { id: toValue(id) }, body })).data,
        onSuccess: ({ budget }) => {
            queries.setQueryData(budgetKeys.one(budget.id), budget);
            void queries.invalidateQueries({ queryKey: budgetKeys.ledger(budget.id) });
            void queries.invalidateQueries({ queryKey: [...budgetKeys.all, "list"] });
        },
    });
}

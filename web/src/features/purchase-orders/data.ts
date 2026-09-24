import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { computed, toValue, type MaybeRefOrGetter } from "vue";

import {
    amendPurchaseOrderLine,
    cancelPurchaseOrder,
    getPurchaseOrder,
    issuePurchaseOrder,
    listGoodsReceipts,
    listPurchaseOrders,
    recordGoodsReceipt,
    shortClosePurchaseOrder,
} from "@/shared/api/generated/purchasing/sdk.gen";
import type {
    AmendLineRequest,
    GoodsReceiptView,
    OrderLineView,
    PurchaseOrderStatus,
    PurchaseOrderSummary,
    PurchaseOrderView,
    RecordReceiptRequest,
} from "@/shared/api/generated/purchasing/types.gen";

export type {
    GoodsReceiptView,
    OrderLineView,
    PurchaseOrderStatus,
    PurchaseOrderSummary,
    PurchaseOrderView,
};

export interface PurchaseOrderFilter {
    status?: PurchaseOrderStatus;
    awaitingGoods?: boolean;
}

// Query keys, in one place, so a mutation can say exactly what it made stale.
export const purchaseOrderKeys = {
    all: ["purchase-orders"] as const,
    list: (filter: PurchaseOrderFilter) => ["purchase-orders", "list", filter] as const,
    one: (id: string) => ["purchase-orders", "one", id] as const,
    receipts: (id: string) => ["purchase-orders", "receipts", id] as const,
};

/** Orders a page at a time, newest first, following the keyset cursor the service returns. */
export function usePurchaseOrderList(filter: MaybeRefOrGetter<PurchaseOrderFilter>, limit = 50) {
    return useInfiniteQuery({
        queryKey: computed(() => purchaseOrderKeys.list(toValue(filter))),
        initialPageParam: undefined as string | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listPurchaseOrders({
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

/**
 * One order. Issuing only asks Budgets for the funds, so while the order waits for that answer it is read again
 * every two seconds, and the page moves on to issued, or back to draft with the reason, by itself.
 */
export function usePurchaseOrder(id: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => purchaseOrderKeys.one(toValue(id))),
        queryFn: async ({ signal }) =>
            (await getPurchaseOrder({ path: { id: toValue(id) }, signal })).data,
        refetchInterval: (query) =>
            query.state.data?.status === "CommitmentPending" ? 2000 : false,
    });
}

/** Every delivery recorded against the order, oldest first. */
export function useGoodsReceipts(id: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => purchaseOrderKeys.receipts(toValue(id))),
        queryFn: async ({ signal }) =>
            (await listGoodsReceipts({ path: { id: toValue(id) }, signal })).data,
    });
}

/**
 * Every command on an order answers with the order as it now is (docs/design.md, "API"), so a success replaces the
 * cached order directly and only the lists, whose membership may have changed, are fetched again.
 */
function useOrderCommand<TInput>(run: (input: TInput) => Promise<PurchaseOrderView>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: run,
        onSuccess: (order) => {
            queries.setQueryData(purchaseOrderKeys.one(order.id), order);
            void queries.invalidateQueries({ queryKey: [...purchaseOrderKeys.all, "list"] });
        },
    });
}

export const useAmendLine = (id: MaybeRefOrGetter<string>) =>
    useOrderCommand(
        async ({ lineNumber, ...body }: AmendLineRequest & { lineNumber: number }) =>
            (await amendPurchaseOrderLine({ path: { id: toValue(id), lineNumber }, body })).data,
    );

export const useIssueOrder = (id: MaybeRefOrGetter<string>) =>
    useOrderCommand(async () => (await issuePurchaseOrder({ path: { id: toValue(id) } })).data);

export const useShortCloseOrder = (id: MaybeRefOrGetter<string>) =>
    useOrderCommand(
        async () => (await shortClosePurchaseOrder({ path: { id: toValue(id) } })).data,
    );

export const useCancelOrder = (id: MaybeRefOrGetter<string>) =>
    useOrderCommand(async () => (await cancelPurchaseOrder({ path: { id: toValue(id) } })).data);

/**
 * A receipt answers with itself, not the order, so the order's received quantities are read again. The mutation
 * settles once they have been, so the sheet closes onto lines that already show the delivery.
 */
export function useRecordReceipt(id: MaybeRefOrGetter<string>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: async (body: RecordReceiptRequest) =>
            (await recordGoodsReceipt({ path: { id: toValue(id) }, body })).data,
        onSuccess: async () => {
            void queries.invalidateQueries({ queryKey: [...purchaseOrderKeys.all, "list"] });
            await Promise.all([
                queries.invalidateQueries({ queryKey: purchaseOrderKeys.one(toValue(id)) }),
                queries.invalidateQueries({ queryKey: purchaseOrderKeys.receipts(toValue(id)) }),
            ]);
        },
    });
}

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { computed, toValue, type MaybeRefOrGetter } from "vue";

import {
    acceptPriceVariance,
    captureInvoice,
    clearSuspectedDuplicate,
    getInvoice,
    listBillablePurchaseOrders,
    listBillableSuppliers,
    listInvoiceExceptions,
    listInvoices,
} from "@/shared/api/generated/payables/sdk.gen";
import type {
    BillablePurchaseOrder,
    CaptureInvoiceRequest,
    InvoiceStatus,
    InvoiceSummary,
    InvoiceView,
} from "@/shared/api/generated/payables/types.gen";

export type { BillablePurchaseOrder, InvoiceStatus, InvoiceSummary, InvoiceView };

export interface InvoiceFilter {
    status?: InvoiceStatus;
}

// Query keys, in one place, so a mutation can say exactly what it made stale.
export const invoiceKeys = {
    all: ["invoices"] as const,
    list: (filter: InvoiceFilter) => ["invoices", "list", filter] as const,
    exceptions: ["invoices", "list", "exceptions"] as const,
    one: (id: string) => ["invoices", "one", id] as const,
    suppliers: ["invoices", "billable-suppliers"] as const,
    orders: (supplierId: string) => ["invoices", "billable-orders", supplierId] as const,
};

/** Invoices a page at a time, newest first, following the keyset cursor the service returns. */
export function useInvoiceList(filter: MaybeRefOrGetter<InvoiceFilter>, limit = 50) {
    return useInfiniteQuery({
        queryKey: computed(() => invoiceKeys.list(toValue(filter))),
        initialPageParam: undefined as string | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listInvoices({
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

/** The approvers' queue, oldest first: whoever has waited longest is decided first. */
export function useInvoiceExceptions(limit = 50) {
    return useInfiniteQuery({
        queryKey: [...invoiceKeys.exceptions, limit],
        initialPageParam: undefined as string | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listInvoiceExceptions({
                    query: { limit, ...(pageParam ? { after: pageParam } : {}) },
                    signal,
                })
            ).data,
        getNextPageParam: (page) => page.next ?? undefined,
    });
}

// The order and the goods reach Payables as events, and the invoice moves on when they do, often seconds later.
const waiting: readonly InvoiceStatus[] = ["Captured", "AwaitingPurchaseOrder", "AwaitingReceipt"];

export function useInvoice(id: MaybeRefOrGetter<string | null | undefined>) {
    return useQuery({
        queryKey: computed(() => invoiceKeys.one(toValue(id) ?? "")),
        queryFn: async ({ signal }) =>
            (await getInvoice({ path: { id: toValue(id) ?? "" }, signal })).data,
        enabled: computed(() => Boolean(toValue(id))),
        refetchInterval: (query) =>
            query.state.data && waiting.includes(query.state.data.status) ? 5_000 : false,
    });
}

// ponytail: the capture form offers the first 200 suppliers and orders; a search field when a clerk has more.
const lookupLimit = 200;

/** Suppliers with an order an invoice can still bill, by name, from Payables' own copy (a clerk cannot read Suppliers). */
export function useBillableSuppliers(enabled: MaybeRefOrGetter<boolean>) {
    return useQuery({
        queryKey: invoiceKeys.suppliers,
        queryFn: async ({ signal }) =>
            (await listBillableSuppliers({ query: { limit: lookupLimit }, signal })).data.items,
        select: (suppliers) =>
            [...suppliers].sort((a, b) => a.legalName.localeCompare(b.legalName)),
        enabled: computed(() => toValue(enabled)),
    });
}

/** The orders an invoice for this supplier can bill, newest first, with the lines to prefill it from. */
export function useBillableOrders(supplierId: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => invoiceKeys.orders(toValue(supplierId))),
        queryFn: async ({ signal }) =>
            (
                await listBillablePurchaseOrders({
                    query: { supplierId: toValue(supplierId), limit: lookupLimit },
                    signal,
                })
            ).data.items,
        enabled: computed(() => toValue(supplierId) !== ""),
    });
}

/**
 * Every command answers with the invoice as it now is (docs/design.md, "API"), so a success replaces the cached
 * invoice directly and only the lists, whose membership may have changed, are fetched again.
 */
function useInvoiceCommand<TInput>(run: (input: TInput) => Promise<InvoiceView>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: run,
        onSuccess: (invoice) => {
            queries.setQueryData(invoiceKeys.one(invoice.id), invoice);
            void queries.invalidateQueries({ queryKey: [...invoiceKeys.all, "list"] });
        },
    });
}

export const useCaptureInvoice = () =>
    useInvoiceCommand(async (body: CaptureInvoiceRequest) => (await captureInvoice({ body })).data);

export const useAcceptPriceVariance = (id: MaybeRefOrGetter<string>) =>
    useInvoiceCommand(
        async (reason: string) =>
            (await acceptPriceVariance({ path: { id: toValue(id) }, body: { reason } })).data,
    );

export const useClearSuspectedDuplicate = (id: MaybeRefOrGetter<string>) =>
    useInvoiceCommand(
        async () => (await clearSuspectedDuplicate({ path: { id: toValue(id) } })).data,
    );

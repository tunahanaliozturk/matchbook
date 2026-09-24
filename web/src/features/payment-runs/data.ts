import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { computed, toValue, type MaybeRefOrGetter } from "vue";

import {
    cancelPaymentRun,
    downloadPaymentFile,
    draftPaymentRun,
    getPaymentRun,
    listPaymentRuns,
    releasePaymentRun,
} from "@/shared/api/generated/payables/sdk.gen";
import type {
    CreditorDropReason,
    CreditorStatus,
    DraftPaymentRunRequest,
    PaymentRunStatus,
    PaymentRunSummary,
    PaymentRunView,
} from "@/shared/api/generated/payables/types.gen";

import { fileNameOf, saveFile } from "./download";

export type {
    CreditorDropReason,
    CreditorStatus,
    PaymentRunStatus,
    PaymentRunSummary,
    PaymentRunView,
};

export interface PaymentRunFilter {
    status?: PaymentRunStatus;
}

// Query keys, in one place, so a mutation can say exactly what it made stale.
export const paymentRunKeys = {
    all: ["payment-runs"] as const,
    list: (filter: PaymentRunFilter) => ["payment-runs", "list", filter] as const,
    one: (id: string) => ["payment-runs", "one", id] as const,
};

/** Payment runs a page at a time, newest first, following the keyset cursor the service returns. */
export function usePaymentRunList(filter: MaybeRefOrGetter<PaymentRunFilter>, limit = 50) {
    return useInfiniteQuery({
        queryKey: computed(() => paymentRunKeys.list(toValue(filter))),
        initialPageParam: undefined as string | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listPaymentRuns({
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

export function usePaymentRun(id: MaybeRefOrGetter<string>) {
    return useQuery({
        queryKey: computed(() => paymentRunKeys.one(toValue(id))),
        queryFn: async ({ signal }) =>
            (await getPaymentRun({ path: { id: toValue(id) }, signal })).data,
    });
}

/**
 * Every command answers with the run as it now is (docs/design.md, "API"), so a success replaces the cached run
 * directly and only the lists, whose membership may have changed, are fetched again.
 */
function usePaymentRunCommand<TInput>(run: (input: TInput) => Promise<PaymentRunView>) {
    const queries = useQueryClient();

    return useMutation({
        mutationFn: run,
        onSuccess: (paymentRun) => {
            queries.setQueryData(paymentRunKeys.one(paymentRun.id), paymentRun);
            void queries.invalidateQueries({ queryKey: [...paymentRunKeys.all, "list"] });
        },
    });
}

export const useDraftPaymentRun = () =>
    usePaymentRunCommand(
        async (body: DraftPaymentRunRequest) => (await draftPaymentRun({ body })).data,
    );

export const useReleasePaymentRun = (id: MaybeRefOrGetter<string>) =>
    usePaymentRunCommand(async () => (await releasePaymentRun({ path: { id: toValue(id) } })).data);

export const useCancelPaymentRun = (id: MaybeRefOrGetter<string>) =>
    usePaymentRunCommand(async () => (await cancelPaymentRun({ path: { id: toValue(id) } })).data);

/**
 * Fetches the bank file with the bearer token in its header, like every other call, and hands it to the browser as
 * a download. A plain link would need the token in its address, where it would outlive the page in history and logs.
 */
export function useDownloadPaymentFile(id: MaybeRefOrGetter<string>) {
    return useMutation({
        mutationFn: async () => {
            const { data, response } = await downloadPaymentFile({ path: { id: toValue(id) } });
            const fallback = `payment-run-${toValue(id)}.xml`;
            saveFile(data, fileNameOf(response.headers.get("content-disposition"), fallback));
        },
    });
}

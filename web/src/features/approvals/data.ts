import { useInfiniteQuery } from "@tanstack/vue-query";

import { listApprovals } from "@/shared/api/generated/requisitions/sdk.gen";

// The requisitions feature invalidates ["approvals"] after every command, since a decision changes this list.
export const approvalKeys = {
    all: ["approvals"] as const,
    list: (limit: number) => ["approvals", "list", limit] as const,
};

/** What the caller can approve or reject now, longest waiting first, along the service's keyset cursor. */
export function useApprovalList(limit = 50) {
    return useInfiniteQuery({
        queryKey: approvalKeys.list(limit),
        initialPageParam: undefined as number | undefined,
        queryFn: async ({ pageParam, signal }) =>
            (
                await listApprovals({
                    query: { limit, ...(pageParam === undefined ? {} : { after: pageParam }) },
                    signal,
                })
            ).data,
        getNextPageParam: (page) => page.nextCursor ?? undefined,
    });
}

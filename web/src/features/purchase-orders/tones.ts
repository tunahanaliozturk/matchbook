import type { Tone } from "@/shared/ui/tones";

import type { PurchaseOrderStatus } from "./data";

// A draft asks a buyer to act; waiting for funds and an issued order are moving, on Budgets and on the goods;
// completed ended well; short-closed and cancelled ended by decision and are at rest.
export const orderTone: Record<PurchaseOrderStatus, Tone> = {
    Draft: "caution",
    CommitmentPending: "progress",
    Issued: "progress",
    Completed: "positive",
    ShortClosed: "neutral",
    Cancelled: "neutral",
};

import type { Tone } from "@/shared/ui/tones";

import type { InvoiceStatus } from "./data";

// The two exceptions ask an AP approver to act; waiting for an order or goods resolves itself when they arrive;
// payable and paid are the match done well; rejected is final.
export const invoiceTone: Record<InvoiceStatus, Tone> = {
    Captured: "neutral",
    SuspectedDuplicate: "caution",
    AwaitingPurchaseOrder: "progress",
    AwaitingReceipt: "progress",
    PriceVariance: "caution",
    Rejected: "negative",
    Payable: "positive",
    Scheduled: "progress",
    Paid: "positive",
};

/** Whether the match result is something to act on or wait for, rather than a record of how it went. */
export const needsAttention = (status: InvoiceStatus): boolean =>
    invoiceTone[status] === "caution" || invoiceTone[status] === "negative";

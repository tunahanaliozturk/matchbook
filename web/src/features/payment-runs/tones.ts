import type { Tone } from "@/shared/ui/tones";

import type { CreditorStatus, PaymentRunStatus } from "./data";

// A draft asks a second treasurer to release it; released is money on its way; a cancelled run moved nothing.
export const paymentRunTone: Record<PaymentRunStatus, Tone> = {
    Draft: "caution",
    Released: "positive",
    Cancelled: "neutral",
};

// A dropped supplier was not paid this time and its invoices are payable again, which someone should look into.
export const creditorTone: Record<CreditorStatus, Tone> = {
    Scheduled: "progress",
    Paid: "positive",
    Dropped: "caution",
};

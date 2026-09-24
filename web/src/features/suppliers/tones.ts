import type { Tone } from "@/shared/ui/tones";

import type { SupplierStatus } from "./data";

// Pending activation asks a supplier approver to act; blocked is a stop.
export const supplierTone: Record<SupplierStatus, Tone> = {
    Draft: "neutral",
    PendingActivation: "caution",
    Active: "positive",
    Blocked: "negative",
};

export const bankAccountTone: Record<"Pending" | "Approved" | "Rejected", Tone> = {
    Pending: "caution",
    Approved: "positive",
    Rejected: "neutral",
};

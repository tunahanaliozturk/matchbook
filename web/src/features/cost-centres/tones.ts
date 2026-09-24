import type { Tone } from "@/shared/ui/tones";

// An inactive cost centre is at rest, not a failure: it takes no new requisitions or budgets, and nothing is lost.
export const costCentreTone: Record<"Active" | "Inactive", Tone> = {
    Active: "positive",
    Inactive: "neutral",
};

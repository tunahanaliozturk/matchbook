import type { Tone } from "@/shared/ui/tones";

import type { RequisitionStatus, StepView } from "./data";

// Pending approval asks an approver to act, submitted waits on Budgets, the two refusals are failures, and a
// cancelled or closed requisition is simply at rest.
export const requisitionTone: Record<RequisitionStatus, Tone> = {
    Draft: "neutral",
    Submitted: "progress",
    PendingApproval: "caution",
    Approved: "positive",
    Ordered: "positive",
    Closed: "neutral",
    BudgetRejected: "negative",
    Rejected: "negative",
    Cancelled: "neutral",
};

/** Only the step being waited on asks anyone to act; the ones after it are at rest until their turn. */
export function stepTone(step: StepView, isCurrent: boolean): Tone {
    if (step.decision === "Approved") return "positive";
    if (step.decision === "Rejected") return "negative";
    return isCurrent ? "caution" : "neutral";
}

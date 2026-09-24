import { nameOf } from "@/auth/people";
import type { Person, Role } from "@/auth/roles";
import { humanise } from "@/shared/format";

import type {
    ApprovalStepKind,
    RequisitionStatus,
    RequisitionView,
    StepView,
    TimelineEntryView,
} from "./data";

// The rules about who acts on a requisition, as the service applies them (docs/services/requisitions.md). The
// service enforces every one; the console uses them to say why a button is not for this person.

const deciders: readonly Role[] = ["approver", "finance-approver", "cfo"];

export const stepNames: Record<ApprovalStepKind, string> = {
    Manager: "Cost centre manager",
    Finance: "Finance approver",
    Cfo: "CFO",
};

/** The step waiting for a decision. Steps are taken in order, so it is the first one nobody has decided. */
export const currentStep = (requisition: RequisitionView): StepView | undefined =>
    requisition.status === "PendingApproval"
        ? requisition.steps.find((step) => step.decision === "Pending")
        : undefined;

/** Who a step is for, or who took it: "Approved by Mark", "For you", "For any finance approver". */
export function stepPeople(step: StepView, me: string | undefined): string {
    if (step.decision !== "Pending") {
        return `${humanise(step.decision)} by ${nameOf(step.decidedBy, me)}`;
    }

    if (step.kind === "Manager") return `For ${nameOf(step.approverId, me)}`;
    return step.kind === "Finance" ? "For any finance approver" : "For the CFO";
}

export interface Decision {
    /** Approve and Reject… are shown: the person holds an approval role and the requisition waits on a step. */
    shown: boolean;
    allowed: boolean;
    /** Why not, in the order the service checks, so the sentence matches the refusal the server would give. */
    reason: string | null;
}

export function decisionFor(requisition: RequisitionView, me: Person | null): Decision {
    const step = currentStep(requisition);
    if (!me || !step || !deciders.some((role) => me.roles.has(role))) {
        return { shown: false, allowed: false, reason: null };
    }

    const refuse = (reason: string): Decision => ({ shown: true, allowed: false, reason });

    if (requisition.requesterId === me.id) {
        return refuse("You raised this requisition, so someone else on its route decides it.");
    }

    const signed = requisition.steps.find((taken) => taken.decidedBy === me.id);
    if (signed) {
        return refuse(
            `You signed step ${signed.sequence} of this requisition. Two signatures have to be two people, so someone else takes step ${step.sequence}.`,
        );
    }

    if (step.kind === "Manager" && step.approverId !== me.id) {
        return refuse(
            `Only the cost centre's manager on record, ${nameOf(step.approverId, me.id)}, takes the manager step.`,
        );
    }

    if (step.requiredRole && !me.roles.has(step.requiredRole as Role)) {
        return refuse(
            `Step ${step.sequence} is for ${step.kind === "Finance" ? "a finance approver" : "the CFO"}.`,
        );
    }

    return { shown: true, allowed: true, reason: null };
}

/** The steps an amount will need once funds are reserved: the manager, finance over 10,000, the CFO over 100,000. */
export function routeFor(amount: number): ApprovalStepKind[] {
    const route: ApprovalStepKind[] = ["Manager"];
    if (amount > 10_000) route.push("Finance");
    if (amount > 100_000) route.push("Cfo");
    return route;
}

/**
 * Budgets' refusal as a sentence. The event carries a code (Matchbook.Contracts FundsRejectionReason), which is
 * shown as it came for any code this console does not know yet.
 */
export function budgetRefusal(reason: string | null, costCentre: string): string {
    if (reason === "insufficient_funds") return `The budget for ${costCentre} has too little left.`;
    if (reason === "no_budget") return `No budget is set for ${costCentre} this year.`;
    if (reason === "cost_centre_unavailable")
        return `Budgets does not know ${costCentre}, or it is not active.`;
    return reason ?? "Budgets gave no reason.";
}

/** A draft to finish, or a refusal to read: what a requester's inbox brings back to them. */
export const waitsOnRequester = (status: RequisitionStatus): boolean =>
    status === "Draft" || status === "BudgetRejected" || status === "Rejected";

/** Who did what: a person by name, or the service that reported it, since other services' facts name nobody. */
export function actorOf(entry: TimelineEntryView, me: string | undefined): string {
    if (entry.actorId) {
        const name = nameOf(entry.actorId, me);
        return name === "someone" && entry.actorName ? entry.actorName : name;
    }

    if (entry.action === "FundsReserved" || entry.action === "FundsRefused") return "Budgets";
    return "Purchasing";
}

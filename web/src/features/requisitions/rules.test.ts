import { describe, expect, it } from "vitest";

import type { Person, Role } from "@/auth/roles";

import type { RequisitionView, StepView, TimelineEntryView } from "./data";
import {
    actorOf,
    budgetRefusal,
    decisionFor,
    routeFor,
    stepPeople,
    waitsOnRequester,
} from "./rules";

const rita = "a0000000-0000-4000-8000-000000000001";
const mark = "a0000000-0000-4000-8000-000000000002";
const maya = "a0000000-0000-4000-8000-000000000003";
const fiona = "a0000000-0000-4000-8000-000000000004";

const person = (id: string, ...roles: Role[]): Person => ({ id, name: id, roles: new Set(roles) });

const step = (
    sequence: number,
    kind: StepView["kind"],
    extra: Partial<StepView> = {},
): StepView => ({
    sequence,
    kind,
    approverId: kind === "Manager" ? mark : null,
    requiredRole: kind === "Finance" ? "finance-approver" : kind === "Cfo" ? "cfo" : null,
    decision: "Pending",
    decidedBy: null,
    decidedAt: null,
    ...extra,
});

const pending = (steps: StepView[], requesterId = rita): RequisitionView => ({
    id: "01a0d377-faac-7d54-aeb4-32bbc91789b0",
    number: "REQ-2026-000042",
    status: "PendingApproval",
    requesterId,
    requesterName: "rita",
    costCentreCode: "ENG-PLATFORM",
    supplierId: "01a0d377-f5f7-7e4b-bd87-a3d90303bbea",
    justification: "Laptops",
    neededBy: "2026-12-01",
    amount: 12_500,
    fiscalYear: 2026,
    rejectionReason: null,
    purchaseOrderId: null,
    purchaseOrderNumber: null,
    createdAt: "2026-09-24T10:00:00+00:00",
    revision: 3,
    lines: [],
    steps,
    timeline: [],
});

describe("decisionFor", () => {
    it("lets the manager on record take the manager step", () => {
        const decision = decisionFor(
            pending([step(1, "Manager"), step(2, "Finance")]),
            person(mark, "approver"),
        );

        expect(decision).toEqual({ shown: true, allowed: true, reason: null });
    });

    it("refuses your own requisition before anything else, whatever roles you hold", () => {
        const own = pending([step(1, "Manager", { approverId: fiona })], fiona);

        const decision = decisionFor(own, person(fiona, "approver", "finance-approver"));

        expect(decision.allowed).toBe(false);
        expect(decision.reason).toMatch(/^You raised this requisition/);
    });

    it("refuses a second signature from someone who signed an earlier step", () => {
        const signed = pending([
            step(1, "Manager", {
                decision: "Approved",
                decidedBy: mark,
                decidedAt: "2026-09-24T11:00:00+00:00",
            }),
            step(2, "Finance"),
        ]);

        const decision = decisionFor(signed, person(mark, "approver", "finance-approver"));

        expect(decision.reason).toMatch(/^You signed step 1 of this requisition/);
    });

    it("names the manager on record when another approver looks at the manager step", () => {
        const decision = decisionFor(pending([step(1, "Manager")]), person(maya, "approver"));

        expect(decision.reason).toBe(
            "Only the cost centre's manager on record, Mark, takes the manager step.",
        );
    });

    it("says which role a later step waits for", () => {
        const atFinance = pending([
            step(1, "Manager", {
                decision: "Approved",
                decidedBy: mark,
                decidedAt: "2026-09-24T11:00:00+00:00",
            }),
            step(2, "Finance"),
        ]);

        expect(decisionFor(atFinance, person(maya, "approver")).reason).toBe(
            "Step 2 is for a finance approver.",
        );
        expect(decisionFor(atFinance, person(fiona, "finance-approver")).allowed).toBe(true);
    });

    it("offers nothing to someone without an approval role, or when no step is waiting", () => {
        const waiting = pending([step(1, "Manager")]);

        expect(decisionFor(waiting, person(rita, "requester")).shown).toBe(false);
        expect(
            decisionFor({ ...waiting, status: "Approved" }, person(mark, "approver")).shown,
        ).toBe(false);
        expect(decisionFor(waiting, null).shown).toBe(false);
    });
});

describe("stepPeople", () => {
    it("names who a pending step is for, and who decided a taken one", () => {
        expect(stepPeople(step(1, "Manager"), mark)).toBe("For you");
        expect(stepPeople(step(2, "Finance"), mark)).toBe("For any finance approver");
        expect(stepPeople(step(3, "Cfo"), mark)).toBe("For the CFO");
        expect(
            stepPeople(step(1, "Manager", { decision: "Rejected", decidedBy: mark }), rita),
        ).toBe("Rejected by Mark");
    });
});

describe("routeFor", () => {
    it("adds finance strictly over 10,000 and the CFO strictly over 100,000", () => {
        expect(routeFor(10_000)).toEqual(["Manager"]);
        expect(routeFor(10_000.01)).toEqual(["Manager", "Finance"]);
        expect(routeFor(100_000)).toEqual(["Manager", "Finance"]);
        expect(routeFor(100_000.01)).toEqual(["Manager", "Finance", "Cfo"]);
    });
});

describe("budgetRefusal", () => {
    it("words the codes Budgets sends, and passes on one it does not know", () => {
        expect(budgetRefusal("insufficient_funds", "ENG-PLATFORM")).toBe(
            "The budget for ENG-PLATFORM has too little left.",
        );
        expect(budgetRefusal("no_budget", "ENG-PLATFORM")).toBe(
            "No budget is set for ENG-PLATFORM this year.",
        );
        expect(budgetRefusal("frozen", "ENG-PLATFORM")).toBe("frozen");
    });
});

describe("waitsOnRequester", () => {
    it("brings back drafts and refusals, not what others are still deciding or what is done", () => {
        expect(waitsOnRequester("Draft")).toBe(true);
        expect(waitsOnRequester("BudgetRejected")).toBe(true);
        expect(waitsOnRequester("Rejected")).toBe(true);
        expect(waitsOnRequester("PendingApproval")).toBe(false);
        expect(waitsOnRequester("Cancelled")).toBe(false);
        expect(waitsOnRequester("Ordered")).toBe(false);
    });
});

describe("actorOf", () => {
    const entry = (extra: Partial<TimelineEntryView>): TimelineEntryView => ({
        sequence: 1,
        at: "2026-09-24T10:00:00+00:00",
        action: "Created",
        actorId: null,
        actorName: null,
        detail: null,
        ...extra,
    });

    it("names a person from the directory, and falls back to the name their token carried", () => {
        expect(actorOf(entry({ actorId: rita, actorName: "rita" }), mark)).toBe("Rita");
        expect(actorOf(entry({ actorId: rita, actorName: "rita" }), rita)).toBe("you");
        expect(
            actorOf(
                entry({ actorId: "b0000000-0000-4000-8000-000000000099", actorName: "temp" }),
                rita,
            ),
        ).toBe("temp");
    });

    it("credits the service that reported a fact nobody signed", () => {
        expect(actorOf(entry({ action: "FundsReserved" }), rita)).toBe("Budgets");
        expect(actorOf(entry({ action: "FundsRefused" }), rita)).toBe("Budgets");
        expect(actorOf(entry({ action: "Closed" }), rita)).toBe("Purchasing");
    });
});

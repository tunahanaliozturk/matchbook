import { describe, expect, it } from "vitest";

import type { StepView } from "./data";
import { requisitionTone, stepTone } from "./tones";

describe("requisitionTone", () => {
    it("asks for action only while approvers are needed, and marks both refusals as failures", () => {
        const byTone = (tone: string) =>
            Object.entries(requisitionTone)
                .filter(([, value]) => value === tone)
                .map(([status]) => status);

        expect(byTone("caution")).toEqual(["PendingApproval"]);
        expect(byTone("progress")).toEqual(["Submitted"]);
        expect(byTone("negative")).toEqual(["BudgetRejected", "Rejected"]);
    });
});

describe("stepTone", () => {
    const step = (decision: StepView["decision"]): StepView => ({
        sequence: 1,
        kind: "Manager",
        approverId: null,
        requiredRole: null,
        decision,
        decidedBy: null,
        decidedAt: null,
    });

    it("asks for action on the current step only", () => {
        expect(stepTone(step("Pending"), true)).toBe("caution");
        expect(stepTone(step("Pending"), false)).toBe("neutral");
        expect(stepTone(step("Approved"), false)).toBe("positive");
        expect(stepTone(step("Rejected"), false)).toBe("negative");
    });
});

import { describe, expect, it } from "vitest";

import { orderTone } from "./tones";

describe("orderTone", () => {
    it("asks for action only on a draft, which waits for a buyer", () => {
        const acting = Object.entries(orderTone).filter(([, tone]) => tone === "caution");

        expect(acting).toEqual([["Draft", "caution"]]);
    });

    it("shows an order waiting on Budgets or on the goods as moving", () => {
        expect(orderTone.CommitmentPending).toBe("progress");
        expect(orderTone.Issued).toBe("progress");
    });

    it("shows how an order ended: completed as done well, closed by decision as at rest", () => {
        expect(orderTone.Completed).toBe("positive");
        expect(orderTone.ShortClosed).toBe("neutral");
        expect(orderTone.Cancelled).toBe("neutral");
    });
});

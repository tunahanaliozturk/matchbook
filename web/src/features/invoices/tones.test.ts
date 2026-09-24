import { describe, expect, it } from "vitest";

import { invoiceTone, needsAttention } from "./tones";

describe("invoiceTone", () => {
    it("asks for action on the two exceptions an approver decides", () => {
        expect(invoiceTone.SuspectedDuplicate).toBe("caution");
        expect(invoiceTone.PriceVariance).toBe("caution");
    });

    it("shows waiting for an order or goods as moving, since it resolves itself", () => {
        expect(invoiceTone.AwaitingPurchaseOrder).toBe("progress");
        expect(invoiceTone.AwaitingReceipt).toBe("progress");
    });

    it("sets the match result apart only where someone has to read it", () => {
        expect(needsAttention("PriceVariance")).toBe(true);
        expect(needsAttention("Rejected")).toBe(true);
        expect(needsAttention("AwaitingReceipt")).toBe(false);
        expect(needsAttention("Payable")).toBe(false);
    });
});

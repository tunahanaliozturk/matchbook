import { describe, expect, it } from "vitest";

import {
    anythingReceived,
    asNumber,
    formatUnitPrice,
    outstanding,
    receiptProblem,
    rejectionSentence,
} from "./orders";

const line = (quantity: number, receivedQuantity: number) => ({
    quantity,
    receivedQuantity,
    unitOfMeasure: "EA",
});

describe("outstanding", () => {
    it("is what was ordered less what arrived", () => {
        expect(outstanding(line(10, 4))).toBe(6);
    });

    it("comes out at the column's three decimals, where floating point would not", () => {
        // 10 - 3.3 is 6.699999999999999 in JavaScript; the service holds 6.700.
        expect(outstanding(line(10, 3.3))).toBe(6.7);
        expect(outstanding(line(0.3, 0.1))).toBe(0.2);
    });

    it("is zero for a line received in full, and never below it", () => {
        expect(outstanding(line(5, 5))).toBe(0);
        expect(outstanding(line(0, 0))).toBe(0);
    });
});

describe("anythingReceived", () => {
    it("is true as soon as one line has any quantity received", () => {
        expect(anythingReceived([line(2, 0), line(3, 0.001)])).toBe(true);
        expect(anythingReceived([line(2, 0), line(3, 0)])).toBe(false);
    });
});

describe("receiptProblem", () => {
    it("allows anything from zero, which leaves the line out, up to what is still to arrive", () => {
        expect(receiptProblem(line(10, 4), 0)).toBeNull();
        expect(receiptProblem(line(10, 4), 6)).toBeNull();
        expect(receiptProblem(line(10, 3.3), 6.7)).toBeNull();
    });

    it("refuses more than is still to arrive, saying how much that is", () => {
        expect(receiptProblem(line(10, 4), 6.001)).toBe("At most 6 EA is still to arrive.");
    });

    it("refuses an empty field, a negative quantity and a fourth decimal place", () => {
        expect(receiptProblem(line(10, 0), null)).toMatch(/Enter a quantity/);
        expect(receiptProblem(line(10, 0), -1)).toMatch(/below zero/);
        expect(receiptProblem(line(10, 0), 1.0005)).toMatch(/three decimal places/);
    });
});

describe("asNumber", () => {
    it("takes a number and leaves out what v-model.number could not read", () => {
        expect(asNumber(2.5)).toBe(2.5);
        expect(asNumber(0)).toBe(0);
        expect(asNumber("")).toBeNull();
        expect(asNumber(Number.NaN)).toBeNull();
    });
});

describe("formatUnitPrice", () => {
    it("shows cents always and up to four decimals when the price has them", () => {
        expect(formatUnitPrice(12.5)).toBe("€12.50");
        expect(formatUnitPrice(0.1234)).toBe("€0.1234");
    });
});

describe("rejectionSentence", () => {
    it("says why Budgets refused, for every reason it sends", () => {
        expect(rejectionSentence("insufficient_funds")).toBe(
            "Budgets did not commit the funds: the budget has too little left for this order.",
        );
        expect(rejectionSentence("no_budget")).toMatch(/no budget is set/);
    });

    it("still says something for a reason this console does not know yet", () => {
        expect(rejectionSentence("something_new")).toBe(
            "Budgets did not commit the funds (something_new).",
        );
    });
});

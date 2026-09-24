import { describe, expect, it } from "vitest";

import { estimatedTotal, formatUnitPrice, lineAmount } from "./lines";

describe("lineAmount", () => {
    it("multiplies quantity by unit price", () => {
        expect(lineAmount(5, 2400)).toBe(12_000);
        expect(lineAmount(2.5, 19.99)).toBe(49.98);
    });

    it("rounds half a cent away from zero, as Amounts.Line does, where floating point would round down", () => {
        // 1.005 * 1 is 1.00499999... as a double.
        expect(lineAmount(1.005, 1)).toBe(1.01);
        expect(lineAmount(1, 0.005)).toBe(0.01);
        expect(lineAmount(1, 0.0049)).toBe(0);
    });

    it("counts a line that is not filled in yet, or cannot be bought, as nothing", () => {
        expect(lineAmount("", 10)).toBe(0);
        expect(lineAmount(2, "")).toBe(0);
        expect(lineAmount(-1, 10)).toBe(0);
        expect(lineAmount(1, -5)).toBe(0);
    });

    it("stays exact where the product passes what a double holds to the cent", () => {
        expect(lineAmount(999_999.999, 99_999.9999)).toBe(99_999_999_800);
    });
});

describe("estimatedTotal", () => {
    it("adds the rounded lines, not the unrounded products", () => {
        const half = { description: "Washer", quantity: 1, unitOfMeasure: "EA", unitPrice: 0.005 };

        expect(estimatedTotal([half, half, half])).toBe(0.03);
        expect(estimatedTotal([])).toBe(0);
    });
});

describe("formatUnitPrice", () => {
    it("keeps the decimals a small unit price needs and shows a round one in cents", () => {
        expect(formatUnitPrice(0.0125)).toBe("€0.0125");
        expect(formatUnitPrice(2400)).toBe("€2,400.00");
    });
});

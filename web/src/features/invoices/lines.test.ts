import { describe, expect, it } from "vitest";

import type { BillablePurchaseOrder } from "./data";
import { formatUnitPrice, lineCents, linesFromOrder, linesTotal, type DraftLine } from "./lines";

const line = (quantity: number, unitPrice: number, billed = true): DraftLine => ({
    lineNumber: 1,
    quantity,
    unitPrice,
    billed,
});

describe("lineCents", () => {
    it("rounds half a cent away from zero, as the services do", () => {
        // 1 x 0.125 is exactly half a cent over 0.12.
        expect(lineCents(1, 0.125)).toBe(13);
        expect(lineCents(3, 0.0005)).toBe(0);
        expect(lineCents(10, 0.0005)).toBe(1);
    });

    it("multiplies the figures as written, not as floating point stores them", () => {
        // 0.1 x 3 is 0.30000000000000004 in floating point; 1.005 x 1 would round to 1.00 there.
        expect(lineCents(3, 0.1)).toBe(30);
        expect(lineCents(1, 1.005)).toBe(101);
        expect(lineCents(2.5, 12.3456)).toBe(3086);
    });

    it("counts a field that holds no number yet as zero", () => {
        expect(lineCents(Number.NaN, 12.5)).toBe(0);
    });
});

describe("linesTotal", () => {
    it("adds the rounded amounts of the billed lines only", () => {
        expect(linesTotal([line(10, 12.5), line(1, 0.125), line(5, 100, false)])).toBe(125.13);
    });

    it("is zero with nothing billed", () => {
        expect(linesTotal([])).toBe(0);
    });
});

describe("formatUnitPrice", () => {
    it("keeps a price's four decimals and pads a round one to cents", () => {
        expect(formatUnitPrice(12.3456)).toBe("€12.3456");
        expect(formatUnitPrice(12.5)).toBe("€12.50");
    });
});

describe("linesFromOrder", () => {
    it("bills every line of the order as ordered", () => {
        const order: BillablePurchaseOrder = {
            id: "0199",
            number: "PO-2026-000017",
            supplierId: "0198",
            issuedAt: "2026-09-01T10:00:00+00:00",
            lines: [
                { lineNumber: 1, quantity: 10, unitPrice: 12.5 },
                { lineNumber: 2, quantity: 2, unitPrice: 99.99 },
            ],
        };

        expect(linesFromOrder(order)).toEqual([
            { lineNumber: 1, quantity: 10, unitPrice: 12.5, billed: true },
            { lineNumber: 2, quantity: 2, unitPrice: 99.99, billed: true },
        ]);
        expect(linesTotal(linesFromOrder(order))).toBe(324.98);
    });
});

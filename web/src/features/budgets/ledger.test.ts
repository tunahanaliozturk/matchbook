import { describe, expect, it } from "vitest";

import { documentOf, fiscalYearFrom, formatMovement, stepName } from "./ledger";

describe("fiscalYearFrom", () => {
    const now = new Date(Date.UTC(2026, 8, 24));

    it("takes the year from the address", () => {
        expect(fiscalYearFrom("2025", now)).toBe(2025);
    });

    it("falls back to the current UTC year when the address has none or a year the service refuses", () => {
        expect(fiscalYearFrom(undefined, now)).toBe(2026);
        expect(fiscalYearFrom("1999", now)).toBe(2026);
        expect(fiscalYearFrom("2101", now)).toBe(2026);
        expect(fiscalYearFrom("20x6", now)).toBe(2026);
        expect(fiscalYearFrom(["2025"], now)).toBe(2026);
    });

    it("counts the year in UTC, as fiscal years are", () => {
        // Half past eleven on New Year's Eve in UTC is already the new year east of Greenwich.
        expect(fiscalYearFrom(null, new Date(Date.UTC(2026, 11, 31, 23, 30)))).toBe(2026);
    });
});

describe("the ledger's columns", () => {
    const id = "01a0d205-b8b3-784d-b649-aa76e930e7f6";

    it("names a step by the figure it moves", () => {
        expect(stepName("Reserve")).toBe("Requested");
        expect(stepName("Commit")).toBe("Ordered");
        expect(stepName("Invoice")).toBe("Spent");
    });

    it("keeps the service's name for a step it does not know yet", () => {
        expect(stepName("WriteOff")).toBe("Write off");
    });

    it("names the document by its kind and the random end of its id", () => {
        expect(documentOf("Reserve", id)).toBe("Requisition ending e930e7f6");
        expect(documentOf("Close", id)).toBe("Purchase order ending e930e7f6");
        expect(documentOf("Open", id)).toBe("This budget");
        expect(documentOf("WriteOff", id)).toBe("Document ending e930e7f6");
    });

    it("signs a movement and leaves a figure that did not move blank", () => {
        expect(formatMovement(5000)).toBe("+€5,000.00");
        expect(formatMovement(-1200.5)).toBe("-€1,200.50");
        expect(formatMovement(0)).toBe("");
    });
});

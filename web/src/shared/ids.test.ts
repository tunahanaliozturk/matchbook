import { describe, expect, it } from "vitest";

import { uuidv7 } from "./ids";

describe("uuidv7", () => {
    it("writes the version and variant bits where RFC 9562 puts them", () => {
        const id = uuidv7();

        expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    });

    it("sorts by the millisecond it was made, which is what keeps lists in creation order", () => {
        const earlier = uuidv7(Date.UTC(2026, 8, 24, 10, 0, 0, 0));
        const later = uuidv7(Date.UTC(2026, 8, 24, 10, 0, 0, 1));

        expect(earlier < later).toBe(true);
        // 2026-09-24T10:00:00Z is 0x01a0d2db9100 milliseconds after the epoch.
        expect(earlier.slice(0, 13)).toBe("01a0d2db-9100");
    });
});

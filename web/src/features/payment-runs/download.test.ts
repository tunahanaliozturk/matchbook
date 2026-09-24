import { describe, expect, it } from "vitest";

import { fileNameOf } from "./download";

describe("fileNameOf", () => {
    it("prefers the encoded name ASP.NET Core writes beside the plain one", () => {
        expect(
            fileNameOf(
                "attachment; filename=run.xml; filename*=UTF-8''payment%20run.xml",
                "fallback.xml",
            ),
        ).toBe("payment run.xml");
    });

    it("takes a plain name, quoted or not", () => {
        expect(fileNameOf('attachment; filename="pain.001.xml"', "fallback.xml")).toBe(
            "pain.001.xml",
        );
        expect(fileNameOf("attachment; filename=pain.001.xml", "fallback.xml")).toBe(
            "pain.001.xml",
        );
    });

    it("falls back when the response names no file", () => {
        expect(fileNameOf(null, "fallback.xml")).toBe("fallback.xml");
        expect(fileNameOf("attachment", "fallback.xml")).toBe("fallback.xml");
    });
});

import { config } from "@/app/config";

/** A line as the form holds it. An emptied number input gives "", which counts as nothing until it is filled. */
export interface LineDraft {
    description: string;
    quantity: number | "";
    unitOfMeasure: string;
    unitPrice: number | "";
}

// The server rounds each line with Amounts.Line: quantity (three decimals) times unit price (four), to the cent,
// half away from zero. The estimate is worked out the same way in integers, because 1.005 is not 1.005 in floating
// point, and a total that differs from the saved amount by a cent would look like a bug.
function lineCents(quantity: number | "", unitPrice: number | ""): bigint {
    const q = Number(quantity);
    const p = Number(unitPrice);
    if (!Number.isFinite(q) || !Number.isFinite(p) || q <= 0 || p < 0) return 0n;

    const tenMillionths = BigInt(Math.round(q * 1_000)) * BigInt(Math.round(p * 10_000));
    return (tenMillionths + 50_000n) / 100_000n;
}

export const lineAmount = (quantity: number | "", unitPrice: number | ""): number =>
    Number(lineCents(quantity, unitPrice)) / 100;

export const estimatedTotal = (lines: readonly LineDraft[]): number =>
    Number(lines.reduce((sum, line) => sum + lineCents(line.quantity, line.unitPrice), 0n)) / 100;

const unitPrice = new Intl.NumberFormat(config.locale, {
    style: "currency",
    currency: config.currency,
    maximumFractionDigits: 4,
});

/** €2,400.00, or €0.0125: unit prices are estimated to four decimals, and a cheap part's price would round away. */
export const formatUnitPrice = (amount: number): string => unitPrice.format(amount);

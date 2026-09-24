import { config } from "@/app/config";

import type { BillablePurchaseOrder } from "./data";

const unitPrice = new Intl.NumberFormat(config.locale, {
    style: "currency",
    currency: config.currency,
    maximumFractionDigits: 4,
});

/** €12.50 or €12.3456: a unit price keeps the four decimals it may have (ADR 0006), and shows none it does not. */
export const formatUnitPrice = (price: number): string => unitPrice.format(price);

/** A line of the invoice being captured. Billed lines are sent; the rest stay on screen to be billed after all. */
export interface DraftLine {
    lineNumber: number;
    quantity: number;
    unitPrice: number;
    billed: boolean;
}

/** Every line of the order, as ordered and billed: most invoices bill their order exactly. */
export const linesFromOrder = (order: BillablePurchaseOrder): DraftLine[] =>
    order.lines.map((line) => ({ ...line, billed: true }));

// Quantities carry up to three decimals and unit prices four (ADR 0006), so their product is a whole number of
// ten-millionths. Doing the sum in integers is what makes the total the clerk sees the total the service computes;
// floating point would put 0.1 x 3 at 0.30000000000000004 and round a half cent the wrong way now and then.
// A field being edited can hold nothing or a half-typed number; it counts as zero until it is one.
const scaled = (value: number, decimals: number) =>
    Number.isFinite(value) ? BigInt(Math.round(value * 10 ** decimals)) : 0n;

/** A line's amount in cents, rounded half away from zero as Amounts.Line does on the server. */
export function lineCents(quantity: number, unitPrice: number): number {
    const product = scaled(quantity, 3) * scaled(unitPrice, 4);
    const sign = product < 0n ? -1n : 1n;
    return Number((sign * (sign * product + 50_000n)) / 100_000n);
}

/** The sum of the billed lines, in euros, as the service will check the declared total against it. */
export const linesTotal = (lines: readonly DraftLine[]): number =>
    lines
        .filter((line) => line.billed)
        .reduce((cents, line) => cents + lineCents(line.quantity, line.unitPrice), 0) / 100;

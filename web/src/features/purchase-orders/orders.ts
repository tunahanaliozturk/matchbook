import { config } from "@/app/config";
import { formatQuantity } from "@/shared/format";

import type { OrderLineView, PurchaseOrderStatus } from "./data";

// The words a person sees for each state. Only one differs from the API's name: CommitmentPending is what the
// order is doing, waiting for Budgets to commit the funds, and the filter and the pill should say so alike.
export const statusLabel: Record<PurchaseOrderStatus, string> = {
    Draft: "Draft",
    CommitmentPending: "Waiting for funds",
    Issued: "Issued",
    Completed: "Completed",
    ShortClosed: "Short closed",
    Cancelled: "Cancelled",
};

/** The two ways a buyer ends an order early. */
export type Closing = "cancel" | "short-close";

// Quantities are kept to three decimals (numeric(18,3)) and floating point is not: 10 - 3.3 is 6.699999... in
// JavaScript. Rounding to the column's precision gives the figure the service holds and compares against.
const thousandths = (value: number) => Math.round(value * 1000) / 1000;

type Received = Pick<OrderLineView, "quantity" | "receivedQuantity">;

/** What is still to arrive on a line: ordered minus received, never below zero. */
export const outstanding = (line: Received): number =>
    Math.max(0, thousandths(line.quantity - line.receivedQuantity));

/** Whether anything at all has arrived, which is what stops an issued order being cancelled. */
export const anythingReceived = (lines: readonly Received[]): boolean =>
    lines.some((line) => line.receivedQuantity > 0);

/** A number from an input bound with v-model.number, which leaves an empty or unreadable field as a string. */
export const asNumber = (value: unknown): number | null =>
    typeof value === "number" && Number.isFinite(value) ? value : null;

/**
 * Why a quantity cannot be recorded against a line, or null when it can. Zero is allowed and leaves the line out
 * of the receipt. The service refuses the same things; checking here lets the sheet say which line is wrong.
 */
export function receiptProblem(
    line: Received & Pick<OrderLineView, "unitOfMeasure">,
    quantity: number | null,
): string | null {
    const left = outstanding(line);

    if (quantity === null) return "Enter a quantity, or 0 to leave this line out.";
    if (quantity < 0) return "A quantity cannot be below zero.";
    if (thousandths(quantity) !== quantity) return "Quantities have at most three decimal places.";
    if (quantity > left)
        return `At most ${formatQuantity(left)} ${line.unitOfMeasure} is still to arrive.`;
    return null;
}

// Unit prices are kept to four decimals (numeric(18,4)), which MoneyText's two would round away.
const unitPrice = new Intl.NumberFormat(config.locale, {
    style: "currency",
    currency: config.currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
});

/** €12.50, or €0.1234 for a price with more precision than cents. */
export const formatUnitPrice = (value: number): string => unitPrice.format(value);

// Budgets answers a refused commitment with a reason code (FundsRejectionReason in the contracts).
const rejections: Record<string, string> = {
    insufficient_funds: "the budget has too little left for this order.",
    no_budget: "no budget is set for this cost centre in this fiscal year.",
    cost_centre_unavailable: "the cost centre is unknown or no longer active.",
    document_closed: "the order was already closed when Budgets answered.",
};

/** Why Budgets refused, as a sentence, for any reason code, including one this console does not know yet. */
export const rejectionSentence = (code: string): string => {
    const reason = rejections[code];
    return reason
        ? `Budgets did not commit the funds: ${reason}`
        : `Budgets did not commit the funds (${code}).`;
};

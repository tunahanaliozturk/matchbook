import { config } from "@/app/config";
import { humanise } from "@/shared/format";

// The earliest and latest fiscal years the service accepts (docs/services/budgets.md, "Database constraints").
const earliestYear = 2000;
const latestYear = 2100;

/** The fiscal year in the address, or the current UTC year when it is missing or not one the service knows. */
export function fiscalYearFrom(value: unknown, now: Date): number {
    const year = typeof value === "string" && /^\d{4}$/.test(value) ? Number(value) : NaN;
    return year >= earliestYear && year <= latestYear ? year : now.getUTCFullYear();
}

// Each step in the words the capsule uses for the figure it moves: requested, ordered, spent.
const steps: Record<string, { name: string; document: string | null }> = {
    Open: { name: "Opened", document: null },
    Allot: { name: "Allotment changed", document: "Allotment change" },
    Reserve: { name: "Requested", document: "Requisition" },
    Release: { name: "Released", document: "Requisition" },
    Commit: { name: "Ordered", document: "Purchase order" },
    Invoice: { name: "Spent", document: "Invoice" },
    Close: { name: "Order closed", document: "Purchase order" },
};

/** What a ledger step did, for the ledger's step column. A step this console does not know keeps its own name. */
export const stepName = (step: string): string => steps[step]?.name ?? humanise(step);

/**
 * The document an entry concerns. Budgets knows documents by id only, never by their human numbers, so the end
 * of the id stands in for it: the start of a version 7 id is its timestamp and is shared by documents made in the
 * same minute. Opening is the budget itself.
 */
export function documentOf(step: string, documentId: string): string {
    const known = steps[step];
    if (known && known.document === null) return "This budget";
    return `${known?.document ?? "Document"} ending ${documentId.slice(-8)}`;
}

const signed = new Intl.NumberFormat(config.locale, {
    style: "currency",
    currency: config.currency,
    signDisplay: "exceptZero",
});

/** +€5,000.00 or -€1,200.00; nothing at all for a figure the entry did not move, so the moves stand out. */
export const formatMovement = (amount: number): string =>
    amount === 0 ? "" : signed.format(amount);

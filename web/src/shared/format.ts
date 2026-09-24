import { config } from "@/app/config";

const money = new Intl.NumberFormat(config.locale, {
    style: "currency",
    currency: config.currency,
});
const plain = new Intl.NumberFormat(config.locale, { maximumFractionDigits: 3 });
const day = new Intl.DateTimeFormat(config.locale, {
    day: "numeric",
    month: "short",
    year: "numeric",
});
const moment = new Intl.DateTimeFormat(config.locale, {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
});

/** €12,500.00. Amounts arrive as numbers with at most two decimals (ADR 0006). */
export const formatMoney = (amount: number): string => money.format(amount);

/** 12 or 2.5: quantities keep up to three decimals, and show none they do not have. */
export const formatQuantity = (quantity: number): string => plain.format(quantity);

/** 24 Sept 2026, from an ISO date or date-time. */
export const formatDay = (value: string): string => day.format(new Date(value));

/** 24 Sept, 14:05, in the viewer's time zone. */
export const formatMoment = (value: string): string => moment.format(new Date(value));

/** A calendar date as the API takes it: yyyy-mm-dd in UTC, which is how fiscal years are counted. */
export const isoDay = (date: Date): string => date.toISOString().slice(0, 10);

/** Splits PendingApproval into "Pending approval": status names become sentence case for people. */
export const humanise = (name: string): string => {
    const words = name.replace(/([a-z])([A-Z])/g, "$1 $2");
    return words.charAt(0).toUpperCase() + words.slice(1).toLowerCase();
};

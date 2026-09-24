// Line glyphs for the source list, drawn on a 20-point grid with a 1.5-point stroke in the spirit of SF Symbols.
export type IconName =
    | "inbox"
    | "requisition"
    | "approval"
    | "order"
    | "invoice"
    | "exception"
    | "payment"
    | "supplier"
    | "costCentre"
    | "budget";

export const paths: Record<IconName, string> = {
    inbox: "M3 11.5 5.2 4.6A1.5 1.5 0 0 1 6.6 3.5h6.8a1.5 1.5 0 0 1 1.4 1.1L17 11.5V15a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 3 15zm0 0h4l1 2h4l1-2h4",
    requisition:
        "M5.5 2.5h6l3.5 3.5v10a1.5 1.5 0 0 1-1.5 1.5h-8A1.5 1.5 0 0 1 4 16V4a1.5 1.5 0 0 1 1.5-1.5zM11 2.5V6h4M7 10h6M7 13h4",
    approval:
        "M10 2.5l1.9 1.4 2.3-.2.8 2.2 2 1.2-.6 2.3.6 2.3-2 1.2-.8 2.2-2.3-.2L10 17.5l-1.9-1.4-2.3.2-.8-2.2-2-1.2.6-2.3-.6-2.3 2-1.2.8-2.2 2.3.2zM7.2 10l1.9 1.9 3.7-3.8",
    order: "M3 6.5 10 3l7 3.5v7L10 17l-7-3.5zM3 6.5 10 10l7-3.5M10 10v7M6.5 4.8l7 3.5",
    invoice:
        "M5 2.5h10v15l-2-1.2-1.7 1.2-1.3-1.2-1.3 1.2L7 16.3 5 17.5zM7.5 6.5h5M7.5 9.5h5M7.5 12.5h3",
    exception: "M10 3.2 17.5 16H2.5zM10 8v3.8M10 14.1v.1",
    payment: "M2.5 6h15v9h-15zM2.5 9h15M5.5 12.5h3",
    supplier: "M3.5 17V8.5L8 6v3l4.5-3v3l4-2.5V17zM3.5 17h13M7 13.5h1.5M11 13.5h1.5",
    costCentre: "M3 4.5h5.5v5H3zM11.5 4.5H17v5h-5.5zM3 12.5h5.5v4H3zM11.5 12.5H17v4h-5.5z",
    budget: "M3 16.5h14M5.5 16.5v-5M10 16.5V6M14.5 16.5v-8",
};

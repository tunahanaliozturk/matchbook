import { readFile } from "node:fs/promises";

import { expect, test } from "@playwright/test";

import { activeSupplier, api, approvedRequisition, costCentreWithBudget, eventually } from "./api";
import { expectAccessible, signedInAs } from "./support";

const day = (offsetDays: number) =>
    new Date(Date.now() + offsetDays * 86_400_000).toISOString().slice(0, 10);

// From a received order to money on its way: alice captures the supplier's invoice and it matches, tess drafts the
// run that pays it, and trevor, a second treasurer, releases it and takes the bank file. The order and the goods
// are set up through the gateway, as the buyer and the receiver would, since they have journeys of their own.
test("an invoice is captured, matches, and is paid by a run a second treasurer releases", async ({
    browser,
}) => {
    test.setTimeout(180_000);

    const supplier = await activeSupplier(30);
    const { code } = await costCentreWithBudget();
    const requisition = await approvedRequisition(supplier.id, code);
    const order = await eventually(
        () =>
            api<{ id: string; number: string }>(
                "bruno",
                "GET",
                `/purchase-orders/by-requisition/${requisition.id}`,
            ).catch(() => null),
        (found) => found !== null,
    );
    await api("bruno", "POST", `/purchase-orders/${order!.id}/issue`);
    await eventually(
        () => api<{ status: string }>("bruno", "GET", `/purchase-orders/${order!.id}`),
        (current) => current.status === "Issued",
    );
    await api("rosa", "POST", `/purchase-orders/${order!.id}/receipts`, {
        lines: [{ lineNumber: 1, quantity: 10 }],
    });
    // Payables hears of the order by an event; the capture form offers it once it has.
    await eventually(
        () =>
            api<{ items: { id: string }[] }>(
                "alice",
                "GET",
                `/invoices/purchase-orders?supplierId=${supplier.id}`,
            ),
        (page) => page.items.some((item) => item.id === order!.id),
    );

    // Alice keys in the invoice, dated thirty days ago on thirty-day terms, so it falls due today.
    const alice = await signedInAs(browser, "alice");
    const number = `JRN-${Date.now()}`;
    await alice.getByRole("link", { name: "Invoices" }).click();
    await expect(alice.getByRole("heading", { level: 1, name: "Invoices" })).toBeVisible();
    await expectAccessible(alice);

    await alice.getByRole("button", { name: "Capture invoice…" }).click();
    const capture = alice.getByRole("dialog", { name: "Capture invoice" });
    await capture.getByLabel("Supplier").selectOption({ label: supplier.legalName });
    await capture.getByLabel("Purchase order").selectOption(order!.id);
    await expect(capture.getByLabel("Quantity, line 1")).toHaveValue("10");
    await expect(capture.getByLabel("Total")).toHaveValue("125");
    await capture.getByLabel("Invoice number").fill(number);
    await capture.getByLabel("Invoice date").fill(day(-30));
    await expectAccessible(alice);
    await capture.getByRole("button", { name: "Capture" }).click();

    await expect(alice.getByRole("heading", { level: 1, name: number })).toBeVisible();
    await expect(alice.getByText("Payable", { exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(alice.getByText(supplier.legalName)).toBeVisible();
    await expectAccessible(alice);
    const invoiceId = alice.url().split("/").pop()!;

    // Aaron's queue holds only exceptions; this invoice matched, so it is not his to decide.
    const aaron = await signedInAs(browser, "aaron");
    await aaron.getByRole("link", { name: "Exceptions" }).click();
    await expect(aaron.getByRole("heading", { level: 1, name: "Exceptions" })).toBeVisible();
    await expect(aaron.getByText(number)).toHaveCount(0);
    await expectAccessible(aaron);

    // Tess drafts today's run. Other invoices due from earlier runs may join it; this journey looks at its own.
    const tess = await signedInAs(browser, "tess");
    await tess.getByRole("link", { name: "Payment runs" }).click();
    await expect(tess.getByRole("heading", { level: 1, name: "Payment runs" })).toBeVisible();
    await expectAccessible(tess);

    await tess.getByRole("button", { name: "Draft payment run…" }).click();
    const draft = tess.getByRole("dialog", { name: "Draft payment run" });
    await expect(draft.getByLabel("Execution date")).toHaveValue(day(0));
    await expectAccessible(tess);
    await draft.getByRole("button", { name: "Draft" }).click();

    await expect(tess.getByRole("heading", { level: 1, name: /^Run for / })).toBeVisible();
    await expect(tess.getByRole("cell", { name: supplier.legalName })).toBeVisible();
    await expect(tess.getByRole("button", { name: "Release" })).toBeDisabled();
    await expect(
        tess.getByText("You drafted this run, so another treasurer has to release it."),
    ).toBeVisible();
    await expectAccessible(tess);

    // Trevor finds it in his inbox, releases it, and downloads the file for the bank.
    const trevor = await signedInAs(browser, "trevor");
    await expect(trevor.getByRole("heading", { name: "Payment runs to release" })).toBeVisible();
    await expectAccessible(trevor);
    await trevor.goto(tess.url());
    await trevor.getByRole("button", { name: "Release" }).click();
    await expect(trevor.getByRole("button", { name: "Download bank file" })).toBeVisible();
    await expect(trevor.getByRole("row", { name: new RegExp(supplier.legalName) })).toContainText(
        "Paid",
    );
    await expectAccessible(trevor);

    const [file] = await Promise.all([
        trevor.waitForEvent("download"),
        trevor.getByRole("button", { name: "Download bank file" }).click(),
    ]);
    expect(file.suggestedFilename()).toMatch(/\.xml$/);
    const xml = await readFile(await file.path(), "utf8");
    expect(xml).toContain("pain.001.001.09");
    expect(xml).toContain(number);

    const paid = await eventually(
        () => api<{ status: string }>("alice", "GET", `/invoices/${invoiceId}`),
        (invoice) => invoice.status === "Paid",
    );
    expect(paid.status).toBe("Paid");
});

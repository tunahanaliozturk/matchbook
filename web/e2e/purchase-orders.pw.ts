import { expect, test, type Page } from "@playwright/test";

import { activeSupplier, api, approvedRequisition, costCentreWithBudget, eventually } from "./api";
import { expectAccessible, signedInAs } from "./support";

/** One section of the inbox, found by its title. */
const queue = (page: Page, title: string) =>
    page.locator("section", { has: page.getByRole("heading", { level: 2, name: title }) });

// An approved requisition becomes a draft order. bruno issues it, Budgets commits the funds, and rosa, who did not
// issue it, records the goods when they arrive. The supplier, budget and requisition behind the order are set up
// through the gateway; they have journeys of their own.
test("a buyer issues an order and a receiver records the goods", async ({ browser }) => {
    const supplier = await activeSupplier();
    const { code } = await costCentreWithBudget();
    const requisition = await approvedRequisition(supplier.id, code, [
        { description: "Widget", quantity: 10, unitOfMeasure: "EA", unitPrice: 12.5 },
        { description: "Gasket", quantity: 4, unitOfMeasure: "EA", unitPrice: 3.25 },
    ]);
    const order = await eventually(
        () =>
            api<{ number: string }>(
                "bruno",
                "GET",
                `/purchase-orders/by-requisition/${requisition.id}`,
            ).catch(() => null),
        (drafted) => drafted !== null,
    );
    const number = order!.number;

    const bruno = await signedInAs(browser, "bruno");
    await expect(queue(bruno, "Orders to issue").getByRole("link", { name: number })).toBeVisible();
    await expectAccessible(bruno);

    await bruno.getByRole("link", { name: "Purchase orders" }).click();
    await bruno.getByRole("button", { name: "Drafts" }).click();
    await expect(bruno).toHaveURL(/view=draft/);
    await expect(bruno.getByRole("link", { name: number })).toBeVisible();
    await expectAccessible(bruno);

    await bruno.getByRole("link", { name: number }).click();
    await expect(bruno.getByRole("heading", { level: 1, name: number })).toBeVisible();
    await expect(bruno.getByText(supplier.legalName)).toBeVisible();
    await expectAccessible(bruno);

    // Issuing only asks Budgets; the page reads the order again until the funds are committed.
    await bruno.getByRole("button", { name: "Issue" }).click();
    const status = bruno.locator("main header");
    await expect(status.getByText("Issued", { exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(bruno.getByRole("button", { name: "Record receipt…" })).toHaveCount(0);
    await expect(bruno.getByRole("button", { name: "Short-close…" })).toBeVisible();
    await expectAccessible(bruno);

    const rosa = await signedInAs(browser, "rosa");
    const waiting = queue(rosa, "Orders waiting for goods").getByRole("link", { name: number });
    await expect(waiting).toBeVisible();
    await expectAccessible(rosa);
    await waiting.click();
    await expect(rosa.getByRole("heading", { level: 1, name: number })).toBeVisible();
    await expectAccessible(rosa);

    await rosa.getByRole("button", { name: "Record receipt…" }).click();
    const receipt = rosa.getByRole("dialog", { name: `Record receipt for ${number}` });
    // Each line starts at what is still to arrive: the whole order, this time.
    await expect(receipt.getByLabel("Line 1, Widget (EA)")).toHaveValue("10");
    await expect(receipt.getByLabel("Line 2, Gasket (EA)")).toHaveValue("4");
    await expectAccessible(rosa);
    await receipt.getByRole("button", { name: "Record" }).click();

    await expect(rosa.getByText("recorded by you")).toBeVisible();
    await expect(rosa.getByText("Everything ordered has arrived.")).toBeVisible();
    await expect(rosa.getByRole("button", { name: "Record receipt…" })).toHaveCount(0);
    await expectAccessible(rosa);
});

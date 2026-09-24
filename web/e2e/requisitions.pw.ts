import { expect, test } from "@playwright/test";

import { activeSupplier, api, costCentreWithBudget, eventually } from "./api";
import { expectAccessible, signedInAs } from "./support";

// A requisition from its first line to its first signature, as two people do it: rita drafts it in the sheet and
// submits it, Budgets holds the money, and mark, who manages the cost centre, approves it from his queue.
test("a requisition is raised by its requester and approved by the cost centre's manager", async ({
    browser,
}) => {
    const supplier = await activeSupplier();
    const { code } = await costCentreWithBudget();
    const neededBy = new Date(Date.now() + 60 * 86_400_000).toISOString().slice(0, 10);

    // Requisitions hears of both by events, and the form offers them once it has.
    await eventually(
        async () => ({
            costCentres: await api<{ code: string }[]>("rita", "GET", "/requisitions/cost-centres"),
            suppliers: await api<{ id: string }[]>("rita", "GET", "/requisitions/suppliers"),
        }),
        ({ costCentres, suppliers }) =>
            costCentres.some((option) => option.code === code) &&
            suppliers.some((option) => option.id === supplier.id),
    );

    const rita = await signedInAs(browser, "rita");
    await rita.getByRole("link", { name: "Requisitions" }).click();
    await expect(rita.getByRole("heading", { level: 1, name: "Requisitions" })).toBeVisible();
    await expectAccessible(rita);

    await rita.getByRole("button", { name: "New requisition…" }).click();
    const sheet = rita.getByRole("dialog", { name: "New requisition" });
    await sheet.getByLabel("Cost centre").selectOption(code);
    await sheet.getByLabel("Supplier").selectOption({ label: supplier.legalName });
    await sheet.getByLabel("Justification").fill("Monitors for the two new platform engineers");
    await sheet.getByLabel("Needed by").fill(neededBy);
    await sheet.getByLabel("Line 1 description").fill("Monitor");
    await sheet.getByLabel("Line 1 quantity").fill("2");
    await sheet.getByLabel("Line 1 unit price").fill("249.99");
    await sheet.getByRole("button", { name: "Add line" }).click();
    await sheet.getByLabel("Line 2 description").fill("Monitor arm");
    await sheet.getByLabel("Line 2 quantity").fill("2");
    await sheet.getByLabel("Line 2 unit price").fill("45.50");
    await expect(sheet.getByRole("row", { name: /Estimated total/ })).toContainText("€590.98");
    await expectAccessible(rita);
    await sheet.getByRole("button", { name: "Create" }).click();

    const title = rita.getByRole("heading", { level: 1, name: /^REQ-\d{4}-\d{6}$/ });
    await expect(title).toBeVisible();
    const number = (await title.textContent())!.trim();
    await expect(rita.getByText("Draft", { exact: true })).toBeVisible();
    await expectAccessible(rita);

    // Budgets answers by event; the page asks again until it has, and the route appears.
    await rita.getByRole("button", { name: "Submit for approval" }).click();
    await expect(rita.getByText("Pending approval", { exact: true })).toBeVisible({
        timeout: 30_000,
    });
    await expect(rita.getByText("For Mark")).toBeVisible();
    await expect(rita.getByRole("button", { name: "Approve" })).toHaveCount(0);
    await expectAccessible(rita);

    const mark = await signedInAs(browser, "mark");
    await mark.getByRole("link", { name: "Approvals" }).click();
    await expect(mark.getByRole("heading", { level: 1, name: "Approvals" })).toBeVisible();
    await expect(mark.getByText("Loading approvals…")).toHaveCount(0);
    await expectAccessible(mark);

    // Longest waiting first, so a queue left long by other runs puts this one on a later page.
    const row = mark.getByRole("link", { name: new RegExp(number) });
    const more = mark.getByRole("button", { name: "Show more" });
    while (!(await row.isVisible()) && (await more.isVisible())) {
        await more.click();
    }
    await row.click();

    await expect(mark.getByRole("heading", { level: 1, name: number })).toBeVisible();
    await mark.getByRole("button", { name: "Approve" }).click();
    await expect(mark.getByText("Approved by you")).toBeVisible();
    await expect(mark.getByRole("button", { name: "Approve" })).toHaveCount(0);
    await expectAccessible(mark);
});

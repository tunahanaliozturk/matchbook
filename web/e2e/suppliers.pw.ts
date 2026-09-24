import { expect, test } from "@playwright/test";

import { expectAccessible, signedInAs, uniqueTaxId } from "./support";

// Four eyes on a supplier, as two people do it: sam creates the supplier and proposes its bank account, sofia checks
// the account in full and approves it, sam submits, sofia activates. Neither can do the other's half.
test("a supplier is created by one person and activated by another", async ({ browser }) => {
    const sam = await signedInAs(browser, "sam");
    const sofia = await signedInAs(browser, "sofia");
    const name = `Journey ${Date.now()} GmbH`;

    await sam.getByRole("link", { name: "Suppliers" }).click();
    await expect(sam.getByRole("heading", { level: 1, name: "Suppliers" })).toBeVisible();
    await expectAccessible(sam);

    await sam.getByRole("button", { name: "New supplier…" }).click();
    const create = sam.getByRole("dialog", { name: "New supplier" });
    await create.getByLabel("Legal name").fill(name);
    await create.getByLabel("Tax ID").fill(uniqueTaxId());
    await create.getByLabel("Country").fill("DE");
    await create.getByLabel("Contact email").fill("ap@journey.example");
    await expectAccessible(sam);
    await create.getByRole("button", { name: "Create" }).click();

    await expect(sam.getByRole("heading", { level: 1, name })).toBeVisible();
    await expect(sam.getByRole("button", { name: "Submit for activation" })).toBeDisabled();

    await sam.getByRole("button", { name: "Propose bank account…" }).click();
    const propose = sam.getByRole("dialog", { name: "Propose bank account" });
    await propose.getByLabel("IBAN").fill("DE89 3704 0044 0532 0130 00");
    await propose.getByLabel("BIC").fill("DEUTDEFF");
    await propose.getByRole("button", { name: "Propose" }).click();
    await expect(sam.getByText("Ending 3000")).toBeVisible();
    await expectAccessible(sam);

    // The approver sees the account number in full, to check it against the supplier's letter.
    await sofia.goto(sam.url());
    await expect(sofia.getByText("DE89 3704 0044 0532 0130 00")).toBeVisible();
    await sofia.getByRole("button", { name: "Approve" }).click();
    await expect(sofia.getByText("Version 1", { exact: true }).first()).toBeVisible();

    await sam.reload();
    await sam.getByRole("button", { name: "Submit for activation" }).click();
    await expect(sam.getByText("Pending activation")).toBeVisible();
    await expect(sam.getByRole("button", { name: "Activate" })).toHaveCount(0);

    await sofia.reload();
    await sofia.getByRole("button", { name: "Activate" }).click();
    await expect(sofia.getByText("Active", { exact: true })).toBeVisible();
    await expect(sofia.getByText("by you").first()).toBeVisible();
});

import { expect, test } from "@playwright/test";

import { expectAccessible, signedInAs } from "./support";

// A budget from nothing, as bob does it: a cost centre with its manager, a budget for the year, a raise, and a
// reduction the service refuses because it would uncover what is consumed. Mark, who approves against the budget,
// reads the same page and is offered nothing to change.
test("a budget admin opens a budget and an approver reads it", async ({ browser }) => {
    const bob = await signedInAs(browser, "bob");
    const code = `JRN-${Date.now().toString(36).toUpperCase()}`;
    const year = new Date().getUTCFullYear();

    await expect(bob.getByRole("heading", { name: "Budgets over their allotment" })).toBeVisible();
    await expectAccessible(bob);

    await bob.getByRole("link", { name: "Cost centres" }).click();
    await expect(bob.getByRole("heading", { level: 1, name: "Cost centres" })).toBeVisible();
    await expectAccessible(bob);

    await bob.getByRole("button", { name: "New cost centre…" }).click();
    const create = bob.getByRole("dialog", { name: "New cost centre" });
    await create.getByLabel("Code").fill(code.toLowerCase());
    await create.getByLabel("Name").fill(`Journey ${code}`);
    await create.getByLabel("Manager").selectOption({ label: "Mark" });
    await expectAccessible(bob);
    await create.getByRole("button", { name: "Create" }).click();
    await expect(create).toBeHidden();
    await expect(bob.getByText("Cost centre created")).toBeVisible();

    await bob.getByRole("link", { name: "Budgets", exact: true }).click();
    await expect(bob.getByRole("heading", { level: 1, name: "Budgets" })).toBeVisible();
    await expectAccessible(bob);

    await bob.getByRole("button", { name: "Open budget…" }).first().click();
    const open = bob.getByRole("dialog", { name: "Open budget" });
    await open.getByLabel("Cost centre").selectOption({ label: `${code}, Journey ${code}` });
    await expect(open.getByLabel("Fiscal year")).toHaveValue(String(year));
    await open.getByLabel("Allotted (EUR)").fill("50000");
    await open.getByRole("button", { name: "Open" }).click();

    // The manager's name comes from the cost centre, which proves the sheet saved mark as its manager.
    await expect(bob.getByRole("heading", { level: 1, name: `${code} ${year}` })).toBeVisible();
    await expect(bob.getByText(`Journey ${code}, managed by Mark`)).toBeVisible();
    await expectAccessible(bob);

    await bob.getByRole("button", { name: "Change allotment…" }).click();
    const raise = bob.getByRole("dialog", { name: "Change allotment" });
    await raise.getByLabel("Raise by").fill("5000");
    await expect(raise.getByText("The allotment becomes")).toHaveText(
        "The allotment becomes €55,000.00, leaving €55,000.00 available.",
    );
    await expectAccessible(bob);
    await raise.getByRole("button", { name: "Raise allotment" }).click();
    await expect(raise).toBeHidden();

    const capsule = bob.getByRole("img", { name: /of €55,000\.00; €55,000\.00 available\.$/ });
    await expect(capsule).toBeVisible();
    const ledger = bob.getByRole("table", { name: "Ledger" });
    await expect(ledger.getByRole("row")).toHaveCount(3);
    await expect(ledger.getByRole("row", { name: /Opened by you.*\+€50,000\.00/ })).toBeVisible();
    await expect(
        ledger.getByRole("row", { name: /Allotment changed by you.*\+€5,000\.00/ }),
    ).toBeVisible();
    await expectAccessible(bob);

    // Lowering past what is consumed is the service's call, and its sentence is what the sheet shows.
    await bob.getByRole("button", { name: "Change allotment…" }).click();
    const lower = bob.getByRole("dialog", { name: "Change allotment" });
    await lower.getByRole("button", { name: "Lower", exact: true }).click();
    await lower.getByLabel("Lower by").fill("60000");
    await lower.getByRole("button", { name: "Lower allotment" }).click();
    await expect(lower.getByRole("alert")).toContainText("The allotment cannot go below");
    await lower.getByRole("button", { name: "Cancel" }).click();

    const mark = await signedInAs(browser, "mark");
    await mark.goto(bob.url());
    await expect(mark.getByRole("heading", { level: 1, name: `${code} ${year}` })).toBeVisible();
    await expect(mark.getByRole("img", { name: /€55,000\.00 available\.$/ })).toBeVisible();
    await expect(mark.getByRole("table", { name: "Ledger" }).getByRole("row")).toHaveCount(3);
    await expect(mark.getByRole("button", { name: "Change allotment…" })).toHaveCount(0);
    await expectAccessible(mark);

    await mark.getByRole("link", { name: "Cost centres" }).click();
    await expect(mark.getByRole("heading", { level: 1, name: "Cost centres" })).toBeVisible();
    await expect(mark.getByRole("button", { name: "New cost centre…" })).toHaveCount(0);
    await expect(mark.getByRole("button", { name: /^Edit / })).toHaveCount(0);
});

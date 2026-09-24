import AxeBuilder from "@axe-core/playwright";
import { expect, type Browser, type Page } from "@playwright/test";

/**
 * A browser of one's own for one person, signed in through the real Keycloak login page. Tokens live in memory
 * (ADR 0009), so every person needs a separate context, exactly as two people need two computers.
 */
export async function signedInAs(browser: Browser, username: string): Promise<Page> {
    const context = await browser.newContext();
    const page = await context.newPage();

    await page.goto("/");
    await page.getByLabel("Username").fill(username);
    await page.getByLabel("Password", { exact: true }).fill("matchbook");
    await page.getByRole("button", { name: "Sign In" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Inbox" })).toBeVisible();

    return page;
}

/**
 * Fails on any serious or critical WCAG 2 A/AA violation on the page as it is now, in the light appearance and the
 * dark one: every colour has two values, and contrast has to hold for both.
 */
export async function expectAccessible(page: Page): Promise<void> {
    const blocking = [];

    for (const colorScheme of ["light", "dark"] as const) {
        await page.emulateMedia({ colorScheme });
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
        blocking.push(
            ...results.violations
                .filter(
                    (violation) =>
                        violation.impact === "serious" || violation.impact === "critical",
                )
                .map((violation) => ({ ...violation, id: `${colorScheme} ${violation.id}` })),
        );
    }

    await page.emulateMedia({ colorScheme: null });

    // Name each offending element and axe's own finding, so a failure says what to fix, not just that something is wrong.
    expect(
        blocking.flatMap((violation) =>
            violation.nodes.map(
                (node) =>
                    `${violation.id} at ${node.target.join(" ")}: ${node.failureSummary?.split("\n").pop()?.trim() ?? ""}`,
            ),
        ),
    ).toEqual([]);
}

/** A tax id no earlier run has used, since the service refuses duplicates after normalising. */
export const uniqueTaxId = () => `DE${String(Date.now()).slice(-9)}`;

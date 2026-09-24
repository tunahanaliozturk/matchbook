// Reads the licence of every installed npm package and refuses anything that is not permissive.
//
// The .NET projects are audited by tools/Matchbook.LicenseAudit. The npm tree needs the same treatment and for
// the same reason: PrimeVue 5 moved to a licence with revenue and headcount conditions and a licence
// key, and nothing in a normal install would have said so. A package that declares
// "SEE LICENSE IN LICENSE.md" is treated as unknown rather than fine, because that is exactly how
// that one arrived.
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const web = join(dirname(fileURLToPath(import.meta.url)), "..");
const modules = join(web, "node_modules");

const allowed = new Set([
    "MIT",
    "ISC",
    "Apache-2.0",
    "BSD-2-Clause",
    "BSD-3-Clause",
    "0BSD",
    "CC0-1.0",
    "Unlicense",
    "BlueOak-1.0.0",
    "Python-2.0",
    "CC-BY-4.0",

    // MIT without the attribution clause, so strictly more permissive than MIT itself.
    "MIT-0",

    // Weak copyleft at file level. It reaches this tree through the build toolchain, never ships in
    // the bundle, and obliges nothing unless one of its own files is modified and redistributed.
    // Allowed deliberately rather than by not looking.
    "MPL-2.0",
]);

/** Splits the compound forms npm allows, so "(MIT OR Apache-2.0)" is judged as its parts. */
function parts(licence) {
    return licence
        .replaceAll("(", "")
        .replaceAll(")", "")
        .split(/\s+(?:OR|AND)\s+/i)
        .map((part) => part.trim())
        .filter(Boolean);
}

function* packages(directory) {
    for (const entry of readdirSync(directory)) {
        const path = join(directory, entry);

        if (entry.startsWith("@")) {
            yield* packages(path);
            continue;
        }

        if (entry === ".bin" || !statSync(path).isDirectory()) {
            continue;
        }

        try {
            yield JSON.parse(readFileSync(join(path, "package.json"), "utf8"));
        } catch {
            // A directory without a manifest is not a package.
        }

        const nested = join(path, "node_modules");

        try {
            if (statSync(nested).isDirectory()) {
                yield* packages(nested);
            }
        } catch {
            // No nested tree, which is the usual case.
        }
    }
}

const refused = [];
const counts = new Map();
let checked = 0;

for (const manifest of packages(modules)) {
    const declared =
        typeof manifest.license === "string"
            ? manifest.license
            : typeof manifest.license?.type === "string"
              ? manifest.license.type
              : Array.isArray(manifest.licenses)
                ? manifest.licenses.map((entry) => entry.type).join(" OR ")
                : "";

    checked++;

    if (declared === "") {
        refused.push(`${manifest.name}@${manifest.version}: declares no licence`);
        continue;
    }

    const pieces = parts(declared);

    if (pieces.some((piece) => allowed.has(piece))) {
        for (const piece of pieces.filter((part) => allowed.has(part))) {
            counts.set(piece, (counts.get(piece) ?? 0) + 1);
        }

        continue;
    }

    refused.push(`${manifest.name}@${manifest.version}: ${declared}`);
}

console.log(`Checked ${checked} packages against ${allowed.size} allowed licences.`);

for (const [licence, count] of [...counts].sort((a, b) => b[1] - a[1])) {
    console.log(`  ${String(count).padStart(5)}  ${licence}`);
}

if (refused.length > 0) {
    console.error(`\n${refused.length} package(s) are not permissively licensed:`);
    refused.forEach((line) => console.error(`  ${line}`));
    console.error(
        "\nReplace the dependency, or add its licence to the allow list in scripts/licence-audit.mjs.",
    );
    process.exit(1);
}

console.log("\nEvery package is permissively licensed.");

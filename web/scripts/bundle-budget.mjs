// Fails when a first visit costs more than it is allowed to.
//
// The number that matters is what a browser downloads before it can show anything: the entry chunk,
// everything it imports statically, and the stylesheets that come with them. Route chunks are not
// counted, because the whole point of splitting them out is that nobody pays for a screen they did
// not open.
import { gzipSync } from "node:zlib";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const web = join(dirname(fileURLToPath(import.meta.url)), "..");
const dist = join(web, "dist");
const budgetBytes = 170 * 1024;

const manifest = JSON.parse(readFileSync(join(dist, ".vite", "manifest.json"), "utf8"));
const entry = Object.values(manifest).find((chunk) => chunk.isEntry);

if (!entry) {
    console.error("The build produced no entry chunk, so there is nothing to measure.");
    process.exit(1);
}

const wanted = new Set([entry.file, ...(entry.css ?? [])]);

for (const name of entry.imports ?? []) {
    const imported = manifest[name];

    if (imported) {
        wanted.add(imported.file);
        (imported.css ?? []).forEach((file) => wanted.add(file));
    }
}

let total = 0;
const lines = [];

for (const file of wanted) {
    const size = gzipSync(readFileSync(join(dist, file))).length;
    total += size;
    lines.push(`  ${file}  ${(size / 1024).toFixed(1)} kB`);
}

console.log("First load, gzipped:");
console.log(lines.join("\n"));
console.log(
    `  total  ${(total / 1024).toFixed(1)} kB of ${(budgetBytes / 1024).toFixed(0)} kB allowed`,
);

if (total > budgetBytes) {
    console.error(
        "The first load is over budget. Split it, or argue for the dependency that grew it.",
    );
    process.exit(1);
}

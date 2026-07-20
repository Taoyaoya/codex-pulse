import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const vendor = join(
  root,
  "node_modules",
  "@openai",
  "codex-win32-x64",
  "vendor",
  "x86_64-pc-windows-msvc",
);
const runtime = join(root, "runtime");

const files = [
  [join(vendor, "bin", "codex.exe"), "codex.exe.gz"],
  [join(vendor, "codex-path", "rg.exe"), "rg.exe.gz"],
  [join(vendor, "codex-resources", "codex-command-runner.exe"), "codex-command-runner.exe.gz"],
  [join(vendor, "codex-resources", "codex-windows-sandbox-setup.exe"), "codex-windows-sandbox-setup.exe.gz"],
];

for (const [source] of files) {
  if (!existsSync(source)) {
    throw new Error(`Missing Codex runtime file: ${source}. Run npm install on Windows x64 first.`);
  }
}

mkdirSync(runtime, { recursive: true });
for (const [source, destination] of files) {
  writeFileSync(join(runtime, destination), gzipSync(readFileSync(source), { level: 9 }));
}
writeFileSync(
  join(runtime, "codex-package.json"),
  readFileSync(join(vendor, "codex-package.json")),
);

console.log("Codex Windows runtime prepared.");


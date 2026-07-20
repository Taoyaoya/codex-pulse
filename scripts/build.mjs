import { copyFileSync, existsSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const version = "2.0.4";
const buildDir = join(root, "build");
const distDir = join(root, "dist");
const brandedRuntime = join(buildDir, "bun-codexpulse.exe");
const output = join(distDir, `CodexPulse-v${version}.exe`);

if (process.platform !== "win32" || process.arch !== "x64") {
  throw new Error("Codex Pulse must currently be built on Windows x64.");
}

mkdirSync(buildDir, { recursive: true });
mkdirSync(distDir, { recursive: true });

execFileSync(process.execPath, [join(root, "scripts", "prepare-runtime.mjs")], { stdio: "inherit" });

const bunRuntime = join(root, "node_modules", "@oven", "bun-windows-x64-baseline", "bin", "bun.exe");
const bun = join(root, "node_modules", "bun", "bin", "bun.exe");
for (const file of [bunRuntime, bun]) {
  if (!existsSync(file)) {
    throw new Error(`Missing build dependency: ${file}. Run npm install first.`);
  }
}

execFileSync(process.execPath, [
  join(root, "tools", "brand-runtime.mjs"),
  bunRuntime,
  brandedRuntime,
  join(root, "assets", "CodexPulse.ico"),
  version,
], { stdio: "inherit" });

execFileSync(bun, [
  "build",
  join(root, "bootstrap", "bootstrap.ts"),
  "--compile",
  "--target=bun-windows-x64-baseline",
  `--compile-executable-path=${brandedRuntime}`,
  `--outfile=${output}`,
], { stdio: "inherit" });

execFileSync(process.execPath, [join(root, "tools", "patch-pe-subsystem.mjs"), output], { stdio: "inherit" });
copyFileSync(output, join(distDir, "CodexPulse.exe"));
console.log(`Built ${output}`);


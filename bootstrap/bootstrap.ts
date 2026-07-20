import appModelsFile from "../src/AppModels.cs" with { type: "file" };
import settingsStoreFile from "../src/SettingsStore.cs" with { type: "file" };
import quotaApiClientFile from "../src/QuotaApiClient.cs" with { type: "file" };
import themeFile from "../src/Theme.cs" with { type: "file" };
import settingsWindowFile from "../src/SettingsWindow.cs" with { type: "file" };
import mainWindowFile from "../src/MainWindow.cs" with { type: "file" };
import codexAccountClientFile from "../src/CodexAccountClient.cs" with { type: "file" };
import windowBackdropFile from "../src/WindowBackdrop.cs" with { type: "file" };
import programFile from "../src/Program.cs" with { type: "file" };
import iconFile from "../assets/CodexPulse.ico" with { type: "file" };
import codexRuntimeGzipFile from "../runtime/codex.exe.gz" with { type: "file" };
import rgRuntimeGzipFile from "../runtime/rg.exe.gz" with { type: "file" };
import commandRunnerGzipFile from "../runtime/codex-command-runner.exe.gz" with { type: "file" };
import sandboxSetupGzipFile from "../runtime/codex-windows-sandbox-setup.exe.gz" with { type: "file" };
import codexPackageFile from "../runtime/codex-package.json" with { type: "file" };

import { existsSync, readdirSync } from "node:fs";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";
import { spawn, spawnSync } from "node:child_process";

const VERSION = "2.0.4";
const sources = [
  ["AppModels.cs", appModelsFile],
  ["SettingsStore.cs", settingsStoreFile],
  ["QuotaApiClient.cs", quotaApiClientFile],
  ["CodexAccountClient.cs", codexAccountClientFile],
  ["Theme.cs", themeFile],
  ["WindowBackdrop.cs", windowBackdropFile],
  ["SettingsWindow.cs", settingsWindowFile],
  ["MainWindow.cs", mainWindowFile],
  ["Program.cs", programFile],
] as const;

function showError(message: string): void {
  const safeMessage = message.replaceAll("'", "''").slice(0, 1800);
  const command = [
    "Add-Type -AssemblyName PresentationFramework;",
    `[System.Windows.MessageBox]::Show('${safeMessage}', 'Codex Pulse', 'OK', 'Error') | Out-Null`,
  ].join(" ");
  spawnSync("powershell.exe", ["-NoProfile", "-WindowStyle", "Hidden", "-Command", command], {
    windowsHide: true,
    stdio: "ignore",
  });
}

async function extractText(assetPath: string, destination: string): Promise<void> {
  const source = await Bun.file(assetPath).text();
  await writeFile(destination, `\uFEFF${source}`, "utf8");
}

function findFrameworkReferences(windowsDirectory: string): string[] {
  const directories: string[] = [];
  const programFilesX86 = process.env["ProgramFiles(x86)"] || process.env.ProgramFiles || "C:\\Program Files (x86)";
  const referenceRoot = join(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework");
  if (existsSync(referenceRoot)) {
    const versions = readdirSync(referenceRoot)
      .filter((name) => /^v\d/.test(name))
      .sort((left, right) => right.localeCompare(left, undefined, { numeric: true }));
    for (const version of versions) {
      directories.push(join(referenceRoot, version));
    }
  }
  for (const framework of ["Framework64", "Framework"]) {
    const runtime = join(windowsDirectory, "Microsoft.NET", framework, "v4.0.30319");
    directories.push(join(runtime, "WPF"));
    directories.push(runtime);
  }
  return directories;
}

function resolveReference(fileName: string, directories: string[]): string {
  for (const directory of directories) {
    const candidate = join(directory, fileName);
    if (existsSync(candidate)) {
      return candidate;
    }
  }
  throw new Error(`未找到 Windows .NET Framework 组件：${fileName}`);
}

async function compileNativeApp(installDirectory: string, nativeExecutable: string): Promise<void> {
  const sourceDirectory = join(installDirectory, "src");
  await mkdir(sourceDirectory, { recursive: true });
  for (const [name, embeddedPath] of sources) {
    await extractText(embeddedPath, join(sourceDirectory, name));
  }
  await writeFile(join(installDirectory, "CodexPulse.ico"), await readFile(iconFile));

  const windowsDirectory = process.env.WINDIR || "C:\\Windows";
  const candidates = [
    join(windowsDirectory, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe"),
    join(windowsDirectory, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe"),
  ];
  const compiler = candidates.find(existsSync);
  if (!compiler) {
    throw new Error("未找到 Windows 自带的 .NET Framework 4.8 编译器。请启用 .NET Framework 4.8。");
  }

  const referenceDirectories = findFrameworkReferences(windowsDirectory);
  const referenceNames = [
    "PresentationCore.dll",
    "PresentationFramework.dll",
    "WindowsBase.dll",
    "System.Xaml.dll",
    "System.Windows.Forms.dll",
    "System.Drawing.dll",
    "System.Web.Extensions.dll",
  ];
  const references = referenceNames.map((name) => resolveReference(name, referenceDirectories));
  const args = [
    "/nologo",
    "/utf8output",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    `/out:${nativeExecutable}`,
    `/win32icon:${join(installDirectory, "CodexPulse.ico")}`,
    ...references.map((reference) => `/reference:${reference}`),
    ...sources.map(([name]) => join(sourceDirectory, name)),
  ];
  const result = spawnSync(compiler, args, {
    cwd: installDirectory,
    windowsHide: true,
    encoding: "utf8",
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0 || !existsSync(nativeExecutable)) {
    const output = `${result.stdout || ""}\n${result.stderr || ""}`.trim();
    throw new Error(`首次启动编译失败：\n${output || `退出码 ${result.status}`}`);
  }
}

async function extractGzipAsset(assetPath: string, destination: string): Promise<void> {
  const compressed = await readFile(assetPath);
  await writeFile(destination, Bun.gunzipSync(compressed));
}

async function extractCodexRuntime(installDirectory: string): Promise<void> {
  const runtimeDirectory = join(installDirectory, "codex-runtime");
  const binDirectory = join(runtimeDirectory, "bin");
  const pathDirectory = join(runtimeDirectory, "codex-path");
  const resourcesDirectory = join(runtimeDirectory, "codex-resources");
  await mkdir(binDirectory, { recursive: true });
  await mkdir(pathDirectory, { recursive: true });
  await mkdir(resourcesDirectory, { recursive: true });
  await extractGzipAsset(codexRuntimeGzipFile, join(binDirectory, "codex.exe"));
  await extractGzipAsset(rgRuntimeGzipFile, join(pathDirectory, "rg.exe"));
  await extractGzipAsset(commandRunnerGzipFile, join(resourcesDirectory, "codex-command-runner.exe"));
  await extractGzipAsset(sandboxSetupGzipFile, join(resourcesDirectory, "codex-windows-sandbox-setup.exe"));
  await writeFile(join(runtimeDirectory, "codex-package.json"), await readFile(codexPackageFile));
}

async function main(): Promise<void> {
  const localAppData = process.env.LOCALAPPDATA || join(homedir(), "AppData", "Local");
  const installDirectory = join(localAppData, "Programs", "CodexPulse");
  const nativeExecutable = join(installDirectory, "CodexPulseApp.exe");
  const codexExecutable = join(installDirectory, "codex-runtime", "bin", "codex.exe");
  const marker = join(installDirectory, "version.txt");
  await mkdir(installDirectory, { recursive: true });

  let installedVersion = "";
  if (existsSync(marker)) {
    installedVersion = (await readFile(marker, "utf8")).trim();
  }
  if (!existsSync(nativeExecutable) || !existsSync(codexExecutable) || installedVersion !== VERSION) {
    await compileNativeApp(installDirectory, nativeExecutable);
    await extractCodexRuntime(installDirectory);
    await writeFile(marker, VERSION, "utf8");
  }

  const child = spawn(nativeExecutable, [], { detached: true, stdio: "ignore", windowsHide: true });
  child.unref();
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  showError(message);
  process.exitCode = 1;
});

import fs from "node:fs";
import * as PE from "pe-library";
import * as ResEdit from "resedit";

const [sourcePath, destinationPath, iconPath, version = "2.0.4"] = process.argv.slice(2);
if (!sourcePath || !destinationPath || !iconPath) {
  throw new Error("用法：node brand-runtime.mjs <源EXE> <目标EXE> <ICO> [版本]");
}

const executable = PE.NtExecutable.from(fs.readFileSync(sourcePath), { ignoreCert: true });
const resources = PE.NtExecutableResource.from(executable);
const icon = ResEdit.Data.IconFile.from(fs.readFileSync(iconPath));
for (const group of ResEdit.Resource.IconGroupEntry.fromEntries(resources.entries)) {
  ResEdit.Resource.IconGroupEntry.replaceIconsForResource(
    resources.entries,
    group.id,
    group.lang,
    icon.icons.map((item) => item.data),
  );
}
for (const info of ResEdit.Resource.VersionInfo.fromEntries(resources.entries)) {
  info.setFileVersion(version, 1033);
  info.setProductVersion(version, 1033);
  info.setStringValues(
    { lang: 1033, codepage: 1200 },
    {
      ProductName: "Codex Pulse",
      ProductVersion: version,
      FileDescription: "Codex quota and reset-card monitor",
      FileVersion: version,
      CompanyName: "Codex Pulse",
      OriginalFilename: "CodexPulse.exe",
      InternalName: "CodexPulse",
    },
  );
  info.outputToResourceEntries(resources.entries);
}
resources.outputResource(executable);
fs.writeFileSync(destinationPath, Buffer.from(executable.generate()));

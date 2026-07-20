import fs from "node:fs";

const executablePath = process.argv[2];
if (!executablePath) {
  throw new Error("用法：node patch-pe-subsystem.mjs <EXE>");
}
const data = fs.readFileSync(executablePath);
if (data.toString("ascii", 0, 2) !== "MZ") {
  throw new Error("不是有效的 PE 文件。");
}
const peOffset = data.readUInt32LE(0x3c);
if (data.toString("ascii", peOffset, peOffset + 4) !== "PE\0\0") {
  throw new Error("PE 签名无效。");
}
const optionalHeader = peOffset + 24;
const magic = data.readUInt16LE(optionalHeader);
if (magic !== 0x10b && magic !== 0x20b) {
  throw new Error("不支持的 PE 可选头。");
}
const subsystemOffset = optionalHeader + 68;
const before = data.readUInt16LE(subsystemOffset);
data.writeUInt16LE(2, subsystemOffset);
fs.writeFileSync(executablePath, data);
console.log(`PE subsystem: ${before} -> 2 (Windows GUI)`);

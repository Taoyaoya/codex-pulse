# Codex Pulse

Codex Pulse 是一个 Windows 桌面额度监控应用，用于查看当前 Codex 每周额度、Token 用量、重置时间和可用重置卡次数。

## 功能

- Codex 每周剩余额度进度条
- 近 7 日 Token 用量
- 具体额度重置日期与时间
- 可用重置卡次数
- ChatGPT/Codex 账号登录和自动同步
- 多账号独立登录、额度缓存与账号切换
- 紫黑玻璃账号面板及刷新进度提示
- 刷新期间锁定账号切换并给出明确提示
- 窗口置顶、边缘缩放和托盘运行
- 半透明圆角玻璃界面
- 与主界面一致的深色托盘菜单

## 构建环境

- Windows 11 x64
- Node.js 20 或更高版本
- Windows 自带的 .NET Framework 4.8 编译器

## 构建

```powershell
npm install
npm run build
```

构建结果：

```text
dist/CodexPulse-v2.1.2.exe
dist/CodexPulse.exe
```

构建脚本会从官方 `@openai/codex` Windows npm 包准备运行时，因此仓库不提交大型二进制文件。

## 数据与隐私

应用不会读取或保存浏览器 Cookie，也不会要求用户手动填写 ChatGPT Token。登录和额度读取由随应用释放的官方 Codex CLI 完成。本地设置和额度缓存保存在：

```text
%LOCALAPPDATA%\CodexPulse
```

这些本地文件不会被提交到仓库。

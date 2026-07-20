# RobocopyGUI 项目状态

## 项目目标

Windows 平台的 Robocopy 图形化工具，提供分步骤复制向导、真实字节进度与可选文件哈希校验。

## 当前架构

- C# WinForms，目标框架 `net8.0-windows`，x64 框架依赖单文件发布。
- 入口与主要界面逻辑位于 `Program.cs`：`MainForm` 负责向导、复制和校验流程；`FilePlan` 与 `HashRecord` 承载计划与校验结果。
- 使用 Windows 自带 `robocopy.exe`；不随项目分发 Robocopy。
- `build.ps1` 通过 `dotnet publish` 生成 `publish\RobocopyGui.exe`。

## 已完成

- 分步骤路径选择、扫描、复制、哈希校验、异常记录和 CSV 报告。
- 真实复制与哈希读取进度；哈希校验在后台执行。
- V0.1.7 稳定界面布局与 GitHub Release 流程。
- GitHub Actions 每日更新仓库自托管 Star 历史 SVG。
- 2026-07-21：项目正式工作目录迁移至 `E:\AI-Workspace\Projects\RobocopyGUI`。

## 进行中与待办

- 见 `TASKS.md`。

## 已知问题与风险

- 项目依赖 Windows 自带的 `robocopy.exe`。
- 发布版需要 .NET 8 Windows Desktop Runtime（x64）。
- UI 未来计划迁移到 WinUI；开始迁移前需先拆分目前集中在 `Program.cs` 的逻辑。

## 构建与验证

```powershell
git status
dotnet restore .\RobocopyGui.csproj
dotnet build .\RobocopyGui.csproj -c Release
dotnet test .\RobocopyGui.csproj -c Release
```

若发布可执行文件：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

## 重要设计决定

- 保持 x64-only，避免维护 32 位发布物。
- 默认以 Local 模式在共享主目录工作；只有需要并行任务时才创建 worktree。
- 使用 MPL-2.0；README 需保留 Robocopy、得意黑与 ChatGPT Codex 的相关说明。


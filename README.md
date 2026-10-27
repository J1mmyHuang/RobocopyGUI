# RobocopyGUI

一个面向 Windows 的 Robocopy 图形化工具，提供分步骤操作、真实文件进度显示，以及可选的文件哈希校验功能，适合重要文件复制后的完整性确认。

## 功能

- 分步骤向导：路径选择、扫描、复制、校验和报告
- 基于实际字节数的复制进度
- 基于实际读取字节数的哈希校验进度
- 支持 MD5、SHA-1、SHA-256 和 SHA-512
- 支持中途停止复制或校验
- 详细文件日志区域可以折叠
- 显示异常文件，并支持导出 CSV 报告
- 深色界面，默认使用得意黑（Smiley Sans）字体

## 系统要求与 Robocopy

- 需要 Windows 10 或 Windows 11，以及系统自带的 `robocopy.exe`。
- 运行发布版 EXE 需要安装 [.NET 10 Windows Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/10.0)；当前发布为单文件、框架依赖版本。
- 本项目仅调用用户电脑中已有的 `robocopy.exe`，不包含、不复制、也不分发 Robocopy 或其他 Windows 系统组件。
- Robocopy 及 Windows 系统组件的权利归 Microsoft 所有，并适用其各自的许可条款。

## 构建

需要 Windows、.NET 10 SDK 和系统自带的 Robocopy。进入源代码目录后执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

编译结果位于 `publish\RobocopyGUI.exe`。

如果只需要生成框架依赖版本，也可以执行：

```powershell
dotnet build .\RobocopyGui.csproj -c Release
``` 

> 当前项目目标框架为 `.NET 10`（`net10.0-windows`）。如需发布无需安装 .NET Runtime 的版本，可在发布时改为自包含模式；代价是 EXE 文件体积会增大。

### 测试

复制计划、Robocopy 调用和哈希校验位于不依赖界面的 `RobocopyGui.Core` 类库中。运行回归测试（需要系统自带的 Robocopy）：

```powershell
dotnet test .\tests\RobocopyGui.Core.Tests\RobocopyGui.Core.Tests.csproj -c Release
```

### WinUI 3 预览版

`RobocopyGui.WinUI` 是按 WinUI 3 / Fluent 2 重做的新界面（Mica 背景、跟随系统明暗主题），与 WinForms 版共用 `RobocopyGui.Core`。除复制外还提供“剪切”模式：开启完整性校验时，只在全部文件校验一致后删除源文件；关闭校验时使用 Robocopy `/MOVE`，开始前会再次确认。

预览版为免安装（unpackaged）、自包含 Windows App SDK 1.8 的 x64 程序，只需 .NET 10 SDK，不需要 Visual Studio：

```powershell
dotnet build .\RobocopyGui.WinUI\RobocopyGui.WinUI.csproj -c Debug
.\RobocopyGui.WinUI\bin\Debug\net10.0-windows10.0.19041.0\win-x64\RobocopyGUI.WinUI.exe
```

预览版尚未加入 `build.ps1` 与 Release 发布流程。

## 使用说明

1. 选择源文件夹和目标文件夹。
2. 扫描文件并确认待复制内容。
3. 执行复制，观察真实字节进度。
4. 按需开启哈希校验，并选择校验算法。
5. 查看结果，必要时导出 CSV 报告。

## 发布版本

可执行文件不放在源代码目录中，而是作为 GitHub Release 附件发布。下载程序请前往仓库的 [Releases 页面](https://github.com/J1mmyHuang/RobocopyGUI/releases)。

## 后续计划

以下为计划中的改进方向，具体实现会根据实际需求与开发进度调整：

- 构建更现代、更完善的 UI 界面；
- 支持明暗模式手动切换，或跟随 Windows 系统主题自动切换；
- 支持中文与英文界面切换。

## 字体致谢

本项目界面使用 [得意黑 Smiley Sans](https://github.com/atelier-anchor/smiley-sans) 字体。字体项目由 atelier-anchor 维护，并依据 [SIL Open Font License 1.1](https://github.com/atelier-anchor/smiley-sans/blob/main/LICENSE) 发布。感谢字体作者和贡献者提供这款优秀的开源字体。

## 开发致谢

本项目的界面设计、功能实现和文档整理过程中使用了 [ChatGPT Codex](https://openai.com/codex/) 的协助。最终代码、构建结果和公开发布内容由项目维护者审阅并负责。

## 许可证

本项目采用 [Mozilla Public License 2.0 (MPL-2.0)](LICENSE)。

当你发布对本项目现有源代码文件的修改，或发布包含这些文件的可执行程序时，需保留 MPL-2.0 许可与版权声明，并以合理方式提供相应源代码。MPL-2.0 为按文件适用的弱著佐权许可证：它允许与独立文件组成更大的作品；但本项目受 MPL 覆盖的源代码文件及其修改仍须遵守 MPL-2.0。

## Star 历史

![RobocopyGUI Star 历史](assets/star-history.svg)

图表由本仓库的 GitHub Actions 每天自动更新。

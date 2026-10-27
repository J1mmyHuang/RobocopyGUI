# RobocopyGUI

一个面向 Windows 的 Robocopy 图形化工具，提供分步骤操作、真实文件进度显示，以及可选的文件哈希校验功能，适合重要文件复制后的完整性确认。

## 功能

- WinUI 3 / Fluent 2 原生界面（Mica 背景），跟随 Windows 明暗主题
- 分步骤显示任务进度：路径选择、扫描、复制、校验、（剪切时）删除源文件、报告
- 复制与剪切两种模式：
  - 开启完整性校验的剪切：全部文件复制并校验一致后，才删除源文件；复制失败、校验异常或中途停止时不删除任何源文件
  - 关闭校验的剪切：使用 Robocopy `/MOVE`，复制成功后立即删除源文件；开始前会再次确认
- 基于实际字节数的复制进度，以及基于实际读取字节数的哈希校验进度
- 支持 MD5、SHA-1、SHA-256 和 SHA-512
- 支持中途停止复制或校验
- 源可以是一个文件夹或单个文件，也可以直接拖到窗口中；和资源管理器一样，文件夹会放进目标文件夹下的同名文件夹
- “高级参数”中显示实际执行的 Robocopy 命令
- 显示异常文件，并支持导出 CSV 报告

## 系统要求与 Robocopy

- 需要 Windows 10 1809 或更高版本、Windows 11（x64），以及系统自带的 `robocopy.exe`。
- 需要安装两个 Microsoft 运行库（x64），程序不随包附带：
  - [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)（.NET Runtime 或 Windows Desktop Runtime 均可）；
  - [Windows App Runtime 1.8](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)。
- 缺少上述运行库时，按设计程序启动会弹出提示并给出下载链接（缺 .NET 时由 .NET 启动器提示，缺 Windows App Runtime 时由 Windows App SDK 的引导程序提示）。**这一行为尚未在未安装运行库的电脑上验证**；如果双击后程序没有任何反应，请先手动安装上面两个运行库。
- 发布版是一个约 10 MB 的 zip 压缩包，解压后运行 `RobocopyGUI\RobocopyGUI.exe`，无需安装。
- 本项目仅调用用户电脑中已有的 `robocopy.exe`，不包含、不复制、也不分发 Robocopy 或其他 Windows 系统组件。
- Robocopy 及 Windows 系统组件的权利归 Microsoft 所有，并适用其各自的许可条款。

## 构建

需要 Windows、.NET 10 SDK 和系统自带的 Robocopy。进入源代码目录后执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

编译结果位于 `publish\RobocopyGUI\RobocopyGUI.exe`，同时生成可上传到 Release 的 `publish\RobocopyGUI-win-x64.zip`。构建只需要 .NET 10 SDK，不需要 Visual Studio。

开发时也可以直接构建并运行：

```powershell
dotnet build .\RobocopyGui.WinUI\RobocopyGui.WinUI.csproj -c Debug
.\RobocopyGui.WinUI\bin\Debug\net10.0-windows10.0.19041.0\win-x64\RobocopyGUI.exe
```

> 程序为免安装（unpackaged）的 x64 应用，依赖用户系统中安装的 .NET 10 运行时和 Windows App Runtime 1.8，发布包里只有程序本身和必要的小型引导/投影库。

### 经典版（WinForms）

`RobocopyGui.csproj` 是原来的 WinForms 界面，只支持复制文件夹，并保持 Robocopy 本身的行为（复制源文件夹里的内容，不在目标中创建同名文件夹）；与新版共用同一个 `RobocopyGui.Core`。如需构建单文件版本：

```powershell
.\build.ps1 -Classic
```

结果位于 `publish\classic\RobocopyGUI.exe`，运行需要 .NET 10 Windows Desktop Runtime（x64）。

### 测试

复制计划、Robocopy 调用和哈希校验位于不依赖界面的 `RobocopyGui.Core` 类库中。运行回归测试（需要系统自带的 Robocopy）：

```powershell
dotnet test .\tests\RobocopyGui.Core.Tests\RobocopyGui.Core.Tests.csproj -c Release
```

## 使用说明

1. 选择（或拖入）一个源文件夹或单个文件，再选择目标文件夹。“浏览…”默认选择文件夹，点右侧箭头可以改为选择文件。
2. 选择“复制”或“剪切”，按需开启或关闭完整性校验并选择算法。
3. 位置卡片下方会显示结果位置，确认无误后点击“开始”；剪切会先弹出确认。
4. 查看结果和异常文件，必要时导出 CSV 报告。

结果位置与资源管理器一致：

| 源 | 目标文件夹 | 结果 |
| --- | --- | --- |
| 文件夹 `D:\照片` | `E:\备份` | `E:\备份\照片\…` |
| 文件 `D:\文档\报告.docx` | `E:\备份` | `E:\备份\报告.docx` |
| 整个磁盘 `D:\` | `E:\备份` | `E:\备份\…`（复制磁盘中的内容；不能剪切整个磁盘） |

剪切一个文件夹时，整个文件夹会移到目标中，源文件夹随后删除；剪切单个文件时，只删除这个文件，它所在的文件夹和其他文件不受影响（关闭校验时使用 Robocopy `/MOV` 而不是 `/MOVE`）。目标文件夹不能位于源文件夹内。

## 发布版本

可执行文件不放在源代码目录中，而是作为 GitHub Release 附件发布。下载程序请前往仓库的 [Releases 页面](https://github.com/J1mmyHuang/RobocopyGUI/releases)。

## 后续计划

以下为计划中的改进方向，具体实现会根据实际需求与开发进度调整：

- 支持明暗模式手动切换（目前跟随 Windows 系统主题）；
- 支持中文与英文界面切换。

## 字体致谢

本项目经典版（WinForms）界面使用 [得意黑 Smiley Sans](https://github.com/atelier-anchor/smiley-sans) 字体。字体项目由 atelier-anchor 维护，并依据 [SIL Open Font License 1.1](https://github.com/atelier-anchor/smiley-sans/blob/main/LICENSE) 发布。感谢字体作者和贡献者提供这款优秀的开源字体。

## 开发致谢

本项目的界面设计、功能实现和文档整理过程中使用了 [ChatGPT Codex](https://openai.com/codex/) 的协助。最终代码、构建结果和公开发布内容由项目维护者审阅并负责。

## 许可证

本项目采用 [Mozilla Public License 2.0 (MPL-2.0)](LICENSE)。

当你发布对本项目现有源代码文件的修改，或发布包含这些文件的可执行程序时，需保留 MPL-2.0 许可与版权声明，并以合理方式提供相应源代码。MPL-2.0 为按文件适用的弱著佐权许可证：它允许与独立文件组成更大的作品；但本项目受 MPL 覆盖的源代码文件及其修改仍须遵守 MPL-2.0。

## Star 历史

![RobocopyGUI Star 历史](assets/star-history.svg)

图表由本仓库的 GitHub Actions 每天自动更新。

# RobocopyGUI

一个面向 Windows 的 Robocopy 图形化工具，提供分步骤操作、真实文件进度显示，以及可选的文件哈希校验功能，适合重要文件复制后的完整性确认。

## 功能

- 分步骤向导：路径选择、扫描、复制、校验和报告
- 基于实际字节数的复制进度
- 基于实际读取字节数的哈希校验进度
- 支持 MD5、SHA-1、SHA-256 和 SHA-512
- 哈希校验在后台执行，界面不会因校验任务卡住
- 支持中途停止复制或校验
- 详细文件日志区域可以折叠
- 手动查看历史日志时不会被自动滚动拉回底部
- 显示异常文件，并支持导出 CSV 报告
- 深色界面，默认使用得意黑（Smiley Sans）字体

## 系统要求与 Robocopy

- 需要 Windows 10 或 Windows 11，以及系统自带的 `robocopy.exe`。
- 本项目仅调用用户电脑中已有的 `robocopy.exe`，不包含、不复制、也不分发 Robocopy 或其他 Windows 系统组件。
- Robocopy 及 Windows 系统组件的权利归 Microsoft 所有，并适用其各自的许可条款。

## 构建

需要 Windows、.NET 8 SDK 和系统自带的 Robocopy。进入源代码目录后执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

编译结果位于 `publish\RobocopyGUI.exe`。

如果只需要生成框架依赖版本，也可以执行：

```powershell
dotnet build .\RobocopyGUI.csproj -c Release
``` 

## 使用说明

1. 选择源文件夹和目标文件夹。
2. 扫描文件并确认待复制内容。
3. 执行复制，观察真实字节进度。
4. 按需开启哈希校验，并选择校验算法。
5. 查看结果，必要时导出 CSV 报告。

## 发布版本

可执行文件不放在源代码目录中，而是作为 GitHub Release 附件发布。下载程序请前往仓库的 [Releases 页面](https://github.com/J1mmyHuang/RobocopyGui/releases)。

## 字体致谢

本项目界面使用 [得意黑 Smiley Sans](https://github.com/atelier-anchor/smiley-sans) 字体。字体项目由 atelier-anchor 维护，并依据 [SIL Open Font License 1.1](https://github.com/atelier-anchor/smiley-sans/blob/main/LICENSE) 发布。感谢字体作者和贡献者提供这款优秀的开源字体。

## 开发致谢

本项目的界面设计、功能实现和文档整理过程中使用了 [ChatGPT Codex](https://openai.com/codex/) 的协助。最终代码、构建结果和公开发布内容由项目维护者审阅并负责。

## 许可证

本项目采用 [Mozilla Public License 2.0 (MPL-2.0)](LICENSE)。

当你发布对本项目现有源代码文件的修改，或发布包含这些文件的可执行程序时，需保留 MPL-2.0 许可与版权声明，并以合理方式提供相应源代码。MPL-2.0 为按文件适用的弱著佐权许可证：它允许与独立文件组成更大的作品；但本项目受 MPL 覆盖的源代码文件及其修改仍须遵守 MPL-2.0。

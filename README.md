# Robocopy 复制与完整性校验

一个 Windows 图形化文件复制工具，使用系统自带的 Robocopy，并支持复制完成后的 MD5、SHA-1、SHA-256、SHA-512 校验。

## 功能

- 分步骤向导：路径、扫描、复制、校验、报告
- 基于实际字节数的复制进度与哈希读取进度
- 校验任务在后台执行，支持中途停止
- 详细日志区域可折叠，手动滚动日志时不会被强制拉回底部
- 异常文件显示和完整 CSV 报告导出
- 深色界面，默认使用得意黑（Smiley Sans）字体

## 构建

需要 Windows、.NET 8 SDK 和系统自带的 Robocopy。进入本目录后执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

输出文件位于 `publish\RobocopyGui.exe`。如果只希望生成框架依赖版本，也可以直接执行：

```powershell
dotnet build .\RobocopyGui.csproj -c Release
```

## 发布到 GitHub

### 方式一：网页操作

1. 登录 GitHub，点击右上角 `+` → `New repository`。
2. 仓库名可以填写 `RobocopyGui`，选择 `Public`。
3. 建议不要勾选自动创建 README、License 或 `.gitignore`，因为本项目已经提供这些文件。
4. 创建仓库后，点击 `uploading an existing file`，把本目录中的文件上传。
5. 将 `RobocopyGui.exe` 作为 Release 附件上传，不建议把编译产物直接混在源代码目录中。

### 方式二：使用 Git 命令

先在 GitHub 创建一个空的 Public 仓库，然后在本目录打开 PowerShell：

```powershell
git init
git add .
git commit -m "Initial open-source release"
git branch -M main
git remote add origin https://github.com/YOUR_NAME/RobocopyGui.git
git push -u origin main
```

把 `YOUR_NAME/RobocopyGui` 替换成你自己的 GitHub 用户名和仓库名。第一次推送时，GitHub 会要求登录或使用 Personal Access Token。

## 发布 EXE

建议在 GitHub 仓库页面依次点击 `Releases` → `Draft a new release`，创建版本标签，例如 `v1.0.0`，然后把 `RobocopyGui.exe` 上传为附件。这样源代码和可下载程序会分开管理。

## 许可证

本项目使用 MIT License，详见 `LICENSE`。

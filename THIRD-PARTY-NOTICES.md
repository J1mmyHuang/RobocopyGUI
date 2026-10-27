# 第三方组件声明 / Third-party notices

RobocopyGUI 自身的源代码以 GNU AGPL-3.0-or-later 授权，并附有一条第 7 条附加许可（见 `LICENSE` 与 `LICENSE-EXCEPTION.txt`）。

发布包里还包含下列第三方组件。它们**不受 AGPL 约束**，分别适用各自的许可条款，许可原文放在 `licenses` 文件夹中。使用、复制或再分发 RobocopyGUI 的发布包，即表示你同意这些组件各自的条款。

RobocopyGUI's own source code is licensed under the GNU AGPL-3.0-or-later with an additional permission under section 7 (see `LICENSE` and `LICENSE-EXCEPTION.txt`). The release package also contains the third-party components below. They are **not** covered by the AGPL; each is governed by its own license terms, included in the `licenses` folder. By using, copying or redistributing the RobocopyGUI release package you agree to those terms.

| 组件 / Component | 发布包中的文件 / Files in the package | 许可 / License |
| --- | --- | --- |
| Windows Community Toolkit 8.2（CommunityToolkit.WinUI.*、CommunityToolkit.Common） | `CommunityToolkit.*.dll` | MIT：`licenses/CommunityToolkit.Windows-License.md`、`licenses/CommunityToolkit.Dotnet-License.md`、`licenses/CommunityToolkit.Dotnet-ThirdPartyNotices.txt` |
| Microsoft Windows App SDK 1.8 | `Microsoft.WinUI.dll`、`Microsoft.WindowsAppRuntime.Bootstrap*.dll`、`Microsoft.InteractiveExperiences.Projection.dll`、`Microsoft.Security.Authentication.OAuth.Projection.dll`、`Microsoft.Windows.*.Projection.dll`、`Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll` | Microsoft Software License Terms：`licenses/Microsoft.WindowsAppSDK-License.txt`、`licenses/Microsoft.WindowsAppSDK-Notice.txt` |
| Microsoft Edge WebView2 SDK | `Microsoft.Web.WebView2.Core*.dll`、`WebView2Loader.dll` | `licenses/Microsoft.Web.WebView2-License.txt`、`licenses/Microsoft.Web.WebView2-Notice.txt` |
| Windows SDK projection for .NET（Microsoft.Windows.SDK.NET.Ref 10.0.19041.57） | `Microsoft.Windows.SDK.NET.dll`、`WinRT.Runtime.dll` | Windows SDK License Terms：<https://aka.ms/WinSDKLicenseURL>。`WinRT.Runtime.dll` 来自 C#/WinRT 项目（MIT，<https://github.com/microsoft/CsWinRT>），随该投影包分发。 |

## 不包含的组件 / Not included

- **.NET 10 运行时**与 **Windows App Runtime 1.8**：由用户从 Microsoft 官方渠道安装，发布包中不附带。
  The .NET 10 runtime and the Windows App Runtime 1.8 are installed by the user from Microsoft and are not part of the package.
- **robocopy.exe**：Windows 自带的组件。RobocopyGUI 只是启动用户电脑上已有的 `robocopy.exe`，不包含、不复制、也不分发它。
  robocopy.exe is part of Windows. RobocopyGUI only starts the copy already present on the user's computer; it does not contain or distribute it.

## 商标 / Trademarks

RobocopyGUI 是独立项目，与 Microsoft 没有关联，也未获 Microsoft 认可或赞助。Microsoft、Windows、WinUI、Robocopy 及相关名称是 Microsoft 的商标或产品名称。

RobocopyGUI is an independent project. It is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft, Windows, WinUI, Robocopy and related names are trademarks or product names of Microsoft.

# 参与贡献 / Contributing

感谢你愿意参与 RobocopyGUI。

## 许可

- 提交到本仓库的贡献（代码、文档等）以 **GNU AGPL-3.0-or-later** 授权，并适用 [LICENSE-EXCEPTION.txt](LICENSE-EXCEPTION.txt) 中的第 7 条附加许可，与项目本身相同。
- 请只提交你有权以上述许可发布的内容；不要提交来源不明、或与 AGPL 不兼容的代码和素材。

Contributions are licensed under the GNU AGPL-3.0-or-later together with the section 7 additional permission in `LICENSE-EXCEPTION.txt`, the same terms as the project itself. Only submit work you have the right to license this way.

## DCO 签署

本项目使用 [Developer Certificate of Origin 1.1](https://developercertificate.org/)（DCO）。每个提交都需要带上 `Signed-off-by` 行，表示你确认 DCO 中的声明：

```powershell
git commit -s -m "fix: ..."
```

`-s` 会在提交信息末尾加上：

```text
Signed-off-by: 你的名字 <你的邮箱>
```

签名中的姓名和邮箱应与提交作者一致。

This project uses the Developer Certificate of Origin 1.1. Sign off every commit (`git commit -s`); the `Signed-off-by` name and email must match the commit author.

## 提交前

- 运行回归测试（需要系统自带的 Robocopy）：

  ```powershell
  dotnet test .\tests\RobocopyGui.Core.Tests\RobocopyGui.Core.Tests.csproj -c Release
  ```

- 会删除、移动或覆盖数据的改动（剪切、`/MOVE`、`/MOV`、`/MIR`、`/PURGE` 等）必须在界面中如实说明后果和范围，并配有测试。
- 界面文字与实际执行的 Robocopy 参数必须一致；“高级参数”中应能看到实际命令。

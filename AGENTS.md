# 多 Agent 协作约定

本项目目录可由 Codex 和 OpenCode 共用。开始任何工作前，必须：

1. 阅读 `PROJECT_STATUS.md`、`TASKS.md`、`CHANGELOG_AI.md` 与本文件。
2. 运行 `git status`，并以磁盘文件、Git 历史和当前 diff 作为唯一可靠状态。
3. 不依赖另一 Agent 的聊天记录；不得覆盖、丢弃或混入另一 Agent 未提交的修改。

协作规则：

- 同一工作目录同一时间只允许一个 Agent 修改。
- 需要并行时，使用不同分支和 Git worktree；合并前检查 diff 与测试结果。
- 修改完成后，运行适用的构建和测试，并记录结果。
- 每项任务完成后更新 `PROJECT_STATUS.md`、`TASKS.md`、`CHANGELOG_AI.md`。
- Windows 项目默认使用 PowerShell 和 Windows 兼容路径。
- 不提交 API Key、Token、密码、`.env`、凭据或私钥。


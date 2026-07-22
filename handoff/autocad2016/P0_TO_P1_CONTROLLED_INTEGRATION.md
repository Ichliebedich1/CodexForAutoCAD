# P0 停止生命周期到 P1 CadContextJson v2 的受控集成清单

最后更新：2026-07-22（北京时间）

## 前置条件

只有在固定 P0 `0.3.2` 候选完成人工 AutoCAD 2016 验证、更新 live evidence 并形成独立
提交后，才允许把该提交引入 `codex/cad-context-v2`。本文件不是合并授权，也不表示 P0
或 P1 已通过实机验证。

## 当前重叠文件

以下文件在两个 Worktree 中均有修改，禁止使用整文件覆盖方式解决冲突：

- `handoff/autocad2016/CURRENT_STATE.md`
- `src/Codex.AutoCAD.Host.2016/Codex.AutoCAD.Host.2016.csproj`
- `src/Codex.AutoCAD.Host.2016/CodexCad2016Commands.cs`
- `src/Codex.AutoCAD.Host.2016/MvpAgentClient.cs`
- `src/Codex.AutoCAD.Host.2016/packages.lock.json`
- `tests/Codex.AutoCAD.Host.2016.Mvp.Specs/Codex.AutoCAD.Host.2016.Mvp.Specs.csproj`
- `tests/Codex.AutoCAD.Host.2016.Mvp.Specs/Program.cs`

## 合并原则

1. `MvpAgentClient.cs` 以 P0 的可重试停止协调和资源清理顺序为生命周期基线，再人工保留
   P1 的 `AgentTurnStartV2Request`、`StartTurnV2Async` 和
   `MvpAgentCapabilityPolicy.SupportsCadContextV2`。不得恢复 v1 回退。
2. Host 项目文件取并集：保留 P0 `MvpAgentStopCoordinator.cs`，同时保留 P1
   `MvpAgentCapabilityPolicy.cs`、v2 Capture/Mapper/Hash 和 Palette Runtime 文件。
3. `CodexCad2016Commands.cs` 保留 P0 的停止命令行为和错误提示，同时保留 P1 Doctor 的
   `codex.autocad.cad-context/2` 与 v2 状态文案。
4. Host MVP Specs 合并为同一套测试入口：保留 P0 停止重试/并发/异常规格，并保留 P1
   `6/6` v2 能力 fail-closed 规格；不得形成两套互不运行的测试程序。
5. `packages.lock.json` 不手工拼接。源文件冲突解决后，以锁定模式重新还原并核对只出现
   预期框架依赖变化。
6. `CURRENT_STATE.md` 人工合并证据边界：P0 live 结论只来自 P0 固定候选，P1 v2 仍保持
   `NetLoadVerified=false`，直到新的合并候选完成人工 NETLOAD。

## 引入后必须重新执行

- P0 Host Stop Specs：预期保持 `13/13` 或合并后更高计数。
- P1 Host v2 Specs：`12/12`。
- P1 Host v2 能力策略 Specs：`6/6`。
- Bridge Client net45/net8、Bridge、AgentLauncher 和认证兼容门禁。
- 完整 Phase 2，动态计数不得低于当前 `232/232`。
- R20.1 net45/x64 Release 构建、禁用 API、秘密扫描和 `git diff --check`。
- 重新冻结 Host DLL 与 AgentHost，并生成新的候选 ID、SHA-256 和测试手册。

## 禁止事项

- 不把 P1 当前 DLL 当作含 P0 修复的产品候选。
- 不以 `ours`/`theirs` 整体选择覆盖 `MvpAgentClient.cs` 或 Host Specs。
- 不修改 v1 固定向量。
- 不继承旧候选的 NETLOAD、DBMOD 或无残留结论到新合并候选。
- 不把 P0 与 P1 合成一个无法独立审计的提交。

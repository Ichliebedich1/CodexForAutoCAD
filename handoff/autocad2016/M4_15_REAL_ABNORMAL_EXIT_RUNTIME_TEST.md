# M4.15.3 真实异常退出矩阵

最后更新：2026-07-27（北京时间）

## 1. 目的和边界

这是进入 M5 CAD 写入前唯一不得延期的实机矩阵。它验证真实 AutoCAD 2016、真实 AgentHost 和
真实本机 Codex app-server 在正常退出、强制终止和启动中断时：

- 每个活动请求只出现一个终态；
- AgentHost/Codex 进程树可在有界时间内清理，最终残留为 `0`；
- 后续 ASK fail-closed，不继续接受 CAD 工具调用；
- 错误只包含稳定 code/stage 和“是否有 request_id”，不泄漏路径、stderr、账户或环境内容；
- CAD 写入和插件保存始终禁用。

本矩阵必须使用脱敏测试图或空白图。不要在生产图、未保存图或带真实客户信息的图中执行。
不要改 PATH、注册表、AppLocker/WDAC/EDR、Defender 排除项、AutoCAD 自动保存设置或
`TRUSTEDPATHS`。不要把任务管理器截图、完整 PID、路径或事件日志原文发到聊天中。

## 2. 候选冻结前置

1. 从干净的已提交候选 C 运行 `scripts/verify-all-gates.ps1`，确认：
   - `9/9`；
   - 双 Shell Phase 2 均全部通过；
   - `Source.HeadCommit=C`、`Source.WorkingTreeDirty=false`；
   - Host/AgentHost SHA-256 已记录；
   - AutoCAD 未被门禁启动，新增残留为 `0`，User PATH 不变。
2. 在同一个干净提交 C 上生成完整 M4 实机候选：

   ```powershell
   $env:CODEX_AUTOCAD_ARTIFACT_BASE = 'E:\cfa'
   $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
   .\scripts\verify-autocad2016-context-v2-candidate.ps1 `
     -CandidateProfile m4-live `
     -Configuration Release `
     -AutoCad2016Dir 'D:\AutoCAD 2016'
   ```

   该命令必须输出 `SOURCE_HEAD=C`，Host/AgentHost DLL SHA-256 必须与同一次
   `m4-readiness.json` 一致。候选 `manifest.json` 必须为 schema 2，并包含 `m4Binding`；
   `AgentHost` 子目录必须同时包含 EXE、DLL、runtimeconfig、deps 和 EXE `.sha256` sidecar。
   `all-gates.json` 必须以同一 Run ID 精确绑定当前 readiness；bootstrap 的隔离 A/B 完整
   runnable 输出树必须再次按相对路径和 SHA-256 一致。任何 dirty source、旧 Run ID、
   hash/文件树不一致或多个 bootstrap 输出匹配都必须拒绝。
3. 只对该完整候选根目录的 `Codex.AutoCAD.Host.2016.dll` 执行 `NETLOAD`；不要加载门禁
   stage 目录、旧候选或任何单独依赖 DLL。
4. AutoCAD 命令行先执行：

   ```text
   DBMOD
   NETLOAD
   CODEXCADDOCTOR
   CODEX16PAL
   CODEX16PALINFO
   DBMOD
   ```

5. 检查 Host 仍为 AutoCAD R20.1 / .NET Framework 4.5 / x64，CAD write、插件保存均 disabled。
6. 使用任务管理器“详细信息”页观察以下三类进程，但反馈只写计数：
   - `acad.exe`
   - `Codex.AutoCAD.AgentHost.exe`
   - 命令行为 `app-server` 的本机 `codex.exe`

任一候选哈希不一致、DBMOD 因插件命令改变、CAD 写入/保存不再 disabled，立即停止整个矩阵。

## 3. 每个场景共同记录

每个场景使用一个新的干净 AutoCAD 进程。开始前记录三类进程计数，结束后等待最多 30 秒并再次
记录。只保存以下脱敏字段：

| 字段 | 允许值 |
|---|---|
| scenario | 本文件固定场景 ID |
| result | `passed` / `failed` |
| public_error_code | 稳定错误码；不要复制错误正文 |
| error_stage | 稳定阶段 |
| request_id_present | `true` / `false`；不要记录真实值 |
| terminal_event_count | 必须为 `1` |
| subsequent_ask_fail_closed | `true` / `false` |
| agenthost_residual_count | 必须为 `0` |
| codex_app_server_residual_count | 必须为 `0` |
| sensitive_data_exposed | 必须为 `false` |
| dbmod_unchanged | 必须为 `true` |

将全部场景写入一份本地脱敏 Markdown 或 JSON 报告，确认不含路径、用户名、PID、提示词正文和
完整 CAD 上下文，再计算该报告 SHA-256。该 SHA-256 才能写入
`live-matrix-results.json` 的 `EvidenceSha256`。

## 4. 场景 A：正常 STOP 基线

1. `CODEX16AGENTSTART`，等待 Palette 明确在线。
2. 预选一个受支持对象并执行 `CODEX16CTX`。
3. 发送一轮带本地随机标记的问题，等待回答完成。
4. 执行 `CODEX16AGENTSTOP` 两次。
5. 等待最多 30 秒，确认 AgentHost 与 Codex app-server 残留均为 `0`。
6. 执行 `CODEX16PALINFO` 和 `DBMOD`。

预期：第二次 STOP 幂等；无错误终态；DBMOD 不变。该场景是后续强杀场景的对照，不单独满足
`RealAbnormalExitMatrixVerified`。

## 5. 场景 B：流式回答中强杀 Codex app-server

1. 启动 AgentHost、捕获只读上下文，发送一个会产生数秒流式回答的问题。
2. 在已有回复增量后，用任务管理器结束该会话对应的 `codex.exe` app-server。
3. 不点击重试，不再次发送原问题，观察 Palette 最终状态。
4. 回答停止后执行一次 `CODEX16ASK`，在输入提示出现时输入一个短问题。
5. 执行 STOP，等待残留收敛。

预期：活动请求只进入一次 `failed`；后续 ASK fail-closed；不自动重复发送；不把原始 stderr 或
本地路径显示给用户；最终 AgentHost/Codex 残留均为 `0`。

## 6. 场景 C：流式回答中强杀 AgentHost

1. 重新启动干净 AutoCAD，启动 AgentHost、捕获上下文并发送流式问题。
2. 在已有回复增量后，用任务管理器结束 `Codex.AutoCAD.AgentHost.exe`。
3. 观察 Palette 只产生一个 `agenthost_unexpected_exit` 终态。
4. 执行一次 `CODEX16ASK`，确认 fail-closed。
5. 等待最多 30 秒，检查 AgentHost 及其 Codex app-server 后代均为 `0`。

预期：`error_stage=agenthost_runtime`、不可自动重试、活动 request 只失败一次；迟到 Bridge
断线不能覆盖该终态。

## 7. 场景 D：启动握手阶段强杀 AgentHost

1. 重新启动干净 AutoCAD并打开 Palette。
2. 执行 `CODEX16AGENTSTART` 后，在“在线”出现前立即结束 AgentHost。
3. 等待至少一个完整启动超时窗口，但不超过 30 秒。
4. 检查 Palette 不会在 STOP/强杀后迟到显示“在线”。
5. 执行 ASK，确认 fail-closed；检查残留为 `0`。

预期：启动中断只产生一个稳定失败/停止终态，不同时显示“启动失败”和“已完成”，不自动重启或
重复发送。

## 8. 场景 E：强杀 AutoCAD

1. 使用空白或可丢弃测试图，确认没有需要保存的修改；记录 DBMOD。
2. 启动 AgentHost，捕获只读上下文并确认一轮 ASK 可用。
3. 再发送一个流式问题，在回答中用任务管理器结束 `acad.exe`。
4. 不手动结束 AgentHost/Codex，等待最多 30 秒。
5. 确认 AutoCAD 已退出，AgentHost 与 Codex app-server 残留均为 `0`。
6. 启动新的 AutoCAD 进程，只做 Doctor/Palette 诊断，确认插件仍可正常加载。

预期：宿主消失后 Job/owner 清理整棵进程树；没有自动保存或恢复写入归因给插件。若 AutoCAD
超过 30 秒仍未退出或产生崩溃循环，记录失败并停止后续矩阵。

## 9. 通过条件

场景 A–E 必须全部通过，且：

- B 证明真实 Codex 强杀；
- C/D 证明真实 AgentHost 强杀和启动中断；
- E 证明真实 AutoCAD 强杀；
- 每个故障的 `terminal_event_count=1`；
- 所有强杀场景最终 `agenthost_residual_count=0`、
  `codex_app_server_residual_count=0`；
- B–D 的后续 ASK 全部 fail-closed；
- 所有场景 `sensitive_data_exposed=false`、`dbmod_unchanged=true`。

任何一项缺失都不能把 `RealAbnormalExitMatrixVerified` 写成 `verified`。

## 10. 结果入库

实机通过后按 `M4_15_LIVE_MATRIX_RESULTS_CONTRACT.md`：

1. 在 `live-matrix-results.json` 中绑定候选提交 C、Host SHA-256、AgentHost SHA-256；
2. 将 `RealAbnormalExitMatrixVerified` 写成 `verified`，其 `EvidenceSha256` 指向脱敏结果报告；
3. 其他八项逐项选择 `verified` 或有理由的 `deferred`；
4. evidence-only 提交 E 只能包含这一个 JSON；
5. 用户创建的回滚 ref 指向实际测试过的 C；
6. 从干净的 E 运行 M4.16 冻结前置检查。

冻结检查通过仍只表示“可以构建冻结候选”，不会自动启用 CAD 写入、保存或进入 M5。

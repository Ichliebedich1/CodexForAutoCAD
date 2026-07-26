# M4.13 审计保留与受控清理边界（2026-07-25）

## 状态

本文件记录当前未提交的 M4.13 切口。它已进入真实 AgentHost CLI，但尚未成为 AutoCAD UI
功能，也不是后台自动清理服务。M4.13 仍为进行中。

当前自动化证据：

- Bridge Specs：71/71。
- Windows PowerShell 5.1 Phase 2：382/382。
- PowerShell 7 Phase 2：382/382。
- Release：0 warning / 0 error。
- Host 禁用 API、AgentHost doctor、敏感信息扫描和 git diff --check：通过。

## 只读计划

命令：

```text
Codex.AutoCAD.AgentHost audit-retention-plan \
  --older-than-days <1..3650> \
  --max-store-mib <1..1048576> \
  --retain-complete <0..4096>
```

边界：

- 固定读取当前用户受保护审计根，不接受任意目录。
- 只生成计划，不删除、移动、改写或修复文件。
- 只有完整且以 session_stopped/session_failed 终止的会话可成为候选。
- 最新最低保留集不可被年龄或容量策略覆盖。
- incomplete、corrupt、anchor_mismatch 固定人工复核。
- 未识别 artifact 计入容量，但不成为候选。
- 输出不含本地路径。
- planId 是对策略、容量、完整性状态、artifact 字节数/UTC 时间及最终 action 的确定性
  SHA-256；generatedAtUtc 不参与，以便未发生语义变化时短时间复算保持稳定。

## 显式执行

命令：

```text
Codex.AutoCAD.AgentHost audit-retention-apply \
  --plan <audit-retention-plan 返回的 64 位小写 planId> \
  --older-than-days <与计划完全相同> \
  --max-store-mib <与计划完全相同> \
  --retain-complete <与计划完全相同>
```

这是有破坏性的运维命令。当前阶段不得从 AutoCAD UI、Agent 工具或后台定时器调用；只允许
明确了解计划内容的本机操作者手工确认 planId。

执行顺序：

1. 创建并验证当前用户受保护审计根、segments、anchors、retention-control 目录及身份句柄。
2. 在 retention-control 内取得排他文件锁；并发 apply 返回 cleanup_busy。
3. 重新读取 Catalog、完整验链、重新计算计划并精确比较 planId。
4. 为全部候选建立一个 journal，记录精确 session、段数、文件名、长度、UTC 时间和 SHA-256。
5. journal 先写临时文件、Flush(true)，再在同一控制目录原子 rename；首个删除只发生在提交后。
6. 删除前再次读取元数据并流式计算 SHA-256；任何变化返回 artifact_changed。
7. anchor 先删除，再按 journal 删除 segments；中断后缺失文件只在同一已验证 journal 内视为
   已完成步骤。
8. 全部完成后原子写 receipt，再移除 journal；相同 planId 重复执行返回 already_applied。

## 中断和冲突

- journal 已提交、尚未删除：相同 planId 可恢复。
- 删除了一部分：相同 planId 从 journal 继续，剩余文件仍逐个复验。
- receipt 已提交但 journal 尚未移除：视为已完成，清除同计划 journal。
- journal schema、未知字段、session、段数、段序列、文件名或哈希无效：journal_invalid。
- 计划后 artifact/容量/时间/策略变化：plan_changed，首个删除前拒绝。
- journal 后 artifact 变化：artifact_changed，保留 journal 和剩余证据供人工处理。
- 发现另一个计划的 journal 或 journal.tmp：journal_conflict；不得并行开启新清理。
- 同一计划已完成：already_applied，不重复删除。

## 已覆盖自动化

- 只删除 eligible_age/eligible_capacity，不删除 retain_minimum 或人工复核证据。
- 计划指纹稳定，store 变化后旧 planId 失效。
- 首删前 journal 耐久提交。
- 删除一步后故障注入与恢复。
- journal 段数损坏、不同计划临时日志、artifact 篡改均失败关闭。
- 并发执行器由控制目录锁串行化。
- receipt 幂等且不包含本地路径。
- 非完整、损坏、anchor mismatch 永不进入删除 journal。
- 测试专用子进程执行真实 `AgentHostAuditRetentionExecutor.Apply`，在 journal 耐久提交并删除首个
  anchor 后由父进程使用 `Process.Kill(entireProcessTree: true)` 强杀；新租约以原 plan ID 恢复，
  候选剩余 segment 被删除、最低保留会话仍存在、journal 被清除且无残留工作器。
- 最近 `256` 份 receipt 保留为精确幂等证据；超出部分按完成时间和 plan ID 严格排序，逐份先
  耐久更新 `codex.autocad.agenthost.audit-retention-receipt-checkpoint/1` 累计链，再删除旧 receipt。
- 检查点保存累计数量/删除统计、链哈希、最后 receipt 哈希和严格游标；检查点提交后中断时，
  残留 receipt 必须与游标和哈希一致才可只完成删除，不得重复累计或静默接受变化内容。
- 已有有效 final receipt 的 foreign `.receipt.json.tmp` 会被清除；无 final 的 foreign temp 返回
  `journal_conflict`，要求按原 plan 恢复，不猜测删除。

## 未完成

- 企业默认保留天数、容量和最低保留会话数；当前必须显式提供参数。
- 系统断电、磁盘满、杀毒/EDR 拦截和企业组策略矩阵。
- 真实生产 AgentHost/AutoCAD 异常退出矩阵；当前强杀证据只来自 Bridge Specs 专用工作器。
- 未知、恶意或无法归属的 control artifact 的企业人工复核/归档流程。
- 同用户恶意进程修改 journal/receipt 的 HMAC、DPAPI 或签名强化。
- 审计归档/备份策略；不得把删除等同于归档。
- AutoCAD 2016 实机矩阵和最终候选冻结。

在这些项目完成前，不得关闭 M4.13，也不得启用后台自动清理。

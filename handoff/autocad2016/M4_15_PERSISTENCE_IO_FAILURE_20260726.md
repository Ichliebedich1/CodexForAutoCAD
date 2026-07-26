# M4.15.5b 持久化 I/O 故障安全夹具

最后验证：2026-07-26（北京时间）

## 本轮结论

本轮只完成自动化故障安全准备，不把测试夹具冒充真实磁盘满、卷离线、系统断电或企业策略证据。

- 审计 JSONL 数据写入发生受控 I/O 失败后，`AgentHostAuditLog` 永久 fail-closed；Bridge 会话被
  终止，后续失败记录或 session 终态不会重新触发底层写入。
- 独立链锚点提交发生受控 I/O 失败后，已经写入的记录与最后耐久锚点不一致，可由完整性验证
  明确检测；审计对象同样永久 fail-closed，不会猜测补写第二终态。
- 审计保留执行器增加仅供内部规格使用的持久化阶段故障钩子。生产默认不启用，不改变正常路径。
- journal/receipt/checkpoint 的临时文件已经耐久但尚未原子提交时，故障统一转换为稳定
  `cleanup_failed`；原始 `IOException`、路径和私有标记不进入公共 stderr。
- journal 提交前失败不删除任何审计 artifact；journal 提交后失败保留可识别的
  `recovery_required` 控制状态。
- 同一 plan ID 重试只收敛一次：首次恢复为 `applied` 或 `recovered`，再次执行固定为
  `already_applied`；删除数量和字节数不重复累计，状态不回退。

## 实现边界

- `AgentHostAuditRetentionPersistenceStage` 当前只覆盖：
  `JournalPrepared`、`ReceiptPrepared`、`ReceiptCheckpointPrepared`。
- 故障钩子只存在于 internal 执行路径和规格夹具，不接受 CLI、配置或环境变量输入。
- 故障钩子抛出的文件系统异常或夹具超时统一包装为
  `AgentHostAuditRetentionExecutionException(cleanup_failed)`。
- CLI 将带文件系统 inner exception 的审计/保留异常归类为 `Environment`，但只输出稳定字段和
  数值脱敏标志。
- 真实文件系统仍由原有 `FileStream` WriteThrough、`Flush(true)`、原子 move/replace、journal、
  receipt 和 checkpoint 机制负责；本轮没有引入自动修复或后台清理。

## 自动化证据

- Bridge Specs：`83/83`。
- Host.2016 MVP：`61/61`。
- PowerShell 7 Phase 2：`421/421`。
- Windows PowerShell 5.1 Phase 2：`421/421`。
- AgentLauncher net8/net45：各 `65/65`，包含连续 `500` 次启停。
- Release：`0 warning / 0 error`。
- Host 禁用 API、秘密扫描、AgentHost doctor、Git diff 检查：通过。
- AgentLauncher evidence：
  `artifacts/autocad2016-agent-bootstrap-10953306bc014e74bd2d2d6f5b6de8af/verification.json`。
- AgentHost DLL SHA-256：
  `780D3CD57786CC624D8A033B2069E41095F7119EE4E695110D7E94E8CCB399D2`。
- 用户 PATH 长度仍为 `661`，SHA-256 仍为
  `05df0d2ffc86d41186216560d37cc16fa0159ed5cef9a89f61042964c196be59`。
- 跟踪的 `packages.lock.json` 与 HEAD 差异数：`0`。
- AgentHost、FakeAgentHost、Bridge TestServer 残留进程：`0`。

## 尚未验证

1. 真实磁盘满、NTFS 配额耗尽、卷离线、介质错误和文件系统只读切换。
2. 系统断电、突然复位以及硬件写缓存未落盘时的恢复结果。
3. AppLocker、WDAC、EDR/杀毒或企业备份软件阻止 journal/receipt/checkpoint 写入。
4. 企业默认保留期、容量、人工归档目的地、ACL、审批、审计和归档后校验。
5. 真实 AutoCAD/Codex/AgentHost 进程在各持久化阶段异常退出后的端到端恢复。

本轮没有启动或控制 AutoCAD，没有启用 CAD 写入、保存、命令、LISP、Shell、文件或网络 Agent
工具。M4.15.5、M4.15、M4 和 M4.16 仍未完成，M5 CAD 写入继续硬禁用。

# M4：AgentHost 只读运行审计基线

最后更新：2026-07-23（北京时间）

## 结论

M4 当前新增的是一个真实接入 AgentHost `bootstrap-serve` 的只读运行审计基线，不是完整
M4 沙箱，也不是 CAD 写入审计。`AgentHostBridgeSession` 必须接收具体的
`AgentHostAuditLog`；没有“只创建接口但不接入调用链”的空实现。

审计文件位于：

```text
%LOCALAPPDATA%\OpenAI\CodexForAutoCAD\audit\agenthost\<bootstrap-session-id>.jsonl
```

每个 bootstrap session 使用 `FileMode.CreateNew` 创建独占文件。目录校验拒绝 UNC/设备路径、
非固定盘和路径中的重解析点；审计目录与普通 Codex 工作目录分离。当前实现尚未设置最小 ACL、
保留期限或清理任务，这些仍是 M4 待办。

## 记录契约

schema 固定为 `codex.autocad.agenthost.audit/1`。每条记录是 UTF-8 单行 JSON，包含固定字段：

```text
schema
sequence
timestampUtc
sessionId
eventType
systemConversationId
systemRequestId
bridgeRequestId
providerThreadId
providerTurnId
method
approvalKind
resolution
outcomeCode
errorCode
```

可选字段为空时省略。所有 ID 和 code 只接受受限 ASCII 标识符；事件类型是闭集。默认硬上限为
`10,000` 条记录或 `4 MiB`，达到任一上限即抛出 `AgentHostAuditException`。

## 已接入事件

- `session_started`、`session_stopped`、`session_failed`
- `bridge_connected`、`bridge_disconnected`
- `request_received`、`request_completed`、`request_failed`
- `thread_started`、`turn_started`
- `cancel_requested`、`cancel_dispatched`
- `approval_requested`
- `turn_completed`、`turn_cancelled`、`turn_failed`

回合和取消记录同时保留系统 request ID、Bridge request ID 和 Provider thread/turn ID；Provider
ID 不会取代系统 ID。审批当前只记录“请求已到达”和审批种类。现有 Runtime 没有可供
`AgentHostBridgeSession` 可靠观察的统一审批解决事件，因此没有伪造 `approval_resolved`。

## 内容边界

审计事件模型没有 prompt、CAD canonical JSON、图纸名称/路径、实体内容、命令文本、工作目录、
环境变量、异常正文、API Key/token 或 Provider 原始 payload 字段。失败只使用稳定的
`errorCode`，例如 `invalid_request`、`invalid_state`、`timeout`、`io_failure`。

写入或容量失败会进入 AgentHost fail-closed 路径，取消运行令牌并主动释放认证 Bridge；不会在
审计不可用时继续处理请求。正常结束会写入 `session_stopped`，未处理的异常由外层记录为
`session_failed`。

## 自动化证据

本切片通过：

```text
Bridge/AgentHost Specs: 44/44
完整 Phase 2: 324/324
Release build: 0 warnings / 0 errors
Host 禁用 API 扫描: passed
敏感信息基础扫描: passed
AgentHost doctor 活体握手: passed
```

覆盖的行为包括：JSONL 白名单和单调序号、记录/字节上限、真实两轮 Bridge 会话、系统/Bridge/
Provider ID 关联、失败请求稳定错误码、取消请求/分派/终态、审批命令和路径脱敏，以及审计
容量耗尽时的连接终止。

这些是托管运行时证据，不证明 AutoCAD `NETLOAD`、真实 Codex 配额耗尽、AutoCAD 异常退出、
审计目录 ACL/保留策略、审批解决或 M5 CAD 写入终态。

脱敏证据为 `evidence/m4-agenthost-runtime-audit-20260723.json`；其 SHA-256 为
`08A6BD80FB38F584F1A8C5C6675C40C18977718B24F12FA76EA61DB9242DC4DE`（仓库规范
CRLF 检出字节）。

## 下一步

1. 继续完成每会话 `CODEX_HOME`、环境白名单、独立凭据和目录 ACL/清理策略。
2. 将统一审批解决、CAD proposal/execute/rollback/Undo 终态接入同一固定字段审计。
3. 增加故障注入、AgentHost/Codex 僵尸进程和日志导出脱敏测试。
4. 在 M4 前置条件关闭前，不启用 AutoCAD 2016 CAD 写入。

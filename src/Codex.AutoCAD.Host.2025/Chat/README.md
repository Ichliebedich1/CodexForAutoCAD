# Chat 状态模型规格

本目录是 AutoCAD 2025 面板与 AgentHost 之间的纯 .NET 状态边界，不引用 AutoCAD、WPF、Shell 或文件系统 API，后续可以原样提取到共享库。

## 事件归约约定

- `IAgentBridgeClient` 先把 App Server/IPC 数据规范化为强类型 `AgentEvent`，UI 不解析原始 JSON。
- 单个事件流的 `Sequence` 必须严格递增；重试必须沿用同一个 `EventId`。
- `ChatSessionState.Apply` 在一把锁内完成完整归约。重复事件返回 `Duplicate`，迟到事件返回 `Stale`，不会重复追加文本或重复解析审批。
- 快照及其中的集合均为只读副本；`Changed` 在状态锁释放后触发。WPF 适配层负责把通知切换到 Dispatcher。
- 为兼容仅有 delta 的上游通知，首个 `AgentAssistantMessageDeltaEvent` 可以隐式创建流式助手消息。
- 回合完成、失败或取消时，仍处于活动状态的消息、工具和审批会被收敛到终态，避免 UI 永久显示“正在运行”。

## 审批安全边界

- UI 只能提交 `AgentApprovalDecisionRequest`，不能直接执行命令、写文件或修改 CAD。
- 决定必须存在于 `AgentApprovalRequestedEvent.AllowedDecisions` 中。
- `ApprovalCardKind.Cad` 明确拒绝 `AcceptForSession`，CAD 写操作只能逐次批准。
- 审批卡和工具时间线只保存展示状态；真正的一次性能力令牌与 CAD 事务仍由可信 AgentHost/宿主审批门管理。

## 建议测试矩阵

可执行规格位于 `tests/Codex.AutoCAD.Chat.Specs`，并由阶段验证脚本统一运行。

1. start → 多个 delta → message completed → turn completed，断言文本按顺序拼接且消息终态正确。
2. tool started → approval requested → accept once → tool completed，断言等待状态和恢复运行状态。
3. decline/cancel/expiry/failure，断言审批卡、关联工具和会话状态一致。
4. 重放相同 `EventId`、提交更小 `Sequence`、跨线程 ID 事件，断言分别为 Duplicate、Stale 和拒绝。
5. 多线程并发调用 `GetSnapshot` 与 `Apply`，断言快照版本单调、集合无半更新状态。

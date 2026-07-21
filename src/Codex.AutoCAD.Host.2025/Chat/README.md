# Chat 状态模型规格

本目录是 AutoCAD 2025 面板与 AgentHost 之间的纯 .NET 状态边界，不引用 AutoCAD、WPF、Shell 或文件系统 API，后续可以原样提取到共享库。

## 定位与当前状态（2026-07-21）

本目录属于 Host.2025 的 UI/状态原型，不是当前 AutoCAD 2016 最小 MVP 的产品入口，也没有经过原版 AutoCAD 2016 R20.1 人工 `NETLOAD` 验证。Host.2025 保留为次要目标；其中已有状态模型和 Specs 只能证明原型层行为，不能作为 AutoCAD 2016 运行或发布证据。

当前已实机通过的 AutoCAD 2016 链路是 Agent MVP `0.3.1`（提交 `7f10d60`）：一条 `Line` 经 `CadContextJson v1 -> Palette -> 认证 AgentHost -> 本机 Codex` 返回回答，并在同一 Codex thread 中完成两轮连续对话。该链路不直接依赖本目录的 Host.2025 UI。

当前边界：

- `0.3.2` AgentHost 停止生命周期仍是未提交、未实机验证的候选；失败后的重复 `STOP` 状态存在误报风险。
- `CadContextJson v2` 的 19 种强类型对象、3 种受限占位和底层 Bridge/AgentHost 能力已经形成基础提交（截至 `50f6cf3`），但 Host.2016 Runtime、Palette 和 Agent Client 仍使用 v1。
- CAD 写入和插件发起保存继续禁用；Host.2025 中的写入相关代码只视为原型，不得据此宣称 AutoCAD 2016 写入支持。
- 面向多 Provider 的 `IAgentProvider`、统一 Provider 事件和 `DirectApiAgentProvider` 重构已列入长期待办，当前延后到 AutoCAD 2016 只读 MVP 的 P0/P1 收口之后。现阶段不开发第二套 Agent Loop，也不为展示进度创建脱离真实调用链的空接口。

如后续抽取本状态模型，UI 只能消费 Provider 无关的内部事件；Codex thread/task ID 必须与系统 session/task ID 分离，Codex 专有 JSON 只能停留在适配层。该方向是后续架构约束，不表示 Provider 重构已经完成。

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

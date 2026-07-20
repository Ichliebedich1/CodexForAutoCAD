# AutoCAD 2016 项目长期记忆与待办

最后更新：2026-07-20（北京时间）

本文件是项目长期记忆中的“架构决策 + 最小 MVP 待办”入口。它必须与
`CURRENT_STATE.md`、`README_FIRST.md`、`COMPANY_PC_RUNBOOK.md` 和阶段证据一起阅读。

## 固定目标

- AutoCAD 2016 优先：进程内 `net45/x64` 薄宿主。
- Agent、Bridge、Codex 和 Sandbox 均保持在进程外 `.NET 8`。
- MVP 只读闭环：选择图元 → 读取信息 → CadContextJson → Palette → 本机 Codex → 连续对话。
- CAD 写入继续关闭；未来写入必须保持“预览 → 一次审批 → 锁内重校验 → 单事务 → 不自动保存”。
- HMAC、sequence、nonce、防重放、结果身份绑定和 fail-closed 不得弱化。

## GPT 建议与本机实际的审查结论

### 已确认可以采用

- Codex 使用 `codex app-server --stdio`，通过结构化 JSONL/JSON-RPC 通信。
- Codex 应被封装成可替换 Provider，UI、任务系统和 CAD 工具不得直接依赖 Codex 类型。
- Provider 抽象放在 `.NET 8 AgentHost`，不放进 AutoCAD `net45` Host。
- 系统 session/task/request ID 与 Provider/Codex thread/turn ID 分离。
- Codex 原始事件只在 Codex Adapter 内解析，再转换为统一事件。
- Direct API 只保留配置、能力声明和明确的未启用响应，不实现第二套 Agent Loop。
- CAD 工具保持 Provider 无关；暂不扩展写入工具层。

### 需要按本机实际修正

- 当前不是终端模拟、ANSI 解析或 MCP；已有 App Server 结构化客户端，不需要重写成另一种协议。
- 当前没有产品级任务队列，只有内存 thread/turn 集合、请求表、事件 Channel 和 UI reducer。
- 当前存在 Runtime、Bridge DTO、Host.2025 UI 三套事件模型；不得再增加第四套，需逐步收敛。
- 已冻结的 MVP v1 契约把 `threadId` 定义为真实 Codex thread；不能在 v1 中静默改成系统 session ID，应增加兼容适配和后续 v2。
- 当前 `AgentHostBridgeSession` 仍直接依赖 `CodexAgentRuntime`；Provider 迁移必须先包适配器，再替换组合根。
- 当前 Bridge/Contracts 声明了审批能力，但 AgentHost live session 尚未完整接通审批解析。
- `bootstrap-serve` 的 Codex 路径仍存在硬编码，产品配置和 health/preflight 尚未完成。

## 已有本机实机证据

- AutoCAD 2016 R20.1、x64、CLR 4.0.30319.42000、AcMgd/AcDbMgd 20.1.0.0 已采集。
- 诊断薄宿主已人工 `NETLOAD`，`CODEXCADDOCTOR`/`CODEXCAD` 可运行，`DBMOD` 未变化。
- Palette 100% DPI 下停靠、浮动、隐藏重开、释放重建、中文换行已通过。
- 六类图元 Line、Circle、Polyline、DBText、MText、BlockReference 各一个已在实机捕获。
- `selected=6`、canonical bytes `738`、捕获/清除期间 `DBMOD` 不变已通过。
- 文档切换清除旧上下文已通过。
- 统一只读 Host 候选已生成 CadContextJson v1 并在 Palette 中显示摘要/JSON；该阶段的正式产品入口和 Agent 链路仍需按冻结候选重新验证。

## 最小 MVP 待办

### M0：基线和契约

- [ ] 收口统一 Host.2016 与 Bridge/AgentHost 两个脏 Worktree 的阶段边界。
- [ ] 保持 v1 wire 契约不变；记录 v1 → v2 的迁移策略。
- [ ] 冻结 Provider-neutral 的 session/task/request/event/error 语义。
- [ ] 本阶段不启动 AutoCAD，不修改 CAD，不实现写入。

### M1：统一只读 Host

- [x] 选择六类白名单图元。
- [x] 生成 CadContextJson v1 候选。
- [x] Palette 显示可读摘要和 canonical JSON 候选。
- [ ] 用同一冻结 DLL 完成真实 NETLOAD 身份绑定和逐字段人工核对。
- [ ] 补齐 Palette 高 DPI 和退出生命周期验证。

### M2：最小 Codex Provider 链路

- [ ] 增加最小 Provider 抽象，但必须立即接入真实 Codex 调用，不能只放空接口。
- [ ] 以适配器包装现有 `CodexAgentRuntime`。
- [ ] AgentHost 生成系统 session/task/request ID，并私下映射 Codex thread/turn。
- [ ] 实现一个真实 thread、一个只读 turn、一种文本事件回传。
- [ ] 接通统一 Host.2016 的认证 Bridge Client。
- [ ] 完成断线、取消、超时、ProviderDisconnected 的 fail-closed 行为。

### M3：连续只读对话

- [ ] 同一系统 session 连续完成两轮问题。
- [ ] 每轮发送最新 CadContextJson 和上下文哈希。
- [ ] 文档切换、清除上下文后拒绝旧上下文。
- [ ] Palette 显示流式文本、完成、失败和取消状态。
- [ ] 用户人工 AutoCAD 2016 验证通过后单独提交 Git。

### M4：Direct API 预留

- [ ] `DirectApiConfiguration`、Provider 注册项和能力声明。
- [ ] 未启用时返回结构化 `ProviderNotEnabled`。
- [ ] 不实现 Direct API 请求，不实现第二套 Agent Loop。

## 暂缓事项

- [ ] CAD 预览、一次性审批、DocumentLock、事务写入。
- [ ] SQLite 长期记忆、审计哈希链、R4 恢复。
- [ ] AppContainer、受限令牌、CPU/内存配额等高级沙箱。
- [ ] `.bundle`、签名、升级/回滚和企业发布。
- [ ] Host.2025 写入原型的继续扩展。

## 阶段纪律

每一阶段必须遵守：

1. 先修改最小范围。
2. 真实编译和自动化 Specs 通过。
3. 需要 AutoCAD 时只提供冻结候选和人工命令，不由 Codex 启动或控制 AutoCAD。
4. 用户实机验证通过后，单独提交一次 Git。
5. 没有真实编译和 NETLOAD 证据，不宣称支持 AutoCAD 2016。


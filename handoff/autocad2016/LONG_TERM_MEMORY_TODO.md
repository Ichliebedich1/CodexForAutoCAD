# AutoCAD 2016 项目长期记忆与待办

最后更新：2026-07-21（北京时间）

本文件是项目长期“架构决策 + 已验证基线 + 未完成队列”入口。它必须与
`CURRENT_STATE.md`、`README_FIRST.md`、`COMPANY_PC_RUNBOOK.md`、阶段测试说明和脱敏
evidence 一起阅读。

## 1. 固定目标和证据口径

- AutoCAD 2016 优先：进程内 `net45/x64` 薄宿主。
- Agent、Bridge、Codex 和 Sandbox 保持进程外 `.NET 8`。
- 只读 MVP：选择图元 -> 结构化 CadContextJson -> Palette -> 本机 Codex -> 连续对话。
- 当前 CAD 写入和插件保存保持关闭。
- 插件不得自动保存 DWG，也不得修改或关闭用户的 AutoCAD 自动保存设置。
- 未来写入固定为“计划/预览 -> 一次审批 -> 锁内重校验 -> 单事务 -> 不自动保存”。
- HMAC、严格递增 sequence、nonce、防重放、结果身份绑定和 fail-closed 不得弱化。
- 没有目标机原版 R20.1 编译和用户人工 `NETLOAD`，不得宣称相应能力支持 AutoCAD 2016。
- 每个阶段验证通过后单独提交 Git。

进度只按真实运行证据判断，不按文件数量、代码数量或接口数量判断。旧审计中的 `25%`
属于 Agent 链尚未实机接通时的历史快照，现已失效。

## 2. 当前最小 MVP 判断

### 已实机成立的 happy path

- [x] 在 AutoCAD 2016 中选择一个或多个受支持图元。
- [x] Host 以只读方式读取真实图元信息。
- [x] 生成 CadContextJson v1。
- [x] 在统一 Palette 显示摘要和 canonical JSON。
- [x] 通过认证 AgentHost 调用本机 `codex app-server --stdio`。
- [x] Codex 使用当前 CAD 上下文回答，并在同一 thread 完成第二轮连续对话。

精确边界：`0.3.1.0` 的完整 AI 实机样本是一个真实 Line；早期统一只读 Host 另对 v1
六类对象完成过捕获和 JSON 展示。不得据此声称任意 AutoCAD 对象均受支持。

### 尚未达到的产品级收口

- [ ] AgentHost 停止后可靠无残留。
- [ ] v2 对象覆盖进入真实产品调用链。
- [ ] 离线、断线、超时、取消、文档切换和 AutoCAD 退出生命周期全部实机通过。
- [ ] 高 DPI 和稳定候选包/人工测试手册完成。

因此“六项最小功能链已在受支持 Line 上实机成立”与“只读 MVP 已达到发布级稳定”是两个
不同结论；后者仍未完成。

## 3. 已验证并应长期保留的基线

### AutoCAD 2016 与 Palette

- [x] 目标机原版 R20.1/x64/CLR/AcMgd/AcDbMgd 环境采集。
- [x] 诊断薄宿主人工 `NETLOAD`，`CODEXCADDOCTOR`/`CODEXCAD` 可运行。
- [x] Palette 100% DPI 打开、停靠、浮动、隐藏重开、释放重建、中文输入和换行。
- [x] 只读选择捕获、清除和文档激活清除旧缓存；相关样本 `DBMOD` 不变。
- [x] v1 六类：Line、Circle、Polyline、DBText、MText、BlockReference。

### 认证和进程边界

- [x] net45/net8 HMAC、sequence、nonce、防重放和固定向量兼容。
- [x] 受限继承句柄、启动身份/哈希校验、启动截止、取消和有界清理的非 CAD bootstrap
  检查点。
- [x] 具体认证 Bridge Client 的能力协商、thread/turn、文本事件、interrupt 和一次审批
  响应协议检查点。

### `0.3.1.0` 只读 Agent MVP

- [x] 已验证阶段提交 `7f10d60`。
- [x] 用户人工 `NETLOAD` 精确候选并确认 Palette 模块版本 `0.3.1.0`。
- [x] 真实 Line -> CadContextJson v1 -> AgentHost -> 本机 Codex -> 第一轮回答。
- [x] 同一 thread 第二轮问题复用前文；用户另确认多次连续对话正常。
- [x] CAD 写入和插件保存保持禁用。
- [ ] AgentHost 停止后无残留；现有证据明确为 `false`。

## 4. 当前 P0：AgentHost 停止生命周期

工作位置：`C:\tmp\CodexForAutoCAD-bridge-client2016`，基线提交 `7f10d60`。

- [x] 已形成 `0.3.2.0` 原版 R20.1 构建候选。
- [x] 已有聚焦停止 Specs `4/4` 的阶段性结果。
- [ ] 修复停止失败后重复 `STOP` 的状态转移风险，确保未清理成功时不能误报“已停止”。
- [ ] 增加失败后再次停止/重试清理的回归测试。
- [ ] 修正 build evidence 早于实际产物的时间顺序问题。
- [ ] 删除/收窄 evidence 对 Palette wiring 的过度声明。
- [ ] 重新运行完整门禁、原版 R20.1 编译并冻结新候选；旧 hash 不作为最终测试身份。
- [ ] 用户在干净 AutoCAD 2016 中人工 `NETLOAD`。
- [ ] 连续两轮 `AGENTSTART -> AGENTSTOP`，Palette 都到达真实终态。
- [ ] 首尾 `DBMOD` 相同；只读进程检查确认 AgentHost 数量为 `0`。
- [ ] 更新运行 evidence 和状态文档后单独提交。

在上述修复和重新冻结前，不请求用户加载现有 `0.3.2.0` 旧候选。

## 5. 当前 P1：CadContextJson v2

工作位置：`C:\tmp\CodexForAutoCAD-context-v2`；分支 `codex/cad-context-v2`；当前已知
HEAD `50f6cf3`。

### 已提交基础

- [x] 冻结独立 v2 契约，保持 v1 wire 和固定向量不变。
- [x] 19 类强类型对象：v1 六类，加 Arc、Ellipse、Spline、Point、Ray、Xline、
  Polyline2d、Polyline3d、Dimension、Hatch、Leader、MLeader、Table。
- [x] 未知类型、读取失败、数据超限使用三类受限占位；合法对象继续发布。
- [x] `entityCount`、`parsedEntityCount`、`unsupportedEntityCount`、`complete` 一致性和
  大小/数量/文字限额。
- [x] Contracts net45/net8 `71/71`；完整 Phase 2 `231/231`；Release 0 warning/error。
- [x] 原版 R20.1 API probe 在 PowerShell 7/5.1 均为 `19 present / 8 absent`。
- [x] Host v2 捕获、选择哈希、Mapper 和 Codec 候选通过原版 R20.1 构建。
- [x] Bridge Client `StartTurnV2Async` 和 AgentHost v1/v2 capability/handler 已存在。

### 必须进入真实调用链的工作

- [ ] `UnifiedReadOnlyContextRuntime` 改用 v2 捕获、选择哈希、Mapper 和 Codec。
- [ ] `UnifiedContextState` 保存 schema/version、四个选择完整性字段和 v2 JSON。
- [ ] Palette 显示 v2 schema、已解析数、占位数和 `complete`。
- [ ] Doctor、命令提示和 BuildInfo 从“v1/六类”更新为准确 v2 文案。
- [ ] `MvpAgentClient` 显式协商 `codex.autocad.cad-context/2` 与
  `agent.turn.start.v2`，随后调用 `StartTurnV2Async`。
- [ ] 缺少 v2 能力时 fail-closed，不回退到未认证通道或伪装成 v1。
- [ ] 修复显式空 `supportedCadContextSchemas` 被 DTO 默认值恢复为 v1 的 fail-open，并
  增加 net45/net8 回归测试。
- [ ] 增加 Host -> Bridge -> AgentHost 的真实 v2 调用链测试，而不是只测试独立类型。
- [ ] 完成最终集成 Runtime 的原版 R20.1 Release 编译和冻结候选。
- [ ] 用户人工 `NETLOAD`，验证多个支持对象 + 一个未知对象仍发布、
  `unsupportedEntityCount=1`、`complete=false`、`DBMOD` 不变。
- [ ] 证明 Agent 使用 v2 上下文回答；更新 evidence 后单独提交。

### 仍需确认的 v2 语义风险

- [ ] MLeader 文本 getter 的 R20.1 语义与脱敏边界。
- [ ] Table 单元格只公开显示文本，不泄露字段表达式、公式内部结构或数据链接。
- [ ] `entity-read-failed` 与 `entity-data-limit` 的分类在所有异常/限额路径保持稳定。

## 6. 只读 MVP 稳定化队列

- [ ] AgentHost 启动失败、异常退出、断线和请求超时均有明确 fail-closed UI 状态。
- [ ] 取消进行中的回合，重复取消幂等，终态后不再接受迟到事件。
- [ ] 文档切换、上下文清除和重新捕获后拒绝旧 context/thread 绑定。
- [ ] AutoCAD 正常退出后线程、管道和 AgentHost 无残留。
- [ ] Palette 125% 和 150% DPI。
- [ ] 流式完成、失败、取消、断线状态的 AutoCAD 2016 实机验证。
- [ ] 冻结可重复加载的只读 MVP 包，整理统一命令清单和脱敏反馈模板。
- [ ] 每个独立阶段验证后单独提交，不将 P0、P1、DPI 或退出生命周期混为一项。

## 7. GPT Provider 建议与本机实际结论

### 可采用，但当前延后

- Codex 应封装成可替换 Provider，UI、任务系统和 CAD 工具不得依赖 Codex 专有类型。
- Provider 抽象位于 `.NET 8 AgentHost`，不进入 AutoCAD `net45` Host。
- 系统 session/task/request ID 与 Codex thread/turn ID 分离。
- Codex 原始事件只在 Codex Adapter 中转换为统一事件。
- Direct API 当前只预留配置、能力和明确的未启用响应，不实现第二套 Agent Loop。
- CAD 工具保持 Provider 无关。

### 必须以现有实现为迁移起点

- 当前已使用 `codex app-server --stdio` 和结构化 JSONL/JSON-RPC，不是终端模拟、ANSI
  解析或 MCP，无需更换协议再开始工作。
- 当前没有产品级通用任务队列；主要是 Runtime 内存 thread/turn、请求表、事件 Channel
  和 UI reducer。
- `AgentHostBridgeSession` 目前直接依赖 `CodexAgentRuntime`；未来先增加适配器，再替换
  组合根，不进行大规模重写。
- v1 的 `threadId` 已冻结为真实 Codex thread，不能在 v1 中静默改成系统 session ID；
  只能通过兼容映射和后续协议版本迁移。
- Runtime、Bridge DTO 和历史 Host.2025 UI 存在事件模型重复，未来要收敛，不能增加
  第四套平行状态系统。
- Bridge/Contracts 有审批语义，但 live AutoCAD 产品链尚未完成审批与写入；代码存在不能
  视为运行通过。
- Codex 路径发现、配置和 health/preflight 仍需从现有 bootstrap 硬编码迁出。

### Provider 抽象的启动条件

只有 P0、P1 和只读稳定化主要门槛通过后，才开始：

- [ ] `IAgentProvider`、统一请求/事件/错误/健康模型。
- [ ] 系统 session/task/request ID 与 Provider ID 映射。
- [ ] Codex Adapter 接入现有真实调用链。
- [ ] 任务状态幂等、取消和断线处理收敛为唯一系统。
- [ ] `DirectApiAgentProvider` 配置与 `ProviderNotEnabled`，不实现 Agent Loop。

## 8. CAD 写入安全闭环（只读 MVP 后）

- [ ] 计划和可检查预览。
- [ ] 零修改结果拒绝。
- [ ] 只有“拒绝”与“一次允许”；token 使用后不可重放。
- [ ] CAD Broker 正确调度 AutoCAD 主线程。
- [ ] `DocumentLock` 内重新核对图纸、revision、选择、图层和空间。
- [ ] 单事务执行，异常完全回滚，并提供正确 Undo 边界。
- [ ] 上下文变化时拒绝执行，不自动重规划/重试写入。
- [ ] Agent 中断、超时、连接不确定时禁止自动重试 CAD 写入。
- [ ] 写入成功后插件不调用保存，关闭图纸仍由 AutoCAD 正常提示用户。
- [ ] 以上项目逐项在 AutoCAD 2016 实机验证后才可声明支持写入。

## 9. 沙箱、记忆和发布（后续）

- [ ] 受限令牌/AppContainer、Job Object、CPU/内存配额和进程树强制清理。
- [ ] 每会话独立 `CODEX_HOME`，空 MCP/插件/凭据隔离和最小可写目录。
- [ ] SQLite 图纸级长期记忆、脱敏日志、审计哈希链和保留/导出/清除策略。
- [ ] R4 恢复点、断电/崩溃恢复和结果不确定处理。
- [ ] 环境采集器 `Location` 分支的已验证提交独立集成，不重复实现或混入 P0/P1。
- [ ] `.bundle`、签名/时间戳、AppLocker/WDAC/EDR 策略。
- [ ] 安装、升级、修复、卸载、回滚、普通用户和干净机验收。
- [ ] 扩展更多 CAD 读取/写入类型；每类都有明确白名单、限额和实机证据。

## 10. Worktree 和所有权纪律

- P0 与 P1 是两个独立阶段，必须分别验证、分别提交，再按受控方式集成。
- 主工作树可能含用户所有的未提交 Host.2025 UI、选择和写入原型及未跟踪文件；不得
  清理、覆盖或混入 AutoCAD 2016 阶段提交。
- MiMo 交付只能作为候选；必须由本项目真实复查 Git、源码、门禁和证据，不按 MCP
  Review Package 的摘要直接合并。
- 不把真实图纸路径、图名、选择哈希、用户名、受信路径、网络路径、许可证、API Key、
  token 或完整环境变量写入 Git 文档或日志。

## 11. 阶段纪律

每个阶段必须按以下顺序完成：

1. 修改最小范围，保持安全不变量和兼容边界。
2. 运行对应 Release 构建、Specs、禁止 API、秘密扫描和证据检查。
3. 需要 AutoCAD 时只提供精确冻结候选和人工命令，不由 Codex 操作 AutoCAD。
4. 用户实机验证通过后更新脱敏 evidence 与状态文档。
5. 单独提交一次 Git；未通过项目继续明确保留为待办。

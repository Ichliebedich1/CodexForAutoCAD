# AutoCAD 2016 当前状态索引

最后更新：2026-07-21（北京时间）

本文件是项目长期“当前状态索引”。它不替代 `README_FIRST.md`、
`COMPANY_PC_RUNBOOK.md`、阶段测试说明、脱敏 evidence JSON 或 Git 历史；只集中记录
目前仍成立的结论、证据边界、活动 Worktree 和后续验证顺序。

长期架构决策和完整待办见 `LONG_TERM_MEMORY_TODO.md`。

## 1. 当前结论

- AutoCAD 2016 优先架构保持不变：进程内 `net45/x64` 薄宿主，进程外 `.NET 8`
  AgentHost/Sandbox。
- `0.3.1.0` 已在用户的原版 AutoCAD 2016 R20.1 中人工 `NETLOAD`，完成了受支持对象的
  只读上下文、Palette、本机 Codex 和同一 thread 两轮连续对话 happy path。
- 上述实机样本为真实 Line；早期统一只读 Host 还分别验证过六类 v1 对象。因此可以确认
  “受支持对象 -> v1 JSON -> Palette -> 本机 Codex -> 连续对话”的最小功能链已成立，
  但不能扩大为任意 DWG 对象、完整生命周期或发布级支持。
- `0.3.1.0` 停止后曾只读发现一个由 AutoCAD 创建的 `AgentHost bootstrap-serve` 残留
  进程。因此问答 happy path 通过不等于 AgentHost 停止生命周期通过。
- P0 `0.3.2.0` 停止修复已有目标机原版 R20.1 构建候选，但尚未人工 `NETLOAD`、尚有
  已知状态/证据问题、尚未形成阶段提交，不得请求用户加载当前旧候选。
- P1 CadContextJson v2 的契约、19 类强类型对象、三类受限占位、Host 捕获候选、Bridge
  v2 路径和 R20.1 API 探针已经提交并自动验证；统一 Runtime、Palette 和真实 Agent
  回合仍使用 v1，故 v2 尚未成为产品能力。
- 旧审计中按当时六项 MVP 计算的 `25%` 已失效，不得继续作为当前进度依据。当前应区分
  “最小 happy path 已实机成立”与“生命周期、对象覆盖和发布稳定化尚未收口”。
- CAD 写入和插件发起的保存继续禁用。

## 2. 证据使用顺序

1. 用户在真实 AutoCAD 2016 中执行的命令记录及其精确冻结候选身份。
2. 原版 R20.1 托管程序集的真实 Release 编译、验证脚本和脱敏 evidence JSON。
3. 验证后单独产生的 Git 提交。
4. 本索引和其他交接文档中的摘要。

若摘要与原始证据冲突，以更具体、更新且可复现的原始证据为准。Specs、静态检查、
固定向量和 DLL 哈希不能替代 AutoCAD 内的人工 `NETLOAD`；没有对应实机证据的能力必须
保持“未验证”。

## 3. 当前架构与调用链

```text
AutoCAD 2016 R20.1
  -> Codex.AutoCAD.Host.2016 (net45/x64，Palette + 只读选择)
  -> 认证 Bridge Client (HMAC + sequence + nonce + 防重放)
  -> AgentHost (.NET 8，独立进程)
  -> codex app-server --stdio (结构化 JSONL/JSON-RPC)
  -> assistant 事件经认证 Bridge 返回 Palette
```

- AutoCAD UI 不直接控制 Codex 子进程；Host 通过 Agent 启动/Bridge 边界调用进程外服务。
- 选择上下文只读打开实体；当前产品链发布 CadContextJson v1。
- Agent/Codex 与 AutoCAD 主线程隔离；CAD 写入、写入工具和插件保存保持关闭。
- 当前还没有完成 Provider-neutral 的正式抽象；该工作明确延后到只读 MVP 稳定化以后。

## 4. 已验证检查点

### 4.1 诊断薄宿主

- 提交：`2d2ad3738095794c8374e916559c0c5d13702ba1`。
- 使用目标机原版 Autodesk `20.1.0.0` 托管程序集编译，用户人工 `NETLOAD`。
- 真实读到 x64、CLR `4.0.30319.42000`、AcMgd/AcDbMgd `20.1.0.0` 和 R20.1。
- `CODEXCADDOCTOR`、`CODEXCAD` 可执行；首次记录 `DBMOD 21 -> 21`。
- 该历史记录没有加载现场 DLL 哈希绑定，不得以后续构建哈希回填。

### 4.2 Palette 运行时

- 提交：`56115e4`。
- 用户已验证 100% DPI 下打开、停靠、浮动、隐藏后重开、释放重建、中文输入和换行。
- 干净样本中 Palette 操作与文档切换前后 `DBMOD` 不变。
- 125%/150% DPI 和 AutoCAD 正常退出生命周期仍未验证。

### 4.3 只读选择与 CadContextJson v1

- 用户曾预选 Line、Circle、Polyline、DBText、MText、BlockReference 各一个；读取成功，
  `selected=6`、binary canonical bytes `738`、`DBMOD 4 -> 4`。
- 统一只读 Host 已真实生成 CadContextJson v1，并在同一 Palette 显示可读摘要和完整
  canonical JSON；Palette 重建保留上下文，`CODEX16CTXCLEAR` 清除内存上下文。
- 文档激活会清除旧缓存；文档名称和路径不进入上下文。
- v1 仍只支持上述六类。选区含其他实体时可能整体
  `validation-unsupported-entity-kind`；这是引入 v2 的直接原因。

### 4.4 认证、Bootstrap 与 Bridge 原语

- net45/net8 的 HMAC、严格递增 sequence、nonce、防重放、一次领取、错误帧拒绝和
  fail-closed 原语已通过跨运行时固定向量与 Specs。
- AgentHost bootstrap 已验证受限继承句柄、启动身份/哈希检查、启动截止、取消和有界
  清理等非 CAD 路径。
- 这些原语证据不等同于 AutoCAD 内停止生命周期或 CAD 写入证据。

### 4.5 `0.3.1.0` 只读 AI happy path

- 分支/Worktree：`codex/bridge-client-net45` / `C:\tmp\CodexForAutoCAD-bridge-client2016`。
- 已验证阶段提交：`7f10d60`。
- 精确 Host SHA-256：
  `A7BFF46F1BA4970818ACB03F51C09EEBF1DDB8A7093D0C4C615E2D877D9236D1`。
- AgentHost SHA-256：
  `8C74B95ECD6680F9A35824DB1C2C543D42B52AB1E4D3565F5B7EE8DBB1DC900E`。
- 用户在真实 AutoCAD 2016 中人工 `NETLOAD`；Palette 显示版本 `0.3.1.0`。
- 用户选择真实 Line，Host 发布 CadContextJson v1，AgentHost 完成认证启动，本机 Codex
  返回第一轮回答；第二轮正确复用同一 thread 的前文标记。用户另确认多次连续对话正常。
- CAD 写入和插件保存保持禁用。
- 证据边界：停止后曾发现一个相关 AgentHost 进程残留，故
  `agentHostNoResidualProcessVerified=false`。

## 5. P0：AgentHost 停止生命周期

状态：**构建候选存在，但未完成、未实机、未提交。**

- 当前隔离 Worktree 仍为 `C:\tmp\CodexForAutoCAD-bridge-client2016`；P0 修改建立在
  `7f10d60` 之后，尚未形成独立提交。
- 已有 `0.3.2.0` 原版 R20.1 构建候选，旧候选 Host SHA-256 为
  `884413F0E7ACD64974F5F42B0251F8BEFCA361FA5C59057C5136C79E9AD33928`；聚焦 Specs
  曾为 `4/4`。
- 当前旧候选不是可请求实机验证的最终候选，原因如下：
  - 停止失败后的重复 `CODEX16AGENTSTOP` 状态转移仍有风险，可能把未完成清理表述为
    “已停止”；必须修复并增加回归测试。
  - 构建 evidence 的时间顺序需与最终产物重新绑定。
  - evidence 对 Palette wiring 的自动验证范围存在过度声明，必须改为真实证据边界。
- 修复后必须重新完成原版 R20.1 Release 编译、冻结 SHA-256 和完整门禁，再由用户在
  干净 AutoCAD 2016 进程中人工 `NETLOAD`。
- 实机验收为连续两轮 `AGENTSTART -> 在线 -> AGENTSTOP -> 已停止`，AutoCAD 保持可用，
  首尾 `DBMOD` 相同，随后只读确认相关 AgentHost 数量为 `0`。
- 运行证据、`CURRENT_STATE.md` 和阶段测试说明更新后，必须单独提交本阶段。

## 6. P1：CadContextJson v2

### 6.1 已提交并自动验证的基础

- 分支/Worktree：`codex/cad-context-v2` / `C:\tmp\CodexForAutoCAD-context-v2`。
- 当前已知 HEAD：`50f6cf3`（`test(host2016): harden r201 api probe evidence`）。
- v1 固定向量保持 `2225` 字节，SHA-256
  `c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423`。
- v2 固定向量为 `6678` 字节，SHA-256
  `21cc9378a618022c5bc21cb35c58db7818272c33d0adc5b5bd8618b4a638c3b4`。
- v2 限额向量为 `17721` 字节，SHA-256
  `fb532a9c3932f400d6fa093cab4d5b2f9abef3a65bb0b2eb890fbe2d1bbf629e`。
- v2 覆盖 19 类强类型对象：Line、Circle、Polyline、DBText、MText、BlockReference、
  Arc、Ellipse、Spline、Point、Ray、Xline、Polyline2d、Polyline3d、Dimension、Hatch、
  Leader、MLeader、Table。
- 未知类型、读取失败和实体数据超限分别降级为只含限界白名单字段的受限占位；不会因
  单个未知对象让整组选区失败，也不会静默丢弃。
- Contracts net45/net8 均为 `71/71` 且输出一致；当前完整 Phase 2 为 `231/231`，Release
  `0` warning / `0` error。
- R20.1 API Surface Probe 在 PowerShell 7 和 5.1 均为 `19 present / 8 absent`；探针 DLL
  SHA-256 为 `A732BB2D49729FA95F52E15A03A3A9C6DC32D514B012A3152EAB67A7F48F41DF`，
  Autodesk DLL copy count 为 `0`。
- Host v2 捕获、选择哈希、Mapper 和 Codec 已能用目标机原版 R20.1 程序集构建。
- Bridge Client 已有 `StartTurnV2Async`，AgentHost 已声明/处理 v1 与 v2 能力。

### 6.2 尚未完成的产品链

- `UnifiedReadOnlyContextRuntime` 仍调用 v1 捕获和 `CadContextJsonV1Codec`。
- `UnifiedContextState` 尚未保存明确 schema/version、`entityCount`、
  `parsedEntityCount`、`unsupportedEntityCount` 和 `complete`。
- Palette、Doctor 和命令文案仍固定显示 v1/六类对象。
- `MvpAgentClient` 仍调用 v1 `StartTurnAsync`；尚未在协商后要求
  `codex.autocad.cad-context/2` 与 `agent.turn.start.v2`。
- 能力响应显式返回空 `supportedCadContextSchemas` 时，反序列化默认值可能重新带入 v1，
  形成 fail-open；必须先修复并加测试。
- 尚无统一 Host -> 认证 Bridge -> AgentHost -> Codex 的真实 v2 端到端证据。
- 尚未对最终集成 Runtime 做原版 R20.1 冻结构建、人工 `NETLOAD` 或独立阶段提交。

### 6.3 v2 最终实机验收

最终候选应让用户选择多个已支持对象并加入一个仍未知对象，验证：

- `published=true`。
- `unsupportedEntityCount=1`。
- `complete=false`，其余支持对象仍完整发布。
- Palette 显示 v2 schema、解析数、占位数和完整性。
- `DBMOD` 前后相同，插件不写入、不保存。
- Agent 通过已认证 `agent.turn.start.v2` 使用该上下文并返回回答。

通过后更新脱敏 evidence 和文档，并单独提交。

## 7. 只读 MVP 稳定化待办

P0/P1 后仍需逐项验证：

1. AgentHost 异常退出、断线、启动失败和请求超时均 fail-closed。
2. 取消进行中的 AI 请求及重复取消幂等性。
3. 文档切换、上下文清除或重新捕获后拒绝旧上下文。
4. AutoCAD 正常退出后线程、管道和 AgentHost 全部清理。
5. Palette 125%/150% DPI。
6. Palette 中完成、失败、取消和断线状态的真实表现。
7. 形成身份明确、可重复加载的只读 MVP 候选包和统一人工测试手册。

## 8. 明确延后的工作

以下项目不阻塞当前只读 MVP，不得插入 P0/P1 之前：

- CAD 计划、预览、拒绝、一次允许、写入 Broker、锁内重校验、单事务、Undo/回滚。
- AppContainer/受限令牌、Job Object、CPU/内存限制、完整进程树清理和每会话独立
  `CODEX_HOME`。
- SQLite 图纸级长期记忆、审计哈希链、保留/导出/清除策略和 R4 恢复。
- Provider-neutral `IAgentProvider`、统一事件/任务状态、系统 ID 映射和 Direct API
  Provider 预留。
- `.bundle`、签名/时间戳、AppLocker/WDAC/EDR、安装升级修复卸载和干净机验收。
- Host.2025 写入原型的继续扩展；主工作树中的用户修改必须保留，不得清理或混入
  AutoCAD 2016 阶段提交。

## 9. 不可弱化的产品约束

- AutoCAD 2016 优先：进程内 `net45/x64` 薄宿主，Agent/Sandbox 保持进程外 `.NET 8`。
- CAD 写入固定为“计划 -> 预览 -> 一次审批 -> `DocumentLock` 内重校验 -> 单事务”。
- 审批只有“拒绝”和“一次允许”，不得增加会话级永久允许。
- HMAC、严格递增 sequence、nonce、防重放、结果身份绑定和 fail-closed 不得降级。
- 图纸、revision、选择、图层和空间必须在事务锁内重新校验。
- Agent 中断、超时或结果不确定时不得自动重试 CAD 写入。
- 插件不得自动保存 DWG，也不得修改或关闭用户的 AutoCAD 自动保存设置。
- Codex 不启动、唤醒、关闭、重启或直接操作用户的 AutoCAD；实机测试只提供精确冻结
  候选和人工步骤。
- 每个阶段必须先完成对应验证，再单独提交 Git。

## 10. 更新纪律

- “已验证”必须写明候选身份、验证方式、范围和仍未证明的边界。
- 构建、Specs、静态扫描和哈希不能替代 AutoCAD 内 `NETLOAD` 或端到端证据。
- 未验证、失败或跳过的项目必须保留为明确待办，不得由代码存在推断完成。
- Git evidence 不记录真实图纸路径、图名、选择哈希、用户名、受信路径、网络路径、许可
  信息、凭据或未脱敏环境变量。

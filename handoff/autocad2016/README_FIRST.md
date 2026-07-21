# Codex for AutoCAD 2016 交接说明

最后更新：2026-07-21（北京时间）

本项目优先适配原版 AutoCAD 2016 R20.1 x64，固定采用进程内 `.NET Framework 4.5/x64`
薄宿主和进程外 `.NET 8 AgentHost/Sandbox`。新任务先读 `CURRENT_STATE.md`，再按需查本
文件、`COMPANY_PC_RUNBOOK.md`、阶段测试说明、evidence JSON 和 Git 历史。摘要冲突时，
以身份绑定更明确、时间更新且可复现的运行证据为准。

## 当前结论

### 已在真实 AutoCAD 2016 中运行通过

- 目标机原版 R20.1、CLR 4、`AcMgd/AcDbMgd 20.1.0.0` 已采集；Host 按
  `net45/x64` 使用原版 Autodesk 程序集真实 Release 编译，Autodesk DLL 不复制到输出。
- 用户已人工 `NETLOAD` 诊断宿主并运行 `CODEXCADDOCTOR/CODEXCAD`；诊断样本
  `DBMOD 21 -> 21`。
- Palette 在 100% DPI 下已实测打开、停靠、浮动、隐藏重开、释放重建和中文换行。
- 统一只读 Host 已把六类真实对象转换成 CadContextJson v1，并在 Palette 显示摘要和
  canonical JSON；捕获、清除、Palette 重建保留和文档切换清除已有运行检查点。
- 精确 `0.3.1.0` 候选已人工 `NETLOAD`。真实 Line 已完成：

  ```text
  Line -> CadContextJson v1 -> Palette -> 认证 AgentHost
       -> codex app-server -> Codex 回答 -> 同一 thread 第二轮回答
  ```

- 用户另确认多次连续上下文对话可用。核心只读 Agent MVP 已在验证后单独提交：
  `7f10d60`（`feat(host2016): connect verified readonly Agent MVP`）。
- 该证据只证明当前受支持对象的只读 happy path；CAD 写入和插件保存始终禁用。

### P0：AgentHost 停止生命周期尚未通过

`0.3.1.0` 问答通过后，独立只读进程检查仍发现一个由 AutoCAD 创建的
`AgentHost bootstrap-serve` 残留进程，不能写成停止成功且无残留。

```text
Worktree: C:\tmp\CodexForAutoCAD-bridge-client2016
Branch: codex/bridge-client-net45
```

`0.3.2.0` 曾形成自动化候选，但复核又发现停止失败后的状态和重试清理语义仍需修正；
旧冻结副本不是最终待测候选。P0 完成条件：

1. 修复失败状态与可重试清理。
2. 重跑聚焦 Specs、Phase 2、原版 R20.1 编译和证据门禁。
3. 重新冻结 Host/AgentHost 身份和 SHA-256。
4. 用户在干净 AutoCAD 2016 会话人工 `NETLOAD`。
5. 连续两轮 `CODEX16AGENTSTART -> CODEX16AGENTSTOP`。
6. Palette 显示真实终态，各次 `DBMOD` 保持不变。
7. CAD 保持打开时，由 Codex 只读确认该候选 AgentHost 数量为 `0`。
8. 补脱敏运行证据并单独提交。

新候选冻结前，不应要求用户重复测试旧 `0.3.2.0`。

### P1：CadContextJson v2 基础完成，产品链未接入

```text
Worktree: C:\tmp\CodexForAutoCAD-context-v2
Branch: codex/cad-context-v2
Latest checkpoint: 50f6cf3
```

已完成并提交的 v2 基础：

- v1 字节和 SHA-256 保持冻结。
- v2 定义 19 类强类型对象：原六类加 Arc、Ellipse、Spline、DBPoint、Ray、Xline、
  Polyline2d、Polyline3d、Dimension、Hatch、Leader、MLeader、Table。
- 未知类型、读取失败、单实体超限使用三类限界脱敏占位；混合选区以
  `complete=false` 和明确计数发布，不再因一个未知对象整体失败。
- net45/net8 Contracts 均为 `71/71`；完整 Phase 2 为 `231/231`，Release
  `0` warning / `0` error。
- 原版 R20.1 API probe 在 PowerShell 7/5.1 下均为 `19 present / 8 absent`；Host v2
  捕获基础已使用原版 R20.1 程序集完成构建检查。
- Bridge Client 已有 `StartTurnV2Async`，AgentHost 已有 v1/v2 方法和能力声明。

这些结果不代表产品已使用 v2。统一 Host 运行时、Palette 和 AI 请求当前仍为 v1。
P1 仍需：

1. Runtime 接入 v2 捕获、映射、Codec 和选择哈希。
2. 状态与 Palette 显示 schema/version、实体总数、解析数、占位数和 `complete`。
3. Doctor、命令文案、BuildInfo 更新到准确的 v2 表述。
4. `MvpAgentClient` 显式协商 `codex.autocad.cad-context/2` 与
   `agent.turn.start.v2` 后调用 `StartTurnV2Async`。
5. 缺少 v2 能力必须 fail-closed，不得回退未认证通道或猜测版本。
6. 修复能力响应明确为空 schema 数组时，被 DTO 默认值恢复为 v1 的 fail-open。
7. 完成真实认证管道 v2 端到端测试。
8. 用原版 R20.1 重建最终集成 Host，冻结可运行 DLL。
9. 用户人工 `NETLOAD` 并实测多个支持对象加一个未知对象：`published=true`、
   `unsupportedEntityCount=1`、`complete=false`、`DBMOD` 不变，Codex 使用 v2 回答。
10. 补 evidence，并将集成阶段单独提交。

未经第 8、9 项，不得宣称 v2 已获 AutoCAD 2016 运行支持。

## 证据状态表

| 能力 | 当前状态 | 证据边界 |
| --- | --- | --- |
| 原版 R20.1 环境和 net45/x64 编译 | 已验证 | 目标机 Autodesk `20.1.0.0` 原版程序集 |
| 诊断 Host NETLOAD | 已实机通过 | Doctor/命令可用；历史现场未绑定 DLL 哈希 |
| Palette 100% DPI | 已实机通过 | 打开、停靠、浮动、隐藏重开、重建、中文换行 |
| 六类对象到 v1 JSON/Palette | 已实机通过 | 六类真实对象、清除和 `DBMOD` 不变 |
| 认证 Agent/Codex happy path | 已实机通过 | `0.3.1.0`、真实 Line、同一 thread 两轮、`7f10d60` |
| AgentHost 停止且无残留 | 未通过 | P0 新候选待修、重冻、NETLOAD 和两轮停止 |
| CadContextJson v2 基础 | 构建/Specs 通过 | `50f6cf3`；19 类加三类占位，不是 Runtime 证据 |
| v2 产品链和混合选区 | 未完成 | Runtime、协商、最终 R20.1 编译和 NETLOAD 均待完成 |
| 离线/断线/超时/取消 | 部分自动化 | AutoCAD 内完整 fail-closed 矩阵未关闭 |
| 125%/150% DPI 和退出 | 未验证 | 不从 100% DPI 外推 |
| CAD 预览、审批、写入 | 未验证 | AutoCAD 2016 产品路径继续禁用 |
| 写入后不自动保存 | 未验证 | 目前只证明只读路径无保存调用 |
| 沙箱、长期记忆、安装发布 | 未完成 | 不属于当前只读 happy path |

## 当前已验证调用链

```text
AutoCAD 2016 / Host.2016 net45
  -> 只读预选集 / CadContextJson v1
  -> Palette / net45 IAgentBridgeClient
  -> HMAC + sequence + nonce 认证命名管道
  -> .NET 8 AgentHost
  -> codex app-server --stdio
  -> assistant 文本返回 Palette
```

同一运行中 thread 可连续对话不等于持久记忆。SQLite、图纸级长期记忆、每会话独立
`CODEX_HOME`、审计哈希链和恢复策略仍未完成。

## 下一步顺序

每项必须验证通过后单独提交：

1. 收口 P0：修复并重冻 `0.3.2.0`，完成人工两轮启停、残留 `0` 和 `DBMOD` 不变。
2. 收口 P1：v2 接入 Runtime/Palette/Bridge，完成最终 R20.1 构建和用户混合选区加
   Codex v2 回合实测。
3. 补稳定化：离线、断线、超时、取消、文档切换、AutoCAD 退出、125%/150% DPI。
4. 稳定只读 MVP 完成后，再恢复 Provider 抽象、长期记忆、高级沙箱和安装发布。
5. 最后进入 CAD 写入：计划、预览、拒绝、一次允许、锁内重校验、单事务、回滚、Undo
   和不自动保存的真实 2016 验证。

## 不可弱化的边界

- Codex 不启动、唤醒、关闭、重启或操作用户的 AutoCAD；实机只由用户执行。
- CAD 写入固定为计划、预览、一次审批、`DocumentLock` 内重校验、单事务。
- 审批只允许拒绝和一次允许，不得永久允许。
- HMAC、严格 sequence、nonce、防重放、结果身份绑定和 fail-closed 不得降级。
- 写入前锁内重校验图纸、revision、选择摘要、图层和空间。
- Agent 中断、超时或结果不确定时不得自动重试 CAD 写入。
- 插件不得自动保存 DWG，也不得替用户关闭 `SAVETIME`。
- 不得降低 `SECURELOAD` 或自动改企业受信路径/注册表策略。
- 不得把 AutoCAD 2025 Host 或 2025 Autodesk 程序集用于 AutoCAD 2016。
- 未经原版 R20.1 编译和用户人工 `NETLOAD`，不得宣称相应能力支持 2016。
- 每阶段先验证、后单独提交。

## 工作区、证据和隐私

- 主工作树的用户 Host.2025 原型不得清理、覆盖或误提交。
- P0/P1 中代码存在不等于产品能力；MiMo/MCP 报告也不能替代 Git、编译和真实门禁。
- 仅在冻结版本、SHA-256 和完整步骤就绪后请求实机测试。
- 不要求用户粘贴 canonical JSON、选择/上下文哈希、图名或图纸路径。
- evidence 不得包含 `TRUSTEDPATHS`、用户名、真实图纸/网络路径、许可证或凭据。

## 支持声明

当前可以准确表述为：已在原版 AutoCAD 2016 R20.1 中验证一个只读 AI happy path：
受支持图元可生成 CadContextJson v1、显示于 Palette，并通过认证 AgentHost 调用本机
Codex 完成同一 thread 两轮对话。AgentHost 停止无残留、v2、完整生命周期、CAD 写入、
沙箱和发布仍未完成。

不得表述为完整支持 AutoCAD 2016，也不得表述为可安全执行 CAD 写入。

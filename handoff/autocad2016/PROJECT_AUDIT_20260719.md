# AutoCAD AI Agent 插件项目审计（2026-07-21 复核）

原始审计日期：2026-07-19

本次复核日期：2026-07-21
审计口径：只把本机真实运行、目标机原版 R20.1 编译和明确边界内的自动化证据写成已
验证；代码存在、文件数量、设计完成、模拟测试或其他 AutoCAD 版本原型不等于产品功能
完成。

本文件保留原文件名以维持已有链接。原审计的 `25%` 只代表 2026-07-19 尚未打通 AI
回合的历史快照，已经被后续实机证据取代，不是当前进度。

## 1. 项目总体状态

### 当前目标

构建 AutoCAD 2016 优先的本机 AI 插件：进程内为 x64、.NET Framework 4.5 薄宿主；
进程外为 .NET 8 AgentHost、Bridge、Codex App Server 和后续 Sandbox。用户选择图元后，
插件读取只读上下文、生成版本化 JSON、在 Palette 显示并交给本机 Codex，同一会话可连续
对话。未来 CAD 写入必须经过计划、预览、一次审批、HMAC/防重放、锁内重校验、单事务，
且插件不自动保存 DWG。

### A. 已在真实 AutoCAD 2016 中运行通过

- 诊断 Host 使用目标机原版 R20.1 程序集编译并由用户人工 `NETLOAD`。
- 100% DPI Palette 已验证打开、停靠、浮动、隐藏重开、释放重建和中文换行。
- 六类真实对象已生成 CadContextJson v1，并在统一 Palette 显示摘要和 canonical JSON；
  捕获、清除、Palette 重建保留和文档切换清除已有运行检查点。
- 精确 `0.3.1.0` 候选已人工 `NETLOAD`。真实 Line 完成选择读取、JSON、Palette、认证
  AgentHost 和本机 Codex 回答。
- 同一 Codex thread 的第二轮对话已返回；用户另确认多次连续上下文对话正常。
- 核心只读 Agent MVP 在验证后单独提交为 `7f10d60`。
- 已验证只读样本的 `DBMOD` 保持不变；CAD 写入和插件保存始终禁用。

### B. 构建或自动化通过，但产品运行尚未验证

- `0.3.2.0` 停止编排已有自动化候选，但失败状态和重试清理仍需修正、重新冻结及实机
  两轮启停；旧候选不得当成最终发布候选。
- CadContextJson v2 已完成 19 类强类型对象、三类受限占位、net45/net8 合同门禁、
  Bridge v2 方法和 AgentHost v1/v2 能力基础。
- v2 最新基础检查点为 `50f6cf3`；Contracts `71/71`、完整 Phase 2 `231/231`，原版
  R20.1 API probe 在两个 PowerShell 下均为 `19 present / 8 absent`。
- HMAC、sequence、nonce、防重放、Bootstrap、Bridge 和 AgentRuntime 的自动化证据较
  完整；这些不能替代最终 AutoCAD 内生命周期和 v2 产品链验证。

### C. 设计或延期阶段

- Provider 抽象、统一 Agent 事件、系统 ID 与 Provider ID 分离、Direct API Provider
  预留；当前只记录长期待办，不阻断最小 MVP。
- SQLite 图纸级长期记忆、审计哈希链、恢复和每会话独立 `CODEX_HOME`。
- 完整 OS 沙箱、企业安装、签名、升级/卸载和发布。
- AutoCAD 2016 CAD 写入产品链。

### D. 当前问题或未完成

- `0.3.1.0` 停止后曾发现一个 AgentHost 残留，停止生命周期未通过。
- 产品运行时、Palette 和 AI 请求仍使用 v1；v2 尚未接入。
- 明确为空的 `supportedCadContextSchemas` 可能被 DTO 默认值恢复成 v1，存在 fail-open
  风险，必须修复并测试。
- v2 最终集成 Host 尚未完成原版 R20.1 冻结构建和人工 `NETLOAD`。
- 离线、断线、超时、取消、文档关闭、AutoCAD 退出和高 DPI 的实机矩阵未关闭。
- AutoCAD 2016 预览、审批和写入尚未实机验证。

### 关于完成度

不再给伪精确总百分比，因为六项只读 happy path、稳定可重复使用的 MVP 和完整可写产品
是三个不同完成线。

- 原审计定义的六项只读 happy path 均已有真实运行证据：选择、读取、JSON、Palette、
  Codex 分析和同一 thread 连续对话。
- 该链路只覆盖 v1 支持对象，且停止无残留尚未通过，因此稳定化 MVP 仍未关闭。
- CAD 写入、安全执行、长期记忆、完整沙箱和发布仍属后续产品阶段。

准确结论是：只读 AI happy path 已运行通过，生命周期和对象覆盖仍在收口。旧 `25%`
不再适用，但也不能写成完整支持 AutoCAD 2016。

## 2. 功能证据清单

| 功能 | 状态 | 验证方式与边界 |
| --- | --- | --- |
| 原版 AutoCAD 2016 Host 加载 | 已运行通过 | 用户人工 `NETLOAD`；x64、CLR 4、R20.1 `20.1.0.0` |
| Palette 100% DPI | 已运行通过 | 打开、停靠、浮动、隐藏重开、重建、中文输入和换行 |
| 预选受支持对象 | 已运行通过 | 六类混合选择检查点及 Agent 候选真实 Line |
| 读取真实图元字段 | 已运行通过但覆盖有限 | v1 六类字段生成 JSON，不外推到全部对象 |
| CadContextJson v1 | 已运行通过 | 真实对象生成 canonical JSON 并显示于 Palette |
| 缓存清除和文档切换 | 已运行通过 | 用户清除及 document-activated 清缓存；`DBMOD` 不变 |
| 认证 AgentHost 启动 | 已运行通过 | `0.3.1.0` AutoCAD 实机认证连接 |
| 本机 Codex 分析 | 已运行通过 | 真实 Line 上下文获得回答 |
| 同一 thread 连续对话 | 已运行通过 | 第二轮复用前轮标记，用户确认多次连续对话 |
| AgentHost 停止无残留 | 未通过 | 曾检出一个残留；P0 待修、重冻、两轮实测 |
| CadContextJson v2 基础 | 构建/Specs 通过 | 19 类加三类占位；`50f6cf3`；不是 Runtime 证据 |
| v2 Palette/Agent/Codex 链路 | 未完成 | Runtime 仍为 v1，缺最终构建和 NETLOAD |
| 125%/150% DPI | 未验证 | 不从 100% DPI 外推 |
| 断线/超时/取消/退出 | 部分完成 | 自动化覆盖存在，AutoCAD 内矩阵未关闭 |
| CAD 预览/一次审批/写入 | 未验证 | AutoCAD 2016 产品路径禁用 |
| 写入后不自动保存 | 未验证 | 只读路径无保存，尚无成功写入证据 |
| 持久记忆 | 未实现 | 当前同 thread 对话不是 SQLite 长期记忆 |
| 完整 OS 沙箱 | 部分完成 | 认证和进程引导存在，完整隔离/资源配额未完成 |
| `.bundle`、签名和安装 | 未实现 | 无发布级 AutoCAD 2016 产品包 |

## 3. 当前架构和调用链

### 技术架构

- 进程内：C#、.NET Framework 4.5、x64、AutoCAD R20.1 API、PaletteSet/WPF。
- 进程外：C#、.NET 8 AgentHost、AgentRuntime、Bridge、AppServer。
- IPC：认证命名管道、HMAC、严格递增 sequence、nonce、防重放。
- Codex：`codex app-server --stdio` 结构化 JSONL。
- 门禁：PowerShell 7/5.1、原版 R20.1 程序集、net45/net8 Specs、禁止 API 和秘密扫描。

### 已运行通过的调用链

```text
AutoCAD 2016
  -> 统一 Host.2016 / SelectImplied / ForRead
  -> CadContextJson v1 / Palette
  -> net45 IAgentBridgeClient / 认证 Bridge
  -> .NET 8 AgentHost / AgentRuntime
  -> codex app-server --stdio
  -> assistant 文本返回 Palette
```

该链路以 `0.3.1.0` 的真实 Line 和同一 thread 两轮对话为证，不证明所有对象类型、停止
无残留、持久记忆或 CAD 写入。

### 上下文、记忆和沙箱边界

- v1 使用白名单 ForRead 读取，生成版本化 canonical JSON 和哈希；文档激活和用户命令
  可清除缓存。
- 当前连续对话依赖运行中的 Agent/Codex thread；尚无 SQLite、图纸级长期记忆、保留/
  清除策略、审计哈希链或每会话独立 `CODEX_HOME`。
- 已有认证 IPC、受限句柄 Bootstrap、请求级 read-only/workspace-write 和部分进程清理。
- 尚缺完整受限令牌/AppContainer、Job Object 强制进程树、CPU/内存配额、独立凭据与 MCP
  配置和完整恢复机制。

## 4. 距离稳定只读 MVP 的未完成项

### P0：停止生命周期

1. 修复停止失败后的状态和可重试清理语义。
2. 重跑门禁和原版 R20.1 编译，冻结新的 `0.3.2.0` 身份。
3. 用户人工 `NETLOAD`，连续两轮启动/停止。
4. Palette 显示真实终态，`DBMOD` 不变，AgentHost 残留为 `0`。
5. 记录 evidence 并单独提交。

### P1：CadContextJson v2 产品接入

1. Runtime、状态和 Palette 改用 v2，显示完整性计数。
2. Doctor/命令文案更新到准确 v2 能力。
3. 显式协商 schema `/2` 和 `agent.turn.start.v2`；缺能力 fail-closed。
4. 修复空 schema 数组恢复默认 v1 的 fail-open。
5. 完成真实认证 v2 调用链测试。
6. 原版 R20.1 最终集成构建并冻结 SHA-256。
7. 用户实测多个支持对象加一个未知对象、`complete=false`、计数正确、`DBMOD` 不变，
   且 Codex 使用 v2 回答。
8. 记录 evidence 并单独提交。

### 稳定化矩阵

- AgentHost 离线、异常退出、断线、超时和重连。
- 运行中请求取消及重复取消幂等性。
- 文档切换、上下文清除后拒绝旧上下文。
- AutoCAD 正常退出后线程、管道和 AgentHost 无残留。
- Palette 125%/150% DPI。
- 流式完成、失败和取消状态与 UI 一致。
- 身份绑定明确的稳定候选和统一人工测试手册。

不沿用旧审计的 24-36 小时估计。P0/P1 实机结果可能暴露目标机问题，应按门禁逐项
收口，不用静态代码量制造时间精度。

## 5. 当前主要风险

1. **生命周期残留**：问答成功不能抵消停止后的残留进程。
2. **对象覆盖不足**：v1 遇到未支持实体会整体 fail-closed，真实图纸容易触发。
3. **能力协商 fail-open**：空 schema 能力不能被默认值解释成 v1 支持。
4. **证据层级混淆**：v2 Specs/R20.1 编译不能写成 v2 已在 AutoCAD 运行。
5. **并行分支漂移**：P0、P1 和主工作树必须按验证阶段吸收，不能混入未验证改动。

原审计指出的开发顺序问题已部分纠正：最小垂直链路已经打通，不再是协议很多而 E2E
为零。当前不建议推倒重写。应冻结 Provider 扩展、Direct API、长期记忆、高级沙箱和安装
发布，先关闭 P0、P1 和稳定化矩阵。Host.2025 原型继续隔离保存，不得作为 2016 支持
证据，也不得由本阶段清理或提交。

## 6. 下一步建议

1. 先关闭 P0：停止失败状态、重新冻结、两轮启停、残留 `0`、单独提交。
2. 再关闭 P1：v2 Runtime/Palette/Bridge、fail-closed 协商、最终 R20.1 构建、混合选区
   和 Codex v2 实测、单独提交。
3. 完成只读稳定化：断线、超时、取消、退出、高 DPI 和旧上下文失效。
4. 再恢复 Provider 抽象、持久记忆和完整沙箱。
5. 最后进入 CAD 写入：计划、预览、一次审批、锁内重校验、单事务、Undo/回滚、中断不
   重试和插件不保存。

## 一句话总结

如果现在停止开发，当前项目的实际价值是：一个已在原版 AutoCAD 2016 中跑通真实图元
到本机 Codex 同一 thread 连续对话的只读 AI MVP happy path；它已经超过单纯技术验证，
但由于停止残留、对象覆盖、生命周期、CAD 写入、沙箱和发布尚未收口，还不是稳定完整的
AutoCAD AI 产品。

---

## 审计边界声明

本次复核没有启动、唤醒、关闭、重启或操作 AutoCAD，也没有把自动化测试当成新的实机
证据。未经目标机原版 R20.1 编译和用户人工 `NETLOAD`，不宣称相应能力支持 AutoCAD
2016；未经真实 CAD 写入、一次审批、锁内重校验、单事务和不保存验证，不宣称写入安全
闭环已成立。

# M2-A/M2-B：图纸级只读索引与 Codex 按需查询

最后更新：2026-07-22（北京时间）

本文件记录 M2 的可编译、可验证调用链：M2-A 建立只读图纸索引，M2-B 将同一索引作为
Codex 的结构化按需查询工具。它不是 M2 完成声明；AutoCAD 2016 实机和 1k/10k/50k
性能证据仍未取得。

## 1. 目标和边界

M2-A 保留 `CadContextJson v2` 的小快照边界，同时让 Host 在 AutoCAD 进程内以只读、
分片方式建立图纸级摘要和可分页实体索引。M2-B 让 Codex 通过现有 AgentRuntime、
AgentHost 和认证 Bridge 按需查询该索引，不复制扫描器。

明确不做：

- 不把 v2 的 64 实体或 256 KiB 上限简单放大。
- 不把整张图纸 JSON 一次发送到 Bridge、AgentHost 或 Codex。
- 不启用 CAD 写入、插件保存、命令字符串、LISP、脚本或反射式 CAD 调用。
- 不引入 Provider-neutral 抽象、Direct API Provider 或第二套 Agent Loop。
- 不允许模型提供 indexId、documentId 或 documentRevision，也不把 Autodesk 类型跨进程。

## 2. 当前架构

```text
AutoCAD 2016 R20.1 / net45 / x64
  UnifiedReadOnlyContextRuntime
    -> CadContextJson v2（有界选择快照，兼容旧调用链）
  DrawingIndexRuntime
    -> Idle 小片 + DocumentLock + ForRead Transaction
    -> DrawingIndexDescriptor v1 + CadQueryEntity[]（内存只读快照）
    -> DrawingIndexAgentSnapshot（纯托管、可失效冻结视图）
  UnifiedPalette
    -> 进度、完整性和最近查询摘要
  CODEX16INDEX / INFO / CANCEL / QUERY / QUERYNEXT

  MvpAgentClient
    <- 认证反向 cad.drawing.query 请求
    -> 只查询绑定当前回合的 DrawingIndexAgentSnapshot

AgentHost / codex app-server
  CodexAgentRuntime -> cad.query_drawing 动态只读工具
  AgentHostCadQueryBroker -> 认证反向 Bridge -> Host 冻结快照
```

CAD 对象读取和索引累计只在 Host 的受控 AutoCAD 生命周期内完成。Agent 查询使用已脱离
Autodesk 对象的纯托管冻结快照，Bridge worker 不进入 Autodesk API。模型只提供过滤器、
页大小和游标；Host 绑定 index/document/revision，系统 request、Provider thread/turn、
tool call 和 query ID 分离并逐项校验。

## 3. 冻结契约

### DrawingIndex v1

`codex.autocad.drawing-index/1` 的摘要包含脱敏的 `indexId`、文档身份指纹、revision、
扫描范围、状态、总数、已索引数、占位/读取失败数、进度、估算内存、类型/图层/空间/块
统计桶和完整性状态。

扫描范围：

- `selection`
- `current_space`
- `model_space`
- `layouts`
- `drawing`

状态语义：

| 状态 | 含义 |
| --- | --- |
| `preparing` / `scanning` | 仍在 Idle 分片工作，不能查询 |
| `ready` | 全部实体已索引且没有受限项 |
| `partial` | 扫描完成，但存在未知、代理或读取失败占位 |
| `limited` | 触发实体、统计桶、内存或时间预算，结果不完整 |
| `cancelled` | 用户取消，已发布实体被清空 |
| `stale` | 文档、revision、DBMOD、空间或对象事件使旧索引失效 |
| `failed` | 无法建立可验证索引 |

### CadQuery v1

`codex.autocad.cad-query/1` 使用绑定 `indexId + documentId + documentRevision` 的请求，
支持类型、图层、空间、块名、文字包含匹配、对象令牌和包围盒过滤。响应每页最多 200
个实体，游标绑定查询过滤器、页大小、索引和指纹；响应状态明确标注 `ok`、`partial`、
`limited`、`stale`、`cancelled` 或 `failed`。

对象返回的是文档局部不透明令牌和受限摘要，不是 DWG 路径或可执行 AutoCAD API。未知、
代理、超长字段和读取异常分别用 `unsupported`、`data_limited`、`read_failed` 状态表达，
不会让整个索引静默伪装成完整。

### Codex 动态查询

AgentRuntime 只在当前 AgentHost Bridge 会话中注册 `cad.query_drawing`。输入支持实体类型、
图层、空间、块名、对象令牌、文字包含、包围盒、是否包含受限项、页大小和游标；模型侧
schema 不包含受信索引身份。工具结果继续使用 `codex.autocad.cad-query/1` 的受限摘要。

当没有已发布 CadContext v2 选择快照，但存在 `ready`、`partial` 或 `limited` 的有效
DrawingIndex 冻结快照时，`CODEX16ASK` 可以继续；两者都不存在时必须拒绝。每个 ASK
只绑定当时的快照。合法反向查询即使早于 `agent.turn.start.v2` 响应到达，也只能凭精确的
request/thread 身份临时绑定，随后必须与 Provider turn 再次匹配。

启动失败、STOP、Bridge 断线、回合取消、超时或终态会取消并排空查询，清除 pending 与
临时 turn。文档修改、撤销、切换、索引替换或显式取消会使旧快照失效，后续查询返回结构化
拒绝，不能读取旧结果。

## 4. 资源和线程边界

- 每个准备片最多处理 4,096 个 ObjectId 或 12 ms。
- 每个读取片最多处理 128 个实体或 12 ms。
- 单次扫描最多 2 分钟、最多报告 2,000,000 个实体、最多保留 100,000 个实体索引。
- 累计实体估算内存预算为 64 MiB；超限返回 `limited`，不抛出未处理异常。
- AutoCAD 对象只在合法文档线程、`DocumentLock` 和 `ForRead` transaction 中访问。
- 统计和查询使用脱离 AutoCAD 对象的强类型副本；不保留 Entity 引用到后台线程。
- 索引完成时将已冻结实体数组所有权转移给 Agent 快照，避免 50k 场景的第二次数组深拷贝；
  外部调用构造快照时仍执行深拷贝。
- Host 本地线程安全遥测记录 Idle 总/准备/读取片数、各阶段最大分片耗时、总扫描耗时以及
  本地/Agent 查询耗时；只由 `CODEX16INDEXINFO` 展示，不改变 DrawingIndex/CadQuery wire。
- `CODEX16INDEXINFO` 和候选 manifest 同时声明单页最多 `200` 个实体、IPC 单帧最多
  `8,388,608` 字节；实机 evidence 另外记录 AutoCAD 扫描前和扫描期间峰值工作集。
- 对象事件、文档激活、关闭、撤销和 DBMOD 变化会使正在构建或已发布索引收敛到
  `stale`，旧游标不可继续使用。

跨多个 Idle 片段保留的 `BlockTableRecordEnumerator` 是本垂直切片唯一需要 AutoCAD
实机重点确认的生命周期风险；候选包没有把它宣称为已验证行为。

## 5. Host 命令

```text
CODEX16INDEX       选择 Selection/Current/Model/Layouts/Drawing，启动只读索引
CODEX16INDEXINFO   查看进度、计数、完整性和最近查询摘要
CODEX16INDEXCANCEL 幂等取消当前索引
CODEX16QUERY       查询首个分页（All/Type/Layer/Space/Block/Text/Object）
CODEX16QUERYNEXT   继续最近一次查询的游标页
```

这些命令用于人工验收和故障排查。UI 不解析原始 Codex JSON，索引也不会整包自动发送给
Agent；Codex 只在 ASK 回合中通过 `cad.query_drawing` 按需取得分页结果。

## 6. 代码和测试入口

- `src/Codex.AutoCAD.Contracts/DrawingIndexContracts.cs`：契约、限制和 fail-closed 验证器。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexCore.cs`：累计、状态策略、过滤、游标和查询引擎。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexEntityReader.cs`：R20.1 只读摘要和受限占位。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexRuntime.cs`：Idle 分片、文档事件和 Host 生命周期。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexAgentSnapshot.cs`：纯托管冻结快照和失效检查。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexPerformanceMetrics.cs`：Host 本地分片、扫描与查询
  性能证据，不引用 Autodesk API。
- `src/Codex.AutoCAD.Host.2016/MvpAgentClient.cs`：回合身份绑定与 Host 反向查询处理。
- `src/Codex.AutoCAD.Contracts/AgentBridgeContracts.cs`：反向查询请求/响应和稳定错误码。
- `src/Codex.AutoCAD.Bridge.Client/AgentBridgeClient.cs`：双向请求、取消、STOP 和关闭排空。
- `src/Codex.AutoCAD.AgentHost/AgentHostCadQueryBroker.cs`：AgentHost 认证反向查询 broker。
- `src/Codex.AutoCAD.AgentRuntime/CadDynamicTools.cs`：`cad.query_drawing` schema 和参数验证。
- `tests/Codex.AutoCAD.Contracts.Specs/DrawingIndexContractsSpecs.cs`：契约、50k 合成数据、
  分页、失效、预算和重复令牌测试。
- `scripts/verify-autocad2016-drawing-index-candidate.ps1`：锁文件恢复、R20.1 构建、
  只读扫描、候选打包、哈希和 evidence 生成。
- `scripts/new-autocad2016-drawing-index-benchmarks.ps1`：离线生成精确 1k/10k/50k 模型空间
  AC1009 DXF，拒绝覆盖且不启动 AutoCAD。
- `scripts/verify-autocad2016-drawing-index-benchmarks.ps1`：双次哈希、独立解析和 evidence
  fail-closed 门禁 `6/6`。
- `scripts/record-autocad2016-drawing-index-benchmark.ps1`：记录不含图名、路径或 JSON 的实机
  数值证据。
- `M2_DRAWING_INDEX_BENCHMARK_FIXTURES_20260722.md`：三档 fixture 生成、哈希、外部工作集
  采样和脱敏 evidence 写入说明。
- `M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`：AutoCAD 2016 唯一人工测试入口。

## 7. 自动化候选

候选目录：

`C:\tmp\CodexForAutoCAD-m2-integration\artifacts\autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024`

| 项目 | 值 |
| --- | --- |
| Host 版本 | `0.4.0.0` |
| 源码提交 | `34cef1214ad22822996db4e4ad33013f855751e3` |
| Host SHA-256 | `BC6011D3C0C00222BE266E27A26770B87FC4CE542A9516640AEC1A959950C5D5` |
| AgentHost EXE SHA-256 | `6DE30DB91C466CA0CA87E6202926FB893165CE8950B1CCAB9E0E3C49650CDD89` |
| manifest SHA-256 | `CDE0E31D9B2342B322D1850224B6DE78755B97EAEF7802C7D609F86E58E7D917` |
| Contracts net8/net45 | `88/88` |
| Bridge Client net8/net45 | `29/29` |
| Bridge/AgentHost | `39/39` |
| AgentRuntime | `34/34` |
| Host MVP | `54/54` |
| 完整 Phase 2 | `314/314` |
| Benchmark fixture/evidence | `6/6` |
| R20.1 Release x64 | 通过 |
| Host A/B | 逐字节一致 |
| AutoCAD live / NETLOAD | 未验证 |

证据：

`handoff/autocad2016/evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024.json`

该候选使用 `obj-########` 不透明对象令牌和 Host 随机生成的 `dq1_...` 分页游标。游标
五分钟过期，并绑定索引 ID、文档 revision、查询形状和 offset。旧 `E85D97EC...` 与
`597A7A3D...` 候选只保留为历史记录，不得继续用于当前测试。

## 8. M2 剩余完成条件

M2-A 和 M2-B 已进入同一真实调用链并通过自动化门禁。下一步不是继续扩展协议，而是由用户
在精确候选上完成 AutoCAD 2016 实机：五种索引范围、本地分页、仅索引无选择集 ASK、
`cad.query_drawing` 多页查询、修改/撤销/切图 stale、查询/回合取消、退出清理，以及
1k/10k/50k 响应性和性能记录。

M2 总完成条件仍是：1k/10k/50k 脱敏图纸可扫描和查询，AutoCAD 可操作、DBMOD 不被读取
改变，未知对象只降低完整性，超预算明确返回 `partial/limited`，且 Codex 能在不接收整图
JSON 的前提下按需查询并在索引失效后 fail-closed。

Autodesk `BlockTableRecordEnumerator` 已在同一个有效只读 transaction 内创建、遍历并
释放，不再跨 Idle 保存。当前剩余的 M2.3 风险是每个 space 的 ObjectId 仍会在一个
preparation Idle 回调内形成托管数组；精确候选必须证明 50k 最大 preparation slice 低于
20 ms。M2.3、M2.13 和 M2.14 在实机响应性、资源、取消、DBMOD 和动态查询证据完成前
保持未完成。

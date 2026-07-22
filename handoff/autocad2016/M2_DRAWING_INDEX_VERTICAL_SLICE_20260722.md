# M2-A：图纸级只读索引垂直切片

最后更新：2026-07-22（北京时间）

本文件记录 M2 的第一条可编译、可验证垂直切片。它不是 M2 完成声明，也不把尚未接入
Codex 的查询命令描述成 Agent 工具。

## 1. 目标和边界

M2-A 解决的问题是：选择上下文仍保留 `CadContextJson v2` 的小快照边界，同时让 Host
能够在 AutoCAD 进程内以只读、分片方式建立图纸级摘要和可分页实体索引。

明确不做：

- 不把 v2 的 64 实体或 256 KiB 上限简单放大。
- 不把整张图纸 JSON 一次发送到 Bridge、AgentHost 或 Codex。
- 不启用 CAD 写入、插件保存、命令字符串、LISP、脚本或反射式 CAD 调用。
- 不引入 Provider-neutral 抽象、Direct API Provider 或第二套 Agent Loop。
- 不把当前 Host 命令当作最终的 Codex 动态工具；该接入属于 M2-B。

## 2. 当前架构

```text
AutoCAD 2016 R20.1 / net45 / x64
  UnifiedReadOnlyContextRuntime
    -> CadContextJson v2（有界选择快照，兼容旧调用链）
  DrawingIndexRuntime
    -> Idle 小片 + DocumentLock + ForRead Transaction
    -> DrawingIndexDescriptor v1 + CadQueryEntity[]（内存只读快照）
  UnifiedPalette
    -> 进度、完整性和最近查询摘要
  CODEX16INDEX / INFO / CANCEL / QUERY / QUERYNEXT

AgentHost / codex app-server
  -> 当前仍只处理既有 v2 对话
  -> M2-A 尚未暴露 drawing-query 工具
```

CAD 对象读取、索引累计和查询响应目前都在 Host 的受控生命周期内完成；M2-B 才会把
深拷贝后的查询请求通过现有认证 Bridge 交给进程外 AgentHost。

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

## 4. 资源和线程边界

- 每个准备片最多处理 4,096 个 ObjectId 或 12 ms。
- 每个读取片最多处理 128 个实体或 12 ms。
- 单次扫描最多 2 分钟、最多报告 2,000,000 个实体、最多保留 100,000 个实体索引。
- 累计实体估算内存预算为 64 MiB；超限返回 `limited`，不抛出未处理异常。
- AutoCAD 对象只在合法文档线程、`DocumentLock` 和 `ForRead` transaction 中访问。
- 统计和查询使用脱离 AutoCAD 对象的强类型副本；不保留 Entity 引用到后台线程。
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

这些命令只用于 M2-A 人工验收和故障排查。UI 不解析原始 Codex JSON，且 M2-A 不会把查询
结果自动发送给 Agent。

## 6. 代码和测试入口

- `src/Codex.AutoCAD.Contracts/DrawingIndexContracts.cs`：契约、限制和 fail-closed 验证器。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexCore.cs`：累计、状态策略、过滤、游标和查询引擎。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexEntityReader.cs`：R20.1 只读摘要和受限占位。
- `src/Codex.AutoCAD.Host.2016/DrawingIndexRuntime.cs`：Idle 分片、文档事件和 Host 生命周期。
- `tests/Codex.AutoCAD.Contracts.Specs/DrawingIndexContractsSpecs.cs`：契约、50k 合成数据、
  分页、失效、预算和重复令牌测试。
- `scripts/verify-autocad2016-drawing-index-candidate.ps1`：锁文件恢复、R20.1 构建、
  只读扫描、候选打包、哈希和 evidence 生成。
- `M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`：AutoCAD 2016 唯一人工测试入口。

## 7. 自动化候选

候选目录：

`C:\\tmp\\CodexForAutoCAD-m2-drawing-index\\artifacts\\autocad2016-m2-drawing-index-v040-2cfbadd8-4028850a-8af00fa8`

| 项目 | 值 |
| --- | --- |
| Host 版本 | `0.4.0.0` |
| Host SHA-256 | `2CFBADD8FF57F6DAAA4727F1B6DE871D509B92E47A680ECCA669A024CBA786A5` |
| AgentHost EXE SHA-256 | `4028850AD9B9EECB8812B07CF3C401AE5287744D839AE66C57AD193C1DB3CE0C` |
| manifest SHA-256 | `3CF194EB69B8C33E8D6B3C7B7D33838D6CB847036819CAC074D9DB7E1AFEF20A` |
| Contracts net8/net45 | `83/83` |
| 完整 Phase 2 | `287/287` |
| R20.1 Release x64 | 通过 |
| Host A/B | 逐字节一致 |
| AutoCAD live / NETLOAD | 未验证 |

证据：

`handoff/autocad2016/evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-2cfbadd8-4028850a-8af00fa8.json`

## 8. M2-A 之后

M2-A 通过自动化门禁并形成独立提交后，下一步是 M2-B：在现有认证 Bridge/AgentHost 上增加
结构化只读 `drawing.query` 请求、响应和 Codex 工具适配。它必须复用本契约和同一索引，
不复制第二套扫描器，不把 Agent 直接绑定 AutoCAD 类型，也不允许模型绕过查询分页和
完整性状态。

M2 总完成条件仍是：1k/10k/50k 脱敏图纸可扫描和查询，AutoCAD 可操作、DBMOD 不被读取
改变，未知对象只降低完整性，超预算明确返回 `partial/limited`。

# CadContextJson v2 Host.2016 捕获层交接

日期：2026-07-21（北京时间）

状态：v2 捕获层、统一 Runtime、Palette 字段和 Agent v2 turn 已接入并通过本机编译/规格；尚未执行 AutoCAD 2016 `NETLOAD`。

## 本阶段完成内容

- 为 AutoCAD 2016 R20.1 实现 19 类强类型只读对象读取：Line、Circle、Polyline、
  DBText、MText、BlockReference、Arc、Ellipse、Spline、DBPoint、Ray、Xline、
  Polyline2d、Polyline3d、Dimension、Hatch、Leader、MLeader、Table。
- 未知实体、读取失败和单实体数据超限不再拖垮整个混合选区，而是生成只含公共字段、
  `dxfName` 和闭集 `reason` 的受限占位。
- `GetObject`、身份读取、公共字段和 payload getter 均按实体隔离；只有无法形成合法
  Handle 等选择级身份时才整体 fail-closed。
- Layer/DXF 使用限界 fallback；不捕获异常文本、堆栈、图纸名称、路径、外部参照路径或
  代理对象私有数据。
- Polyline2d/3d 子对象只在同一 `StartOpenCloseTransaction` 中以 `ForRead` 打开。
- MLeader 在调用索引集合 API 和分配顶点数组前检查 `LeaderCount`、`LeaderLineCount`、
  单线及累计顶点上限。
- 选择与实体状态哈希按数值 Handle 排序；重复、空或非法 Handle fail-closed。

## 修改范围

- `src/Codex.AutoCAD.Host.2016/CadContextJsonV2Mapper.cs`
- `src/Codex.AutoCAD.Host.2016/CadContextV2CapturePolicy.cs`
- `src/Codex.AutoCAD.Host.2016/CanonicalSelectionHashV2.cs`
- `src/Codex.AutoCAD.Host.2016/ReadOnlySelectionCaptureV2.cs`
- `src/Codex.AutoCAD.Host.2016/Codex.AutoCAD.Host.2016.csproj`
- `tests/Codex.AutoCAD.Host.2016.V2.Specs/**`
- 本交接、状态索引和本阶段脱敏 evidence。

## 实际验证结果

### 原版 R20.1 程序集

| 程序集 | Assembly Version | Authenticode | SHA-256 |
| --- | --- | --- | --- |
| `acmgd.dll` | `20.1.0.0` | Valid / Autodesk | `CCE13839886E827C392637D4F6B670E4CA7780E1A02B660E41D049E9C492B97F` |
| `acdbmgd.dll` | `20.1.0.0` | Valid / Autodesk | `9C27F4A71E4DFAEC393B53AB15A657FA37CA9F8A7B09E0522894AB3B354603BB` |
| `accoremgd.dll` | `20.1.0.0` | Valid / Autodesk | `80860722FB2D40209D63E4720BE9D5018A5A4F27FE23F844DFE2951CA35E30B0` |

### Host 构建

- 两份独立临时源码副本分别执行 locked restore + Release/net45/x64 Rebuild。
- 两份 Host DLL 均为 `105984` 字节。
- 两份 SHA-256 均为
  `700A0BF9CBD976625F1EF4D7BE820DD257263295466EDA13FBC8109D89F96DD0`。
- Autodesk DLL copy count：`0`。
- 该哈希尚未绑定到 AutoCAD 运行时，也不是发布候选身份。

### Specs

- Host v2 Specs：net45 `12/12`、net8 `12/12`，stdout 完全一致。
- Host 选择冻结向量：`147` bytes；
  `0ba4970c01da7877a41c9de960f1decd090d0f6646e9eff7a979c71db5bb8990`。
- Contracts：net45 `39/39`、net8 `39/39`，stdout 完全一致。
- v1 固定向量保持 `2225` bytes / `c5a03d4c...4423`。
- v2 固定向量保持 `6678` bytes / `21cc9378...c3b4`。
- Phase 2：Release `0` warning / `0` error，8 个规格项目动态汇总 `199/199`；Host
  禁止 API、AgentHost doctor、秘密扫描和 `git diff --check` 通过。
- 本次继续开发后，Bridge 新增真实 v2 `StartTurnV2Async` 回合并达到 `38/38`；随后完整
  Phase 2 本机门禁动态汇总为 `232/232`，同时通过 Host 禁用 API、AgentHost doctor、
  `git diff --check` 和秘密扫描。历史 `199/199` evidence 尚未重生成，仍保留为历史记录。
- 当前 Contracts 已扩展为 `71/71`，Host v2 能力策略为 `6/6`；缺少 v2 方法、缺少 v2
  schema、null 或空 schema 列表均 fail-closed，不回退到 v1。
- 文档切换或清除若发生在 Agent 异步启动期间，发送 v2 turn 前会重新校验当前状态引用；
  旧上下文会被拒绝。该竞态保护已通过 R20.1 编译，但尚无 AutoCAD live 证据。

## 证据边界

本阶段没有启动、唤醒、关闭、重启或操作 AutoCAD，没有执行 `NETLOAD`，没有读取真实
DWG，也没有发送 CAD 命令。以下能力仍未由运行证据证明：

- `UnifiedReadOnlyContextRuntime` 在真实 AutoCAD 进程中调用 v2 捕获器。
- Palette 在真实 AutoCAD 进程中显示 v2 摘要、完整性计数和 canonical JSON。
- Bridge/AgentHost 在真实 AutoCAD 进程中完成 v2 能力协商和 `agent.turn.start.v2`。
- AutoCAD 2016 实机混合选区。
- DBMOD、不保存和文档切换的 v2 运行时证据。

旧 `verify-autocad2016-unified-host.ps1` 冻结于更早的 v1 候选，其 Compile/IL/hash 预期
不适用于当前带 SDK 项目引用的 Host 图；本阶段没有修改旧 verifier 或用关闭 locked
restore 的方式强行让它通过，而是用两份独立源码副本完成可重复构建检查。

## 下一步

1. 先让 P0 的 `0.3.2` AgentHost 停止生命周期修复通过人工实机验证并单独提交；当前
   P1 分支基于 `7f10d60`，不得冻结一个重新带回旧停止残留风险的可加载候选。
2. 受控引入已验证 P0 提交后，审核 Bridge v2 跨版本兼容夹具并重新运行完整门禁。
3. 重新建立适用于合并后项目图的 v2 Host 验证器并冻结可运行 DLL。
4. 由用户在现有 AutoCAD 2016 中人工 `NETLOAD`，验证多种支持对象加一个未知对象、
   `complete=false`、计数、DBMOD 不变和插件不保存。
5. 具体人工步骤以合并并冻结候选后生成的正式手册为准；当前预备草案见
   `MVP_CONTEXT_V2_RUNTIME_TEST_DRAFT.md`。

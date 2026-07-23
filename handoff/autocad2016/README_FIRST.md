# Codex for AutoCAD 2016：先读这里

最后更新：2026-07-23（北京时间）

长期目标与完整 M0-M12 队列见 `LONG_TERM_MEMORY_TODO.md`；当前证据边界见
`CURRENT_STATE.md`。本文件只提供当前基线、候选身份、操作入口和下一步验证顺序。

## 1. 当前准确结论

AutoCAD 2016 R20.1 已建立一个真实运行的 CadContextJson v2 只读 AI 基线：

- `net45/x64` 统一 Host 可人工 `NETLOAD`。
- Doctor 显示 `codex.autocad.cad-context/2`。
- 100% DPI Palette 的打开、停靠、浮动、隐藏重开、重建、中文输入和布局通过。
- 一个 50 对象混合选区成功发布，其中 6 个对象以受限 placeholder 表示；
  `jsonBytes=23142`，`DBMOD 21 -> 21`。
- 本机 Codex 使用真实 v2 CAD 上下文完成两轮连续对话。
- 显式 CAD 上下文清除和文档激活清除旧缓存通过。
- P0 停止生命周期已有独立实机证据：重复 STOP、DBMOD 不变、AgentHost 残留为 0。
- CAD 写入和插件发起的保存始终禁用。

在该基线上，M1 `0.3.3.0` 候选已经完成代码与自动化冻结：

- Bridge 断线后原子离线并终止当前回合，后续 ASK fail-closed。
- request_id、回合状态、取消、10 分钟超时和唯一终态由 Host 管理。
- 重复取消幂等，终态后的迟到事件不能恢复或覆盖状态。
- `CODEX16NEWCHAT` 保留 CAD 上下文并建立新 Codex 对话。
- `CODEX16CLEARALL` 清除 CAD 上下文、回答文本和当前对话。
- 对话按图纸隔离；切换图纸立即清空旧回答，下一次 ASK 建立新 thread。
- CAD 写入和插件保存仍保持禁用。

M2 `0.4.0.0` 已把独立只读整图索引和 Codex 按需查询接成一条调用链：

- 选择快照仍使用 `codex.autocad.cad-context/2` 和原 64 实体/256 KiB 上限。
- 整图能力使用 `codex.autocad.drawing-index/1` 和 `codex.autocad.cad-query/1`。
- 支持选择集、当前空间、模型空间、布局和整张图纸范围。
- Idle 分片读取支持进度、幂等取消、2 分钟超时、100,000 实体索引和 64 MiB 估算预算。
- 类型、图层、空间、块、文字、包围盒和对象令牌可过滤并用稳定游标分页。
- 文档/revision/DBMOD/空间变化使旧索引 `stale`；未知或读取失败对象形成受限占位。
- M2-B 已注册只读 `cad.query_drawing` 动态工具，通过 AgentHost 和认证反向 Bridge 查询
  Host 冻结快照；模型不能提供或覆盖索引、文档和 revision 身份。
- 无有效选择上下文但有有效 DrawingIndex 时，`CODEX16ASK` 仍可启动同一 Codex 对话；
  取消、断线、回合终态、文档修改、撤销或切换均使旧绑定 fail-closed。
- 已建立模型空间精确 1k/10k/50k 的确定性 AC1009 DXF，独立解析与脱敏 evidence 门禁
  `6/6`；`CODEX16INDEXINFO` 已显示 Host 本地分片、总扫描、内存和查询耗时。
- 查询页硬上限为 `200` 个实体，IPC 单帧硬上限为 `8,388,608` 字节；两项均写入候选
  manifest 并由 `CODEX16INDEXINFO` 显示，不依赖人工记忆常量。

M3 `0.4.1.0` 正在开发第一条读取语义纵切，不是冻结候选：

- 选择快照、整图索引、Palette 和 `CODEX16CTXINFO` / `CODEX16INDEXINFO` 会按实际类型
  显示未支持、数据超限和读取失败对象的数量；统计不带图层、Handle、路径或对象内容。
- `CODEX16TYPEINFO` 显示 19 类现有强类型对象的中文名称与人工创建入口。
- `BlockReference` 的受限 `blockDetails` 已贯通 DrawingIndex、CadQuery、认证 Bridge 和
  Agent 工具：属性/动态属性、嵌套块计数与深度、布局标志和安全 Xref 布尔元数据均有上限。
  外部 Xref 定义和真实路径不会读取或传播，详情会降级为 `limited`。
- 自动门禁已通过 Contracts `86/86`、Bridge Client net45/net8 各 `29/29`、Bridge `39/39`、
  AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2 `310/310`；R20.1 API 双 Shell
  Probe 为 `29 passed / 8 expected failed`，目标 R20.1/net45/x64 Host A/B 输出逐字节一致，
  当前 Host SHA-256 为
  `FB18D95981F607B22D8C023BF63915614DFF8964BF985BE6CB0ABEA26D9B3673`，且 Autodesk DLL
  复制数为 `0`。
- 中文字段核对目录见 `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`；它不替代脱敏
  示例测试图、R20.1 Probe 或实机逐类字段证据。

脱敏实机范围证据：
`evidence/cad-context-v2-live-observation-20260722.json`。

这仍不是完整产品：

- `0.3.3.0` 尚未按精确哈希在 AutoCAD 2016 中 `NETLOAD`，M1 实机矩阵仍待执行。
- M2 `0.4.0.0` 尚未人工 `NETLOAD`；五种范围、无选择集 ASK、Agent 动态分页、
  1k/10k/50k 响应性、Idle 枚举器生命周期、取消、失效和退出清理均是未验证项。
- 当前选择快照仍最多 64 个实体、canonical JSON 最多 256 KiB；大图走独立索引。
- 19 类对象尚未逐类完成字段实机核对。
- AutoCAD 正常退出、125%/150% DPI 和故障矩阵尚未完成。
- CAD 写入、完整 OS 沙箱、长期记忆、签名安装和企业部署尚未完成。

## 2. 当前候选身份

当前开发候选是 M2-A/M2-B 加基准资产和性能遥测的合并候选：

```text
Module version: 0.4.0.0
CadContext schema: codex.autocad.cad-context/2
DrawingIndex schema: codex.autocad.drawing-index/1
CadQuery schema: codex.autocad.cad-query/1
Candidate directory:
C:\tmp\CodexForAutoCAD-m2-benchmark\artifacts\autocad2016-m2-drawing-index-v040-e85d97ec-fa16355c-898671e2

Host:
Codex.AutoCAD.Host.2016.dll
SHA-256:
E85D97EC02505EF69C67F710EAD5D35D18481B7D2DBB4C3D87195FCDE4156B7E

AgentHost:
AgentHost\Codex.AutoCAD.AgentHost.exe
SHA-256:
FA16355C185F61CD7E85446E884C2FF9D7C745E5E2EB0CC40747C916C215371B

Manifest SHA-256:
95427BD85E70870C483512CD4401228B70F63608802512119F5ECB6486844356
```

该候选通过 Contracts net8/net45 `84/84`、Bridge Client net8/net45 `29/29`、
Bridge/AgentHost `39/39`、AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2
`308/308`、benchmark `6/6`、30 文件 Host.2016 只读 Compile 闭包、R20.1/net45/x64 双构建位级一致、
敏感信息扫描和候选包自身 AgentHost doctor。构建证据为
`evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-e85d97ec-fa16355c-898671e2.json`。

它尚未按精确哈希在 AutoCAD 内人工 NETLOAD，因此保持 `NetLoadVerified=false`。已经取得
实机证据的仍是旧 `0.3.2.0` P1 候选 Host `0D72EDC3...`、AgentHost `10BEA363...`；M1
`0.3.3.0` 与 M2 `0.4.0.0` 的自动化证据都不能继承该实机结论。

## 3. 当前架构

```text
AutoCAD 2016 R20.1 / x64
  Codex.AutoCAD.Host.2016 / .NET Framework 4.5
  - Palette
  - 只读选择捕获
  - CadContextJson v2
  - DrawingIndex v1 / CadQuery v1
  - Idle 分片扫描与本地分页命令
  - 纯托管冻结查询快照
  - 认证 Bridge Client
                 |
                 | 当前用户命名管道（双向请求）
                 | HMAC + sequence + nonce + 防重放
                 v
  AgentHost / .NET 8
  - CodexAgentRuntime
  - codex app-server --stdio
  - cad.query_drawing 动态只读工具
  - 认证反向 CadQuery broker
  - 结构化事件返回 Palette
```

AutoCAD UI 不直接启动或解析 Codex 控制台文本。当前没有 Provider-neutral 抽象，也不开发
Direct API Provider 或第二套 Agent Loop。M2-B 复用 M2-A 的同一索引、现有 AgentRuntime
和认证 Bridge，没有增加第二套扫描器或 Agent 调用链；Bridge 工作线程只查询脱离 Autodesk
对象的冻结托管快照。

## 4. 常用命令

```text
CODEXCADDOCTOR
CODEXCAD
CODEX16PAL
CODEX16PALINFO
CODEX16CTX
CODEX16CTXINFO
CODEX16TYPEINFO
CODEX16CTXCLEAR
CODEX16INDEX
CODEX16INDEXINFO
CODEX16INDEXCANCEL
CODEX16QUERY
CODEX16QUERYNEXT
CODEX16AGENTSTART
CODEX16ASK
CODEX16CANCEL
CODEX16NEWCHAT
CODEX16CLEARALL
CODEX16AGENTSTOP
CODEX16PALRESET
```

语义注意：

- `CODEX16CTXCLEAR` 只清除内存中的 CAD 上下文，不创建新 Codex thread。
- 因此清除 CAD 上下文后，当前会话仍可能记得先前聊天内容。
- `CODEX16NEWCHAT` 保留当前 CAD 上下文，清空可见旧回答并建立新对话。
- `CODEX16CLEARALL` 清除 CAD 上下文、回答文本和当前对话；下次 ASK 建立新 thread。
- 切换图纸会清除旧 CAD 上下文与可见回答，并使旧对话失效；图 B 不复用图 A thread。
- 活动回合期间执行新建对话或清除全部会返回结构化 `busy`，不会覆盖活动回合。
- `CODEX16ASK` 能弹出输入提示不代表旧上下文可发送；必须实际提交后才算 fail-closed
  验证。
- `CODEX16INDEX` 建立与 CadContext v2 分离的图纸级内存索引；它不会整包自动发送给 Codex，
  但有效冻结快照可在 ASK 回合内由 `cad.query_drawing` 按需分页查询。
- `CODEX16TYPEINFO` 只显示 M3 中文对象目录，不读取、修改或保存当前图纸。
- `CODEX16QUERY`/`CODEX16QUERYNEXT` 只查询已完成的当前索引，索引失效后必须重建。
- 没有已发布选择上下文时，只要 DrawingIndex 仍有效也可 ASK；两者都没有时必须拒绝。

## 5. 已通过的实机项目

- [x] Host 加载和 Doctor。
- [x] v2 schema 和只读/禁保存声明。
- [x] 100% DPI Palette 全部人工交互。
- [x] 50 对象混合选区和 6 个 placeholder。
- [x] DBMOD 在混合读取样本中保持 `21 -> 21`。
- [x] 本机 Codex v2 两轮连续对话。
- [x] 显式上下文清除。
- [x] 文档激活清除旧上下文。
- [x] P0 AgentHost 停止无残留。

## 6. 尚需实机验证

M1 仍使用 `M1_READONLY_STABILITY_RUNTIME_TEST_20260722.md` 和精确 `0.3.3.0` 候选；M2
使用 `M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md` 和上述 `0.4.0.0` 候选。当前允许延期，
但不得写成已通过：

1. `CODEX16NEWCHAT` 保留 CAD 上下文但不保留旧聊天记忆。
2. `CODEX16CTXCLEAR` 只清 CAD 上下文并保留当前聊天。
3. `CODEX16CLEARALL` 同时清 CAD 上下文、回答文本和对话。
4. 图 A/图 B 的上下文、回答和 Codex 对话严格隔离。
5. 回合取消和重复取消，终态后状态不回退。
6. v2 上下文已发布时 Palette Reset 后仍保留上下文。
7. 正常退出 AutoCAD，不先 STOP，确认 AgentHost/Codex 残留为 0。
8. 125% 和 150% DPI。
9. AgentHost 启动失败、Bridge 断线、请求超时和迟到事件。
10. M2 五种范围、本地分页、未知占位、取消、失效和退出清理。
11. 仅有 DrawingIndex、无选择上下文时 ASK，并明确触发 `cad.query_drawing` 多页查询。
12. 索引修改/撤销/切图失效、查询/回合取消及断线后的 fail-closed。
13. M2 1k/10k/50k 图纸扫描、Agent 查询、UI 响应、内存和 DBMOD 基准。
14. M3 的 19 类强类型对象逐类字段核对、R20.1 API Probe、示例图资产和高价值受限读取。

## 7. 当前开发顺序

1. M0：已完成 P0/P1 集成、evidence/文档收拢、门禁复跑和统一候选冻结。
2. M1：代码、自动化和 `0.3.3.0` 候选冻结完成；当前只剩实机矩阵与 evidence 绑定。
3. M2-A/M2-B：图纸索引、分页命令、Codex `cad.query_drawing`、自动化和 `0.4.0.0`
   候选均完成；等待实机与性能 evidence。
4. M3：读取对象语义与覆盖已开始开发纵切；当前中文目录和占位实际类型统计不等于实机
   逐类字段通过。
5. M4：进程沙箱、配置和审计基础。
6. M5：AutoCAD 2016 `create_line` 安全写入最小闭环。
7. 后续阶段见 `LONG_TERM_MEMORY_TODO.md`。

## 8. 构建与自动化边界

M2 `0.4.0.0` 候选已重跑以下门禁：

- Contracts net8/net45：`84/84`。
- Bridge Client net8/net45：`29/29`。
- Bridge/AgentHost：`39/39`；AgentRuntime：`33/33`；Host MVP：`53/53`。
- 完整 Phase 2：`308/308`；benchmark fixture/evidence：`6/6`。
- R20.1 Host Release：0 warning / 0 error。
- Host.2016 真实 Compile 闭包：30 个源文件，CAD 写入/命令/保存 API 扫描通过。
- R20.1/net45/x64 A/B 输出位级一致。
- Host 禁止 API、秘密扫描、diff 和候选包自身 AgentHost doctor。

这些门禁不替代 AutoCAD 2016 人工 `NETLOAD`。历史 `0.3.2.0` 实机结果也不能自动证明
新的 `0.4.0.0` 候选，更不能证明 50k 运行时性能。

M3 当前自动门禁已完整运行：Contracts `86/86`、Bridge Client net45/net8 各 `29/29`、
Bridge `39/39`、AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2 `310/310`。R20.1
API 双 Shell Probe 为 `29 passed / 8 expected failed`，两个 Shell 的成员集合和 Probe DLL
哈希一致；R20.1/net45/x64 Host A/B 输出也逐字节一致，Host SHA-256 为
`FB18D95981F607B22D8C023BF63915614DFF8964BF985BE6CB0ABEA26D9B3673`，Autodesk DLL 复制数
为 `0`。这些记录分别见
`evidence/v2-api-surface-probe-m3-cross-shell-20260723.json` 和当前 `artifacts` 编译输出。
它们不是候选 manifest，不是冻结候选，也尚未 AutoCAD `NETLOAD`；候选冻结和实机测试仍须
在对应纵切完成后单独执行。

## 9. 安全与隐私

- 不向聊天或 Git 粘贴完整 canonical JSON。
- 不记录真实图纸路径、图名、Handle、选择哈希、上下文哈希或外参路径。
- 不记录 API Key、token、完整环境变量、`TRUSTEDPATHS`、用户名或许可证信息。
- Autodesk DLL 不提交仓库、不复制到插件包。
- 插件不自动保存 DWG，不修改 SAVETIME。
- Codex 不自动启动、关闭、重启或操作用户的 AutoCAD；实机步骤由用户执行。
- 未完成安全写入闭环前，CAD 写入保持编译期和运行时禁用。

## 10. 关键 evidence

- `evidence/cad-context-v2-live-observation-20260722.json`：本次 P1 AutoCAD live 范围。
- `evidence/agent-stop-live-observation-20260722.json`：P0 停止生命周期。
- `evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30.json`：
  P1 R20.1 构建和候选身份。
- `evidence/cad-context-v2-candidate-package-doctor-20260722-refresh.json`：候选 AgentHost doctor。
- `evidence/agenthost-v2-live-two-turns-20260722-refresh.json`：非 AutoCAD 的真实 Codex v2 两轮。
- `evidence/phase2-final-gate-20260722-exit-retry.json`：P1 Phase 2 `259/259`。
- `evidence/host2016-terminate-exit-retry-20260722.json`：退出清理重试自动化 `24/24`。
- `evidence/m0-baseline-verification-20260722.json`：M0 聚合门禁、候选身份和实机边界。
- `M0_BASELINE_RELEASE_20260722.md`：M0 冻结记录与下一阶段入口。
- `evidence/cad-context-v2-candidate-build-autocad2016-m1-readonly-v033-c3478920-a47d86a6-7fc17895.json`：
  M1 `0.3.3.0` 自动化冻结、候选身份和未实机边界。
- `M1_READONLY_STABILITY_RUNTIME_TEST_20260722.md`：M1 唯一当前实机测试入口。
- `M2_DRAWING_INDEX_VERTICAL_SLICE_20260722.md`：M2-A/M2-B 架构、契约与边界。
- `M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`：M2 唯一实机测试入口。
- `M2_DRAWING_INDEX_BENCHMARK_FIXTURES_20260722.md`：固定三档性能图生成、哈希和脱敏记录。
- `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`：M3 中文对象目录、字段核对模板和边界。
- `evidence/v2-api-surface-probe-m3-cross-shell-20260723.json`：M3 块读取所需 R20.1 API 的
  双 Shell 脱敏 Probe 结果，不等于 AutoCAD 实机验证。
- `evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-e85d97ec-fa16355c-898671e2.json`：
  M2 自动化冻结、候选身份和未实机边界。

## 11. 支持声明

当前可以准确表述为：

> AutoCAD 2016 R20.1 已实机跑通 CadContextJson v2 的只读选择、Palette、本机 Codex 和
> 两轮连续对话基线；50 对象混合选区中的未知对象不会中断发布。M1 `0.3.3.0` 已完成
> 只读稳定化代码与自动化冻结。M2 `0.4.0.0` 已实现独立 DrawingIndex/CadQuery、Idle
> 分片、本地分页命令和 Codex `cad.query_drawing` 认证反向查询；确定性 1k/10k/50k
> fixture、性能遥测和脱敏记录器已经通过自动化，但尚未完成 AutoCAD 实机性能验证。
> M3 `0.4.1.0` 已将受限块属性、动态块、嵌套块、布局和安全 Xref 元数据接入整图只读
> 查询；尚未冻结候选或取得逐类实机字段证据。
> 安全 CAD 写入、完整沙箱、长期记忆和发布安装
> 尚未完成。

不得表述为完整支持 AutoCAD 2016，也不得表述为已经支持安全 CAD 写入。

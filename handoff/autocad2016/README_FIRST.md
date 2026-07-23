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

M3 `0.4.2.0` 的读取语义纵切已冻结自动化候选，但尚未人工 `NETLOAD`：

- 选择快照、整图索引、Palette 和 `CODEX16CTXINFO` / `CODEX16INDEXINFO` 会按实际类型
  显示未支持、数据超限和读取失败对象的数量；统计不带图层、Handle、路径或对象内容。
- `CODEX16TYPEINFO` 显示 19 类现有强类型对象的中文名称与人工创建入口。
- `BlockReference` 的受限 `blockDetails` 已贯通 DrawingIndex、CadQuery、认证 Bridge 和
  Agent 工具：属性/动态属性、嵌套块计数与深度、布局标志和安全 Xref 布尔元数据均有上限。
  外部 Xref 定义和真实路径不会读取或传播，详情会降级为 `limited`。
- Region、Solid、Mesh、Surface、RasterImage、Underlay、Proxy 和 Wipeout 已在
  DrawingIndex/CadQuery 中归为受限类别，只保留类型、图层、空间和范围摘要，固定为
  `Unsupported=true` / `data_limited`；它们没有改变 CadContextJson v2 的强类型选择快照。
- 自动门禁已通过 Contracts `87/87`、Bridge Client net45/net8 各 `29/29`、Bridge `39/39`、
  AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2 `319/319`；R20.1 API 双 Shell
  Probe 为 `29 passed / 8 expected failed`，目标 R20.1/net45/x64 Host A/B 输出逐字节一致，
  当前 Host SHA-256 为
  `B5081C63DD11BD36706B529EC28C58BB1DEA22FEF6D50BA0E76C5E3E4CE67879`，且 Autodesk DLL
  复制数为 `0`。
- 中文字段核对目录见 `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`；它不替代脱敏
  示例测试图、R20.1 Probe 或实机逐类字段证据。
- M3 精确候选为
  `artifacts/autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7/`；AgentHost
  SHA-256 为 `E3DBE95546D193D9AF451A0420E648085F9E2AF9ECCC6E956BD85BC26ACDA615`，manifest
  SHA-256 为 `2633642C2F993FC320A0662FD95D4BC900CD4A453ABCDD6B7BEB7C596EF30348`，冻结 evidence
  为 `evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7.json`。
  它保持 `NetLoadVerified=false`、`AutoCadLiveEvidence=false`，不能继承 M2 或 P1 的实机结论。
- M3 另有离线验证 `6/6` 的 AC1015 核心 DXF fixture，涵盖 14 个可直接编码的基础/旧式实体变体。
  它不是 AutoCAD 实机通过证据，Dimension、Hatch、Leader、MLeader 和 Table 仍需专用脱敏测试图。

M4 进程隔离已完成一个不依赖 AutoCAD 实机的小阶段：

- 校验后的 AgentHost 在恢复前进入未命名 Windows Job Object；正常 STOP、Job 拥有者退出，或
  已认证 AgentHost 自行退出而启动器仍存活时，都会回收 AgentHost 及其普通后代。最后一条由
  service session 退出监视器关闭保留的 Job。
- 同一 Job 保留 `KILL_ON_JOB_CLOSE`，并默认限制进程树最多 `16` 个进程、Job 总提交内存
  最多 `4 GiB`、CPU hard cap `75%`、累计 Job user-time `8` 小时；认证后的 service session
  另有 `24` 小时墙钟截止。非法配置在创建子进程前 fail-closed。
- net45/net8 AgentLauncher Specs 各 `37/37`；Windows 已读回同一 Job 工厂设置的实际标志
  与值，CPU-busy synthetic child user-time 耗尽、墙钟终止、显式 STOP 胜过已撤销截止、一次
  清理重试及连续失败后阻断后续启动均通过；正常 STOP 先等待 `1` 秒自然退出，再进入原有
  `5` 秒强制回收。新增 synthetic 异常退出规格确认根 AgentHost 退出后后代不会因启动器仍持有
  Job 而残留；相关进程基线/终态为 `0 -> 0`。这不等于 AutoCAD 异常退出实机验证。
- AgentHost 只读会话现在强制写入每会话独立的有界 JSONL 审计，覆盖 session、Bridge、请求、
  thread/turn、取消、审批请求和 turn 终态；仅记录受限 ID/方法/稳定状态码，审计故障会关闭
  Bridge。workspace 和 audit 使用受保护的当前用户/SYSTEM/Administrators ACL；session 正常
  退出删除，残留按 `24` 小时/最多 `64` 个清理，审计按 `30` 天/最多 `512` 个清理，清理不
  跟随重解析点。审计 `/2` 已加入 canonical SHA-256 前序链和有界完整性验证；它没有签名、远端
  锚定或 WORM 存储。当前 Bridge 为 `50/50`。
- Codex 子进程现先清空父环境，再使用固定 `16` 个变量名；`TEMP`/`TMP` 指向每会话 workspace，
  不自动传入 token/API key、代理、父 `PATH`、`CODEX_HOME` 或自定义变量。AppServer 为 `29/29`，
  该检查点的完整 Phase 2 为 `350/350`，真实 doctor 和两轮 Codex live `2/2` 继续通过。
- Bridge、Bridge Client、AgentHost、AgentRuntime 与 Host.2016 的失败说明现通过
  `AgentBridgeErrorSanitizer` 收敛到固定错误码/安全说明。未知码会降级为 `internal_error`；
  异常、Provider/处理器文本、stderr、伪路径和令牌不能再作为 IPC、运行时或 Palette 失败说明传播。
  Contracts、Bridge、Bridge Client 和运行时的路径形态 sentinel 回归已通过；Bridge 为 `56/56`、
  该检查点的完整 Phase 2 为 `350/350`。该检查点未启动 AutoCAD，详情见
  `M4_DIAGNOSTIC_SANITIZATION_20260723.md`。
- 本地 Codex 配置读取和 AgentHost CLI 错误现以 `CodexLocalConfigurationFailurePolicy` 固定为
  闭合错误码与安全说明；未知配置失败降级为 `invalid_configuration`，未知命令、异常类型和无效
  `--codex` 的路径形态不再回显。AppServer `30/30`、完整 Phase 2 `351/351`、Release `0` warning /
  `0` error 和受控 doctor 握手通过；未启动 AutoCAD。详见
  `M4_CONFIGURATION_ERROR_SANITIZATION_20260723.md`。
- `AgentBootstrapLaunchException` 的公开诊断现由 `AgentBootstrapLaunchFailurePolicy` 固定：十个已知
  Bootstrap 失败只有安全 code/说明，未知值降级为 `agenthost_internal_error`，调用方传入的原始诊断、
  内部异常和 stderr 不会经 `Message`、`InnerException` 或 `ToString()` 泄露。Launcher net8/net45
  各 `38/38`、Host MVP `53/53`，双 Shell Bridge `56/56`、Phase 2 `351/351` 均通过；未启动
  AutoCAD。详见 `M4_BOOTSTRAP_ERROR_SANITIZATION_20260723.md`。
- 工作目录磁盘硬配额尚未实现：本机 Windows 10 Pro 未部署 FSRM/`SrmSvc`，卷 quota 未启用，
  也没有 VHD 预配模块；现有 Job Object 只限制进程资源。项目拒绝用目录轮询冒充硬配额，须先由
  部署提供 FSRM 目录配额或专用固定大小卷并完成实际拒绝验证。详见
  `M4_WORKSPACE_HARD_QUOTA_FEASIBILITY_20260723.md`。
- 每次生产 app-server 调用都固定附加 `-c mcp_servers={}`，以 Codex 结构化配置覆盖默认用户
  profile 的 MCP server 表。此变更的 AppServer `29/29`、AgentHost Release `0` warning / `0` error
  和真实两轮 live `2/2` 已通过；它不隔离默认用户 `CODEX_HOME`、凭据、技能或插件配置。
- 可选每会话 Codex 状态隔离已接入认证 `bootstrap-serve`：只有配置非秘密的
  `CODEX_AUTOCAD_CREDENTIAL_TARGET`，且其值是格式受限的 `CodexForAutoCAD/...` Windows Generic
  Credential 引用时，AgentHost 才读取该凭据并在私有 lease workspace 创建 `codex-home`/`codex-sqlite`。
  运行时 app-server 取得 `CODEX_HOME`、`CODEX_SQLITE_HOME` 和 token，版本预检不取得 token；没有
  引用则保持默认用户 profile 兼容路径。AppServer `29/29`、Bridge `55/55` synthetic 规格通过；未创建
  或读取真实 Credential Manager 条目，真实隔离登录与插件配置面仍未验收。
- `doctor`、`run` 和认证 `bootstrap-serve` 会先在同一受控子进程环境中运行 `codex --version`；当前
  仅接受 `>=0.144.4 <0.145.0`，本机 `0.144.4` 与其后的 app-server `initialize` 已通过。未审查
  的次版本、非 UTF-8、超限和超时输出 fail-closed，不公开路径、版本原文或 stderr。版本细节和
  升级规则见 `M4_CODEX_VERSION_PREFLIGHT_20260723.md`。
- 这没有故意耗尽真实 Codex 的进程槽或内存、测量 CPU 节流性能，也没有启动或控制 AutoCAD；
  工作目录磁盘硬配额、真实隔离登录与插件配置审查、受保护审计锚点、审批解决和 CAD 写入终态
  仍未完成。

脱敏实机范围证据：
`evidence/cad-context-v2-live-observation-20260722.json`。

这仍不是完整产品：

- `0.3.3.0` 尚未按精确哈希在 AutoCAD 2016 中 `NETLOAD`，M1 实机矩阵仍待执行。
- M2 `0.4.0.0` 尚未人工 `NETLOAD`；五种范围、无选择集 ASK、Agent 动态分页、
  1k/10k/50k 响应性、Idle 枚举器生命周期、取消、失效和退出清理均是未验证项。
- 当前选择快照仍最多 64 个实体、canonical JSON 最多 256 KiB；大图走独立索引。
- M3 精确候选尚未完成 19 类对象、`blockDetails`、复杂对象和高价值受限读取的实机字段核对。
- AutoCAD 正常退出、125%/150% DPI 和故障矩阵尚未完成。
- CAD 写入、完整 OS 沙箱、长期记忆、签名安装和企业部署尚未完成；现有 Job 资源限制只是
  M4 的一个已验证小阶段。

## 2. 当前候选身份

当前可用于下一轮 M3 实机核对的是下列 `0.4.2.0` 自动化候选：

```text
Module version: 0.4.2.0
CadContext schema: codex.autocad.cad-context/2
DrawingIndex schema: codex.autocad.drawing-index/1
CadQuery schema: codex.autocad.cad-query/1
Candidate directory:
C:\tmp\CodexForAutoCAD-m3-highvalue-limited\artifacts\autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7

Host:
Codex.AutoCAD.Host.2016.dll
SHA-256:
B5081C63DD11BD36706B529EC28C58BB1DEA22FEF6D50BA0E76C5E3E4CE67879

AgentHost:
AgentHost\Codex.AutoCAD.AgentHost.exe
SHA-256:
E3DBE95546D193D9AF451A0420E648085F9E2AF9ECCC6E956BD85BC26ACDA615

Manifest SHA-256:
2633642C2F993FC320A0662FD95D4BC900CD4A453ABCDD6B7BEB7C596EF30348
```

该候选通过 Contracts net8/net45 `87/87`、Bridge Client net45/net8 各 `29/29`、
Bridge/AgentHost `39/39`、AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2
`319/319`、benchmark `6/6`、M3 核心读取 DXF fixture `6/6`、R20.1 API 双 Shell Probe `29 passed / 8 expected failed`、
R20.1/net45/x64 Host A/B 位级一致、敏感信息扫描和候选包自身 AgentHost doctor。构建证据为
`evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7.json`。

它没有启动、重启或操作 AutoCAD，尚未按精确哈希在 AutoCAD 内人工 `NETLOAD`，因此保持
`NetLoadVerified=false`、`AutoCadLiveEvidence=false`。M2 `0.4.0.0` 候选仍是其独立
图纸索引/性能验收入口；M3 候选不继承 M2、M1 或 P1 的实机结论。

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
14. 按精确 M3 `0.4.2.0` 候选进行 `NETLOAD`，完成 19 类强类型对象、`blockDetails`、示例图资产、
    复杂对象和 8 类高价值受限类别的实机字段/降级核对。

## 7. 当前开发顺序

1. M0：已完成 P0/P1 集成、evidence/文档收拢、门禁复跑和统一候选冻结。
2. M1：代码、自动化和 `0.3.3.0` 候选冻结完成；当前只剩实机矩阵与 evidence 绑定。
3. M2-A/M2-B：图纸索引、分页命令、Codex `cad.query_drawing`、自动化和 `0.4.0.0`
   候选均完成；等待实机与性能 evidence。
4. M3：读取对象语义与覆盖的自动化候选已经冻结；中文目录、占位实际类型统计、8 类受限
   索引分类和 API Probe 不等于按精确 `0.4.2.0` 候选取得的实机逐类字段通过。
5. M4：进程树清理、进程数/内存/CPU/运行时限制、AgentHost 只读 JSONL 审计、工作区/审计
   ACL 与有界保留、Codex 子进程父环境白名单、版本/App Server 健康预检和本地审计哈希链已完成；
   继续磁盘硬配额、每会话 `CODEX_HOME`/凭据、插件配置隔离、受限令牌/AppContainer、受保护
   审计锚点和 CAD 写入终态。
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

M3 自动候选门禁已完整运行：Contracts `87/87`、Bridge Client net45/net8 各 `29/29`、
Bridge `39/39`、AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2 `319/319`。R20.1
API 双 Shell Probe 为 `29 passed / 8 expected failed`，两个 Shell 的成员集合和 Probe DLL
哈希一致；R20.1/net45/x64 Host A/B 输出也逐字节一致，Host SHA-256 为
`B5081C63DD11BD36706B529EC28C58BB1DEA22FEF6D50BA0E76C5E3E4CE67879`，Autodesk DLL 复制数
为 `0`。精确候选目录为
`artifacts/autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7/`，manifest SHA-256 为
`2633642C2F993FC320A0662FD95D4BC900CD4A453ABCDD6B7BEB7C596EF30348`；冻结记录为
`evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7.json`。
它没有启动或操作 AutoCAD，也尚未按精确哈希 `NETLOAD`；实机测试仍须由用户单独执行。

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
- `evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7.json`：
  M3 自动化冻结、候选身份和未实机边界。
- `evidence/m4-agenthost-job-resource-limits-20260723.json`：M4 AgentHost Job 进程树清理、
  进程数/总提交内存限制、双运行时 Specs 和未实机边界。
- `M4_AGENTHOST_UNEXPECTED_EXIT_CLEANUP_20260723.md`：已认证 AgentHost 自行退出、启动器仍存活时的
  retained-Job 清理、自动重试与明确未覆盖边界。
- `evidence/m4-agenthost-unexpected-exit-cleanup-20260723.json`：上述 synthetic 进程树回收的
  脱敏门禁摘要；不等于 AutoCAD 异常退出实机验证。
- `M4_RUNTIME_AUDIT_BASELINE_20260723.md`：M4 AgentHost 只读 JSONL 审计契约、脱敏字段、
  fail-closed 行为、自动化证据和未完成边界。
- `evidence/m4-agenthost-runtime-audit-20260723.json`：M4 只读运行审计的脱敏结构、门禁结果和
  未实机/未写入边界。
- `M4_AUDIT_HASH_CHAIN_20260723.md`：M4 审计 `/2` canonical SHA-256 链、验证范围与不能宣称的
  外部不可篡改边界。
- `evidence/m4-agenthost-audit-hash-chain-20260723.json`：哈希链切口的构建、规格和脱敏证据。
- `M4_CODEX_CHILD_ENVIRONMENT_ALLOWLIST_20260723.md`：M4 Codex 子进程父环境白名单、变量用途和
  默认用户登录兼容边界。
- `evidence/m4-codex-child-environment-allowlist-20260723.json`：M4 环境隔离规格、真实 doctor/live
  和进程清理的脱敏证据。
- `M4_PRIVATE_STORAGE_RETENTION_20260723.md`：M4 workspace/audit 私有 ACL、lease、保留和
  不跟随重解析点的清理策略。
- `evidence/m4-agenthost-private-storage-retention-20260723.json`：双 Shell 门禁、真实 Codex live、
  ACL 观察和工作区删除的脱敏证据。
- `M4_CODEX_VERSION_PREFLIGHT_20260723.md`：M4 Codex 版本范围、严格预检、App Server 健康顺序和
  升级规则。
- `evidence/m4-codex-version-preflight-20260723.json`：版本预检、真实 doctor/live、双 Shell 门禁和
  未完成安全边界的脱敏证据。
- `M4_EMPTY_MCP_BOUNDARY_20260723.md`：生产 app-server 的默认空 MCP 配置覆盖及明确未完成边界。
- `evidence/m4-codex-empty-mcp-boundary-20260723.json`：空 MCP 规格、Release、真实两轮 live 和
  非 AutoCAD 范围的脱敏证据。
- `M4_CODEX_SESSION_ISOLATION_20260723.md`：可选每会话 Codex 状态、Windows Generic Credential
  引用、失败关闭、默认兼容与未完成的真实验证边界。
- `evidence/m4-codex-session-isolation-20260723.json`：隔离目录/凭据 synthetic 规格和非实机范围的
  脱敏证据。
- `M4_CONFIGURATION_ERROR_SANITIZATION_20260723.md`：本地 Codex 配置与 AgentHost CLI 的固定错误码、
  安全说明和非 AutoCAD 验证边界。
- `evidence/m4-configuration-error-sanitization-20260723.json`：上述配置/CLI 诊断切口的脱敏门禁摘要。
- `M4_BOOTSTRAP_ERROR_SANITIZATION_20260723.md`：AgentHost Bootstrap 公开异常的固定 code/安全说明、
  未知值归一化和非 AutoCAD 验证边界。
- `evidence/m4-bootstrap-error-sanitization-20260723.json`：上述 Bootstrap 诊断脱敏的 Launcher、Host MVP
  与双 Shell 回归记录。
- `M4_WORKSPACE_HARD_QUOTA_FEASIBILITY_20260723.md`：工作目录硬配额的本机能力审计、禁止的伪方案与
  部署前置条件。
- `evidence/m4-workspace-hard-quota-feasibility-20260723.json`：上述审计的脱敏能力记录，明确硬配额未完成。

## 11. 支持声明

当前可以准确表述为：

> AutoCAD 2016 R20.1 已实机跑通 CadContextJson v2 的只读选择、Palette、本机 Codex 和
> 两轮连续对话基线；50 对象混合选区中的未知对象不会中断发布。M1 `0.3.3.0` 已完成
> 只读稳定化代码与自动化冻结。M2 `0.4.0.0` 已实现独立 DrawingIndex/CadQuery、Idle
> 分片、本地分页命令和 Codex `cad.query_drawing` 认证反向查询；确定性 1k/10k/50k
> fixture、性能遥测和脱敏记录器已经通过自动化，但尚未完成 AutoCAD 实机性能验证。
> M3 `0.4.2.0` 已将受限块属性、动态块、嵌套块、布局和安全 Xref 元数据接入整图只读
> 查询，并将 8 类高价值对象接为 `data_limited` 的索引分类；尚未按精确候选 `NETLOAD`
> 或取得逐类实机字段/降级证据。
> M4 已为 AgentHost/Codex Job 进程树应用清理与 CPU/内存/时间边界，将内容脱敏的只读运行
> 审计接入真实 AgentHost 会话，并为 workspace/audit 启用受保护 ACL 与有界保留，为 Codex
> 子进程启用固定父环境白名单、默认空 MCP 及 `>=0.144.4 <0.145.0` 的版本/App Server 健康预检；
> 认证 `bootstrap-serve` 已有可选每会话 `CODEX_HOME`/Windows Generic Credential 路径，默认仍保持
> 用户 profile 兼容模式。Bridge/运行时/Host/本地配置/Bootstrap 公开诊断均已收敛为固定安全说明；
> 审计 `/2` 已有本地 canonical SHA-256 链，但没有签名、远端锚定或 WORM
> 存储。真实隔离登录、插件配置隔离、磁盘硬配额、其余沙箱、受保护审计锚点和 CAD 写入终态仍未完成。
> 安全 CAD 写入、完整沙箱、长期记忆和发布安装
> 尚未完成。

不得表述为完整支持 AutoCAD 2016，也不得表述为已经支持安全 CAD 写入。

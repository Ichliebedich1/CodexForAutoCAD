# Codex for AutoCAD 2016：先读这里

最后更新：2026-07-26（北京时间）

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
- request_id、回合状态、取消、覆盖 Provider 启动阶段的 10 分钟总超时和唯一终态由 Host 管理。
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

M3 `0.4.2.0` 已形成 source-bound 自动化冻结候选：

- 选择快照、整图索引、Palette 和 `CODEX16CTXINFO` / `CODEX16INDEXINFO` 会按实际类型
  显示未支持、数据超限和读取失败对象的数量；统计不带图层、Handle、路径或对象内容。
- `CODEX16TYPEINFO` 显示 19 类现有强类型对象的中文名称与人工创建入口。
- `BlockReference` 的受限 `blockDetails` 已贯通 DrawingIndex、CadQuery、认证 Bridge 和
  Agent 工具：属性/动态属性、嵌套块计数与深度、布局标志和安全 Xref 布尔元数据均有上限。
  外部 Xref 定义和真实路径不会读取或传播，详情会降级为 `limited`。
- Region、Solid、Mesh、Surface、RasterImage、Underlay、Proxy 和 Wipeout 在
  DrawingIndex 中作为可查询的 `data_limited` 安全类别，不修改 CadContextJson v2。
- 自动门禁已通过 Contracts `96/96`、Bridge Client net45/net8 各 `30/30`、Bridge `39/39`、
  AgentRuntime `34/34`、Host MVP `54/54`、完整 Phase 2 `323/323`；R20.1 API 双 Shell
  Probe 为 `29 passed / 8 expected failed`，目标 R20.1/net45/x64 Host A/B 输出逐字节一致，
  当前 Host SHA-256 为
  `467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D`，且 Autodesk DLL
  复制数为 `0`。
- 核心读取 DXF fixture 双次生成、独立解析和哈希门禁为 `6/6`。
- 中文字段核对目录见 `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`；它不替代脱敏
  AutoCAD 测试图、R20.1 Probe 或实机逐类字段证据。当前候选尚未 `NETLOAD`。

M4 当前只形成自动化集成检查点，不是安全候选：

- `codex/m4-integration@0763022` 已接入 Codex stderr 无内容摘要、本地固定
  `codex.exe` 配置/健康检查、可选 session `CODEX_HOME`、显式环境白名单、
  `KILL_ON_JOB_CLOSE` 进程树边界和 internal-only RestrictedToken 能力探针。
- M4.4/M4.5 已收回公共实验身份入口并禁止 CurrentUser 回退。本机探针只得到原语
  `available`、认证前 `child_exited`，不能解释为生产受限身份成功。
- 未提交 M4.6/M4.8/M4.9 切口已增加 Job 成员检测、分配后反查、当前 Windows 嵌套 Job 验证、
  连续 `500` 次 service 启停回收、受检的 `0–30 s` 停止宽限，以及进程数、Job 内存、累计
  用户时间和服务墙钟的稳定结构化终态，以及 Job 内存/用户时间组合耗尽；已提交 M4.6–M4.9
  检查点为 `15352ff`。当前未提交 M4.11 配置/读取切口的 AgentLauncher
  net45/net8 各 `60/60`，Host MVP
  `56/56`，双 Shell Phase 2 均为 `360/360`，Bridge `49/49`、认证兼容 net45/net8
  各 `35/35`，Release
  `0 warning / 0 error`；阶段证据固定写入
  `evidence/agent-bootstrap-verification-20260719.json`，其最终 SHA-256 由门禁输出记录，
  不在受该证据 manifest 约束的文档中反向固化。
  企业嵌套 Job 策略矩阵仍未验证。R20.1 API Probe 为
  `29 passed / 8 expected failed`、Autodesk DLL 复制数 `0`。
- 版本硬门槛和健康预检已进入正式调用链；`6d99bb9` 已完成显式环境白名单、可选每会话
  `CODEX_HOME`、空 MCP/插件及租约基础，但生产默认未启用。M4.8 自动化切口已将每会话
  `workspace`、`audit`、`codex-home`、schema marker 和活动 lease 接入真实启动/停止链，使用
  受保护最小 ACL、拒绝 reparse 根，并支持过期崩溃恢复；企业/AutoCAD 实机矩阵和独立 Git
  提交仍缺。working set 已明确只做性能 telemetry，Job 总提交内存是硬边界。真实
  Codex/AutoCAD 配额矩阵和磁盘硬配额仍未完成。M4.11 已完成默认禁用配置、产品专属
  Credential Manager target、有界二进制读取、Dispose 清零、认证传输、隔离 home 和 fake
  stdin 登录失败矩阵；真实 Credential Manager/Codex keyring、受限身份全链仍缺。M4.13 已将
  audit/2 哈希链、独立持久 segments/anchors、STOP 后保留和自动轮转接入生产 AgentHost；
  只读 AgentHostAuditCatalog 已在真实受保护根上完成 complete、incomplete、corrupt、
  `anchor_mismatch` 四态分类；只有 session 终态链是 complete，无终态崩溃前缀标为
  `incomplete/session_not_terminal` 并禁止导出，临时 anchor 和身份不一致均保守报告、不自动修复。
  Bridge 80/80、
  双 Shell Phase 2 416/416、bootstrap net8/net45 各 63/63。受控 `audit-export --session <id>`
  固定读取当前用户受保护根，只导出完整会话；不接受任意路径或输出文件，先在内存中完成验链
  和脱敏 JSON，失败不产生半份输出。只读 `audit-retention-plan` 已能按显式年龄/容量/最低保留
  策略生成不含路径的候选计划；非完整证据固定人工复核，未知文件计入容量但不自动清理。显式
  `audit-retention-apply --plan <id>` 已接入：执行前重新验链和重算计划，在受保护 control 目录
  先提交全计划耐久 journal，再逐文件复验 SHA-256 后删除；支持中断恢复、完成 receipt 幂等、并发
  排他和篡改失败关闭。测试专用子进程已在 journal 提交并删除首个 anchor 后被强杀，随后由新
  租约使用原 plan ID 完成恢复且无残留工作器。已知 control artifact 也已实现有界收敛：保留
  最近 256 份 receipt，更旧 receipt 在删除前逐份耐久折叠到固定累计链检查点；检查点中断恢复
  不重复累计，已完成计划的冗余 foreign temp 可清除。它不是后台自动清理；默认保留策略、
  系统断电、真实生产 AgentHost/AutoCAD 异常退出、未知/恶意 control artifact 的企业归档流程、
  签名/HMAC 与企业故障矩阵仍未完成。M4.14 已完成 Contracts 统一 sanitizer、Bridge 公开异常/反向图纸
  查询错误响应、AppServer RPC/data/通用异常与显式分类、AgentHost 未知命令、设备路径/转义
  JSON/URI 变体、嵌套异常图、AgentLauncher bootstrap 失败纵切，以及 AgentHost
  `doctor`/`run` 成功响应的环境指纹收口；stderr 仍为无文本摘要。
  Bridge 服务端与客户端远端异常现在还会保留合法稳定错误码、归一非法错误码，并只公开
  来源分类和数值脱敏证据；原始 message/code/inner exception 不能从公共异常旁路外逃。
  Host.2016 Palette/Bridge 断线与 CadQuery 命令行公共错误已统一经过最外层 sanitizer；
  DrawingIndex 启动、CadQuery 和 CadQuery 下一页的通用 catch 也不再输出 CLR 类型名，而是
  返回稳定 code/stage、分类和数值脱敏标志。Host MVP 为 59/59；AppServer `ProtocolFaulted`
  也只公开固定消息安全快照、分类和数值
  脱敏标志，不再保留任意原始异常。AgentHost `doctor/run` 通用失败、协议故障 stderr 和
  bootstrap CLI 错误也已改为稳定错误码、阶段、分类与数值脱敏元数据，不再输出 CLR 类型名。
  AppServer 服务端请求失败响应已在唯一 `WriteErrorAsync` 出站边界统一脱敏：保留 RPC
  数值 code，丢弃处理器提供的原始 data，只回传安全 message、分类、数值脱敏标志和
  data-presence；专项现为 `43/43`。三个 AgentHost 审计 CLI 命令也已增加共同最外层失败边界，
  未预期异常只输出固定错误码、阶段、分类和数值脱敏标志，已有非法参数、预期拒绝和闭集
  ReasonCode 不变。AppServer Client/transport 的 stderr 摘要观察者已逐项隔离；AgentRuntime
  projection/observer 公共诊断不再保留原始异常图，失败 turn 只保留最小安全快照，observer
  失败也不再持有原始 Agent 事件；动态工具校验错误在进入事件和回传 Codex 前统一按
  `RemoteError` 脱敏，AgentRuntime 专项现为 `39/39`。Bridge 公共
  `Completion`/`TerminalError` 只保留固定 `BridgeTerminalException` 安全快照。配置请求、
  AppServer 启动配置、AgentRuntime options/handle/input 和 Bridge request/notification 的
  record 字符串也已改为只输出存在性/数量摘要，不再展开路径、完整 PATH、环境、参数、提示词、
  Provider 标识、schema 或 `BodyJson`。AppServer initialize response、notification、server
  request、RPC error、request resolution、turn interrupt 和 approval event 包装器也不再展开
  CodexHome、Provider ID、method、JSON、错误正文、任意 result 或审批 payload；真实属性与
  wire JSON 保持不变。AgentRuntime 的 turn handle、item snapshot、消息增量、工具、
  turn/review、CAD proposal/rejection 和审批事件字符串也只报告类型、枚举和存在性，不再输出
  Provider IDs、回复内容、工具 JSON、错误正文或审批 payload；事件字段与 UI 消费路径保持
  不变。AppServer 四类审批请求、嵌套权限/网络/文件系统模型、审批响应、CAD 文档身份、
  变更摘要和预览对象的字符串也已改为只报告类型、存在性、枚举和数量；命令、工作目录、
  授权路径、Provider ID、理由、策略修订和预览 JSON 不再被默认日志展开，wire JSON 不变。
  AppServer initialize 请求侧的 client info/capabilities/params 也只报告配置存在性、布尔能力
  和数量；AgentRuntime 的 CAD 点、`create_line` 提案、批次和 Broker 结果也不再通过默认
  record 字符串展开坐标、图层、Provider IDs 或结果正文。两处只改变诊断 `ToString()`，实际
  wire、属性、解析和 Broker 结果不变；AppServer 为 `45/45`，双 Shell Phase 2 为 `416/416`。
  AgentHost audit 内部异常链已证明没有生产公共外逃路径，
  因而保留内部归因能力而不做无证据重构。当前
  `Replace`/`Sanitize`
  静态复核未发现另一套诊断清洗器；AgentRuntime、Bridge、Host、AgentHost 审计导出/保留、
  CLI JSON、Doctor/Run、Host BuildInfo、DrawingIndex/CadQuery 和剩余公共 record/EventArgs
  字符串出口也已复核，未发现新的可复现公共泄漏。M4.14 的代码、自动化和静态公共出口审计
  已收口；真实 Codex/AutoCAD、组策略、EDR、受限账户和系统断电矩阵属于 M4.15。
  M4.15.1 已将 Windows/企业策略阻止 AgentHost 启动映射为稳定、不可自动重试且脱敏的
  `agenthost_process_start_blocked`，并保留 RestrictedToken 隔离失败的独立语义；这只是
  自动化分类与正式调用链证据，不是 AppLocker、WDAC、EDR/杀毒或企业组策略实机通过。
  M4.15.2a 又将父 Job 环境中的嵌套分配拒绝映射为
  `agenthost_nested_job_assignment_failed`；失败后不允许无 Job 回退，Host 只显示脱敏的
  父 Job/进程隔离检查提示。当前 Windows 正向嵌套分配已验证，但真实不可嵌套企业父 Job
  尚未验证。
  M4.15.3a 已把 AgentHost 根进程意外退出从泛化 Bridge 断线中分离为
  `agenthost_unexpected_exit`；正常 STOP 和资源限制不会误报为崩溃，资源终态在竞态中优先，
  活动请求只提交一个 `failed` 且后续 ASK fail-closed。当前 Launcher net8/net45 各
  `65/65`、Host MVP `60/60`、双 Shell Phase 2 `417/417`；R20.1 Host A/B 五文件逐字节
  一致，Host SHA-256 为
  `DA5C6D100E4B8CEDCEEB1C4389E09A77667F6879C05A64EF4EC1A0EF43275255`，Autodesk DLL
  复制数为 `0`。真实 Codex/AgentHost/AutoCAD 强杀仍未验证。
  M4.15.3b 又让 STOP/AutoCAD 退出清理主动取消正在进行的 AgentHost 启动，预期中断不误报
  “启动失败”、不能上线且只发布一个停止终态。当前 Host MVP `61/61`、双 Shell Phase 2
  `418/418`；最新 R20.1 Host SHA-256 为
  `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`。真实分阶段启动中断
  仍未验证。
  M4.15.5a 进一步让 `audit-retention-plan` 输出无路径 `controlStatus`：合法中断状态为
  `recovery_required`，未知/危险/无效控制 artifact 为 `manual_review_required`；执行器持锁复检
  后拒绝未知或危险控制区，不删除原证据。当前 Bridge `81/81`、双 Shell Phase 2 `419/419`，
  bootstrap net8/net45 各 `65/65`；真实磁盘满、断电、企业默认保留和人工归档仍未验证。
  M4.15.5b 又增加 synthetic 持久化 I/O 故障夹具：审计流/锚点失败后永久 fail-closed，Bridge
  会话终止且不补写第二终态；retention 在 journal/receipt/checkpoint 提交边界统一返回稳定
  `cleanup_failed`，journal 提交前不删 artifact，提交后可恢复，同一 plan ID 只收敛一次。当前
  Bridge `83/83`、双 Shell Phase 2 `421/421`，最新 AgentHost SHA-256 为
  `780D3CD57786CC624D8A033B2069E41095F7119EE4E695110D7E94E8CCB399D2`。这些自动化不等同于
  真实磁盘满、卷离线或断电。
  M4.15.6 已把当前自动化证据收口成机器可读绑定：双 Shell Phase 2 仍为 `421/421`，R20.1
  Host A/B 五文件逐字节一致、0 warning/0 error、Autodesk DLL 复制数 `0`，Host SHA-256 为
  `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`；AgentHost SHA-256
  仍为上述 `780D3CD...`。readiness 汇总器在 PowerShell 7/5.1 自检和正式运行中均通过，
  输出语义等价且不含本机路径、原始 PATH、环境内容或凭据。状态明确为
  `automated_readiness_only`，真实凭据/受限身份/磁盘满/断电/异常退出/企业执行控制与归档
  全部仍未验证，M4 和 M4.16 均未完成。
  生产凭据 Broker 完成前隔离
  codex-home 不得启用。
- M4.9 结构化终态和 working-set 决策见
  `M4_9_RESOURCE_LIMIT_TERMINALS_20260724.md`。
- M4.11 当前配置/读取边界见
  `M4_11_CREDENTIAL_BROKER_BOUNDARY_20260725.md`。
- M4.13 保留计划、显式清理、journal/receipt 和恢复边界见
  `M4_13_AUDIT_RETENTION_CLEANUP_20260725.md`。
- M4.14 统一诊断脱敏已完成调用链和剩余边界见
  `M4_14_DIAGNOSTIC_SANITIZATION_20260725.md`。
- M4.15.1 企业策略阻止启动的分类、UI 提示和证据边界见
  `M4_15_ENTERPRISE_POLICY_FAILURE_20260726.md`。
- M4.15.2a 嵌套 Job 分配拒绝的分类、无回退和证据边界见
  `M4_15_NESTED_JOB_FAILURE_20260726.md`。
- M4.15.3a AgentHost 意外退出、竞态和唯一终态边界见
  `M4_15_AGENTHOST_UNEXPECTED_EXIT_20260726.md`。
- M4.15.3b STOP/退出主动取消启动的边界见
  `M4_15_STARTUP_INTERRUPTION_20260726.md`。
- M4.15.5a retention control 人工复核状态和 fail-closed 边界见
  `M4_15_RETENTION_CONTROL_REVIEW_20260726.md`。
- M4.15.5b 持久化 I/O 故障、恢复与单次收敛边界见
  `M4_15_PERSISTENCE_IO_FAILURE_20260726.md`。
- M4.15.6 自动化 readiness 绑定、运行命令和明确未验证矩阵见
  `M4_15_AUTOMATED_READINESS_20260726.md`。
- M4.4/M4.5 证据和边界见
  `M4_4_M4_5_RESTRICTED_IDENTITY_PROBE_20260724.md`。
- M4.16 完成前 CAD 写入继续禁用，M5 不得进入产品调用链。

脱敏实机范围证据：
`evidence/cad-context-v2-live-observation-20260722.json`。

这仍不是完整产品：

- `0.3.3.0` 尚未按精确哈希在 AutoCAD 2016 中 `NETLOAD`，M1 实机矩阵仍待执行。
- M2 `0.4.0.0` 尚未人工 `NETLOAD`；五种范围、无选择集 ASK、Agent 动态分页、
  1k/10k/50k 响应性、50k preparation Idle 分片、取消、失效和退出清理均是未验证项。
- 当前选择快照仍最多 64 个实体、canonical JSON 最多 256 KiB；大图走独立索引。
- 19 类对象尚未逐类完成字段实机核对。
- AutoCAD 正常退出、125%/150% DPI 和故障矩阵尚未完成。
- CAD 写入、完整 OS 沙箱、长期记忆、签名安装和企业部署尚未完成。

## 2. 当前候选身份

当前最新的正式 source-bound 自动化候选是 M3 `0.4.2.0`：

```text
Source commit: 00fe879a0ac056fab48c955e71d63c51ef3577d9
Candidate ID:
autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc
Host SHA-256:
467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D
AgentHost SHA-256:
44CD544883F7BA7B790044220FAE3C5DDD2515C589CE3CC6910260F6C6795EF5
Manifest SHA-256:
02B5AE218CAFC19892F7CF086330D46EB237131A67BA61700D644E6A7E74D520
```

M4 集成分支从 `25c373d` 生成的 `5DB1497A...` AgentHost 仅是回归产物，没有
M4.16 候选身份，也不得替代上述 M3 正式候选。M2 性能实机仍使用以下
M2-A/M2-B 加基准资产和性能遥测候选：

```text
Module version: 0.4.0.0
Source commit: 34cef1214ad22822996db4e4ad33013f855751e3
CadContext schema: codex.autocad.cad-context/2
DrawingIndex schema: codex.autocad.drawing-index/1
CadQuery schema: codex.autocad.cad-query/1
Candidate directory:
C:\tmp\CodexForAutoCAD-m2-integration\artifacts\autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024

Host:
Codex.AutoCAD.Host.2016.dll
SHA-256:
BC6011D3C0C00222BE266E27A26770B87FC4CE542A9516640AEC1A959950C5D5

AgentHost:
AgentHost\Codex.AutoCAD.AgentHost.exe
SHA-256:
6DE30DB91C466CA0CA87E6202926FB893165CE8950B1CCAB9E0E3C49650CDD89

Manifest SHA-256:
CDE0E31D9B2342B322D1850224B6DE78755B97EAEF7802C7D609F86E58E7D917
```

该候选通过 Contracts net8/net45 `88/88`、Bridge Client net8/net45 `29/29`、
Bridge/AgentHost `39/39`、AgentRuntime `34/34`、Host MVP `54/54`、完整 Phase 2
`314/314`、benchmark `6/6`、30 文件 Host.2016 只读 Compile 闭包、R20.1/net45/x64 双构建位级一致、
敏感信息扫描和候选包自身 AgentHost doctor。构建证据为
`evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024.json`。

查询结果中的对象身份是 `obj-########` 不透明令牌，不是 AutoCAD Handle。分页使用 Host
随机生成的 `dq1_...` 游标；游标五分钟过期，并绑定索引、文档 revision、查询形状和 offset。
旧 `E85D97EC...` 与 `597A7A3D...` 候选仅供追溯，均不得用于当前实机测试。

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
5. M4：M4.14 统一诊断脱敏已收口；M4.15.1 进程策略阻止、M4.15.2a 嵌套 Job 拒绝、
   M4.15.3a AgentHost 意外退出、M4.15.3b 启动中断主动取消和 M4.15.5a/b retention control
   人工复核及持久化 I/O 故障纵切已完成，继续推进真实企业父 Job、真实 Codex/AutoCAD 强杀与启动中断、
   受限账户、EDR、磁盘满、系统断电和企业归档矩阵；M4 整体仍未完成。
6. M5：AutoCAD 2016 `create_line` 安全写入最小闭环。
7. 后续阶段见 `LONG_TERM_MEMORY_TODO.md`。

## 8. 构建与自动化边界

M2 `0.4.0.0` 候选已重跑以下门禁：

- Contracts net8/net45：`88/88`。
- Bridge Client net8/net45：`29/29`。
- Bridge/AgentHost：`39/39`；AgentRuntime：`34/34`；Host MVP：`54/54`。
- 完整 Phase 2：`314/314`；benchmark fixture/evidence：`6/6`。
- R20.1 Host Release：0 warning / 0 error。
- Host.2016 真实 Compile 闭包：30 个源文件，CAD 写入/命令/保存 API 扫描通过。
- R20.1/net45/x64 A/B 输出位级一致。
- Host 禁止 API、秘密扫描、diff 和候选包自身 AgentHost doctor。

这些门禁不替代 AutoCAD 2016 人工 `NETLOAD`。历史 `0.3.2.0` 实机结果也不能自动证明
新的 `0.4.0.0` 候选，更不能证明 50k 运行时性能。

M3 当前自动门禁已完整运行：Contracts `96/96`、Bridge Client net45/net8 各 `30/30`、
Bridge `39/39`、AgentRuntime `34/34`、Host MVP `54/54`、完整 Phase 2 `323/323`。R20.1
API 双 Shell Probe 为 `29 passed / 8 expected failed`，两个 Shell 的成员集合和 Probe DLL
哈希一致；R20.1/net45/x64 Host A/B 输出也逐字节一致，Host SHA-256 为
`467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D`，Autodesk DLL 复制数
为 `0`。核心读取 DXF fixture 门禁为 `6/6`。source-bound 候选 ID 为
`autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc`，源码提交为
`00fe879a0ac056fab48c955e71d63c51ef3577d9`；候选 evidence 见
`evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc.json`。
这些自动化记录尚未由 AutoCAD `NETLOAD`、19 类字段矩阵或复杂块/Xref 实机证据补全。

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
- `evidence/cad-context-v2-candidate-build-autocad2016-m1-readonly-v033-e6701a77-4b602965-561c6af3.json`：
  M1 `0.3.3.0` 自动化冻结、候选身份和未实机边界。
- `M1_READONLY_STABILITY_RUNTIME_TEST_20260722.md`：M1 唯一当前实机测试入口。
- `M2_DRAWING_INDEX_VERTICAL_SLICE_20260722.md`：M2-A/M2-B 架构、契约与边界。
- `M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`：M2 唯一实机测试入口。
- `M2_DRAWING_INDEX_BENCHMARK_FIXTURES_20260722.md`：固定三档性能图生成、哈希和脱敏记录。
- `evidence/m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024.json`：
  M2 自动化冻结、候选身份和未实机边界。
- `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`：M3 中文对象目录、字段核对模板和边界。
- `evidence/v2-api-surface-probe-m3-cross-shell-20260723.json`：M3 块读取所需 R20.1 API 的
  双 Shell 脱敏 Probe 结果，不等于 AutoCAD 实机验证。
- `evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc.json`：
  M3 `0.4.2.0` source-bound 自动化冻结、哈希、门禁和未实机边界。

## 11. 支持声明

当前可以准确表述为：

> AutoCAD 2016 R20.1 已实机跑通 CadContextJson v2 的只读选择、Palette、本机 Codex 和
> 两轮连续对话基线；50 对象混合选区中的未知对象不会中断发布。M1 `0.3.3.0` 已完成
> 只读稳定化代码与自动化冻结。M2 `0.4.0.0` 已实现独立 DrawingIndex/CadQuery、Idle
> 分片、本地分页命令和 Codex `cad.query_drawing` 认证反向查询；确定性 1k/10k/50k
> fixture、性能遥测和脱敏记录器已经通过自动化。Autodesk 枚举器生命周期已收口到单个
> transaction，但 50k preparation 最大 Idle 分片和 AutoCAD 实机性能仍未验证，因此
> M2.3、M2.13、M2.14 尚未完成。
> M3 `0.4.2.0` 已冻结 source-bound 自动化候选：placeholder 类型统计、中文对象目录、
> 有界块详情、8 类高价值 `data_limited` 降级和核心 DXF fixture 均进入真实门禁；尚未
> 取得 AutoCAD `NETLOAD`、19 类逐类字段及复杂块/Xref 实机证据。
> 安全 CAD 写入、完整沙箱、长期记忆和发布安装
> 尚未完成。

不得表述为完整支持 AutoCAD 2016，也不得表述为已经支持安全 CAD 写入。

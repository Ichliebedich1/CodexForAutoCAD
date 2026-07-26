# AutoCAD 2016 当前状态索引

最后更新：2026-07-25（北京时间）

本文件是项目的长期“当前状态索引”。它不替代 `README_FIRST.md`、
`COMPANY_PC_RUNBOOK.md`、测试报告、证据 JSON 或 Git 历史；只把当前成立的结论、
证据边界、活动阶段和待验证队列集中在一处。

## 证据使用顺序

1. 真实 AutoCAD 命令记录与冻结候选 SHA-256。
2. 对应阶段的验证脚本输出和脱敏 evidence JSON。
3. 已验证后单独产生的 Git 提交。
4. 本索引及交接文档中的摘要。

若摘要与原始证据冲突，以更具体、更新且可复现的原始证据为准。没有真实编译和
NETLOAD 证据的能力一律视为未支持。

## 当前活动快照（2026-07-25）

- P0 `codex/bridge-client-net45` 的 0.3.2 停止生命周期候选已由用户在 AutoCAD 2016
  中完成人工启停、重复 STOP、DBMOD 和残留检查；独立提交为 `8a4ee57`，实机证据为
  `agent-stop-live-observation-20260722.json`，该证据不继承给 P1。
- P1 `codex/cad-context-v2` 已在隔离 Worktree 完成 CadContextJson v2 的产品 Runtime、
  Palette、Bridge/AgentHost v2 测试接入；v1 固定向量未修改，源码级回归与托管门禁为
  `259/259`，R20.1 Host 编译和双 Shell v2 API Probe 证据已保留。
- P1 当前明确支持的协议标识为 `codex.autocad.cad-context/2`，Agent 回合方法为
  `agent.turn.start.v2`；未知/读取失败/超限对象使用受限占位，并通过
  `entityCount`、`parsedEntityCount`、`unsupportedEntityCount`、`complete` 表达完整性。
- P1 冻结候选现已取得 AutoCAD 2016 live 基线：模块版本 `0.3.2.0`、Doctor v2、
  100% DPI Palette、50 对象混合选区、6 个受限 placeholder、真实 v2 Codex 两轮对话、
  显式清除和文档激活清除均由用户实测。混合选区为 `selected=50`、
  `jsonBytes=23142`、`DBMOD 21 -> 21`；未知对象没有中断整体发布。
- 该 live 基线的脱敏范围证据为
  `evidence/cad-context-v2-live-observation-20260722.json`。它不证明全部 19 类对象已逐类
  核对，也不证明 AutoCAD 正常退出、125%/150% DPI、Bridge 断线、请求超时或取消。
- P1 候选：`artifacts/autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30/`；
  Host SHA-256 `0D72EDC38A30E7BF33AAEE4DCB1D50D341C4C883146677537C4BB5E7551D0AD7`，
  AgentHost EXE SHA-256 `10BEA363AC80C856FA513F4312B60410DB62BBF4917CE634B589CBA59DA65442`，
  manifest SHA-256 `A16831703985906F724B8EB93BDB0BC801A5781A3228F0694CB1A20A4AC5960F`。
- M0 已在合并提交 `e66ef1e` 和构建稳定化提交 `c96e9a3` 上冻结统一自动化候选
  `artifacts/autocad2016-mvp-context-v2-v032-37c1953d-ab1ce675-8926ed54/`。Host SHA-256
  为 `37C1953D9AD996F9892486300295E69043F8E020D506E0683FC1301F8FC4C532`，AgentHost EXE
  为 `AB1CE675EF48947F670E0A4FC013E09108AF9A91D5D14F49874039F42018CD3A`，manifest 为
  `FF11069F766A055D3F2DEA7D9D320CB1B4A5D874260FB4E47EE083D42E12F8BD`。
- 该 M0 候选已从精确提交通过 Phase 2 `259/259`、Host MVP `24/24`、R20.1 A/B、双 Shell
  API Probe、真实本机 Codex v2 两轮 `2/2`、manifest 和候选 doctor。其精确哈希尚未在
  AutoCAD 内 NETLOAD，不能把 P1 的实机哈希绑定自动迁移过来；边界见
  `evidence/m0-baseline-verification-20260722.json`。
- 候选包内 AgentHost 已单独运行 `doctor` 并完成本机 Codex app-server 初始化；脱敏证据见
  `evidence/cad-context-v2-candidate-package-doctor-20260722-refresh.json`。该证据仍不替代
  AutoCAD `NETLOAD` 或真实 v2 对话。
- P1 候选 AgentHost 已通过真实本机 Codex v2 live 规格：认证 capability 明确包含
  `agent.turn.start.v2` 与 `codex.autocad.cad-context/2`，同一 thread 使用两份合成 Line v2
  上下文完成两轮回答，上下文哈希在接收响应和 assistant 事件中一致回显，停止后残留
  AgentHost 为 `0`。最新证据见 `evidence/agenthost-v2-live-two-turns-20260722-refresh.json`；该规格没有
  启动 AutoCAD，因此仍不替代 P1 `NETLOAD`、真实选区和 Palette v2 实机证据。
- 采集器集成和退出清理重试回归后当前线完整 Phase 2 门禁通过 `259/259`、Release `0/0`、
  Host 禁用 API、doctor、秘密扫描和 diff；新候选构建证据为
  `evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30.json`。
- `MvpAgentRuntime.Terminate()` 现在最多执行两次有界清理；首次同步/异步失败或空任务时
  自动重试，连续失败才报告脱敏错误。退出清理自动化为 `24/24`，证据见
  `evidence/host2016-terminate-exit-retry-20260722.json`。
- 本轮已修正 P1 Host 的 Doctor 和 `CODEXCAD` 命令文案，使其显示 v2，不改变 v1 契约或
  历史验证记录。
- Host 的 v2 能力判定已抽成独立 fail-closed 策略，并增加 `6/6` 回归：只有同时声明
  `agent.turn.start.v2` 与 `codex.autocad.cad-context/2` 才接受；null、空 schema、缺方法或
  只有 v1 schema 均拒绝。目标机 R20.1 net45/x64 Release 复编译为 0 错误。
- M0 已在独立 `codex/m0-baseline` Worktree 中完成 P1、P0 和主分支文档的受控整合、
  自动化复验和候选冻结；本地 `main` 已安全快进并吸收冻结提交 `4833e76`。主工作树中的
  Host.2025 UI/选择/写入原型不属于本阶段，仍保留且未被清理、覆盖或误提交；远端尚未
  因本次收尾自动推送。
- 生命周期审计补上了一个窄边界：`MvpAgentRuntime` 保存上下文引用后，在 Agent 启动完成
  以及发送 v2 turn 前都会执行 generation/reference fail-closed 重校验；文档切换或清除
  若在竞态窗口发生，旧上下文会被拒绝。文档激活清缓存已实测，但用户在 post-switch
  `CODEX16ASK` 提示阶段取消，没有真正提交问题，因此实际发送拒绝仍未验证。
- `CODEX16CTXCLEAR` 当前只清除 CAD 上下文，不创建新 Codex thread；用户观察到清除后
  仍记得旧对话标记。这不是 CAD 缓存未清，而是产品尚未提供“新建对话/清除全部”的明确
  语义，列入 M1。
- 当前 v2 选择快照仍有 `64` 实体和 `256 KiB` canonical JSON 硬上限。用户已明确要求
  整图数量级支持；M2 将保留 v2 兼容快照，新增 DrawingIndex、分页和按需 CadQuery，
  不通过简单放大常量实现。
- M1 已冻结包含启动阶段总超时修复的 `0.3.3.0` 候选：
  `C:\tmp\CodexForAutoCAD-m1-readonly-stability\artifacts\autocad2016-m1-readonly-v033-e6701a77-7a3abcea-ed93a77c`。
  Host SHA-256 为 `E6701A771D17EC3EC8B2CA7DA78B553E27897639DC48B3BC0435F07249C9B5F6`，
  AgentHost SHA-256 为 `7A3ABCEABA0E590839DEC344FA68755A213D8716CDA777EC9D891EABB055E50D`，
  manifest SHA-256 为 `AFB4016B0E8941187C3EC324AB8732B7D724A68A61886E787C5DA5732CDEC767`。
  自动化为 Host MVP `41/41`、Phase 2 `276/276`；精确候选仍待 AutoCAD 实机绑定。
- M2 DrawingIndex 自动化候选已通过 Phase 2 `308/308`、benchmark `6/6`：
  `C:\tmp\CodexForAutoCAD-m2-benchmark\artifacts\autocad2016-m2-drawing-index-v040-e85d97ec-8e6b26fd-7614b6b2`。
  Host SHA-256 `E85D97EC02505EF69C67F710EAD5D35D18481B7D2DBB4C3D87195FCDE4156B7E`，
  AgentHost SHA-256 `8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A`，
  manifest SHA-256 `BF20A62F8CC71AB3B6A7AA6F329DF8520E136EE7A0B1ED6283AEFAFE343BFCD3`。
  尚未受控集成或实机 `NETLOAD`。
- M3 读取语义自动化候选已通过 Phase 2 `310/310`、benchmark `6/6` 和 R20.1 双 Shell
  API stage（29 个运行时成员通过、8 个冻结的 R20.1 unavailable 成员）：
  `C:\tmp\CodexForAutoCAD-m3-read-semantics\artifacts\autocad2016-m3-read-semantics-v041-fb18d959-8e6b26fd-7fd527a7`。
  Host SHA-256 `FB18D95981F607B22D8C023BF63915614DFF8964BF985BE6CB0ABEA26D9B3673`，
  AgentHost SHA-256 `8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A`，
  manifest SHA-256 `4B3B710F3773D10F0B30A31B357CB7D3D35445BA294F1F1ABEDDC8C378B1ED00`。
  尚未完成 19/19 对象实机字段矩阵。
- 2026-07-25 已第二次确认隔离 `DOTNET_CLI_HOME` 在未关闭 .NET 全局工具 PATH 自动写入时，
  会把临时 `.dotnet/tools` 永久追加到用户 PATH，导致 Windows Shell 不稳定。当前 PATH
  已恢复到 661 字符、13 项，临时项目项为 0；用户级和当前进程防护变量均为 `0`。主仓
  脚本已补显式防护，新增 `verify-dotnet-cli-path-guard.ps1`；该修改尚未重跑完整 Phase 2，
  但静态守卫、PowerShell 解析、diff 检查和受保护的新 CLI Home 运行时回归已通过。历史
  Worktree 不批量改写。详见 `DOTNET_CLI_PATH_INCIDENT_20260725.md`。

## 已验证检查点

### 诊断薄宿主

- 提交：`2d2ad3738095794c8374e916559c0c5d13702ba1`。
- 目标：AutoCAD 2016 R20.1 x64，进程内 .NET Framework 4.5 薄宿主。
- 已使用目标机原版 Autodesk `20.1.0.0` 托管程序集真实编译并由用户手工
  `NETLOAD`。
- `CODEXCADDOCTOR` 与 `CODEXCAD` 可执行；首次记录为 `DBMOD 21 -> 21`。
- 该历史记录没有取得已加载 DLL 的现场路径/哈希绑定，不得由后续构建哈希回填。

### Palette 运行时检查点

- 提交：`56115e4`（`feat(host2016): add verified palette runtime checkpoint`）。
- 冻结候选 DLL SHA-256：
  `90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE`。
- 用户已在原本打开的 AutoCAD 2016 中手工加载并验证 Palette。
- 100% DPI 下打开、停靠、浮动、隐藏后重开、释放重建、中文输入和换行正常。
- 干净样本中 Palette 操作及文档切换前后 `DBMOD=4`；插件未读取选择、未写入
  CAD、未保存图纸。
- 125%/150% DPI 与 AutoCAD 退出生命周期仍未验证。

### 跨运行时认证与 Bootstrap 原语

- 阶段证据：`handoff/autocad2016/evidence/auth-bootstrap-verification-20260719.json`；
  阶段提交以包含该 evidence、源码、Specs 和验证器的同一 Git 提交为准。
- 基线为已验证 Palette 提交 `56115e4`；.NET SDK 固定为 `8.0.319`。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过同一完整门禁。
- 托管核心 Release 隔离构建为 `0` warning / `0` error；Bridge 回归 `29/29`。
- IPC/Bootstrap 在 net45 与 net8 均为 `35/35`，固定 frame、KDF、Host→Agent 与
  Agent→Host HMAC 字节完全一致；两次隔离构建的六个主产物逐字节一致。
- 公共 API 规范化面在 net45/net8 均为 `103` 项且 SHA-256 相同；MemberRef、关键
  状态机方法和完整 Bootstrap 实现 IL 已分别按目标框架冻结。
- 已验证发送 payload 只能尝试写一次，部分写入或 Flush 失败都会永久消费；接收
  payload 禁止转发；端点角色由材料来源固定；入站 Guard 与出站 Authenticator 各只能
  领取一次；认证后解析使用内部 frame 副本以避免调用者 TOCTOU。
- 该检查点自身只证明内存/Stream 协议原语。后续“真实进程外 AgentHost 安全引导”
  检查点已补齐有界的真实密钥交付、确认身份、启动截止 fail-closed 中止和最多 `5` 秒
  有界终止清理证据，但长运行 live
  Bridge 与 AutoCAD 集成仍不属于本原语检查点。

### 真实进程外 AgentHost 安全引导

- 阶段证据：`handoff/autocad2016/evidence/agent-bootstrap-verification-20260719.json`；
  阶段提交以包含 Launcher、AgentHost、Specs、验证器、evidence 和本次文档更新的同一
  Git 提交为准。
- 最终阶段入口为 `scripts/verify-autocad2016-agent-bootstrap-stage.ps1`；它自动运行双
  PowerShell 的 bootstrap、认证兼容与 Phase2 门禁，将 raw evidence/log 留在 ignored
  `artifacts/`，并在 Git evidence 中只保存其 SHA-256、规范化比较结果、计数和限制。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过同一完整门禁；每次
  均完成两次隔离 Release 构建，完整可运行输出树 `106` 个文件按相对路径、长度和
  SHA-256 逐字节一致，Specs 执行后再次复核未变化。
- net45 与 net8 Launcher Specs 均为固定 ID 集 `15/15`；验证器拒绝缺失、重复、未知
  或两个运行时同时删除的测试。
- 真实 AgentHost bootstrap-doctor 已通过受限继承的 stdin/stdout/stderr 句柄完成认证；
  命令行和环境变量不携带 bootstrap 密钥或 frame。子进程领取句柄后清除继承位，父进程
  可继承 canary 句柄未进入子进程。
- 启动前要求批准的 EXE SHA-256；父进程以 `CREATE_SUSPENDED` 创建后校验 PID、创建
  FILETIME、映像路径、卷/文件 ID 和第二次 SHA-256，再恢复主线程。批准 SHA-256 不匹配
  和确认 PID/创建时间不匹配均动态 fail-closed。
- 未确认挂起、有效确认后继续挂起两条路径均由启动截止触发 fail-closed 中止，随后在
  最多 `5` 秒有界清理窗口内证明子进程终止；取消路径也执行相同的有界终止清理。这里
  不声称终止本身严格完成于配置的启动截止内。重复真实引导 `5` 次通过，相关进程
  基线/终态均为 `0 -> 0`。stderr 始终排空且只公开受限字节数与
  截断标志，失败异常不公开原始文本。
- 冻结构建哈希：AgentHost EXE `002BBA9D...49706`，AgentHost DLL
  `852BD92C...86033`，net45 Launcher `597D99E8...F849`，net8 Launcher
  `84E0E2A7...1FE9`；完整值保存在阶段 evidence。
- Phase 2 回归为 Release `0` warning / `0` error、七个既有 Specs `145/145`、
  AgentHost doctor、Host 禁止 API、秘密扫描和 diff 通过；认证兼容回归在两个 PowerShell
  下均保持 Bridge `29/29`、net45/net8 `35/35` 和固定向量一致。
- 本检查点未启动、重启或操作 AutoCAD。它不证明长运行 `IAgentBridgeClient`、Host.2016
  live handshake、外部进程复制句柄的对抗性、刻意替换 EXE 的 suspended-launch TOCTOU
  动态攻击、CAD 审批/写入或完整 AutoCAD 2016 支持。

### 只读选择上下文运行时检查点

- 当前阶段基线：`2036fd6`；分支：`codex/selection2016-readonly-v2`。阶段提交应由
  包含候选源码、Specs、验证器、evidence 和本次交接更新的同一 Git 提交记录。
- 冻结候选 DLL SHA-256：
  `AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8`；大小
  `31744` 字节，冻结副本保持只读。
- PowerShell 7 与 Windows PowerShell 5.1 的隔离 Release 双重构建、`25/25` Specs、
  IL、禁止 API、依赖和输出门禁已通过，四次构建逐字节一致。
- 用户在原本打开的 AutoCAD 2016 中手工 NETLOAD 该冻结候选。预选的 Line、Circle、
  Polyline、DBText、MText、BlockReference 各 `1` 个成功发布为只读上下文；
  `selected=6`、canonical bytes `738`，捕获前后 `DBMOD 4 -> 4`。
- `CODEX16CTXCLEAR` 后 `published=false`、`selected=0`，且 `DBMOD 4 -> 4`。
- 文档激活事件以 `cleared-document-activated` 清除旧缓存，事件计数为 `1`；切换前原图
  清除后的命令行 `DBMOD=21`，切换后目标图命令行 `DBMOD=21`，两值相等。这只证明
  缓存未跨图纸保留，不推断更多文档状态。
- 首次没有预选集时的 `validation-no-implied-selection` 是执行前置 `DBMOD` 后预选被
  AutoCAD 取消所触发的预期 fail-closed，不是候选运行失败。
- 选择哈希按脱敏策略不写入仓库；实体总数未单独计量，插件自动保存也未做独立
  运行时动作验证，因此对应 runtime 布尔值继续保持 `false`。

### CadContextJson v1 与 Host/Agent/UI 公共契约

- 规范：`handoff/autocad2016/MVP_PUBLIC_CONTRACT_V1.md`；证据：
  `handoff/autocad2016/evidence/cad-context-contract-v1-verification-20260719.json`。
- CadContextJson schema 固定为 `codex.autocad.cad-context` / version `1`，与 IPC
  `protocolVersion` 和 Host/Agent/UI `contractVersion` 明确分离。
- Line、Circle、Polyline、DBText、MText、BlockReference 六类图元使用显式强类型
  payload；包含真实坐标、图层、文字、半径、顶点和有效块名。文档名称、路径和
  `pathHash` 不进入 v1。
- canonical JSON 固定为严格 UTF-8、无 BOM/空白、固定字段顺序、Handle 数值排序和
  invariant G17 数字格式。冻结向量为 `2225` 字节，SHA-256
  `c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423`。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过：每个 Shell 两次隔离
  Release 构建逐字节一致，net45/net8 Contracts Specs 均为 `27/27` 且输出完全一致；
  当前 Phase 2 回归为 Release `0` warning / `0` error、`157/157`。
- 能力协商、方法/事件/错误闭集、thread/turn、assistant 文本事件、上下文哈希回显、
  离线/断线/超时 fail-closed 和审批仅 `allow_once`/拒绝已经冻结。UI 不得增加
  `allow_for_session`、未认证回退或隐式协议字段。
- 本检查点没有构建或 NETLOAD 统一 Host.2016，也没有操作 AutoCAD；它不证明 Palette
  JSON 展示、具体 `IAgentBridgeClient`、长运行 live Bridge、真实 Codex 对话或完整
  AutoCAD 2016 支持。

### 具体 IAgentBridgeClient 跨运行时检查点

- 阶段入口：`scripts/verify-autocad2016-bridge-client-stage.ps1`；阶段证据：
  `handoff/autocad2016/evidence/bridge-client-stage-verification-20260720.json`。
- PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 均通过同一完整门禁；每个
  Shell 都执行两次隔离 Release 构建，net45/net8 Client、Specs、TestServer 和 Bridge
  产物逐字节一致。
- net45 与 net8 Bridge Client Specs 均为 `22/22` 且输出一致；Bridge 回归 `34/34`；
  当前 Phase 2 为 Release `0` warning / `0` error、八个 Specs `184/184`。
- 已验证能力协商、thread start、携带 CadContextJson v1 身份的 turn start、assistant
  delta/completed 事件、interrupt 和仅一次审批响应；thread/turn/context 身份必须逐项
  回显并绑定。
- HMAC、严格递增 sequence、nonce、防重放、坏 MAC/序号间隙/nonce 重放拒绝均通过；
  未知字段、重复字段、错误大小写、尾随 JSON、非法 UTF-8 和超大帧在分配前均
  fail-closed。
- 合法 turn 终态事件在身份校验后消费活动 turn；同一 turn 的后续迟到事件不再具有活动
  身份并按 fail-closed 拒绝。离线、断线、请求超时、取消、并发 Stop 和重复 Dispose 均
  有有界终态；连接故障会发出 `ConnectionFaulted`，密钥副本清零，TestServer 无残留。
- 本检查点没有启动、重启或操作 AutoCAD，`NetLoadVerified=false`、
  `AutoCadLiveEvidence=false`。它不证明统一 Host.2016 已接入长运行 AgentHost，也不证明
  真实 Codex thread/turn、Palette 回答回传或两轮 CAD 对话。

## 历史演进检查点

以下两节保留 v1 到 v2 的形成过程，用于解释提交、候选和证据边界；它们不是当前状态。
当前有效结论以本文件顶部“当前活动快照（2026-07-22）”和末尾“下一步顺序”为准。

### 统一 Host.2016 只读 MVP（历史 v1 检查点）

- 公共契约决策门已通过，Codex 与 Kimi 均必须以 `MVP_PUBLIC_CONTRACT_V1.md` 为唯一
  wire/UI 数据基线；视觉实现可以并行，但不得反向改变协议。
- 诊断、Palette、只读选择捕获和 CadContextJson v1 已整合为唯一
  `Codex.AutoCAD.Host.2016` net45/x64 产品入口。用户已对上一统一候选人工 `NETLOAD`，
  验证 Palette 打开、四个真实图元生成 `1700` 字节 JSON、摘要展示和 `DBMOD 0 -> 0`。
- 上一 Agent 候选执行 `CODEX16AGENTSTART` 时在能力协商阶段以 `ArgumentException`
  fail-closed；没有进入 CAD 写入，相关 AgentHost 进程未残留。根因是 Host 将含空格和
  `/` 的显示文字用作 v1 机器标识符。
- 修复后的生产标识符固定为 `codex-autocad-2016-mvp`、`0.3.1.0` 和
  `autocad-r20.1-net45-x64`。精确回归 `HOST2016_CAPABILITIES_IDENTITY` 为 `1/1`。
- 当前实机候选 ID：`autocad2016-mvp-agent-v031-a7bff46f-8c74b95e`；Host 程序集版本
  `0.3.1.0`、SHA-256
  `A7BFF46F1BA4970818ACB03F51C09EEBF1DDB8A7093D0C4C615E2D877D9236D1`。AgentHost 为
  framework-dependent single-file，SHA-256
  `8C74B95ECD6680F9A35824DB1C2C543D42B52AB1E4D3565F5B7EE8DBB1DC900E`。
- 当前非 CAD 门禁：Release、Phase 2 `192/192`、精确候选 net45 Bridge Client
  `22/22`、精确候选 net45 AgentLauncher `15/15`、AgentService `7/7`、真实本机 Codex
  能力协商与同一 thread 两轮上下文问答 `2/2`；secret scan、`git diff --check` 和相关
  进程残留 `0` 均通过。
- 用户已在真实 AutoCAD 2016 中人工 `NETLOAD` 该精确候选，Palette 显示模块版本
  `0.3.1.0`；真实 Line 已发布为 CadContextJson v1 并显示于 Palette。AgentHost 完成
  认证启动，本机 Codex 返回第一轮回答，同一 thread 的第二轮问题正确复用了前一轮
  标记；用户另确认多次连续上下文对话正常。核心只读 AI 链路因此可记为
  `NetLoadVerified=true`、`AutoCadLiveEvidence=true`。脱敏证据见
  `handoff/autocad2016/evidence/agent-mvp-runtime-verification-20260721.json`。
- 核心只读 Agent MVP 已在自动化门禁和上述实机证据通过后单独提交：`7f10d60`
  (`feat(host2016): connect verified readonly Agent MVP`)。
- 用户确认执行了规定的停止与 `DBMOD` 检查，但随后独立只读进程检查仍发现 `1` 个由
  当前 AutoCAD 进程创建的候选 `AgentHost bootstrap-serve` 进程。因此核心问答通过不等于
  停止生命周期通过；`agentHostNoResidualProcessVerified=false`，在定位并复验前不得把
  “有界停止且无残留”写成已验证。
- CAD 写入和插件保存继续禁用；本阶段不触碰一次审批、事务写入或自动保存设置。

### P0 已完成检查点：AgentHost 停止生命周期

- 根因已限定在 Host 停止编排：旧实现先等待 Bridge 停止；Bridge 在 net45 管道读未及时
  解除时可能超时抛错，随后 AgentHost 会话终止被跳过，而命令层只观察异常、不显示结果。
  底层 AgentHost 终止器及测试进程清理仍保持 AgentService `7/7`、net45 Launcher
  `15/15` 和 Bridge Client `22/22`。
- 修复已改为分阶段、可重试的停止协调器：Bridge Stop、Bridge Dispose 与 AgentHost Stop
  分别记录；成功阶段不会重复，失败阶段可由下一次 STOP 重试；并发 STOP 共享同一 attempt，
  Palette/状态回调异常不能阻止资源清理。Host 停止规格为 `13/13`。
- 当前自动化门禁：Bridge Client net45/net8 `25/25`、Bridge `37/37`、Phase 2 `195/195`；
  AgentLauncher net45/net8 `26/26`；认证兼容 net45/net8 `35/35`。PowerShell 7 与 Windows
  PowerShell 5.1 均通过，目标机原版 R20.1 程序集 net45/x64 A/B 构建逐字节一致。
- 冻结并已完成实机验证的候选：`autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd`；
  Host `0.3.2.0`，SHA-256
  `1CC9D2943F1AB3C37395927B0E2EAF4189A0B3BE4B2E8FA4A61AE8470D3478DC`；AgentHost SHA-256
  `8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A`。用户已确认
  `NETLOAD`、模块版本、三次启停请求、Palette 的 `Agent Bridge 状态: online`、最终
  `AgentHost 已停止`、`DBMOD 20 -> 20`；本机只读进程检查为 AgentHost 残留 `0`。
  对应观察记录见 `evidence/agent-stop-live-observation-20260722.json`。当前为
  `NetLoadVerified=true`、`AutoCadLiveEvidence=true`。测试步骤见
  `MVP_AGENT_STOP_RUNTIME_TEST_20260722.md`。
- 旧候选 `autocad2016-mvp-agent-v032-884413f0-8c74b95e` 已撤销并标记
  `revoked-do-not-load`；其旧 evidence 时间早于最终产物，不得用于 P0 结论。
- 2026-07-22 重新运行候选冻结脚本后，`sourceSnapshotAtUtc`、`candidateFrozenAtUtc` 和
  `recordedAtUtc` 均晚于本次最终构建；候选哈希与固定目录保持一致。该证据仍明确保留
  `NetLoadVerified=true`、`AutoCadLiveEvidence=true`；该候选已完成本轮人工实机验证。

### CadContextJson v2 AutoCAD 实机验证（历史形成过程）

- 当前 CadContextJson v1 只对白名单中的 Line、Circle、Polyline、DBText、MText、
  BlockReference 提供强类型 payload；选区中任一其他实体会以
  `validation-unsupported-entity-kind` 整体 fail-closed。这是当前最常见的实际使用阻断。
- v1 已冻结，不在原 schema 上悄悄加入旧消费者无法理解的新实体。下一阶段先定义
  CadContextJson v2，新增高频实体强类型 payload，并为仍未知的实体提供显式、限界、
  脱敏的占位记录和 `complete=false`/计数；不得静默丢弃未知实体，也不得读取代理对象
  私有数据、图纸路径或外部资源路径。
- 第一批优先评估 Arc、Ellipse、Spline、Point、Ray/Xline、2D/3D legacy Polyline、
  Dimension、Hatch、Leader/MLeader 和 Table。按目标机 R20.1 原版 API 可编译性、固定
  白名单字段、数量/顶点/文字上限和真实图纸常见度逐项冻结，不一次承诺所有 AutoCAD
  或垂直产品代理对象。
- CadContextJson v2 独立契约、验证器和确定性 Codec 已在隔离分支
  `codex/cad-context-v2` 中冻结候选；v1 源文件未修改，原固定向量仍为 2225 字节、
  `c5a03d4c...4423`。v2 固定向量为 6678 字节、`21cc9378...c3b4`，
  net8 与 net45 Contracts Specs 均为 `39/39`。
- v2 覆盖原六类及 Arc、Ellipse、Spline、Point、Ray、Xline、Polyline2d、
  Polyline3d、Dimension、Hatch、Leader、MLeader、Table，共 19 个强类型 payload。
  未知、读取失败或超过单实体限额的对象使用仅含 `dxfName/reason` 的受限占位；
  `entityCount`、`parsedEntityCount`、`unsupportedEntityCount` 和 `complete`
  必须相互一致。
- 目标机原版 Acdbmgd 20.1.0.0 公共 API 字段审计已记录在
  `MVP_CAD_CONTEXT_V2.md`；契约证据见
  `evidence/cad-context-contract-v2-verification-20260721.json`。
- Host.2016 的真实对象读取、v2 JSON 映射、逐实体降级和选择状态哈希已经形成构建
  候选。合并后 Host v2/Contracts/Bridge/停止协调回归已汇总为 `235/235`；v1/v2 固定
  向量不变。
- 之后已将 v2 捕获器接入 `UnifiedReadOnlyContextRuntime`，并让统一 Palette 显示
  schema/version、解析数、占位数和 `complete`；`MvpAgentClient` 已显式要求 v2 能力并
  调用 `agent.turn.start.v2`。这些是源码编译证据，不是 AutoCAD live 证据。
- 新增真实 Bridge → AgentHost v2 turn 规格后，Bridge 为 `38/38`，完整 Phase 2 本机门禁
  动态汇总为 `235/235`；Host 禁用 API、AgentHost doctor、diff 和秘密扫描均通过。
- 两份独立临时输出使用目标机原版 R20.1 程序集完成 locked Release 重建，Host DLL
  逐字节一致，SHA-256 为 `4D3386D9A825B2842290ACB51376FBA6BE6603F49295E606F8C9F3F92B538C08`，
  Autodesk DLL copy count 为 `0`。P1 候选和 manifest 已冻结；自动化证据见
  `evidence/cad-context-v2-candidate-build-autocad2016-mvp-context-v2-v032-4d3386d9-751b97c7-7216527a.json`。
- 上述自动化检查点把 `HostV2CaptureImplemented`、`R201HostCompileVerified`、
  `RuntimeIntegrationImplemented`、`CandidateFrozen` 和 `BridgeV2Negotiated` 提升为 `true`；
  在该检查点当时，`NetLoadVerified`、`AutoCadLiveEvidence` 和实机混合选区仍为 `false`。
  后续 2026-07-22 用户实机已把这些 live 基线提升为通过，范围和限制见顶部活动快照及
  `evidence/cad-context-v2-live-observation-20260722.json`。
- 对象扩展候选冻结前要求先修复 AgentHost 停止残留；P0 与 P1 后续已分别验证、分别提交。
- P1 已受控吸收 P0 停止修复，候选冻结提交为 `c174166`，随后独立引入采集器收口提交
  `5325e35` 和证据提交 `3ea4961`；P0 与 P1 仍保持独立提交和独立 evidence。P1
  候选随后已完成用户人工实机测试；这不扩大为尚未执行的退出、DPI、断线、超时、取消或
  19 类对象逐类字段验证。

### 已验证但尚待集成的独立阶段

- 环境采集器的注册表 `Location` 工作已经受控引入当前 P1 线，提交为 `5325e35`：支持
  `AcadLocation`、`InstallLocation`、`Location`，覆盖 Location-only 非标准安装、指向
  `acad.exe` 的规范化，以及 probe、根键、子键枚举和属性读取四类失败分支。
- 当前机器 PowerShell 7.6.3 与 Windows PowerShell 5.1.19041.6456 自测均为 `24/24`；
  两个 Shell 的真实只读采集均发现 `1` 个可构建 R20.1 安装，`Location` 提示数为 `1`，
  总失败计数为 `0`。脱敏证据见
  `evidence/environment-collector-location-verification-20260722.json`。
- 主工作树仍含用户所有的未提交 Host.2025 UI、选择和写入原型，以及其他未跟踪文件。
  这些变化没有进入 `codex/bridge-client-net45`，不能作为本 AutoCAD 2016 候选的构建或
  验证证据，也不得由本阶段清理、覆盖或提交。

## 不可弱化的产品约束

- AutoCAD 2016 优先：进程内 `net45/x64` 薄宿主，Agent/Sandbox 保持进程外 .NET 8。
- CAD 写入固定为“计划 -> 预览 -> 一次审批 -> `DocumentLock` 内重校验 -> 单事务”。
- 审批只有“拒绝”和“一次允许”，不得提供会话级永久允许。
- HMAC、严格递增 sequence、nonce、防重放、结果身份绑定和 fail-closed 不得降级。
- 图纸、revision、选择、图层和空间必须在事务锁内重新校验。
- Agent 中断、超时或结果不确定时不得自动重试 CAD 写入。
- 插件不得自动保存 DWG，也不得关闭用户的 AutoCAD 自动保存设置。
- 不启动、唤醒、关闭或重启用户的 AutoCAD；需要实机时只给出冻结候选和人工步骤。
- 每个阶段必须先验证，再单独提交 Git。
- 正式侧边栏 UI 已通过公共契约决策门；任何 wire 不兼容变化必须升级 v2，不能由 UI
  原型隐式扩展 v1。

## 待实机验证队列

P0 与 P1 happy path 已通过，不再重复请求相同测试。M1 冻结新候选后，按
`READONLY_MVP_REMAINING_LIVE_TESTS_20260722.md` 依次验证：

1. 使用精确 M1 `0.3.3.0` 候选完成文档切换、Reset、退出、DPI 和故障矩阵。
2. 使用精确 M2 候选完成 1k/10k/50k 扫描、分页查询、取消、stale/partial/limited 和 DBMOD 观察。
3. 使用精确 M3 候选完成 19 类对象字段、实际类型统计、placeholder 和块详情核对。
4. 未完成实机证据前，不把 M1/M2/M3 候选标记为产品完成。

## 下一步顺序

1. 核心 Agent MVP、P0 停止生命周期已分别提交：`7f10d60`、`8a4ee57`。
2. P1 已完成自动化、冻结和 AutoCAD 2016 live 基线。
3. M0 自动化、候选冻结和本地 `main` 收拢均已完成。
4. M1 自动化候选已冻结，等待精确候选实机绑定；M2/M3 已完成自动化候选，可在 M1 实机通过后受控整合。
5. M4 沙箱与审计基础完成前，不启用 M5 CAD 写入。

## 更新纪律

- “已验证”必须同时写明候选身份、验证范围和证据边界。
- 本地 Specs、静态扫描和构建哈希不能替代 AutoCAD 内 NETLOAD 或端到端证据。
- 未验证、失败或因条件缺失跳过的项目必须保留为明确的 `false`/待办。
- 不记录 `TRUSTEDPATHS` 内容、用户名、真实图纸路径、网络路径、许可证或凭据。

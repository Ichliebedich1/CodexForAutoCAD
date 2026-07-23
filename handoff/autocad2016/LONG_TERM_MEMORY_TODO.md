# AutoCAD 2016 项目长期记忆与完整待办

最后更新：2026-07-23（北京时间）

本文件是项目长期“固定目标、已验证基线、M0-M12 未完成队列和阶段纪律”的权威入口。
运行状态细节见 `CURRENT_STATE.md`，人工测试入口见 `README_FIRST.md`，脱敏证据见
`handoff/autocad2016/evidence/`。

## 1. 总目标

交付一个 AutoCAD 2016 优先的本机 Codex 插件，能够：

- 理解选择集和整张图纸数量级的 CAD 信息。
- 通过结构化查询让 Codex 按需获取图纸细节。
- 提供稳定的多轮对话。
- 通过强类型工具安全修改 CAD。
- 所有写入经过计划、预览、一次审批、锁内重校验、单事务、Undo 和审计。
- 不自动保存 DWG，也不修改或关闭用户的 AutoCAD 自动保存设置。
- AgentHost、Codex 和沙箱均运行在 AutoCAD 进程外。
- 最终具备长期记忆、审计、恢复、签名安装和企业部署能力。
- AutoCAD 2016 完成后再适配 AutoCAD 2025。

## 2. 明确冻结的范围

除非用户以后单独重新立项，否则以下内容不属于本项目结束条件，也不得通过空接口制造进度：

- Provider-neutral `IAgentProvider` 抽象。
- Direct API Provider。
- OpenAI、Anthropic、国产模型或本地模型的第二套调用链。
- 自研 Agent Loop。
- 任意 AutoCAD 命令字符串、LISP、脚本或反射式 API 调用。
- 自动保存或覆盖 DWG。
- Agent 对结果不确定的 CAD 写入自动重试。

当前唯一实际 Agent 为本机 Codex；现有集成使用
`codex app-server --stdio` 的结构化 JSONL/JSON-RPC，不是终端模拟或 ANSI 文本解析。

## 3. 固定架构与安全不变量

- AutoCAD 2016 进程内保持 `net45/x64` 薄宿主。
- AgentHost、Bridge、Codex、沙箱、长期记忆和审计保持进程外 .NET 8。
- HMAC、严格递增 sequence、nonce、防重放、结果身份绑定和 fail-closed 不得弱化。
- CAD 写入固定为“强类型计划 -> 可检查预览 -> 一次审批 -> DocumentLock 内重校验 ->
  单事务 -> 单 Undo 边界 -> 结构化终态”。
- 审批只有“拒绝”和“一次允许”，不得增加会话级永久允许。
- 文档、revision、选择、空间、图层和计划哈希必须在锁内重新校验。
- 断线、超时、取消、上下文变化或结果不确定时不得开始或自动重试 CAD 写入。
- 没有目标机原版 R20.1 编译和用户人工 `NETLOAD`，不得宣称相应能力支持 AutoCAD 2016。
- 自动化门禁、模拟服务和合成上下文不能替代 AutoCAD 实机证据。
- 每个可验证阶段通过后单独提交 Git。

## 4. 当前已验证基线

### P0：AgentHost 停止生命周期

- [x] 提交 `8a4ee57`。
- [x] AutoCAD 2016 人工启停和重复 STOP。
- [x] DBMOD 不变，AutoCAD 可继续使用。
- [x] 相关 AgentHost 残留为 0。
- [x] 脱敏证据：`agent-stop-live-observation-20260722.json`。

### P1：CadContextJson v2 产品调用链

候选身份：

- Host 版本：`0.3.2.0`。
- schema：`codex.autocad.cad-context/2`。
- Host SHA-256：
  `0D72EDC38A30E7BF33AAEE4DCB1D50D341C4C883146677537C4BB5E7551D0AD7`。
- AgentHost SHA-256：
  `10BEA363AC80C856FA513F4312B60410DB62BBF4917CE634B589CBA59DA65442`。
- manifest SHA-256：
  `A16831703985906F724B8EB93BDB0BC801A5781A3228F0694CB1A20A4AC5960F`。

已验证：

- [x] Runtime、Palette、Bridge 和 AgentHost 使用 v2，缺少 v2 能力时不回退 v1。
- [x] 19 类强类型契约和未知/读取失败/数据超限三类受限 placeholder。
- [x] Host.2016 MVP `24/24`、Phase 2 `259/259` 和 AgentHost v2 两轮 live `2/2`。
- [x] AutoCAD 2016 人工 NETLOAD、Doctor v2 和 CAD 写入/插件保存禁用。
- [x] 100% DPI Palette 打开、停靠、浮动、隐藏重开、重建、中文输入和布局。
- [x] 50 对象混合选区成功发布：44 个强类型、6 个 placeholder、
  `jsonBytes=23142`、`DBMOD 21 -> 21`。
- [x] 本机 Codex 使用真实 v2 CAD 上下文完成两轮连续对话。
- [x] 显式 CAD 上下文清除和文档激活清除旧缓存。
- [x] 脱敏范围证据：
  `cad-context-v2-live-observation-20260722.json`。

证据边界：

- [ ] 19 类对象尚未逐类完成字段实机核对。
- [ ] 文档切换后真正提交问题的 fail-closed 尚未实测；用户在提示阶段取消。
- [ ] v2 上下文已发布时 Palette Reset 后保留上下文尚未实测。
- [ ] AutoCAD 正常退出清理、125%/150% DPI、启动失败、断线、超时、取消和迟到事件待验证。
- [ ] `CODEX16CTXCLEAR` 只清 CAD 上下文，不新建 Codex thread；需补充明确的对话清除语义。
- [ ] 选择快照仍有 64 实体和 256 KiB JSON 上限，不能满足整图规模。

## 5. M0：收拢当前基线

完成定义：主分支真实包含已实机验证的 v2 调用链，文档与证据一致。

M0 源码集成提交为 `e66ef1e`，候选构建稳定化提交为 `c96e9a3`。冻结自动化候选为
`autocad2016-mvp-context-v2-v032-37c1953d-ab1ce675-8926ed54`；聚合证据见
`m0-baseline-verification-20260722.json`。该精确候选尚未 NETLOAD，不能替代 P1 实机
候选的哈希绑定。

- [x] 建立独立 `codex/m0-baseline` 集成 Worktree。
- [x] 将 P1 代码、测试、脚本和历史 evidence 合入集成线，代码无冲突。
- [x] 创建本次 v2 AutoCAD 实机脱敏 observation evidence。
- [x] 更新 README、CURRENT_STATE、README_FIRST、测试手册和本文件。
- [x] 运行 JSON、Markdown、禁止敏感信息和 diff 检查。
- [x] 重新运行 R20.1 Release、Host MVP、Phase 2、AgentHost live 和候选包身份门禁。
- [x] 冻结新的统一只读 v2 基线提交、版本、DLL 哈希和候选目录。
- [x] 建立现有 Worktree 的“已合并、保留、可删除”清单。
- [x] 在不覆盖主工作树 Host.2025 原型的前提下，让 `main` 前进到已验证基线。

2026-07-22，本地 `main` 已安全快进并吸收冻结提交 `4833e76`；主工作树中用户所有的
Host.2025 原型和未跟踪辅助文件仍保留且未纳入提交。远端推送不属于本次 M0 收尾动作。

## 6. M1：只读 MVP 稳定化

完成定义：形成可长期日常使用的稳定只读 MVP 候选。

当前冻结候选：

- Host 版本：`0.3.3.0`。
- 候选：`autocad2016-m1-readonly-v033-c3478920-a47d86a6-7fc17895`。
- Host SHA-256：
  `C34789205C56D125C363962FEA8BA0EDCED0C23589D21EFB1586535DE348DAF3`。
- AgentHost SHA-256：
  `A47D86A6512B23694B566B0FF272EA3C22183F691ABF3334EE639A7A0EF03FE0`。
- manifest SHA-256：
  `2702D4F1E86ECD87F31A84541D96DECDE48C9632E67EF8473FB4CEC41C947EFF`。
- 自动化：Host MVP `40/40`、Phase 2 `275/275`、25 文件 Host.2016 只读闭包、
  R20.1/net45/x64 双构建位级一致、候选 AgentHost doctor、diff 和敏感信息扫描通过。
- 证据：
  `cad-context-v2-candidate-build-autocad2016-m1-readonly-v033-c3478920-a47d86a6-7fc17895.json`。
- 实机入口：`M1_READONLY_STABILITY_RUNTIME_TEST_20260722.md`。

代码与自动化已完成：

- [x] Bridge 断线时原子切换 offline，终止当前回合，后续 ASK 必须拒绝。
- [x] 启动失败、异常退出、协议错误、超时和取消使用稳定错误码。
- [x] UI、日志和 evidence 不输出原始路径、凭据或未脱敏异常。
- [x] 每个请求维护 Host 自有 request_id、回合状态、10 分钟超时和唯一终态。
- [x] 重复取消幂等；终态后拒绝迟到事件，状态不得回退。
- [x] 修正 Palette 中完成、已清除、已断线、已停止和图纸切换后的可见状态。
- [x] 明确提供“清除 CAD 上下文”“新建 Codex 对话”“全部清除”。
- [x] 对话按图纸隔离，图 A 的 CAD 上下文、可见回答或 Provider thread 不得混入图 B。

仍需用户实机绑定；完成前 M1 不得标记完成：

- [ ] 按精确 `0.3.3.0` Host/AgentHost 哈希在 AutoCAD 2016 人工 `NETLOAD`。
- [ ] 验证新建对话、只清 CAD 上下文、清除全部和图 A/图 B 对话隔离。
- [ ] 验证取消、重复取消和活动回合期间新建/清除返回 `busy`。
- [ ] 验证 v2 上下文存在时 Palette Reset 后仍保留。
- [ ] 验证 AutoCAD 正常退出后 AgentHost、管道和 Codex 子进程残留为零。
- [ ] 完成 125% 和 150% DPI。
- [ ] 完成启动失败、断线、超时、取消、文档切换和退出实机矩阵。

## 7. M2：整图规模上下文与查询

完成定义：50,000 对象级测试图可扫描、总结和按需查询；AutoCAD 保持可操作，读取不改变
DBMOD。

不得把 `MaximumEntities` 简单改成几万。

M2-A 图纸索引和 M2-B Codex 动态查询已形成同一自动化候选：

- Host 版本：`0.4.0.0`。
- 候选：`autocad2016-m2-drawing-index-v040-e85d97ec-fa16355c-898671e2`。
- Host SHA-256：
  `E85D97EC02505EF69C67F710EAD5D35D18481B7D2DBB4C3D87195FCDE4156B7E`。
- AgentHost SHA-256：
  `FA16355C185F61CD7E85446E884C2FF9D7C745E5E2EB0CC40747C916C215371B`。
- manifest SHA-256：
  `95427BD85E70870C483512CD4401228B70F63608802512119F5ECB6486844356`。
- 自动化：Contracts net8/net45 `84/84`、Bridge Client net8/net45 `29/29`、
  Bridge/AgentHost `39/39`、AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2
  `308/308`、benchmark fixture/evidence `6/6`、R20.1 Release、30 文件只读闭包和 Host
  A/B 位级一致通过。
- 证据：
  `m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-e85d97ec-fa16355c-898671e2.json`。
- 说明：`M2_DRAWING_INDEX_VERTICAL_SLICE_20260722.md`。
- 实机入口：`M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`。

M2-A/M2-B 代码与自动化已完成：

- [x] 保留 CadContextJson v2 作为兼容选择快照，64 实体/256 KiB 上限未放大。
- [x] 新增版本化 DrawingIndex v1 和 CadQuery v1 契约及 fail-closed 验证器。
- [x] 支持选择集、当前空间、模型空间、布局和整张图纸扫描范围。
- [x] AutoCAD API 在文档线程的 Idle 小片、DocumentLock 和 ForRead transaction 中读取；
  索引只保留深拷贝强类型摘要。
- [x] 支持进度、幂等取消、2 分钟超时、100,000 实体索引和 64 MiB 估算预算。
- [x] 建立类型、图层、空间、块、包围盒、文字、对象令牌和数量摘要。
- [x] 支持绑定索引/过滤器/页大小的游标分页，不一次发送整图 JSON。
- [x] 为 Codex 提供 `cad.query_drawing`，支持按类型、图层、空间、块、范围、文字和对象
  ID 的只读分页查询。
- [x] Host 命令可发布大选择集总数、摘要、分页引用和完整性，不复用 v2 数量上限。
- [x] 文档、revision、DBMOD、当前空间和对象事件使旧索引失效，旧查询拒绝。
- [x] 未知、代理、读取失败和数据受限对象形成受限占位，不中断整图扫描。
- [x] AgentHost 通过认证反向 Bridge 查询 Host 拥有的纯托管冻结快照；Bridge 线程不进入
  Autodesk API，模型不能提供索引/文档/revision 身份。
- [x] 无选择上下文但有有效 DrawingIndex 时允许 ASK；系统 request、Provider thread/turn、
  tool call 和 query ID 分离并逐项绑定。
- [x] 覆盖反向查询早于 turn-start 响应的竞态、取消/STOP 排空、断线、终态、身份不匹配和
  stale 索引拒绝；50k 发布避免第二次数组深拷贝。
- [x] 建立确定性 1,000、10,000、50,000 对象 AC1009 脱敏基准图；三个 fixture 均为
  精确模型空间实体数，双次生成哈希、独立 DXF 解析和拒绝覆盖门禁通过。
- [x] `CODEX16INDEXINFO` 输出 Host 本地 Idle 分片、总扫描、内存和查询耗时，并显示查询页
  `200` 与 IPC 单帧 `8,388,608` 字节硬上限；新增只接受数值/布尔值与候选 ID 的脱敏
  evidence 记录器，包含 AutoCAD 扫描前/峰值工作集并拒绝 DBMOD 变化和覆盖已有证据。
- [ ] 使用三档实机数据冻结 UI 最大连续卡顿、总扫描时间、工作集、查询和 IPC 验收预算；
  当前 12 ms cooperative slice、120 s 扫描、64 MiB 估算内存只是代码 guardrail，不得冒充
  已验证的产品性能预算。
- [x] 自动化证明实体、统计桶、内存和时间预算映射为 `partial/limited`，不伪装完整。

M2 仍未完成，以下内容不能由自动化候选替代：

- [ ] 在 AutoCAD 2016 按精确 `0.4.0.0` 哈希人工 NETLOAD 五种范围、本地查询、分页和 DBMOD。
- [ ] 仅建立 DrawingIndex、不发布选择上下文，验证 ASK 与 `cad.query_drawing` 多页查询。
- [ ] 实机验证修改/撤销/切图后的 stale 拒绝，以及查询/回合取消和断线 fail-closed。
- [ ] 实机验证跨 Idle 片段的 `BlockTableRecordEnumerator` 生命周期和退出清理。
- [ ] 实机验证扫描中修改图纸、切换布局、取消、超时和旧游标拒绝。
- [ ] 使用 `M2_DRAWING_INDEX_BENCHMARK_FIXTURES_20260722.md` 完成 1k/10k/50k 扫描响应性、
  总时间、工作集、本地查询和 Agent 查询真实性能 evidence。

## 8. M3：CAD 读取语义和对象覆盖

完成定义：不支持对象只降低完整性，不使整次捕获失败。

当前 `0.4.2.0` M3 自动化候选已经冻结，但没有 AutoCAD `NETLOAD` 证据。精确候选为
`autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7`；Host SHA-256 为
`B5081C63DD11BD36706B529EC28C58BB1DEA22FEF6D50BA0E76C5E3E4CE67879`，AgentHost SHA-256 为
`E3DBE95546D193D9AF451A0420E648085F9E2AF9ECCC6E956BD85BC26ACDA615`，manifest SHA-256 为
`2633642C2F993FC320A0662FD95D4BC900CD4A453ABCDD6B7BEB7C596EF30348`。说明与人工字段核对
模板见 `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`。

当前 `0.4.2.0` 离线 Phase 2 doctor 已完成本机 app-server 初始化握手。它只证明候选包在
当前工作站可完成受限健康检查，不构成真实 Codex 回合认证、AutoCAD `NETLOAD` 或 CAD 查询
成功的证据；下一次 `CODEX16ASK` 若出现认证失败，应先在 Codex 客户端手工重新登录，再单独
记录实机结果。不得把原始认证错误、令牌或环境变量写入 evidence。

- [x] 在选择快照、整图索引、Palette 和诊断中按实际类型/数量显示未支持、数据超限和
  读取失败对象；统计不包含图层、Handle、路径或对象内容，且类型桶有界。
- [x] 新增 `CODEX16TYPEINFO`，为 19 类现有强类型对象列出中文名称和人工创建入口。
- [x] 为选择统计、DrawingIndex 累积边界、中文目录和真实 mapper → 可读摘要调用链增加
  源码级回归；当前自动门禁为 Contracts `87/87`、Bridge Client net45/net8 各 `29/29`、
  Bridge `39/39`、AgentRuntime `33/33`、Host MVP `53/53`、完整 Phase 2 `319/319`。R20.1
  API 双 Shell Probe 为 `29 passed / 8 expected failed`；目标 R20.1/net45/x64 Host A/B
  输出逐字节一致，当前 Host SHA-256 为
  `B5081C63DD11BD36706B529EC28C58BB1DEA22FEF6D50BA0E76C5E3E4CE67879`，Autodesk DLL 复制数
  为 `0`。
- [x] 冻结 M3 精确候选及 manifest：完整 Phase 2 `319/319`、benchmark fixture/evidence
  `6/6`、M3 核心读取 DXF fixture `6/6`、R20.1 API 双 Shell Probe `29 passed / 8 expected failed`、Host A/B 位级一致和
  候选 AgentHost doctor 均通过。脱敏证据为
  `evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7.json`
  （文件 SHA-256：`EA27EC4E9E9CE95D8CB488AB42B39260AD5EA71766907FEF56C0F36C630DD2B4`）。
  该过程没有启动、重启或操作 AutoCAD，故仍为 `NetLoadVerified=false`、
  `AutoCadLiveEvidence=false`。
- [x] 提供 M3 中文对象目录、首要字段和未来实机记录模板。
- [x] `BlockReference` 的受限 `blockDetails` 已贯通 DrawingIndex → CadQuery → 认证 Bridge →
  Agent 工具；属性/动态属性、嵌套块、布局和安全 Xref 元数据均受契约、深拷贝、内存预算和
  IPC 测试保护。外部 Xref 定义及真实路径不会读取或传播，详情降级为 `limited`。
- [x] M3 块读取所需 R20.1 API 已由双 Shell Probe 固定为 `29 passed / 8 expected failed`；
  脱敏 evidence 为 `evidence/v2-api-surface-probe-m3-cross-shell-20260723.json`。
- [x] 冻结可重复的 `AC1015` 核心 DXF fixture：14 个安全直接编码的实体变体、带属性和嵌套
  定义的 BlockReference、确定性 hash/文件集/实体顺序/多段线标志离线校验 `6/6`。生成器和
  校验器不启动或控制 AutoCAD，说明见 `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`。
- [x] Region、Solid、Mesh、Surface、RasterImage、Underlay、Proxy 和 Wipeout 已进入
  DrawingIndex/CadQuery 的受限类别映射；它们仅保留有界通用摘要，固定标记为
  `Unsupported=true`、`data_limited`，不会扩张冻结的 CadContextJson v2 强类型 schema。
  Contracts `INDEX-M3-003`、Host v2 `HOST2016-V2-016` 和 R20.1 编译期 Probe 覆盖该边界。
- [ ] 为 Dimension、Hatch、Leader、MLeader、Table 和高价值受限对象冻结脱敏示例测试图；
  中文目录和创建入口不能替代这些剩余测试资产。
- [ ] 逐类实机验证现有 19 种强类型对象。
- [ ] 用精确 M3 候选实机核对块属性、动态块、嵌套块、布局和安全 Xref 降级；复杂块语义
  与异常图仍需扩展。
- [ ] 完善 Dimension、Hatch、Leader、MLeader、Table 的 R20.1 字段语义。
- [ ] 用精确 `0.4.2.0` 候选实机验证上述高价值受限类别和垂直产品代理对象：类型、图层、
  空间、范围、`data_limited` 降级及 DBMOD 不变；不得把此项记录为完整字段读取。
- [ ] 长文字、复杂 Hatch、Table、Spline 受限但不拖垮整体。
- [ ] 每类对象具有契约、边界、R20.1 API Probe 和实机字段证据。

## 9. M4：进程沙箱、配置和审计基础

完成定义：AutoCAD 或 AgentHost 异常退出后进程树可确定清理；凭据和用户配置不进入日志或
普通工作目录。此阶段是启用 CAD 写入前置条件。

- [x] M4 第一诊断边界：Codex 子进程 stderr 持续排空且只传递有界 `bytes`/`truncated`
  摘要，进程退出事件等待该摘要形成但不阻塞进程事件线程；AgentHost 不再向控制台公开
  stderr 原文、协议异常正文、工作目录或 `CODEX_HOME` 路径。它尚未形成完整沙箱或 M4 候选，详情见
  `M4_PROCESS_ISOLATION_BASELINE_20260723.md`。
- [x] M4 本机 Codex 启动配置：`--codex`、`CODEX_EXECUTABLE`、npm 和绝对 PATH 发现会收敛为
  固定本地磁盘的绝对 `codex.exe`；显式无效配置不回退，工作目录和启动/关闭超时同样受检。
  doctor 只记录来源标签。说明见 `M4_LOCAL_CODEX_CONFIGURATION_20260723.md`。
- [x] M4 Codex 版本/App Server 健康预检：`doctor`、`run` 和认证 `bootstrap-serve` 先在同一
  受控 child allowlist 中运行严格、最多 `4 KiB` 的 `codex --version`；当前产品范围为
  `>=0.144.4 <0.145.0`，本机 `0.144.4`、其后的 `initialize`、真实两轮 live 和双 Shell
  `341/341` 均通过。未审查次版本、非 UTF-8、超限和超时输出 fail-closed；详情见
  `M4_CODEX_VERSION_PREFLIGHT_20260723.md`。这不覆盖每会话凭据、插件配置隔离或未来版本协议。
- [x] 可选每会话独立 `CODEX_HOME`/`CODEX_SQLITE_HOME` 与 Windows Generic Credential 边界：仅认证
  `bootstrap-serve` 读取 `CODEX_AUTOCAD_CREDENTIAL_TARGET`，只接受 `CodexForAutoCAD/` 受限引用，
  通过 `CredRead` 取得 token 后在私有 lease workspace 创建状态目录；运行时子进程才获得 token，版本
  预检明确排除它。默认未配置时保留旧兼容路径；项目不复制、链接、直接读取、记录或修改全局
  profile。AppServer `29/29`、Bridge `55/55` 为 synthetic 覆盖，真实 Credential Manager、真实隔离
  登录和完整插件配置面仍待人工验收。见 `M4_CODEX_SESSION_ISOLATION_20260723.md`。
- [x] 默认空 MCP：生产 `codex app-server --stdio` 固定追加 `-c mcp_servers={}`，覆盖默认用户
  profile 的 MCP server 表；AppServer `27/27`、Release 和真实两轮 live `2/2` 已通过。项目代码
  不直接读取或复制 profile 内容，但也不隔离 `CODEX_HOME`、凭据、技能或插件配置。见
  `M4_EMPTY_MCP_BOUNDARY_20260723.md`。
- [ ] 真实隔离登录与空插件配置边界：不可复制或猜测默认 profile 布局；必须以真实 Generic Credential
  的受控人工验证确认空私有 `CODEX_HOME` 可工作，并逐项审查 Codex 插件/技能等剩余配置读取面。
- [x] Codex 子进程生产路径先清空父环境，再注入固定 `16` 个变量名；`TEMP`/`TMP` 绑定
  `AgentWorkspace.Temp`，`PATH` 不复制父值。token/API key、代理、`CODEX_HOME`、`PSModulePath`
  和自定义变量均不自动继承。synthetic child、真实 doctor、两轮 live 与清理均通过；这不等于
  每会话凭据隔离。见 `M4_CODEX_CHILD_ENVIRONMENT_ALLOWLIST_20260723.md`。
- [x] AgentHost 启动链在恢复前创建 `KILL_ON_JOB_CLOSE` 的未命名 Windows Job Object 并分配
  已校验的 AgentHost；普通后代继承该边界。隔离 `bootstrap-serve` 规格已按 PID 验证
  `StopAsync`、拥有 Job 的启动器直接退出，以及已认证 AgentHost 自行退出但启动器仍存活时均回收
  父/后代；最后一条由退出监视器触发保留 Job 的有界关闭和一次重试。当前完整 AgentLauncher 门禁
  net45/net8 各 `37/37`。真实 Codex/AutoCAD 异常退出、嵌套 Job 企业环境和完整僵尸进程矩阵仍未
  验收，详见 `M4_AGENTHOST_UNEXPECTED_EXIT_CLEANUP_20260723.md`。
- [x] 同一 Job 默认限制 AgentHost/Codex 进程树最多 `16` 个进程、总提交内存最多 `4 GiB`；
  可接受范围分别为 `2..64` 与 `512 MiB..16 GiB`，非法值在进程创建前结构化失败。
  `QueryInformationJobObject` 已读回实际标志和值，但没有故意耗尽真实 Codex 的进程槽或内存。
  脱敏证据为 `evidence/m4-agenthost-job-resource-limits-20260723.json`。
- [x] 同一 Job 增加 CPU hard cap 和累计 Job user-time：默认分别为 `75%` 和 `8` 小时；认证后的
  service session 增加默认 `24` 小时墙钟截止。Windows 读回、非法边界、CPU-busy synthetic
  child user-time 耗尽终止、墙钟终止、显式 STOP 胜过已撤销截止、一次自动清理重试及连续
  失败后阻断后续启动在 net45/net8 均通过。Job user-time 明确不是墙钟时间，CPU 节流性能未
  测量。见
  `M4_CPU_RUNTIME_LIMITS_20260723.md` 和
  `evidence/m4-agenthost-cpu-runtime-limits-20260723.json`。
- [ ] 评估并实现受限令牌或 AppContainer。
- [ ] 设置可靠的工作目录磁盘硬配额；为进程数/内存/CPU 限制增加真实 Codex 耗尽或节流验证与
  用户可理解的失败诊断。不要用轮询目录大小冒充硬配额。
- [x] AgentHost workspace 与 audit 根、子目录和受管理文件关闭 ACL 继承，仅允许当前用户、
  SYSTEM 和内置 Administrators；每 session 使用独占 lease，正常退出删除，残留默认按
  `24` 小时/最多 `64` 个清理，审计默认按 `30` 天/最多 `512` 个清理。清理拒绝重解析根、
  不跟随目录链接并限制单次 `50,000` 项；真实 Codex 并发 Bridge/AgentHost STOP 后当前
  workspace 消失。见 `M4_PRIVATE_STORAGE_RETENTION_20260723.md` 和
  `evidence/m4-agenthost-private-storage-retention-20260723.json`。
- [x] Bridge、Bridge Client、AgentHost、AgentRuntime 与 Host.2016 的已发布失败出口已纳入
  `AgentBridgeErrorSanitizer`：错误码必须属于闭合白名单，错误说明必须精确匹配固定安全文本；
  未受信错误码统一降级为 `internal_error`。契约、Bridge、运行时和 client 规格使用路径形态
  `M4-SENTINEL` 验证异常正文不会跨越 IPC 或 UI 失败面；该检查点的双 Shell 及完整 Phase 2 为
  `350/350`。
  详见 `M4_DIAGNOSTIC_SANITIZATION_20260723.md`。
- [x] 本地 Codex 配置读取与 AgentHost CLI 的错误面现由
  `CodexLocalConfigurationFailurePolicy` 收敛：配置异常只携带固定安全说明和闭合 error code，
  未知失败值归一化为 `invalid_configuration`；CLI 不回显未知命令、异常类型或路径形态 sentinel。
  AppServer 为 `30/30`、完整 Phase 2 为 `351/351`。详见
  `M4_CONFIGURATION_ERROR_SANITIZATION_20260723.md`。
- [ ] 将安全日志导出和未来新增错误出口逐项纳入同一固定代码/说明策略；不能把已覆盖的
  Bridge/运行时/Host/本地配置边界误写成完整审计脱敏完成。
- [x] AgentHost 只读运行审计基线：`bootstrap-serve` 每会话在当前用户本地固定盘的独占 JSONL
  文件中记录启动/停止/失败、Bridge 连接、请求、thread/turn、取消、审批请求和 turn 终态；
  `/2` 每条为固定字段白名单、单调 sequence 和 canonical SHA-256 前序哈希链，默认限制
  `10,000` 条/`4 MiB`，审计故障会关闭 Bridge；内置有界验证器覆盖字段/删行/序号/前序哈希篡改
  及缺失终态。
  提示词、CAD JSON、路径、命令、环境变量、异常正文、token 和 Provider 原始 payload 不进入
  记录。说明见 `M4_RUNTIME_AUDIT_BASELINE_20260723.md` 和
  `M4_AUDIT_HASH_CHAIN_20260723.md`。当前链无签名、HMAC、远端锚定或 WORM 存储，不等同于
  外部不可篡改审计。
- [ ] 将审批解决、M5 CAD 提案/执行终态和安全日志导出接入审计；选择并验证受保护的哈希锚点、
  签名或 append-only 存储。审计目录最小 ACL 与保留/清理及本地哈希链已完成；当前 CAD 写入保持
  禁用，不能把只读审计基线写成 CAD 操作审计。
- [ ] 扩展安全故障注入和僵尸进程测试：synthetic AgentHost 意外退出已覆盖，但真实 Codex 子树、
  AutoCAD 异常退出、嵌套 Job/受限桌面环境和失败清理后的用户可理解诊断仍待验收。

## 10. M5：AutoCAD 2016 安全写入最小闭环

完成定义：Codex 可安全创建一条直线，但无法绕过预览、审批和事务边界。

现有 `CadApprovalGate` 和 `IAgentCadProposalBroker` 保留；Host.2025 的
`LineWriteWorkflow` 只作原型参考，不是 2016 产品证据。

- [ ] Host.2016 实现真实 CAD Broker 和主线程调度。
- [ ] 首个唯一写入工具保持 `create_line`。
- [ ] Agent 只能提交强类型计划。
- [ ] 计划进入审批前完成 Schema、策略、数量和坐标验证。
- [ ] 生成确定性预览和增删改摘要。
- [ ] 零修改、无效几何和不可预览计划直接拒绝。
- [ ] 审批只有拒绝和一次允许。
- [ ] token 绑定图纸、revision、选择、空间、图层和计划哈希并防重放。
- [ ] DocumentLock 内重新核对全部状态。
- [ ] 使用一个事务和一个 Undo 边界。
- [ ] 失败全部回滚；结果不确定时停止并要求人工检查。
- [ ] 取消或断线后不再开始新写操作。
- [ ] 写入成功后不调用保存。
- [ ] 记录结构化执行结果和审计事件。
- [ ] 实机覆盖批准、拒绝、过期/重复 token、状态变化、取消、事务异常、Undo 和关闭提示。

## 11. M6：写入类型扩展与恢复

完成定义：支持操作形成稳定白名单；未列入白名单的操作无法由 Agent 执行。

按风险开放：

1. Circle、Arc、Polyline、DBText、MText。
2. 插入已有 Block、Dimension、Hatch。
3. 修改图层、颜色、文字和有限几何参数。
4. Move、Copy、Rotate、Scale。
5. 删除、批量替换和其他高风险操作最后开放。

每种操作必须有强类型契约、限额、风险等级、预览、锁内重校验、事务、Undo、回滚、
不确定结果处理、自动化和 AutoCAD 2016 实机证据。批量或删除操作需要用户明确批准的
恢复检查点。禁止以命令字符串、LISP 或脚本实现。

## 12. M7：会话、长期记忆和恢复

完成定义：重启后可恢复安全的聊天和任务信息，但不会自动继续未确认写入。

- [ ] 建立系统 conversation、turn、request 和 CAD operation ID。
- [ ] Codex thread ID 仅作为内部映射元数据。
- [ ] 使用 SQLite 保存图纸级会话、摘要、用户偏好和任务终态。
- [ ] 默认不保存完整 canonical JSON、真实图纸路径或敏感提示词。
- [ ] 图纸身份使用稳定但脱敏的标识。
- [ ] 支持记忆启用、暂停、查看、导出和清除。
- [ ] 支持保留期限和容量限制。
- [ ] 写入审计使用追加式记录和哈希链。
- [ ] 实现迁移、损坏检测、备份和崩溃恢复。
- [ ] 重启后不自动恢复任何未确认 CAD 写入。

## 13. M8：UI、设置和可用性

完成定义：普通用户无需理解 thread、JSON、AgentHost 或实体内部类型即可判断状态和操作。

- [ ] 整图扫描进度、取消和完整性。
- [ ] 可解析、不支持、超限和失败对象的中文统计。
- [ ] 明确区分当前选择、整张图纸和当前对话。
- [ ] 新对话、清除 CAD 上下文、清除全部。
- [ ] 流式回答、取消、失败、断线和重连状态。
- [ ] 写入计划、预览、风险、一次审批和执行结果。
- [ ] Codex 路径、健康、超时、沙箱和记忆设置。
- [ ] 日志导出前自动脱敏。
- [ ] 100%、125%、150%、多显示器和 Windows 缩放测试。
- [ ] 键盘操作、中文输入、无障碍和小尺寸 Palette。

Kimi 可以后参与 UI，但项目不以 Kimi 可用为继续开发的前置条件。

## 14. M9：工程质量、CI 和性能

完成定义：任何提交都不能绕过关键构建、安全和回归门禁。

- [ ] 建立 Windows CI。
- [ ] 固定 .NET SDK、离线 net45 参考程序集和依赖锁。
- [ ] 自动运行 Contracts、IPC、Bridge、AppServer、AgentRuntime、Host 和安全 Specs。
- [ ] R20.1 API Probe、禁止 API、秘密扫描和包身份检查成为门禁。
- [ ] 让完整依赖闭包的独立 R20.1 构建可位级复现。当前同一新鲜依赖闭包下 Host A/B
  输出一致；独立依赖构建仍会改变 timestamp、MVID 和 TargetFramework metadata，虽然
  归一化后的 Host IL 主体相同，不能把这一诊断当作完整可重复构建已完成。
- [ ] 增加覆盖率报告，关键状态机要求分支覆盖。
- [ ] 对协议、JSON、游标和审批状态机进行属性或模糊测试。
- [ ] 增加并发、断线、取消、迟到事件和重复请求压力测试。
- [ ] 建立 1k/10k/50k 性能回归。
- [ ] 建立 AgentHost 长运行和反复启停 soak test。
- [ ] 生成 SBOM、第三方许可证和依赖漏洞报告。
- [ ] 每个可验证阶段单独提交。

## 15. M10：.bundle、签名和企业部署

完成定义：不依赖源码目录、手工环境变量或开发工具即可安装运行。

- [ ] 完成真实 .bundle 和完整命令注册。
- [ ] 包含 Host、AgentHost、依赖、哈希 sidecar、默认配置和文档。
- [ ] DLL、EXE、Bundle/安装包数字签名和可信时间戳。
- [ ] 支持当前用户和全机安装。
- [ ] 支持安装、升级、修复、卸载和回滚。
- [ ] 升级时安全迁移配置和 SQLite。
- [ ] 卸载后无进程、计划任务、PATH 污染或危险残留。
- [ ] 普通用户、管理员、干净机和无开发工具环境验收。
- [ ] SECURELOAD、TRUSTEDPATHS、AppLocker、WDAC 和常见 EDR 验证。
- [ ] 企业静默部署、版本锁定和禁用写入策略。
- [ ] 用户、管理员、故障排查和应急响应文档。

## 16. M11：AutoCAD 2025 兼容

完成定义：2016 与 2025 共享产品核心，版本差异仅位于 Autodesk Host 适配层。

- [ ] 只在 AutoCAD 2016 GA 后开始正式移植。
- [ ] 复用 Contracts、AgentHost、Bridge、安全和存储核心。
- [ ] Host.2025 保持独立薄宿主。
- [ ] 不把当前未提交原型直接当作产品实现。
- [ ] 完成 2025 Palette、整图索引、查询和安全写入。
- [ ] 使用原版 2025 托管程序集编译。
- [ ] 重复功能、生命周期、DPI、写入、安全和安装矩阵。
- [ ] 两个版本使用独立产物和 evidence，共享协议版本。

## 17. M12：最终发布验收

项目仅在以下全部成立后完成：

- [ ] AutoCAD 2016 全部功能与故障矩阵通过。
- [ ] 50,000 对象级整图扫描和查询达到冻结性能预算。
- [ ] 未知对象不会导致整图失败。
- [ ] CAD 写入无法绕过预览和一次审批。
- [ ] 写入失败可回滚，成功操作可一次 Undo。
- [ ] 插件永不自动保存。
- [ ] 异常退出无残留进程。
- [ ] 沙箱、凭据、日志和长期记忆安全审计通过。
- [ ] 安装、升级、修复、卸载和回滚通过。
- [ ] 普通用户、干净机和企业策略环境通过。
- [ ] 版本、签名、SBOM、evidence 和文档全部冻结。
- [ ] 完成独立安全复核和发布回滚演练。
- [ ] 若 AutoCAD 2025 仍属于范围，其验收也全部通过。

## 18. Worktree、隐私和阶段纪律

- 主工作树含用户所有的 Host.2025 UI、选择和写入原型及未跟踪文件；不得清理、覆盖或
  混入 AutoCAD 2016 阶段提交。
- MiMo、Kimi 或其他 Agent 交付只作为候选，必须复查 Git、源码、门禁和证据。
- 不把真实图纸路径、图名、选择/上下文哈希、用户名、受信/网络路径、许可证、API Key、
  token、完整环境变量或 canonical JSON 写入 Git 文档与普通日志。
- 删除 Worktree 前必须确认：提交已合并或有保留分支/标签、工作树干净、必要 evidence 与
  artifact 已归档。删除 Worktree 不等于删除分支；删除未提交工作树会丢失本地修改。
- 每个阶段按“最小范围实现 -> Release/Specs/安全门禁 -> 冻结候选 -> 用户实机 ->
  脱敏 evidence -> 独立提交”的顺序推进。

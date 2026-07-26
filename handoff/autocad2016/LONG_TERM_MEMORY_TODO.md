# AutoCAD 2016 项目长期记忆与完整待办

最后更新：2026-07-26（北京时间）

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
- 集成分支：`codex/m1-integration@ebba703`，从 `main@9edc83e` 受控吸收 10 个提交；
  树与来源 `88c0a29` 一致，无 Host.2025/Kimi/M4 夹带。
- 候选：`autocad2016-m1-readonly-v033-e6701a77-4b602965-561c6af3`。
- Host SHA-256：
  `E6701A771D17EC3EC8B2CA7DA78B553E27897639DC48B3BC0435F07249C9B5F6`。
- AgentHost SHA-256：
  `4B60296581224ADCDF1E8B0C8F1C766AE896796DA2DCF0B73E5EEFE6BBFE6966`。
- manifest SHA-256：
  `B081B93A6BE99D8D16304A3A1B2EABD93D352E92613F370C5450E448E8507E40`。
- 自动化：Host MVP `41/41`；PowerShell 7 与 Windows PowerShell 5.1 均为 Phase 2
  `276/276`；25 文件 Host.2016 只读闭包、
  R20.1/net45/x64 双构建位级一致、候选 AgentHost doctor、diff 和敏感信息扫描通过。
- 证据：
  `cad-context-v2-candidate-build-autocad2016-m1-readonly-v033-e6701a77-4b602965-561c6af3.json`。
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
- 源码提交：`34cef1214ad22822996db4e4ad33013f855751e3`。
- 候选：`autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024`。
- Host SHA-256：
  `BC6011D3C0C00222BE266E27A26770B87FC4CE542A9516640AEC1A959950C5D5`。
- AgentHost SHA-256：
  `6DE30DB91C466CA0CA87E6202926FB893165CE8950B1CCAB9E0E3C49650CDD89`。
- manifest SHA-256：
  `CDE0E31D9B2342B322D1850224B6DE78755B97EAEF7802C7D609F86E58E7D917`。
- 自动化：Contracts net8/net45 `88/88`、Bridge Client net8/net45 `29/29`、
  Bridge/AgentHost `39/39`、AgentRuntime `34/34`、Host MVP `54/54`、完整 Phase 2
  `314/314`、benchmark fixture/evidence `6/6`、R20.1 Release、30 文件只读闭包和 Host
  A/B 位级一致通过。
- 证据：
  `m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-bc6011d3-6de30db9-a43ac024.json`。
- 说明：`M2_DRAWING_INDEX_VERTICAL_SLICE_20260722.md`。
- 实机入口：`M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md`。
- 旧 `E85D97EC...` 和 `597A7A3D...` 候选仅保留为历史记录，不得继续用于当前验收。

M2-A/M2-B 代码与自动化已完成：

- [x] 保留 CadContextJson v2 作为兼容选择快照，64 实体/256 KiB 上限未放大。
- [x] 新增版本化 DrawingIndex v1 和 CadQuery v1 契约及 fail-closed 验证器。
- [x] 支持选择集、当前空间、模型空间、布局和整张图纸扫描范围。
- [x] AutoCAD API 在文档线程的 Idle 小片、DocumentLock 和 ForRead transaction 中读取；
  索引只保留深拷贝强类型摘要。
- [x] 支持进度、幂等取消、2 分钟超时、100,000 实体索引和 64 MiB 估算预算。
- [x] 建立类型、图层、空间、块、包围盒、文字、对象令牌和数量摘要。
- [x] 对象身份使用 `obj-########` 不透明令牌，不向 Agent 暴露 AutoCAD Handle。
- [x] 支持 Host 随机生成的 `dq1_...` 游标分页；游标五分钟过期，并绑定索引、revision、
  查询形状和 offset，不一次发送整图 JSON。
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
- [x] `BlockTableRecordEnumerator` 在同一个有效只读 transaction 内创建、遍历并释放，
  不再跨 Idle 或 transaction 保存 Autodesk 枚举器。
- [ ] M2.3：每个 space 的 ObjectId 仍在单个 preparation Idle 回调内形成托管数组；必须
  使用精确候选证明 50k 最大 preparation 分片低于 20 ms，或继续拆分准备阶段。
- [ ] M2.13：完成 1k/10k/50k 响应性、耗时、工作集、取消和 DBMOD 实机性能资源门禁。
- [ ] M2.14：完成精确候选的五种范围、本地/Agent 查询、失效、退出清理和整图候选冻结。

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

当前自动化冻结候选为 `0.4.2.0`，由源码提交
`00fe879a0ac056fab48c955e71d63c51ef3577d9` 精确生成。候选 ID、哈希、自动门禁和人工
边界见 `M3_CAD_READ_SEMANTICS_OBJECT_TEST_20260723.md`；尚无 AutoCAD `NETLOAD` 证据。

- [x] 在选择快照、整图索引、Palette 和诊断中按实际类型/数量显示未支持、数据超限和
  读取失败对象；统计不包含图层、Handle、路径或对象内容，且类型桶有界。
- [x] 新增 `CODEX16TYPEINFO`，为 19 类现有强类型对象列出中文名称和人工创建入口。
- [x] 为选择统计、DrawingIndex 累积边界、中文目录和真实 mapper → 可读摘要调用链增加
  源码级回归；当前自动门禁为 Contracts `96/96`、Bridge Client net45/net8 各 `30/30`、
  Bridge `39/39`、AgentRuntime `34/34`、Host MVP `54/54`、完整 Phase 2 `323/323`。R20.1
  API 双 Shell Probe 为 `29 passed / 8 expected failed`；目标 R20.1/net45/x64 Host A/B
  输出逐字节一致，当前 Host SHA-256 为
  `467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D`，Autodesk DLL 复制数
  为 `0`。这些是冻结候选自动化证据，不是实机证据。
- [x] 提供 M3 中文对象目录、首要字段和未来实机记录模板。
- [x] `BlockReference` 的受限 `blockDetails` 已贯通 DrawingIndex → CadQuery → 认证 Bridge →
  Agent 工具；属性/动态属性、嵌套块、布局和安全 Xref 元数据均受契约、深拷贝、内存预算和
  IPC 测试保护。外部 Xref 定义及真实路径不会读取或传播，详情降级为 `limited`。
- [x] M3 块读取所需 R20.1 API 已由双 Shell Probe 固定为 `29 passed / 8 expected failed`；
  脱敏 evidence 为 `evidence/v2-api-surface-probe-m3-cross-shell-20260723.json`。
- [x] 冻结含 14 个实体记录的确定性脱敏核心 DXF fixture；双次生成、独立解析和 manifest
  哈希门禁为 `6/6`。它是自动化核心资产，不替代 19 类 AutoCAD 实机字段矩阵。
- [x] Region、Solid、Mesh、Surface、RasterImage、Underlay、Proxy、Wipeout 在
  DrawingIndex 中作为可查询的 `data_limited` 类别安全降级，不伪装完整 payload。
- [ ] 补齐无法由核心 DXF fixture 表达的 19 类对象/复杂块脱敏 AutoCAD 测试图。
- [ ] 逐类实机验证现有 19 种强类型对象。
- [ ] 用精确 M3 候选实机核对块属性、动态块、嵌套块、布局和安全 Xref 降级；复杂块语义
  与异常图仍需扩展。
- [ ] 完善 Dimension、Hatch、Leader、MLeader、Table 的 R20.1 字段语义。
- [ ] 实机确认 Region、Solid、Mesh、Surface、Image/Underlay、Proxy、Wipeout 的
  `data_limited` 查询结果、范围摘要和无路径泄露边界。
- [ ] 长文字、复杂 Hatch、Table、Spline 受限但不拖垮整体。
- [ ] 每类对象具有契约、边界、R20.1 API Probe 和实机字段证据。

## 9. M4：进程沙箱、配置和审计基础

完成定义：AutoCAD 或 AgentHost 异常退出后进程树可确定清理；凭据和用户配置不进入日志或
普通工作目录。此阶段是启用 CAD 写入前置条件。

当前受控集成检查点为 `codex/m4-integration@15352ff`：

- M4.1 部分完成：本机 Codex 绝对路径、工作目录、启动/关闭超时已进入真实 AgentHost
  调用链；允许版本、请求超时、日志和资源策略尚未形成完整配置层。
- M4.2 的代码和自动化基础完成：自动发现、固定可执行文件身份租约、版本窗口
  `>=0.144.4 <0.145.0`、启动前版本预检和 `app-server initialize` 健康检查已进入真实
  调用链；正式 M4 候选的 AutoCAD 启停/退出实机仍缺，因此不扩大成 M4.16 完成。
- M4.3 的可选 session-home、显式环境白名单、空 MCP/插件、租约和安全清理基础已完成；
  M4.8 ACL/lease 自动化切口已完成，生产默认仍因空 home 未登录而保持关闭，等待 M4.11
  凭据 Broker。
- M4.4/M4.5 已在当前分支受控提交为 `0763022`：公共产品面不暴露实验身份，internal-only
  RestrictedToken 探针跨机器接受结构化结果且绝不回退 CurrentUser。本机探针结果为
  `available/child_exited`，不是认证成功或生产沙箱证据。
- M4.6 当前切口已通过自动化和双 Shell 阶段门禁并包含在 `15352ff`：`KILL_ON_JOB_CLOSE`
  覆盖 AgentHost 及
  普通后代，分配前检测任意 Job、分配后反查目标 Job；当前 Windows 已真实通过外层/内层
  嵌套 Job 分配、Stop/AgentHost 异常/owner 退出回收、资源限制和连续 `500` 次 service
  启停回收。企业组策略、Windows 版本与宿主 Job 组合矩阵仍缺，因此该
  子目标不能标为全部完成。
- M4.8 自动化切口已进入真实启动/停止链：每个系统 session 拥有受保护 ACL 的
  `workspace`、`audit`、`codex-home`、schema marker 和活动 lease；拒绝 UNC、device path、
  junction/reparse 根和目录替换，支持并发 session、清理失败重试、`24 h` 过期策略和有界
  崩溃恢复。无 marker legacy 目录及活动 lease 保持不动。企业/AutoCAD 实机矩阵和独立提交
  仍缺，`codex-home` 在 M4.11 凭据 Broker 完成前继续禁用。
- M4.9 当前切口已把 Job 进程数、总提交内存、CPU rate、累计用户时间、服务墙钟和受检的
  `0–30 s` 停止宽限接入真实默认启动路径；非法配置 fail-closed，配置值真实进入 Stop。
  Job completion port 与 Host 有界仲裁已让进程数、Job 内存、累计用户时间和墙钟耗尽产生
  稳定、脱敏、不可自动重试的结构化终态；普通 Bridge fault 不能抢先覆盖资源原因。Job 内存
  与用户时间组合耗尽遵循首个权威终态。明确不启用默认 working-set hard limit，Job 总提交
  内存是硬边界，working set 只做性能 telemetry。真实 Codex/AutoCAD 耗尽矩阵和企业配置
  策略仍缺。
- 当前未提交 M4.11 配置/读取切口的专项 AgentLauncher net45/net8 各为 `60/60`，
  Host MVP 为 `56/56`，双 Shell Phase 2
  均为 `360/360`、
  Bridge `49/49`、认证兼容 net45/net8 各 `35/35`、Release `0 warning / 0 error`；
  阶段证据为 `evidence/agent-bootstrap-verification-20260719.json`；最终 SHA-256 由门禁输出
  记录，避免文档和证据 manifest 间接自引用。R20.1 双 Shell Probe 仍为
  `29 passed / 8 expected failed`。这只证明当前切口没有破坏 M3/R20.1，只读回归包不是
  M4.16 安全候选。

- [x] M4 第一诊断边界：Codex 子进程 stderr 持续排空且只传递有界 `bytes`/`truncated`
  摘要，进程退出事件等待该摘要形成但不阻塞进程事件线程；AgentHost 不再向控制台公开
  stderr 原文、协议异常正文、工作目录或 `CODEX_HOME` 路径。它尚未形成完整沙箱或 M4 候选，详情见
  `M4_PROCESS_ISOLATION_BASELINE_20260723.md`。
- [x] M4 本机 Codex 启动配置：`--codex`、`CODEX_EXECUTABLE`、npm 和绝对 PATH 发现会收敛为
  固定本地磁盘的绝对 `codex.exe`；显式无效配置不回退，工作目录和启动/关闭超时同样受检。
  doctor 只记录来源标签。说明见 `M4_LOCAL_CODEX_CONFIGURATION_20260723.md`。
- [x] 冻结当前产品支持的 Codex 版本范围 `>=0.144.4 <0.145.0`，版本预检与
  `app-server initialize` 绑定同一可执行文件身份租约；正式候选实机仍属于 M4.16 门禁，
  详见 `M4_2_CODEX_HEALTH_PREFLIGHT_20260724.md`。
- [ ] 每会话独立 CODEX_HOME：`6d99bb9` 已完成可选配置、32 字节系统 session ID 租约、空 MCP/
  插件配置、稳定错误和安全清理基础，M4.8 又完成受保护 ACL/lease 自动化切口；生产默认仍未
  启用。不得复制、链接、解析或记录全局 Codex profile，必须先完成 M4.11 凭据 Broker。见
  `M4_3_CODEX_SESSION_HOME_BASELINE_20260724.md`。
- [ ] 独立凭据边界：已完成默认禁用配置、产品专属 Windows Credential Manager target、
  Generic Credential 的 `4 KiB` 有界二进制读取、稳定脱敏错误、Dispose 原位清零、认证
  一次性凭据帧、隔离 `CODEX_HOME` 和 stdin 登录调用链；fake login 已覆盖成功、非零退出、
  `auth.json`、超时、取消及 argv/环境不含 token。真实 Credential Manager、Codex/keyring、
  `auth.json` 生产行为、恢复、过期、撤销、RestrictedToken 全链和实机矩阵尚未验证。
  全局 profile 不能作为复制来源。见
  `M4_11_CREDENTIAL_BROKER_BOUNDARY_20260725.md`。
- [x] 子进程显式环境白名单基础：真实 transport 使用 `InheritParentEnvironment=false`，只保留
  必要 Windows/profile/temp/代理变量和受控 PATH；显式 session `CODEX_HOME` 会覆盖父值。完整
  凭据、身份和企业代理策略仍分别由 M4.7/M4.11/M4.15 验收。
- [x] AgentHost 启动链在恢复前创建 `KILL_ON_JOB_CLOSE` 的未命名 Windows Job Object 并分配
  已校验的 AgentHost；普通后代继承该边界。当前切口还验证任意 Job 成员检测、目标 Job
  成员反查、当前 Windows 嵌套 Job、Stop/AgentHost 异常/owner 退出回收、资源限制和连续
  `500` 次 service 启停，AgentLauncher net45/net8 各 `57/57`。企业嵌套 Job 策略矩阵、
  真实 AutoCAD 退出和完整僵尸进程实机仍未验收，详见
  `M4_PROCESS_ISOLATION_BASELINE_20260723.md`。
- [x] 收回未成熟 RestrictedToken 公共入口，并将能力探针限制为 internal-only；公共配置、
  结果和 doctor 不暴露实验身份选择或原始 telemetry。
- [x] RestrictedToken 探针已改为跨机器可移植结果模型；net45/net8 均禁止 CurrentUser
  回退。本机为原语 `available`、认证前 `child_exited`，不能记作 M4.7 生产身份成功，详见
  `M4_4_M4_5_RESTRICTED_IDENTITY_PROBE_20260724.md`。
- [ ] 在 RestrictedToken 或预配置 AppContainer 下实现生产身份隔离；必须完成真实
  AgentHost/Codex bootstrap、Pipe、STOP、最小 DACL 和越权拒绝矩阵。
- [x] Job CPU rate、总提交内存、进程数、累计用户时间和服务墙钟硬限制已进入真实默认启动
  路径；停止宽限为受检 `0–30 s` 配置，默认 `1 s`。进程数、Job 内存、累计用户时间和墙钟
  已有权威结构化终态、Bridge fault 竞态门禁、组合耗尽与错误脱敏。working set 明确只做
  telemetry，不启用默认硬限制；真实 Codex/AutoCAD 和企业策略仍待 M4.9 收口。见
  `M4_9_RESOURCE_LIMIT_TERMINALS_20260724.md`。
- [ ] 工作目录磁盘硬配额属于 M4.10，不能用 Job 内存限制或目录大小统计替代。
- [x] M4.8 自动化切口：工作目录使用受保护最小 ACL、活动 lease、固定 marker 和有界过期
  清理；STOP/启动失败/owner 崩溃路径已验证。企业/AutoCAD 实机矩阵和独立提交仍待完成。
- [ ] 将凭据访问收敛为不向 argv、普通环境变量、日志或工作区暴露原始秘密的 Broker；配置和
  有界读取切口已通过 net45/net8 `60/60` 与双 Shell Phase 2 `360/360`，但传输、登录和生产
  受限身份仍缺。在获得受支持的认证恢复机制前不得复制或解析用户 Codex profile。
- [x] 将 AgentRuntime、Bridge、Host、配置和日志导出纳入同一脱敏策略；已完成统一
  Contracts sanitizer、Bridge 公开异常/反向查询错误响应、AppServer 公共异常与 stderr
  事件隔离、AgentLauncher bootstrap 失败、AgentHost `doctor`/`run` 成功状态最小化及
  CLI/审计最外层失败边界、Host.2016 UI/CadQuery 公共错误、AgentRuntime 公共诊断事件和
  动态工具校验错误出站脱敏，并完成其余公共出口静态复核。真实环境故障验证归入 M4.15。
- [x] M4.12 bounded JSONL 已覆盖启动、停止、请求、取消、断线、审批和现有终态；并发写入
  保持完整单调，部分磁盘写入后永久 fail-closed。CAD 写入终态要等 M5 调用链接入。
- [x] M4.13 已接通 audit/2 哈希链、独立受保护持久 segments/anchors、STOP 后保留、自动轮转、
  CreateNew 防覆盖以及删除/插入/修改/截断/anchor/跨段重排检测。
- [x] M4.13 脱敏导出核心在导出前强制验链，只输出白名单字段并省略 Provider thread/turn、
  payload 和路径。
- [x] M4.13 增加只读 `AgentHostAuditCatalog`：在真实受保护审计根上分类 `complete`、
  `incomplete`、`corrupt`、`anchor_mismatch`；只有 `session_stopped/session_failed` 终态链才是
  `complete`，无终态崩溃前缀标为 `incomplete/session_not_terminal` 并禁止导出。临时 anchor、
  缺段、缺 anchor、链损坏和 session 身份/anchor 不一致均保持 fail-closed，不自动修复、删除
  或覆盖；当前 Bridge Specs `71/71`。
- [x] M4.13 受控 `audit-export --session <system-session-id>` 已接入：固定当前用户受保护根，
  不接受任意路径或输出目标，只导出 `complete` 会话；先在内存中验链和序列化，失败不产生
  半份 JSON，并返回稳定错误码；无终态崩溃前缀同样拒绝。当前双 Shell Phase 2 为 `387/387`。
- [x] M4.13 新增只读 `audit-retention-plan`：显式年龄、容量和最低完整会话保留策略；
  只对完整终态会话产生年龄/容量候选，非终态、损坏和 anchor mismatch 固定人工复核，未知
  artifact 计入容量但不自动清理；计划不含路径且不改变任何文件。
- [x] M4.13 新增显式 `audit-retention-apply --plan <id>`：执行前重新验链和重算计划，独立
  受保护 control 目录持有排他锁、耐久 journal 和幂等 receipt；journal 在首删前绑定精确段数、
  文件元数据及 SHA-256。中断可恢复，计划变化、文件篡改、日志损坏、不同计划冲突和并发执行
  均失败关闭。Bridge Specs `71/71`，双 Shell Phase 2 `387/387`。
- [x] M4.13 Bridge Specs 专用子进程已覆盖真实执行器跨进程强杀恢复：journal 耐久提交且删除
  首个 anchor 后由父进程强杀，新租约以原 plan ID 恢复，最低保留会话未删除、journal 清除且
  无残留工作器。该证据不等同于系统断电或真实生产 AgentHost/AutoCAD 异常退出。
- [x] M4.13 已知 control artifact 有界收敛：最近 `256` 份 receipt 保留为精确幂等证据，更旧
  receipt 在删除前逐份耐久折叠到固定累计链检查点；检查点提交后中断可恢复且不重复累计。
  有有效 final receipt 的 foreign temp 会自动清除，无 final 的 foreign temp 保持冲突。
- [x] M4.15.5a 增加控制区人工复核状态：只读 `controlStatus` 将合法 journal/temp 标为
  `recovery_required`，将未知文件/目录、reparse、超限/不可读和严格 schema 无效 artifact 标为
  `manual_review_required`；输出不含路径、文件名或内容。`audit-retention-apply` 持锁后复检，
  未知/危险/inventory 不完整时以稳定同名原因码拒绝且保留原证据。Bridge `81/81`、双 Shell
  Phase 2 `419/419`；这不等同于真实磁盘满、断电或企业归档验证。详见
  `M4_15_RETENTION_CONTROL_REVIEW_20260726.md`。
- [x] M4.15.5b 增加受控持久化 I/O 故障安全夹具：审计数据写入和独立锚点提交失败后永久
  fail-closed，Bridge 会话终止且不会补写第二终态；retention 在 journal/receipt/checkpoint 原子
  提交边界把文件系统故障统一映射为 `cleanup_failed`。journal 提交前不删除 artifact，提交后
  保持 `recovery_required`；同一 plan ID 重试只收敛一次，再次执行固定 `already_applied`，公共
  stderr 不含原始异常、路径或内容。Bridge `83/83`、双 Shell Phase 2 `421/421`、Launcher
  net8/net45 各 `65/65`。这些是 synthetic 自动化证据，不等同于真实磁盘满、卷离线或断电。
  详见 `M4_15_PERSISTENCE_IO_FAILURE_20260726.md`。
- [x] M4.15.6 自动化收口 evidence：`verify-phase2.ps1` 增加可选机器可读 JSON；新增
  `verify-m4-r201-host-build.ps1` 对当前 Host.2016 执行两次 R20.1/net45/x64 Release 构建并
  验证位级一致、0 warning/0 error、Autodesk DLL 复制数 `0`；新增
  `verify-m4-automated-readiness.ps1` 严格绑定双 Shell Phase 2 `421/421`、Agent bootstrap、
  认证原语、Host/AgentHost 候选哈希、源码 manifest、锁文件、用户 PATH 长度/哈希、秘密/API
  扫描和残留进程 `0`。汇总器 PowerShell 7/5.1 自检和正式输出均通过，输出语义等价且不含
  本机路径或环境内容。状态固定 `automated_readiness_only`，所有真实机器和企业矩阵仍为
  `false`，不得称为 M4 完成或 M4.16 冻结。详见 `M4_15_AUTOMATED_READINESS_20260726.md`。
- [ ] M4.13 继续补齐企业默认保留期/容量、系统断电及真实生产 AgentHost/AutoCAD 异常退出恢复，
  并决定是否用签名/HMAC 提升同用户篡改抵抗；完成企业/AutoCAD 实机矩阵后才能关闭。
- [ ] 为已能明确标记的未知、恶意或无法归属 control artifact 定义企业人工复核/归档流程、
  归档目的地、ACL、审批、审计和恢复规则；不得猜测删除。
- [ ] 继续执行真实磁盘满/NTFS 配额、卷离线、杀毒/EDR、系统断电和生产进程僵尸/异常退出
  故障矩阵；M4.15.5b synthetic I/O 夹具不得替代这些实机或企业证据。
- [x] M4.14 第一个真实纵切：新增按来源分类的 `DiagnosticSanitizer`，以 `4096` 输入和 `512`
  输出硬上限清除 Bearer/敏感键值、带引号 JSON secret、Windows/UNC 路径、URI、身份和危险
  控制字符；正则超时固定 fail-safe。Bridge 客户端公开异常保留稳定 `Code`，消息统一清洗且
  不再保留原始 inner exception；反向整图查询跨进程错误响应已验证。Contracts `97/97`、
  Bridge.Client `31/31`、Host.2016 MVP `57/57`。
- [x] M4.14 AppServer 公开异常纵切：stderr 继续保持无文本摘要；RPC code 保留但 message 统一
  清洗，原始 JSON data 不再保留，只公开 data-presence 和脱敏 flags；通用/协议异常不再保留
  任意原始 inner exception。AppServer Specs 由 `32/32` 增至 `34/34`。
- [x] M4.14 AppServer 显式分类纵切：公开异常携带 `DiagnosticClassification` 和数值
  `DiagnosticRedactions`；配置/版本预检、RPC、通用/协议异常分别归入稳定闭集。AppServer
  Specs 增至 `35/35`。
- [x] M4.14 AgentHost 未知命令纵切：任意首参数先按 `Configuration` 分类并统一脱敏，JSON
  只输出清洗命令、数值脱敏计数和固定 usage；Bridge Specs 先 RED `71/72`，后 GREEN `72/72`。
- [x] M4.14 诊断变体与异常图纵切：覆盖设备命名空间路径、带空格/引号路径、转义 JSON secret
  和完整 URI 变体；嵌套/AggregateException 最多遍历 `16` 节点、深度 `8`，引用去重且不保留
  异常对象、堆栈或 `Data`。Contracts 由 RED `97/98`/缺 API 编译失败收敛到 `99/99`。
- [x] M4.14 AgentLauncher bootstrap 失败纵切：配置/Credential Manager、进程环境、stderr 和
  其余失败映射稳定分类；固定安全消息和错误码不变，只保留数值脱敏证据。专项 net8/net45
  各 `63/63`。
- [x] M4.14 AgentHost 成功状态纵切：`doctor`/`run` 使用最小公共 DTO，不再公开 App Server
  `userAgent`、`platformOs`、`platformFamily` 或 `codexHome`；Bridge Specs `73/73`，
  双 Shell Phase 2 `392/392`，Release `0 warning / 0 error`。
- [x] M4.14 Host.2016 公共错误边界纵切：`MvpAgentFailure.FormatForUser` 在结构化格式化后
  强制执行统一有界 sanitizer；Palette/Bridge 断线和 `CODEX16QUERY`/`CODEX16QUERYNEXT`
  命令行不再公开未经处理的异常消息，身份正则覆盖域账号/邮箱紧邻中文。Host.2016 MVP
  `58/58`，双 Shell Phase 2 `393/393`。
- [x] M4.14 AppServer 协议故障事件纵切：`ProtocolFaulted` 不再向公共订阅者转交任意原始
  `Exception`；兼容异常投影只含固定安全消息，不保留原对象、StackTrace、`Data` 或 inner
  graph，分类与脱敏标志单独结构化公开。AppServer 先 RED `35/36`，后 GREEN `36/36`；
  双 Shell Phase 2 `394/394`。
- [x] M4.14 AgentHost CLI 失败纵切：`doctor/run` 通用失败不再公开 CLR 类型名，统一输出
  `agenthost_cli_failure`、`agenthost_internal_error`、`errorStage=agenthost_cli`、来源分类和
  数值脱敏标志；已知配置、版本预检和健康错误码保持兼容。协议故障 stderr 只输出固定码、
  分类和数值标志；`bootstrap-doctor/bootstrap-serve` 非法参数与其余失败也使用稳定错误码，
  不改变认证帧或 Launcher 只计数字节的 stderr 边界。Bridge 依次 RED `73/74`、编译 RED、
  `75/76`，最终 GREEN `76/76`；双 Shell Phase 2 `397/397`，Release `0 warning / 0 error`。
- [x] M4.14 AppServer 服务端请求失败响应出站纵切：处理器返回的失败在唯一
  `WriteErrorAsync` 边界统一按 `RemoteError` 分类脱敏；保留 JSON-RPC 数值 code，不再把
  原始 message 或任意 JSON data 写回本机 Codex 子进程，只回传有界安全 message、分类、
  数值脱敏标志和 data-presence。真实传输规格先 RED `36/37`，后 GREEN `37/37`；双 Shell
  Phase 2 `398/398`，Release `0 warning / 0 error`。
- [x] M4.14 AgentHost 审计 CLI 最外层失败纵切：`audit-export`、`audit-retention-plan` 和
  `audit-retention-apply` 统一经过共同捕获边界；未预期异常只输出固定
  `agenthost_audit_failure`、稳定 error code、`errorStage=agenthost_audit`、来源分类和数值
  脱敏标志。已有非法参数、预期拒绝和闭集 ReasonCode 保持兼容。规格以缺少共同入口的编译
  RED 复现，最终 Bridge `77/77`、双 Shell Phase 2 `399/399`、Release
  `0 warning / 0 error`。
- [x] M4.14 AppServer stderr 观察者隔离纵切：Client 逐观察者分发并把异常投影为既有安全
  `ProtocolFaulted`；底层 `CodexProcessTransport` 同样隔离观察者，不能中断排空、退出传播或
  后续观察者。先 RED `37/38`、GREEN `38/38`，再以真实子进程 RED `38/39`、GREEN `39/39`。
- [x] M4.14 AgentRuntime 公共诊断纵切：`ProjectionFailed`/`EventObserverFailed` 只公开新建
  固定消息异常快照、分类和数值脱敏标志，不保留原始异常、StackTrace、`Data` 或 inner graph；
  动态工具校验失败在进入内部事件和回传 Codex 前按 `RemoteError` 脱敏。失败 turn 的完整
  Provider JSON 已收敛为 `id`、`status` 和脱敏 `error.message`，observer 失败也不再持有原始
  Agent 事件，只保留事件类型安全快照。先编译 RED 后 `35/35`，再 RED `35/36`、GREEN
  `36/36`，最后两个真实泄漏 RED/GREEN 收敛到 `38/38`；双 Shell Phase 2 `405/405`。
- [x] M4.14 Bridge terminal 公共诊断纵切：notification handler/transport 原始异常不再从
  `Completion` 或 `TerminalError` 外逃；公共终态使用固定消息 `BridgeTerminalException`，
  只保留分类和数值脱敏标志，强类型 authentication/capacity/protocol 语义及迟到 fault 观察
  保持不变。Bridge 完整回归 `77/77`。
- [x] M4.14 Host.2016 通用命令 catch 纵切：DrawingIndex 启动、CadQuery 和 CadQuery 下一页
  不再输出 `exception.GetType().Name`；统一返回 `internal_error`、稳定 stage、`Exception`
  分类和数值脱敏标志，原始异常图在计算标志后立即丢弃。规格先编译 RED，后 Host.2016 MVP
  `59/59`、双 Shell Phase 2 `406/406`；R20.1/.NET Framework 4.5/x64 产品和四个 net45
  依赖均 `0 warning / 0 error`。
- [x] M4.14 配置与公共 record 字符串纵切：`CodexLocalAppServerConfigurationRequest`、
  `AppServerClientOptions` 不再展开路径、完整 PATH、启动参数或环境；AgentRuntime 的
  runtime/thread/turn options、thread handle 和 text/local-image/mention 输入不再展开路径、
  提示词、Provider 标识或 schema；`BridgeRequest`/`BridgeNotification` 不再展开 method、
  identity 或完整 `BodyJson`。真实 RED 分别为 AppServer `39/40`、`40/41`，AgentRuntime
  `38/39` 和 Bridge 专项 `0/1`；最终 AppServer `41/41`、AgentRuntime `39/39`、Bridge
  `78/78`、双 Shell Phase 2 `410/410`，Release `0 warning / 0 error`。
- [x] M4.14 AppServer transport/control 包装器字符串纵切：
  `AppServerInitializeResponse`、`AppServerNotification`、`AppServerServerRequest`、
  `AppServerRpcError`、`ServerRequestResolution`、`TurnInterruptParams` 与
  `RpcApprovalEvent<T>` 不再递归展开 CodexHome、Provider ID、method、JSON、错误正文、
  任意 result 或审批 payload；实际属性、wire JSON、事件分发与审批处理保持不变。两个真实
  RED 依次为 `41/42`、`42/43`，GREEN 后 AppServer `43/43`；完整 bootstrap net8/net45
  各 `63/63`，双 Shell Phase 2 `412/412`，Release `0 warning / 0 error`，相关进程残留 `0`，
  User PATH 长度/哈希保持不变。
- [x] M4.14 AgentRuntime handle/event 字符串纵切：扩展既有公开 record 规格，真实复现
  `AgentTurnHandle`、`AgentItemSnapshot`、消息增量、工具状态/进度、turn/review、
  CAD proposal/rejection 和四类审批事件默认字符串展开 Provider IDs、内容、JSON、错误正文
  或审批 payload，专项由 `39/39` 先 RED `38/39`、后 GREEN `39/39`。公共字符串只保留类型、
  枚举与字段存在性；事件属性、投影、流式内容、审批转发和只读查询行为保持不变。
- [x] M4.14 AgentHost 审计异常外逃复核：`AgentHostAuditException` 可在 audit 内部保留原始
  IO/环境 inner 供本地失败归因，但生产 request response 固定为 `handler_error`，Bridge
  terminal、bootstrap/audit CLI 和导出边界均只公开安全快照或白名单字段；未发现 Palette、
  Bridge response、stderr、CLI JSON 或脱敏导出中的外逃路径，因此不机械删除 public 构造器。
- [x] M4.14 当前 `Replace`/`Sanitize` 静态复核：未发现另一套诊断清洗器；CAD 文字摘要、
  cursor/Base64、命令行引用、哈希和原子文件替换均是独立业务/编码语义，不机械改写。
- [x] M4.14 Bridge 远端异常纵切：服务端 `BridgeRemoteException` 和客户端
  `AgentBridgeClientException`/`AgentBridgeRemoteException` 的 message、code 与嵌套异常统一
  进入分类式脱敏；合法闭集错误码保持兼容，非法码归一为 `remote_error`/`internal_error`，
  不保留原始 inner exception。Bridge `80/80`、双 Shell Phase 2 `414/414`；四个 net45
  依赖与 R20.1/.NET Framework 4.5/x64 Host 产品构建 `0 warning / 0 error`，Autodesk DLL
  复制数 `0`。
- [x] M4.14 AppServer 审批 payload 字符串纵切：四类审批请求、权限/网络/文件系统子模型、
  审批响应、CAD 文档身份、变更摘要和预览对象不再由默认 record `ToString()` 展开命令、路径、
  Provider ID、理由、策略修订或 JSON；只报告类型、存在性、枚举和数量，wire JSON 与审批
  决策保持不变。专项先 RED `43/44`、后 GREEN `44/44`，AgentRuntime `39/39`，双 Shell
  Phase 2 `415/415`。
- [x] M4.14 initialize 与 CAD 提案 record 字符串纵切：AppServer client info/capabilities/
  params 先 RED `44/45`、后 GREEN `45/45`，不再展开任意客户端配置；AgentRuntime CAD 点、
  `create_line` 提案、批次和 Broker 结果加入既有公开 record 规格，先 RED `38/39`、后 GREEN
  `39/39`，不再展开坐标、图层、Provider IDs 或结果正文。两处均只修改 `ToString()`，wire、
  强类型属性、提案解析与 Broker 语义保持不变。双 Shell Phase 2 `416/416`，bootstrap
  net8/net45 各 `63/63`，Release 与 R20.1/.NET Framework 4.5/x64 产品构建
  `0 warning / 0 error`。
- [x] M4.14 剩余公共出口审计收口：复核 AgentRuntime、Bridge、Host、AgentHost 审计
  导出/保留、CLI JSON、Doctor/Run、Host BuildInfo、DrawingIndex/CadQuery 和剩余公共
  record/EventArgs 字符串，未发现新的可复现公共泄漏。双 Shell Phase 2 `416/416`、
  bootstrap net8/net45 各 `63/63`、Host A/B 哈希一致、Autodesk DLL 复制数 `0`、相关进程
  残留 `0`、User PATH 长度/哈希不变。发现真实旁路时才重新打开，不得误改 CAD 产品数据。
- [x] M4.15.1 企业策略阻止启动纵切：正式 `CreateProcess` 失败链将当前用户错误 5、
  显式策略错误 577/1260 和应用阻止错误 4551–4557 映射为不可自动重试的
  `agenthost_process_start_blocked`；RestrictedToken 普通访问拒绝仍归入隔离失败。Host UI
  提示管理员检查 AppLocker、WDAC、杀毒/EDR 和签名策略，且不公开原始 Win32 正文、路径或
  异常图。bootstrap net8/net45 各 `64/64`，双 Shell Phase 2 `416/416`，Host MVP
  `59/59`。这不是任何真实企业策略环境的通过证据。
- [x] M4.15.2a 嵌套 Job 拒绝自动化准备：正式 `AssignProcessToJobObject` 失败链在目标已属于
  父 Job 时返回不可自动重试的 `agenthost_nested_job_assignment_failed`，普通分配失败仍保留
  隔离语义；Host 给出脱敏父 Job/进程隔离提示。当前 Windows 正向嵌套分配继续通过，失败链
  不存在无 Job 回退。bootstrap net8/net45 各 `65/65`，双 Shell Phase 2 `416/416`，
  R20.1 Host A/B 哈希一致且 Autodesk DLL 复制数 `0`。真实不可嵌套父 Job 和企业矩阵未验证。
- [x] M4.15.3a AgentHost 意外退出自动化准备：`AgentHostServiceSession` 独立发布
  `ProcessExitFailureTask`，正常 STOP 与资源限制保持 `None`，无资源终态的根进程退出发布
  `agenthost_unexpected_exit`。Host 在 Bridge fault 竞态中保持资源终态优先、退出终态其次、
  泛化断线最后，活动请求只失败一次且后续 ASK fail-closed。Launcher net8/net45 各 `65/65`，
  Host MVP `60/60`，双 Shell Phase 2 `417/417`；R20.1 Host A/B 五文件逐字节一致，Host
  SHA-256 为 `DA5C6D100E4B8CEDCEEB1C4389E09A77667F6879C05A64EF4EC1A0EF43275255`，
  Autodesk DLL 复制数 `0`。真实 Codex/AgentHost/AutoCAD 正常退出与强杀未验证。
- [x] M4.15.3b 启动中断自动化准备：Host 首次启动持有链接生命周期令牌，STOP/退出清理在后台
  主动取消 bootstrap、Bridge 握手、能力协商或 thread 创建，再等待并复用既有协调器清理已建立
  资源；预期 STOP 中断不误报启动失败、不能上线且只发布一个停止终态。Host MVP `61/61`，
  双 Shell Phase 2 `418/418`；R20.1 Host A/B 五文件逐字节一致，Host SHA-256 为
  `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`，Autodesk DLL
  复制数 `0`。真实 AutoCAD/Codex 分阶段启动中断和残留进程矩阵仍未验证。
- [ ] M4.15 准备并执行真实 Codex/AutoCAD 异常退出、嵌套 Job、组策略、EDR/杀毒、受限账户、
  系统断电与企业保留策略矩阵；M4.15.1、M4.15.2a、M4.15.3a/b 和 M4.15.5a/b 自动化准备已完成，
  继续补真实机器 evidence、磁盘满/断电夹具和企业归档流程。

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

用户已于 2026-07-25 确认、并于 2026-07-26 再次确认当前 Kimi 侧边栏视觉方案暂时冻结；
不再继续视觉迭代，但仍需按 M8 接入真实状态机并修复审查项。Kimi 可后续参与 UI，项目不以
Kimi 可用为继续开发前置条件。

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

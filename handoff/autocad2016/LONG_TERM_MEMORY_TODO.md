# AutoCAD 2016 项目长期记忆与完整待办

最后更新：2026-07-24（北京时间）

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
- 候选：`autocad2016-m2-drawing-index-v040-597a7a3d-432e7cf9-f1f2addd`。
- Host SHA-256：
  `597A7A3DC047B7A8188C0E4C7768032A5D8DA428AE210AE615713B8497AB0637`。
- AgentHost SHA-256：
  `432E7CF97D9E968D96C83FDE4FDD3C40961326E90CCD16D90BC3E34F21C968F6`。
- manifest SHA-256：
  `7E6116AF0F2D6BDBEB64DB6D009705E21358CB55609E36697EC179D17B690C18`。
- 自动化：Contracts net8/net45 `84/84`、Bridge Client net8/net45 `29/29`、
  Bridge/AgentHost `39/39`、AgentRuntime `33/33`、Host MVP `52/52`、完整 Phase 2
  `307/307`、R20.1 Release、29 文件只读闭包和 Host A/B 位级一致通过。
- 证据：
  `m2-drawing-index-candidate-autocad2016-m2-drawing-index-v040-597a7a3d-432e7cf9-f1f2addd.json`。
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
- [ ] 建立 1,000、10,000、50,000 对象的脱敏基准图。
- [ ] 冻结 UI 卡顿、总扫描时间、内存和 IPC 大小预算。
- [x] 自动化证明实体、统计桶、内存和时间预算映射为 `partial/limited`，不伪装完整。

M2 仍未完成，以下内容不能由自动化候选替代：

- [ ] 在 AutoCAD 2016 按精确 `0.4.0.0` 哈希人工 NETLOAD 五种范围、本地查询、分页和 DBMOD。
- [ ] 仅建立 DrawingIndex、不发布选择上下文，验证 ASK 与 `cad.query_drawing` 多页查询。
- [ ] 实机验证修改/撤销/切图后的 stale 拒绝，以及查询/回合取消和断线 fail-closed。
- [ ] 实机验证跨 Idle 片段的 `BlockTableRecordEnumerator` 生命周期和退出清理。
- [ ] 实机验证扫描中修改图纸、切换布局、取消、超时和旧游标拒绝。
- [ ] 完成 1k/10k/50k 扫描响应性、总时间、工作集、本地查询和 Agent 查询性能 evidence。

## 8. M3：CAD 读取语义和对象覆盖

完成定义：不支持对象只降低完整性，不使整次捕获失败。

- [ ] 逐类实机验证现有 19 种强类型对象。
- [ ] 提供中文对象名称、创建方法和脱敏示例测试图。
- [ ] Palette 显示不支持对象的实际类型和数量。
- [ ] 完善块属性、动态块、嵌套块、布局和空间信息。
- [ ] Xref 只公开安全元数据，不公开外部真实路径。
- [ ] 完善 Dimension、Hatch、Leader、MLeader、Table 的 R20.1 字段语义。
- [ ] 增加 Region、Solid、Mesh、Surface、Image/Underlay 等高价值受限读取。
- [ ] 垂直产品代理对象至少提供类型、图层、范围和占位信息。
- [ ] 长文字、复杂 Hatch、Table、Spline 受限但不拖垮整体。
- [ ] 每类对象具有契约、边界、R20.1 API Probe 和实机字段证据。

## 9. M4：进程沙箱、配置和审计基础

完成定义：AutoCAD 或 AgentHost 异常退出后进程树可确定清理；凭据和用户配置不进入日志或
普通工作目录。此阶段是启用 CAD 写入前置条件。

- [ ] Codex 路径、兼容版本、超时和工作目录进入正式配置。
- [ ] 启动前执行 Codex 版本和 app-server 健康检查。
- [ ] 每会话独立 CODEX_HOME。
- [ ] 默认空 MCP、空插件配置和独立凭据边界。
- [ ] 子进程仅继承白名单环境变量。
- [ ] 使用 Windows Job Object 管理 AgentHost 与 Codex 整个进程树。
- [ ] 评估并实现受限令牌或 AppContainer。
- [ ] 设置 CPU、内存、进程数、运行时间和工作目录配额。
- [ ] 工作目录使用最小 ACL 并按策略清理。
- [ ] Codex stderr、异常、配置和环境变量统一脱敏。
- [ ] 建立启动、停止、请求、取消、断线、审批和写入终态的结构化审计。
- [ ] 增加安全故障注入和僵尸进程测试。

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

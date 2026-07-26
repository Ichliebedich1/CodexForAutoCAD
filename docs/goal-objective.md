# Codex for AutoCAD 最终产品详细目标

位于 `D:\AutoCAD 2016\AA插件\CodexForAutoCAD\docs\goal-objective.md` 的文件是本项目最权威的目标文件。

最后重写：2026-07-26

## 0. 目标、范围与当前口径

最终目标是在 AutoCAD 2016 x64 上交付一个可安装、可审计、默认安全、可长期日用的
Codex 原生侧边栏，并在 2016 正式完成后适配 AutoCAD 2025。产品必须支持：

1. 读取当前选择和整张图纸，建立可取消的索引并分页查询。
2. 使用本机 `codex app-server --stdio` 完成流式、多轮、按图纸隔离的对话。
3. 只通过强类型 CAD 工具、确定性预览、一次审批、锁内重验、单事务和单次 Undo 修改图纸。
4. 不自动保存，不允许 Agent 执行任意 AutoCAD 命令字符串、LISP、脚本、Shell 或任意文件/网络操作。
5. 进程、凭据、配置、会话、审计、安装、升级和回滚均有明确安全边界。

当前明确不做 Provider-neutral 抽象、Direct API Provider 和自研 Agent Loop。本阶段唯一
Agent 是本机 Codex。未来若重新立项，必须另建目标，不在当前 UI、任务链和数据库中预埋
大量空接口。

构建环境安全前置：任何设置 `DOTNET_CLI_HOME` 的脚本或子进程环境都必须在同一作用域
显式设置 `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0`。2026-07-25 已两次观察到遗漏该变量会将
临时 `.dotnet/tools` 永久追加到用户 PATH，最终破坏 Windows Shell 登录环境。门禁必须在
运行前后验证用户 PATH 不含项目临时工具目录且哈希不变；不得用 `setx PATH` 或写入
User/Machine PATH。事故记录见
`handoff/autocad2016/DOTNET_CLI_PATH_INCIDENT_20260725.md`。

当前真实完成度口径：

- 64 个以内选择集 + 本机 Codex 两轮对话的窄只读 MVP：约 80%–85%。
- 可长期日用、断线和取消可控的稳定只读 MVP：约 65%–70%。
- 包含 50,000 对象整图索引与查询：约 35%–40%。
- 按本文 M0–M12 最终产品范围计算：主线约 22%。

`Worktree 完成`、`自动化通过`、`候选包生成`、`AutoCAD 实机通过`和`已进入 main`是五种
不同状态。只有全部满足该子目标要求，才允许将它标为完成。

## 1. 统一完成准则

每个 `Mx.y` 必须有以下证据；纯文档子目标可省略不适用项，但必须说明原因：

1. 真实调用链：代码接入产品入口，不以未调用的接口或演示类冒充完成。
2. 自动化：新增或更新目标测试，失败路径与安全边界必须覆盖。
3. 编译：托管核心 Release 0 warning / 0 error；Host.2016 使用原版 R20.1 程序集、
   `net45/x64` 编译。
4. 安全门禁：禁用 API、秘密扫描、脱敏检查、`git diff --check` 通过。
5. Git：最小范围独立提交，提交中不得夹带无关 Worktree 或用户原型。
6. 候选：从精确提交构建，生成 manifest、SHA-256、版本和回滚点。
7. 实机：涉及 AutoCAD、Codex、进程生命周期、DPI 或安装的目标必须由用户在真实环境验证。
8. 证据：只保存脱敏 JSON/Markdown，不记录图纸路径、用户名、Handle、令牌、完整环境变量。
9. 文档：同步 `CURRENT_STATE.md`、`LONG_TERM_MEMORY_TODO.md`、README 和对应 runbook。

里程碑状态定义：

- `未开始`：主线没有可调用实现。
- `进行中`：实现、测试、集成、候选或实机证据至少缺一项。
- `完成`：该里程碑全部必选子目标通过，且状态文档与 Git 提交一致。
- `阻断`：安全硬前置未满足，后续能力不得启用。

## M0：冻结统一只读 v2 基线

大目标：把 AutoCAD 2016 的 net45/x64 薄宿主、CadContextJson v2、认证 Bridge、
AgentHost 和本机 Codex 两轮对话收拢为可复现、可回滚的统一只读基线。

当前状态：主线完成。源码、自动化和候选已冻结；精确 M0 候选未单独 NETLOAD，此证据
边界保留到下一次最终候选实机验证，不重复打断当前开发。

### M0.1 固定产品调用链

- 交付：`Host.2016 -> MvpAgentClient -> AgentLauncher -> Bridge -> AgentHost ->
  codex app-server --stdio`。
- 技术：Host 只依赖 net45 兼容的 Contracts、IPC、Bridge Client 和 Launcher；AgentHost
  保持进程外 `net8.0-windows`。
- 验收：没有终端模拟、ANSI 解析、键盘注入或 UI 轮询作为 Codex 协议。

### M0.2 固定 CadContextJson v2

- 交付：版本 `codex.autocad.cad-context/2`、受限 placeholder、完整性计数和 v2 turn 方法。
- 技术：v1 固定向量不得被修改；v2 使用结构化 DTO 与 canonical JSON。
- 验收：未知对象不使整个选择失败，DBMOD 不变，v2 capability 必须 fail-closed。

### M0.3 冻结候选身份

- 交付：源提交、模块版本、Host/AgentHost/manifest SHA-256、候选目录和回滚点。
- 验收：manifest 中所有受管文件哈希一致，不打包 Autodesk DLL，不继承其他候选的实机结论。

### M0.4 固定证据边界

- 交付：自动化 evidence、真实 Codex evidence、AutoCAD live evidence 分开保存。
- 验收：明确记录精确 M0 哈希 `NetLoadVerified=false`，不得把 P1 live 哈希迁移为 M0 证据。

## M1：稳定只读会话与生命周期

大目标：把当前 happy-path 只读 MVP 变成断线、取消、超时、文档切换和退出均可预测的
日常使用候选。任何回合最多产生一个终态；错误后不误报“完成”。

当前状态：主线约 20%–25%。`m1-readonly-stability` Worktree 的 10 个 M1 提交（含 `88c0a29`）
已在当前 HEAD 重新验真：Host MVP `41/41`、双 Shell Phase 2 `276/276`、R20.1 v2 API
双 Shell 均通过（19 passed / 8 expected failed，Build 0 warning / 0 error），候选冻结门禁通过，
只读 Compile 闭包为 25 个源文件。Host 二进制 `E6701A77…`、AgentHost `7A3ABCEA…`
与此前冻结候选逐文件一致（manifest 仅因候选 ID/时间字段变化而不同）。M1 仍未受控吸收进
`main`，也尚未完成精确候选的 AutoCAD 实机矩阵。旧 `verify-autocad2016-host.ps1`/unified
verifier 冻结在早期诊断 Host 图，不作为本次 M1 候选通过依据；当前依据是
`verify-autocad2016-context-v2-candidate.ps1` 及其 evidence。

### M1.1 建立系统会话状态机

- 交付：`SystemConversation`、`Turn`、`Request` 三层状态模型。
- 技术：状态只允许按表迁移，例如 `created -> starting -> running ->
  completed|failed|cancelled`；状态机位于 Host 应用服务，不由 Palette 控件维护。
- 自动化：合法迁移、非法回退、重复启动、完成后再运行均被拒绝。
- 完成条件：UI 和 Bridge 回调都只能通过同一个状态机改变状态。

### M1.2 保证唯一终态

- 交付：每个 request 只能接受一次 `completed`、`failed` 或 `cancelled`。
- 技术：使用原子 compare/exchange 或锁内终态提交；终态后丢弃 TextDelta、usage 和 tool 事件。
- 自动化：完成后迟到失败、取消后迟到完成、重复完成、并发终态竞争。
- 完成条件：测试中 100% 请求只出现一个终态，UI 不重复提示。

### M1.3 Bridge 断线原子离线化

- 交付：EOF、坏认证帧、命名管道关闭、AgentHost 退出统一进入 `offline`。
- 技术：一个断线入口原子取消当前回合、释放 send gate、清除连接引用并阻止后续 ASK。
- 自动化：断线发生在启动、发送、流式、完成边界和停止期间。
- 完成条件：断线后下一次 ASK fail-closed，不能继续投递旧事件。

### M1.4 实现请求超时

- 交付：启动超时、请求总超时、无事件空闲超时分别配置和报告。
- 技术：链接用户取消、文档失效和进程退出 token；超时必须进入确定的 failed/cancelled 状态。
- 自动化：边界前完成、边界时竞争、超时后迟到事件、重复 timeout callback。
- 已验证：`HOST2016_TURN_START_TIMEOUT_FAILS_CLOSED` 覆盖 `starting_provider` 阶段；
  Provider 尚未返回 turn ID 时也按 Host 总截止时间 fail-closed，晚到响应不能复活请求。
- 完成条件：超时后不再显示流式文本，不自动重发请求。

### M1.5 实现幂等取消

- 交付：Palette Cancel、命令取消和 Host shutdown 共用取消协调器。
- 技术：首次取消发送 provider cancel；重复取消只返回当前状态，不重复写管道。
- 自动化：运行前取消、运行中取消、终态后取消、断线后取消、并发双取消。
- 完成条件：取消确认有界返回，AutoCAD UI 始终可操作。

### M1.6 分离系统 ID 与 Codex ID

- 交付：system conversation ID、turn ID、request ID 与 Codex thread ID 显式分离。
- 技术：Codex thread ID 只存在内部映射和脱敏诊断元数据，不作为数据库主键或 UI 标识。
- 自动化：重建 Codex thread 不改变系统 conversation；不同 conversation 不共享 provider thread。
- 完成条件：业务层状态不依赖 Codex 专有字段。

### M1.7 按 DWG 隔离对话

- 交付：每个打开文档具有独立 conversation 映射和上下文 generation。
- 技术：使用稳定的进程内文档身份，不记录真实文件路径；DocumentActivated/ToBeDestroyed
  触发映射切换或销毁。
- 自动化：A/B 图切换、关闭 A、重开同名图、未保存新图、多文档并发。
- 完成条件：图 B 的回答不引用图 A 的上下文或历史。

### M1.8 拆分三种清除语义

- 交付：`Clear CAD Context`、`New Conversation`、`Clear All` 三个独立命令/操作。
- 技术：清上下文不销毁对话；新对话不修改 DWG；清全部同时清上下文与会话映射。
- 自动化：每种操作后的 generation、thread、按钮和 ASK 行为。
- 实机：验证清上下文后仍记得对话，新对话后不再记得旧标记。

### M1.9 修正 Palette 生命周期状态

- 交付：离线、启动中、在线、运行、取消中、完成、失败、断线、停止的单一呈现模型。
- 技术：按钮 enablement 从状态派生，不由异步事件处理器各自设置。
- 自动化：Send 在流式终态前保持禁用；Stop/Cancel/New/Clear 的可用性符合状态表。
- 完成条件：不会出现断线后显示“回答完成”或停止后仍可发送。

### M1.10 结构化并脱敏错误

- 交付：稳定 `error_code`、`stage`、`request_id`、`retryable`、`sanitized_details`。
- 技术：启动、协议、认证、超时、Codex stderr 和进程退出均经统一 sanitizer。
- 自动化：路径、用户名、令牌、环境变量、原始 stderr 不出现在日志或 UI。
- 完成条件：用户可根据错误码排查，不需要暴露异常堆栈。

### M1.11 确定性进程清理

- 交付：正常退出、异常退出、重复 Stop 均释放 AgentHost、Codex、Pipe 和临时资源。
- 技术：同步/异步有界清理、最多一次受控重试；不绑定 AutoCAD UI 线程。
- 自动化：500 次启停、Host Dispose 竞态、AgentHost 忽略取消、迟到 fault。
- 完成条件：测试和实机退出后相关残留进程为 0。

### M1.12 冻结稳定只读候选

- 交付：精确提交候选、manifest、哈希、runbook 和 evidence。
- 实机：文档切换后真实 ASK 拒绝、取消、断线、超时、Palette Reset、正常退出、
  125%/150% DPI。
- 完成条件：所有必选稳定性矩阵通过，DBMOD 不因只读功能改变。
- 当前自动化候选：`autocad2016-m1-readonly-v033-e6701a77-7a3abcea-ed93a77c`；
  Host `E6701A771D17EC3EC8B2CA7DA78B553E27897639DC48B3BC0435F07249C9B5F6`，
  AgentHost `7A3ABCEABA0E590839DEC344FA68755A213D8716CDA777EC9D891EABB055E50D`，
  manifest `AFB4016B0E8941187C3EC324AB8732B7D724A68A61886E787C5DA5732CDEC767`。
  该候选尚未按精确哈希在 AutoCAD 2016 中 `NETLOAD`，不得继承旧候选的实机结论。

## M2：50,000 对象级整图索引与查询

大目标：保留 v2 小选择快照兼容性，同时新增不会阻塞 AutoCAD 主线程的 DrawingIndex 和
CadQuery。模型按需分页查询，不把整图 JSON 一次性塞入上下文。

当前状态：主线约 0%–5%。M2 DrawingIndex Worktree 已完成只读索引、分页查询、反向
Agent 查询和 1k/10k/50k 基准自动化冻结，但尚未受控集成，亦未完成 AutoCAD 实机矩阵。
当前自动化候选为 `autocad2016-m2-drawing-index-v040-e85d97ec-8e6b26fd-7614b6b2`，
Host SHA-256 `E85D97EC02505EF69C67F710EAD5D35D18481B7D2DBB4C3D87195FCDE4156B7E`，
AgentHost SHA-256 `8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A`，
manifest SHA-256 `BF20A62F8CC71AB3B6A7AA6F329DF8520E136EE7A0B1ED6283AEFAFE343BFCD3`。
该候选通过 Phase 2 `308/308` 和 benchmark `6/6`；精确 `NETLOAD` 前不算 M2 完成。

### M2.1 冻结索引协议

- 交付：版本化 `DrawingIndexDescriptor`、`CadQueryRequest/Result`、分页 cursor 和完整性模型。
- 技术：协议不引用 Autodesk 类型；记录 index ID、document identity、revision、scope、
  counts、partial、limits。
- 自动化：canonical 序列化、未知字段、版本不兼容和大小上限。

### M2.2 定义扫描范围

- 交付：selection、current space、model space、specific layout、entire drawing 五种 scope。
- 技术：范围必须显式，默认不自动扫描整图；布局使用受限内部引用，不暴露 Handle 给 Agent/UI。
- 自动化：各 scope 对同一 fixture 的计数和隔离。

### M2.3 实现 AutoCAD 主线程分片采集

- 交付：在合法文档线程、DocumentLock 规则和只读 Transaction 内逐片读取。
- 技术：使用 Idle/dispatcher 分片，每片目标预算不超过 20 ms；不得将 DBObject 跨线程传递。
- 自动化：调度器用 fake clock 验证时间预算、取消和公平性。
- 实机：50k 扫描期间拖动 Palette、切图和执行普通命令仍有响应。

### M2.4 建立深拷贝 DTO 边界

- 交付：主线程只输出不可变或深拷贝 DTO，后台仅处理 DTO。
- 技术：禁止把 ObjectId、DBObject、Transaction、Document 或 Editor 放入后台队列。
- 自动化：依赖/源码扫描阻止 Autodesk 类型越过边界。

### M2.5 建立基础索引

- 交付：类型、图层、空间、块 effective name、文字 token、包围盒和数量索引。
- 技术：优先使用紧凑列式/分桶结构，避免每对象重复 JSON；稳定顺序保证确定性。
- 自动化：同一 fixture 多次索引产生相同摘要和查询顺序。

### M2.6 单对象失败降级

- 交付：`unsupported`、`read_failed`、`data_limited` 项和分类计数。
- 技术：每对象隔离异常；达到全局预算后返回 partial，不抛弃已完成分片。
- 自动化：混合有效、代理、异常和超限对象。
- 完成条件：一个坏对象不能让整图索引失败。

### M2.7 文档 revision 与失效

- 交付：索引绑定文档身份和 revision。
- 技术：修改、Undo/Redo、切换、关闭、对象事件或显式重建使旧 index/cursor 失效。
- 自动化：revision 变化后旧查询返回稳定 `stale_index`。

### M2.8 实现结构化查询

- 交付：按类型、图层、块、空间、范围、文字和稳定对象引用查询。
- 技术：组合条件有最大复杂度、最大页长和最大扫描成本；默认只返回摘要字段。
- 自动化：过滤、排序、空结果、超限、恶意复杂查询。

### M2.9 实现安全分页 cursor

- 交付：cursor 绑定 index ID、revision、query hash、offset 和 expiry。
- 技术：HMAC 或等价完整性保护；拒绝伪造、跨图、跨查询和过期 cursor。
- 自动化：篡改每个字段、重放、过期和并发页请求。

### M2.10 接入 AgentHost CAD 查询工具

- 交付：认证 Bridge 上的 `cad.query_drawing`。
- 技术：AgentHost 只提交结构化查询，Host 返回有界结果；AgentHost 不引用 Autodesk API。
- 自动化：capability negotiation、超时、取消、断线、分页和大小限制。

### M2.11 接入 Palette 进度

- 交付：扫描范围、进度、取消、partial/limited、对象统计、index revision。
- 技术：UI 订阅应用状态，不直接控制扫描线程；进度事件限频，防止消息洪泛。
- 自动化：取消失败后可重试，空索引显示“未建立”。

### M2.12 固定性能 fixture

- 交付：精确 1k、10k、50k 模型空间实体的 DXF 和冻结 manifest。
- 技术：生成器、实体计数、文件大小、SHA-256 可复现；fixture 不含真实客户图纸。
- 验收：CI 校验 fixture 身份，不把 DXF 误当成已完成实机性能证据。

### M2.13 性能与资源门禁

- 目标：50k 扫描可取消；UI 单次冻结不超过 250 ms；取消响应不超过 1 秒；
  新增工作集不超过 512 MiB。
- 技术：记录 p50/p95 分片时长、总耗时、分配和峰值；基准超过冻结值 15% 阻止发布。
- 实机：在目标 AutoCAD 2016 机器三档各运行至少 3 次，DBMOD 不变。

### M2.14 冻结整图读取候选

- 交付：协议、源码、性能报告、候选哈希、实机 evidence 和故障排查文档。
- 完成条件：50k 的总结必须来自查询/聚合，不允许将完整整图 JSON发送给 Codex。

## M3：CAD 读取语义与对象覆盖

大目标：完成常用 R20.1 实体的强类型语义；特殊对象降级成受限摘要，不因一个未知对象
使整次选择或整图索引失败。

当前状态：主线约 35%–40%。M3 Worktree 已完成 19 类读取目录、实际类型/placeholder
统计、受限块详情和 R20.1 API/fixture 自动化冻结；逐类实机字段核对和高价值受限对象
仍未闭合，相关 Worktree 仅作待审实现。当前自动化候选为
`autocad2016-m3-read-semantics-v041-fb18d959-8e6b26fd-7fd527a7`，
Host SHA-256 `FB18D95981F607B22D8C023BF63915614DFF8964BF985BE6CB0ABEA26D9B3673`，
AgentHost SHA-256 `8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A`，
manifest SHA-256 `4B3B710F3773D10F0B30A31B357CB7D3D35445BA294F1F1ABEDDC8C378B1ED00`。
该候选通过 Phase 2 `310/310`、benchmark `6/6` 和 R20.1 双 Shell API stage；
精确 `NETLOAD` 与 19/19 实机矩阵前不算 M3 完成。

### M3.1 冻结 19 类强类型清单

- 交付：Line、Circle、Arc、Ellipse、Spline、Point、Ray、Xline、Polyline、
  Polyline2d、Polyline3d、DBText、MText、BlockReference、Dimension、Hatch、
  Leader、MLeader、Table。
- 技术：每类契约有稳定 type discriminator、公共字段和类型专属字段。
- 自动化：固定向量与 schema 兼容测试。

### M3.2 基础几何语义

- 交付：直线/圆/弧/椭圆/样条/点/射线/构造线的坐标、方向、法向和参数。
- 技术：统一坐标精度与有限数验证；角度单位在契约中固定。
- 验收：R20.1 LIST/属性面板逐字段核对。

### M3.3 Polyline 语义

- 交付：三类 Polyline 的 closed、elevation、normal、全部受限 vertices、bulge。
- 技术：顶点数超限时截断并提供 total/returned/truncated。
- 自动化：2D/3D、空、闭合、bulge、极大顶点数。

### M3.4 文字语义

- 交付：DBText/MText 的文字、位置、高度、旋转和受限格式摘要。
- 技术：长文字按字符/字节双预算截断；不扩展字段或外部资源。
- 自动化：中文、换行、格式码、空字符串和超长文本。

### M3.5 块语义

- 交付：位置、旋转、比例、effective name、dynamic、xref 标志、受限属性。
- 技术：嵌套块只返回深度受限摘要；xref 不返回真实路径。
- 自动化：匿名动态块、嵌套、属性、缺失定义和循环引用保护。

### M3.6 Dimension/Hatch/Leader/MLeader/Table

- 交付：测量值、显示文字、关键位置、pattern、比例、边界摘要、箭头、顶点、行列和单元格文本。
- 技术：只使用 R20.1 实际存在 API；复杂集合统一数量预算和 partial 标志。
- 验收：API Probe + AutoCAD 2016 逐类人工字段对照。

### M3.7 高价值受限对象

- 交付：Region、Solid、Mesh、Surface、Image、Underlay、Wipeout、Proxy 的安全摘要。
- 技术：只返回类型、图层、空间、包围盒和有限状态；不加载外部内容。
- 完成条件：这些对象不再导致 `validation-unsupported-entity-kind` 整体失败。

### M3.8 统一限制与错误模型

- 交付：字段、顶点、边界、文字、单对象字节和全局字节预算。
- 技术：限制命中产生 `data_limited`，读取异常产生 `read_failed`，并保持已读对象。
- 自动化：每类边界值、越界值和异常注入。

### M3.9 隐私与脱敏

- 交付：Xref、Image、Underlay、Proxy 的路径、Handle 和厂商私有数据禁出规则。
- 自动化：fixtures 中嵌入伪路径/用户名/令牌，验证 UI、JSON、日志均不出现。

### M3.10 固定 R20.1 API

- 交付：双 PowerShell API Probe 证据和实际使用成员清单。
- 技术：编译反射/签名探针只针对原版 AutoCAD 2016 托管程序集。
- 完成条件：代码不得依赖 2017+ 或 2025 专有 API。

### M3.11 建立逐类测试资产与说明

- 交付：脱敏测试图、中文“对象对应什么 AutoCAD 元素”说明和逐类核对表。
- 技术：测试图不来自生产图；每类对象有最小可识别样本。

### M3.12 完成 19/19 实机矩阵

- 验收：19 类强类型字段全部核对；高价值受限对象均可发布 placeholder/摘要；
  DBMOD 不变；无完整 JSON 外发。
- 完成条件：实机 evidence 与精确候选哈希绑定。

## M4：进程沙箱、配置、资源限制与审计

大目标：在启用任何 CAD 写入前，确保 AgentHost/Codex 进程树可限制、可回收，配置和凭据
不泄露，审计可验证。M4 是 M5 的硬前置。

当前状态：主线约 25%–30%；M4 独立 Worktree 加权约 70%–75%，但尚未进入 main。
M4.4/M4.5 已在 `m4-diagnostic-sanitization` Worktree 完成、提交并通过双 Shell Phase 2、
351/351 Specs 和专用 AgentLauncher 41/41 门禁。受限身份隔离本身仍约 20%–25%；
当前 private desktop 只是探针，不能称为生产沙箱。M4.14 已完成 Contracts 统一 sanitizer、
Bridge 公开异常和反向整图查询错误响应、AppServer 主要公开异常、AgentLauncher bootstrap
失败、AgentHost `doctor`/`run` 成功状态最小化，以及 Host.2016 Palette/Bridge 断线/CadQuery
命令行公共错误纵切；AgentHost 通用 CLI 失败、协议故障 stderr 和 bootstrap CLI 错误也已
收敛为稳定错误码、阶段、分类与数值脱敏元数据。AppServer 服务端请求失败响应也已在唯一
`WriteErrorAsync` 出站边界统一清洗 message 并丢弃原始 data；三个 AgentHost 审计 CLI
命令的未预期异常也已统一收口。AgentRuntime 失败 turn/observer、Bridge terminal 和
Host.2016 DrawingIndex/CadQuery 通用命令 catch 的原始诊断旁路也已关闭。配置请求、
AppServer 启动配置、AgentRuntime options/handle/input 与 Bridge request/notification 的
record 字符串投影也已收口，不再展开路径、环境、参数、提示词、Provider 标识、schema 或
完整 JSON；AgentHost audit 内部异常链已证明没有生产公共外逃路径。AgentRuntime、Bridge、
Host、AgentHost 审计导出/保留、CLI JSON、Doctor/Run、Host BuildInfo、DrawingIndex/CadQuery
及剩余公共 record/EventArgs 字符串出口已完成静态复核，未发现新的可复现公共泄漏。M4.14
代码、自动化和静态公共出口审计已收口；真实环境故障验证归入 M4.15。

### M4.1 正式配置模型

- 交付：Codex 路径、允许版本、启动/请求超时、工作区、日志、进程和资源限制，以及允许模型、
  默认模型、允许思考强度和默认思考强度配置。
- 技术：机器策略、管理员配置和用户配置分层合并；管理员可锁定模型与思考强度；未知、错误、
  不在白名单内或相互冲突的值 fail-closed。
- 边界：UI 提交的任意字符串不得直接穿透到 AgentHost/Codex；Contracts、Bridge 和 AgentHost
  必须共同验证并返回实际接受的模型与思考强度。
- 自动化：缺失、损坏、旧版本、相对路径、UNC、设备路径、越界路径、非法模型、非法思考强度、
  管理员锁定和配置层优先级。

### M4.2 Codex 自动发现与健康预检

- 交付：显式路径优先，其次受控 PATH/注册位置发现；执行 `codex --version` 和 app-server 握手。
- 技术：禁止 Shell 字符串拼接；使用参数数组和绝对路径。
- 完成条件：启动失败返回稳定错误码，不显示真实本地路径或 stderr。

### M4.3 每会话隔离 CODEX_HOME

- 交付：会话专属目录、最小配置、默认空 MCP/插件和受控缓存。
- 技术：只继承环境白名单；会话结束按 lease 清理；异常退出可恢复清理。
- 自动化：全局配置污染、MCP 注入、插件注入和环境变量泄漏。

### M4.4 收回未成熟 RestrictedToken 公共入口

- 交付：公共产品 API 不暴露尚不能完成 bootstrap 的身份选项。
- 技术：RestrictedToken 只留 internal 测试能力探针，或受显式 experimental gate 保护。
- 自动化：公共调用方无法选择该模式；默认绝不回退 CurrentUser。
- 当前切口：M4 Worktree 已修改时，必须先完成最终双 Shell 门禁和受控提交再计入主线。

### M4.5 使身份探针跨机器可移植

- 交付：确定性单元测试与机器能力探针分开。
- 技术：允许“受限成功”“结构化隔离失败”或“受限子进程失败”，但任何平台都禁止静默
  回退 CurrentUser；不能固定要求本机特有 `child_exited`。
- 自动化：net45/net8、Windows PowerShell/PowerShell 7 双矩阵。

### M4.6 Job Object 管理整棵进程树

- 交付：AgentHost、Codex 及其后代加入同一个受控 Job。
- 技术：kill-on-job-close、嵌套 Job 检测、进程数限制和父进程异常退出清理。
- 自动化：子进程再生、忽略取消、崩溃、并发 Stop 和 500 次启停。
- 当前切口：已实现分配前任意 Job 成员检测、分配后目标 Job 反查和结构化隔离失败；当前
  Windows 已通过嵌套 Job、资源限制、Stop/异常/owner 退出回收，以及 net45/net8、双 Shell
  各连续 500 次 service 启停。AgentLauncher 为 `57/57`，Host MVP 为 `56/56`，Phase 2 为
  `360/360`，且门禁后
  AgentHost/FakeAgentHost 残留为 0。M4.6–M4.9 自动化检查点已提交为 `15352ff`。
- 尚缺：企业组策略、Windows 版本和宿主 Job 组合矩阵，真实 AutoCAD 正常/异常退出进程树
  验收。
- 完成条件：自动化和企业/AutoCAD 实机矩阵均无孤儿 AgentHost/Codex，证据绑定精确提交。

### M4.7 建立生产身份隔离

- 交付：RestrictedToken 或预配置 AppContainer 下真实 bootstrap、Pipe、STOP 成功。
- 技术：为 runtime、workspace、pipe、window station/desktop 配置最小 DACL；不把
  private desktop 本身当作身份隔离。
- 验收：白名单路径可访问，越权路径被拒绝，且不回退 CurrentUser。
- 当前边界：RestrictedToken/private-desktop 原语可用，但受限 FakeAgentHost 在认证前
  `child_exited`；公共产品入口保持关闭。M4.8 会话 workspace ACL/lease 自动化切口已完成，
  但 AgentHost 仍为 framework-dependent，且 M4.11 凭据 Broker 未完成，不能据此启用生产
  RestrictedToken。
- 实施依赖：保持 M4.8 会话工作区/ACL/lease 边界，先完成 M4.11 的受支持凭据恢复边界，
  再把受限身份接入真实 AgentHost/Codex；不得复制全局 Codex profile 或失败后回退 CurrentUser。

### M4.8 工作区 ACL 与生命周期

- 交付：每会话工作区、审计目录、lease、过期清理和崩溃恢复。
- 技术：拒绝 junction/symlink/reparse 越界；目录归属和 ACL 在启动前验证。
- 自动化：路径替换竞态、陈旧 lease、并发会话和清理失败。
- 当前切口：已在系统 session ID 生成后、AgentHost 启动前创建受保护的每会话目录，包含
  `workspace`、`audit`、`codex-home`、活动 lease 和固定 schema marker；只允许当前用户、
  SYSTEM 和 Administrators，验证固定本地磁盘、owner、DACL、最终句柄路径并拒绝 reparse 根。
  STOP 在进程和 I/O 收口后清理，失败可重试；默认 `24 h`、单次最多 `64` 个候选的恢复逻辑
  只清理合法、过期、无活动 lease 的 schema 目录，不碰 legacy 目录。net45/net8 自动化及
  owner 崩溃恢复已通过。
- 尚缺：企业 ACL/组策略和 AutoCAD 正常/异常退出实机矩阵。
  `codex-home` 虽已创建，但 M4.11 凭据 Broker 完成前不得启用生产隔离登录。
- 自动化证据：M4.6–M4.9 自动化检查点已提交为 `15352ff`；evidence 为
  `handoff/autocad2016/evidence/agent-bootstrap-verification-20260719.json`，bootstrap schema
  为 16。该身份不替代 AutoCAD/企业实机证据。

### M4.9 CPU/内存/进程/时间限制

- 交付：最大进程数、Job 总提交内存、CPU rate/累计用户时间、墙钟时间和停止 grace period；
  working set 作为性能 telemetry 和发布预算，不作为默认硬终止条件。
- 技术：使用 Job limits 和 watchdog；配额命中产生结构化终态。
- 自动化：逐项耗尽、组合耗尽、清理和错误脱敏。
- 当前切口：真实默认启动路径已应用最大进程数、Job 总提交内存、CPU hard cap、累计用户
  时间和服务墙钟限制；停止宽限已成为 `0–30 s` 的受检配置，默认 `1 s`，非法值在启动前
  fail-closed，配置值已由 net45/net8 规格证明进入 Stop 等待路径。Windows Job completion
  port 对进程数、Job 内存和累计用户时间提供权威通知，watchdog 对服务墙钟提供终态；Host
  通过有界仲裁防止普通 Bridge fault 抢先覆盖资源原因。四类稳定错误码均使用
  `error_stage=agenthost_runtime`、`retryable=false`，活动 request 只进入一次 `failed`，
  后续 ASK fail-closed。真实 Job 内存与用户时间组合耗尽只接受先到的权威终态，不推断固定
  优先级。检查点 Launcher net45/net8 `57/57`、Host MVP `56/56`，双 Shell Phase 2
  `360/360`，已提交为 `15352ff`。
  明确不启用 `JOB_OBJECT_LIMIT_WORKINGSET`：Job 总提交内存是安全硬边界，working set 沿用
  外部只读采样和性能门禁；企业可选驻留集策略必须另经 Windows/组策略/真实 Codex 矩阵。
- 尚缺：真实 Codex/AutoCAD 耗尽矩阵和企业配置策略。磁盘硬配额仍由 M4.10 负责。

### M4.10 磁盘硬配额

- 交付：部署时选择 FSRM 或固定容量卷，并在不可用时明确拒绝启用写权限。
- 技术：目录大小轮询只能做 telemetry，不能冒充硬配额。
- 验收：写满、低空间、配额配置缺失和恢复矩阵。
- 当前切口：目标 Windows 10 Pro 没有 FSRM，采用管理员预置专用固定容量卷路线。默认
  `ReadOnlyUnenforced` 不声称硬配额；显式模式在 AgentHost 进程启动前验证 Volume GUID、
  固定非系统 NTFS/ReFS 卷、非卷根 session 目录、受保护 ACL、管理员最大卷容量和正的最低
  可用空间。Launcher 不创建、挂载、扩容或修改卷，失败返回
  `agenthost_disk_quota_unavailable`。
- 证据边界：当前只证明 `VolumeBoundaryVerified=true` 的物理卷预检；由于 M4.7/M4.11 尚未
  证明 AgentHost/Codex 只能写入该卷，`ProcessWriteConfinementVerified=false` 且
  `HardLimitVerified=false`。Launcher net45/net8 专项为 `63/63`，完整双 Shell Phase 2
  为 `360/360`，bootstrap schema 为 17，阶段 evidence schema 为 4。
  实际专用卷写满/拒绝/恢复、运维专用性和 AutoCAD/企业矩阵仍未验证，因此 M4.10 继续为
  进行中。

### M4.11 凭据 Broker

- 交付：按需向受限进程提供最小凭据材料。
- 技术：受保护继承句柄或认证通道；API token 不落盘、不放普通环境变量、不进入 argv。
- 自动化：日志、崩溃转储输入、子进程环境和工作区秘密扫描。
- 当前切口：已完成默认禁用配置、产品专属 Windows Credential Manager target 校验、
  Generic Credential 的 `4 KiB` 有界二进制读取、稳定脱敏错误、幂等 Dispose 原位清零、
  认证一次性凭据帧、隔离 `CODEX_HOME` 和 AgentHost 的 Codex CLI stdin 登录调用链。
  fake Codex 登录规格覆盖成功、非零退出、`auth.json`、超时、取消及 argv/环境不含 token；
  AgentLauncher net45/net8 各 `63/63`，Bridge `60/60`，完整 bootstrap 门禁 net45/net8
  各 `63/63`，双 Shell Phase 2 均为 `371/371`。
- 证据边界：真实 Windows Credential Manager 凭据、真实 Codex CLI/keyring 后端、
  生产 `auth.json` 行为、RestrictedToken 下 Credential Manager/keyring/Codex/Pipe/STOP
  全链、撤销/过期/并发和 AutoCAD/企业矩阵仍未验证；因此 Broker 生产入口继续禁用，
  M4.11 仍为进行中。

### M4.12 结构化审计日志

- 交付：JSONL 记录启动、请求、取消、断线、审批和终态；并冻结 CAD 执行事件的事件类型、字段白名单、脱敏规则和哈希链纳入方式。
- 技术：每条含 system IDs、request ID、事件类型、时间、脱敏 payload；不记录完整 CAD JSON。
- 自动化：schema、顺序、并发写、截断记录和磁盘错误。
- 当前切口：`codex.autocad.agenthost.audit/2` 已记录 session、Bridge、request、thread、turn、
  cancel、approval 和终态，使用字段白名单、UTC 时间、单调段内 sequence、记录数/字节上限和
  耐久 flush；审计不可继续时 Bridge 会话 fail-closed。并发写入保持完整顺序，部分写入留下
  可检测截断尾部并永久失败关闭。Bridge 为 `60/60`，双 Shell Phase 2 为 `371/371`。
   CAD 执行事件的 schema 在本里程碑冻结，真实接线归 M5.13。
  - 完成条件：审计基础设施与全部事件 schema（含 CAD 执行事件）完成并通过自动化；CAD 执行
    事件的生产接线属于 M5.13，本项不得因 M5 写入链未启用而阻塞 M4 收口。该拆分不放松任何
    安全要求：写入链启用时若未按已冻结 schema 接入哈希链，M5.13 即为未完成。

### M4.13 审计哈希链与锚点

- 交付：前项哈希、当前记录哈希、session/segment 标识、受保护链锚点。
- 技术：支持完整性验证、轮转和脱敏导出；链损坏不得悄悄忽略。
- 自动化：删除、插入、修改、截断和跨段重排检测。
- 当前切口：生产 `bootstrap-serve` 已从会被 STOP 清理的
  `workspace\sessions\<session>\audit` 切换到当前用户独立持久根
  `<LocalAppData>\OpenAI\CodexForAutoCAD\audit\agenthost`。根目录及独立
  `segments`/`anchors` 子目录使用当前用户、SYSTEM、Administrators 精确受保护 ACL，拒绝
  UNC、设备路径、非固定盘和 reparse traversal；目录身份在会话期持有句柄。JSONL 与 anchor
  分目录保存并使用 CreateNew，anchor 每条记录后耐久原子更新。STOP 删除 session workspace 后，
  持久 JSONL 和 anchor 仍存在且可验证。
- 轮转：生产日志默认每段最多 `10,000` 条或 `4 MiB`，达到边界时自动创建单调
  `segment-000001`… 文件，新段首条继承上一段最终 hash；默认最多 `64` 段，任何旧段、旧
  anchor 或段数耗尽均失败关闭，不覆盖历史文件。删除、插入、修改、截断、anchor 篡改和跨段
  重排均进入规格。
- 目录分类：只读 `AgentHostAuditCatalog` 先验证受保护根，再按文件名配对 segments/anchors，调用完整哈希链验证后分类 `complete`、`incomplete`、`corrupt`、`anchor_mismatch`。只有末条为 `session_stopped` 或 `session_failed` 的链才可标记 `complete`；进程崩溃留下的哈希/anchor 一致但无终态前缀标为 `incomplete/session_not_terminal`，禁止导出。临时 anchor、缺段、缺 anchor、异常尾部、链损坏和 session/anchor 身份不一致均不自动修复、删除或覆盖；读取有 64 MiB segment、64 KiB anchor 和目录/会话数量上限。
- 当前证据：Bridge `71/71`；PowerShell 7 与 Windows PowerShell 5.1 Phase 2 均
  `387/387`；Agent bootstrap net8/net45 各 `63/63`；Release `0 warning / 0 error`；
  AgentHost/FakeAgentHost 残留进程为 `0`。bootstrap evidence 位于
  `artifacts/autocad2016-agent-bootstrap-42de6fb97e9c4484a35333fa9e87df0d/verification.json`。
- 产品入口：新增 `audit-export --session <system-session-id>`，只接受内部 session ID，不接受
  任意源路径或 `--output`；固定读取当前用户受保护审计根，只允许 Catalog 判定为 `complete`
  的会话。导出在内存中完成完整验链与脱敏序列化后才写标准输出，失败仅返回稳定错误码，不
  产生半份 JSON。受保护目录规格覆盖完整、缺 anchor、链损坏、anchor mismatch、无终态崩溃
  前缀、非法 session ID 和不可写目标。
- 保留规划：新增只读 `audit-retention-plan`，策略必须显式提供 `older-than-days`、
  `max-store-mib` 和 `retain-complete`；固定读取当前用户受保护根，不接受任意目录，不删除、
  移动、改写或修复文件。只有完整终态会话可成为 `eligible_age`/`eligible_capacity` 候选；
  最新最低保留集为 `retain_minimum`，非终态、损坏和 anchor mismatch 固定
  `retain_manual_review`。未知 artifact 计入当前容量但不会被自动清理，无法仅靠安全候选满足容量时
  明确返回 `capacitySatisfied=false`；计划 JSON 不输出路径。
- 受控清理：新增 `audit-retention-apply --plan <64-lower-hex>`。计划 JSON 带确定性 SHA-256
  `planId`；执行命令必须再次提供同一策略和 plan ID，随后在当前用户受保护根内重新分类、验链、
  计算计划并精确比对。任何容量、时间、artifact、状态或策略变化都以 `plan_changed` 拒绝。
  清理锁、journal 和 receipt 位于独立受保护 `retention-control` 子目录并持有目录身份句柄；全计划
  journal 在首个删除前以临时文件、耐久 flush 和同卷原子 rename 提交，记录每个会话的精确段数、
  文件名、长度、UTC 时间和 SHA-256。恢复时逐文件重新核对，变化即 `artifact_changed`；日志损坏、
  段缺失、其他计划遗留日志和并发执行均失败关闭。中断后以同一 plan ID 继续，完成 receipt 使重复
  执行返回 `already_applied`。生产入口不接受任意目录或文件路径。
- 强杀恢复证据：Bridge Specs 专用子进程执行真实 `AgentHostAuditRetentionExecutor.Apply`，在
  journal 耐久提交并删除首个 anchor 后由父进程强杀；新租约以原 plan ID 恢复，候选剩余
  segment 被删除、最低保留会话未删除、journal 清除且无残留工作器。该证据不等同于系统断电、
  真实生产 AgentHost 全链或 AutoCAD 实机异常退出。
- control/receipt 收敛：最近 `256` 份 receipt 保留为精确幂等证据；更旧 receipt 按完成时间和
  plan ID 严格排序，逐份先耐久折叠到固定
  `codex.autocad.agenthost.audit-retention-receipt-checkpoint/1` 累计链，再删除原 receipt。检查点
  保存累计删除统计、链哈希、最后 receipt 哈希和严格游标；检查点提交后中断可验证残留 receipt
  并只完成删除，不重复累计。已有有效 final receipt 的 foreign temp 可安全收敛；无 final 的
  foreign temp 保持冲突，未知/恶意 control artifact 不自动删除。
- 证据边界：当前完成的是显式人工确认后的受控删除，不是后台自动清理或自动修复。企业默认保留期/
  容量、系统断电、真实生产 AgentHost/AutoCAD 异常退出矩阵、未知/恶意 control artifact 的
  企业人工复核与归档、同用户恶意篡改的签名或 HMAC 强化、企业策略和 AutoCAD 实机仍未完成；
  因此 M4.13 仍为进行中。

### M4.14 统一诊断脱敏

- 交付：异常、stderr、配置、用户名、路径、令牌和环境信息统一 sanitizer。
- 技术：先结构化分类再清洗；不依赖仅替换当前用户名的脆弱规则。
- 自动化：嵌套异常、URI、Windows/UNC 路径、Bearer/token 和 JSON payload。
- 当前进度：Contracts 已新增分类式 `DiagnosticSanitizer`，输入最多 `4096` 字符、公开输出
  最多 `512` 字符，覆盖 Bearer/敏感键值、带引号 JSON secret、Windows/UNC 路径、URI、
  域账号/邮箱身份、控制字符和双向格式字符，正则超时返回固定安全 fallback。
- 真实调用链：`AgentBridgeClientException` 与 `AgentBridgeRemoteException` 已统一清洗公开消息，
  保留稳定错误码但丢弃原始 inner exception；反向整图查询的跨进程错误响应已验证不泄露原始
  token、路径、URI、身份或嵌套异常。Contracts `99/99`、Bridge.Client `31/31`、Host.2016 MVP
  `59/59`。AppServer stderr 原本即为无文本摘要；RPC 异常不再保留原始 JSON data，只公开
  data-presence、脱敏 flags 和清洗后消息，通用/协议异常不再保留原始 inner exception；
  AppServer `37/37`。AppServer 公开异常已显式携带分类与数值脱敏计数，配置/版本预检、RPC、
  通用/协议异常分别归入稳定闭集；AgentHost 未知命令不再原样回显任意首参数。
  Contracts 现已覆盖设备命名空间路径、带空格/引号路径、转义 JSON secret、完整 URI 变体，
  并以最多 `16` 节点/深度 `8`、引用去重的方式处理嵌套与聚合异常图。
  `AgentBootstrapLaunchException` 已将配置/Credential Manager、进程环境、stderr 和其余失败映射
  到稳定分类，固定消息/错误码不变，只保留数值脱敏证据；专项 net8/net45 各 `63/63`。
  AgentHost `doctor`/`run` 已改用最小公共状态模型，不再公开 App Server 原始 `userAgent`、
  `platformOs`、`platformFamily` 或 `codexHome`。其通用 CLI 失败现使用稳定
  `agenthost_cli_failure`、`errorStage=agenthost_cli`、来源分类和数值脱敏标志；协议故障
  stderr 与 `bootstrap-doctor/bootstrap-serve` CLI 失败也不再输出 CLR 类型名。Bridge
  `77/77`。Host.2016 的
  `MvpAgentFailure.FormatForUser`
  已在结构化格式化后统一执行最外层 sanitizer；Palette/Bridge 断线和 CadQuery 命令行不再
  公开未经处理的异常消息，身份正则覆盖域账号或邮箱紧邻中文的边界。AppServer
  `ProtocolFaulted` 不再保留任意观察者的原始异常、StackTrace、`Data` 或 inner graph，只公开
  固定消息安全快照、稳定分类和数值脱敏标志。服务端请求失败响应在 `WriteErrorAsync`
  出站边界按 `RemoteError` 分类脱敏，保留 RPC 数值 code，丢弃处理器原始 data，只回传安全
  message、分类、数值脱敏标志和 data-presence；真实传输规格先 RED `36/37`，后 GREEN
  `37/37`。`audit-export`、`audit-retention-plan` 和 `audit-retention-apply` 已增加共同
  最外层失败边界；未预期异常只输出固定 `agenthost_audit_failure`、稳定 error code、
  `errorStage=agenthost_audit`、分类和数值脱敏标志，已有非法参数、预期拒绝和闭集 ReasonCode
  保持不变；Bridge `77/77`。AppServer Client 与底层 transport 的 stderr 摘要事件现在逐
  观察者隔离，异常不能阻断后续观察者、stderr 排空或退出传播；Client 侧只经固定安全
  `ProtocolFaulted` 投影报告。AgentRuntime 的 projection/observer 公共诊断不再保留原始
  异常、StackTrace、`Data` 或 inner graph，只公开固定消息快照、分类和数值脱敏标志；失败
  turn 只保留 `id`、`status` 和脱敏 `error.message`，observer 失败只保留事件类型快照。
  Bridge 公共 `Completion`/`TerminalError` 使用固定 `BridgeTerminalException` 安全快照；
  Host.2016 DrawingIndex 启动、CadQuery 和 CadQuery 下一页通用 catch 统一返回稳定
  code/stage、分类和数值脱敏标志，不再输出 CLR 类型名。动态工具校验失败原因在写入事件和
  回传本机 Codex 前按 `RemoteError` 脱敏。`CodexLocalAppServerConfigurationRequest` 与
  `AppServerClientOptions` 的字符串只报告配置存在性、条目数量和数值限制；AgentRuntime
  runtime/thread/turn options、thread handle、text/local-image/mention 输入，以及
  `BridgeRequest`/`BridgeNotification` 的字符串不再输出路径、提示词、Provider 标识或
  `BodyJson`。AppServer initialize response、notification、server request、RPC error、
  request resolution、turn interrupt 和 approval event 包装器也只报告存在性、成功状态或
  数值错误码，不再递归展开 CodexHome、Provider ID、method、JSON、错误正文、任意 result
  或审批 payload；实际属性、wire JSON、事件分发与审批处理保持不变。AgentRuntime 的 turn
  handle、item snapshot，以及消息增量、工具状态/进度、turn/review、CAD proposal/rejection
  和四类审批事件的字符串也只报告类型、枚举与字段存在性，不再展开 Provider IDs、回复内容、
  工具 JSON、错误正文或审批 payload；真实事件字段、投影和审批转发保持不变。
  AppServer 四类审批请求、嵌套权限/网络/文件系统模型、审批响应、CAD 文档身份、变更摘要与
  预览对象的字符串也已收口为类型、存在性、枚举和数量；命令、工作目录、授权路径、Provider
  ID、理由、策略修订和预览 JSON 不再被默认日志展开，wire JSON 与审批决策保持不变。
  AppServer initialize 请求侧的 client info、capabilities 和 params 也只报告配置存在性、
  布尔能力与数量，不再展开任意客户端名称、标题、版本或方法列表；initialize wire JSON
  保持不变。AgentRuntime 的 CAD 点、`create_line` 提案、提案批次和 Broker 结果 records
  也已接入同一诊断字符串门禁，不再展开坐标、图层、Provider IDs 或结果正文；强类型属性、
  提案解析和 Broker 语义不变，CAD 写入仍禁用。
  Bridge 服务端 `BridgeRemoteException` 与客户端 `AgentBridgeClientException`/
  `AgentBridgeRemoteException` 的 message、code 和嵌套异常也已统一进入分类式脱敏；合法
  稳定错误码保持兼容，非法码分别归一为 `remote_error`/`internal_error`，原始 inner
  exception 不再保留。
  `AgentHostAuditException`
  已追踪至 Bridge、CLI、导出和 UI，当前没有 raw inner 外逃路径，故不机械改动。AppServer
  `45/45`、Bridge `80/80`、AgentRuntime `39/39`，双 Shell Phase 2 `416/416`，Release
  `0 warning / 0 error`。当前 `Replace`/`Sanitize` 静态复核未发现另一套诊断清洗器，CAD
  文字摘要、cursor、命令行引用、哈希和原子文件替换保持各自语义；R20.1/.NET Framework
  4.5/x64 产品和四个 net45 依赖也已 `0 warning / 0 error` 构建通过；AgentLauncher
  bootstrap net8/net45 各 `63/63`，含连续 `500` 次启停回收。
- 收口证据：PowerShell 7 与 Windows PowerShell 5.1 Phase 2 均为 `416/416`；
  AgentLauncher bootstrap net8/net45 各 `63/63`，均含连续 `500` 次启停回收；
  R20.1/.NET Framework 4.5/x64 Host A/B 哈希一致，Autodesk DLL 复制数 `0`；相关进程
  残留 `0`，User PATH 长度/哈希保持不变。
- 状态：M4.14 的代码、自动化和静态公共出口审计已完成。真实 Codex/AutoCAD、组策略、EDR、
  受限账户、系统断电和企业保留策略故障验证属于 M4.15；M4 整体仍未完成，不允许提前进入
  M5 CAD 写入。

### M4.15 故障与企业策略矩阵

- 交付：真实 Codex/AutoCAD 异常退出、嵌套 Job、组策略限制、杀毒拦截和受限账户矩阵。
- M4.15.1 进程启动策略阻止分类：正式 `CreateProcess` 失败链将当前用户访问拒绝、显式
  Windows 执行策略/镜像哈希错误和应用阻止错误映射为稳定、不可自动重试的结构化错误；
  RestrictedToken 的普通 ACL/身份失败保持独立隔离语义。Host 只显示脱敏、可操作的管理员
  检查提示。当前代码和自动化已完成，真实 AppLocker/WDAC/EDR/组策略机器仍待验证。
- M4.15.2 嵌套 Job 企业矩阵：覆盖父进程未在 Job、已在可嵌套 Job、不可嵌套 Job、分配拒绝、
  owner 异常退出和 STOP；不允许回退为无 Job 启动。
  当前自动化准备 M4.15.2a 已完成：正式分配失败链会区分普通隔离失败和父 Job 中的嵌套分配
  失败，后者使用稳定、不可自动重试的脱敏错误和 Host 提示；当前 Windows 正向嵌套分配、
  双目标 Launcher 规格与无回退边界已验证。真实不可嵌套父 Job 和企业组合矩阵仍未验证。
- M4.15.3 真实进程退出矩阵：真实 Codex、AgentHost 和 AutoCAD 的正常退出、异常退出、
  强制终止及启动中断都必须产生唯一终态、无僵尸进程且不泄露 stderr。
  当前自动化准备 M4.15.3a 已完成：Launcher 独立发布 AgentHost 进程退出终态，正常 STOP 与
  资源限制不误报崩溃，无资源终态的根进程退出使用不可自动重试的
  `agenthost_unexpected_exit`；Host 在 Bridge fault 竞态中保持资源终态优先、退出终态其次、
  泛化断线最后，活动请求只提交一次 `failed` 并使后续 ASK fail-closed。M4.15.3b 也已完成
  自动化准备：Host STOP/退出清理主动取消进行中的 bootstrap、Bridge 握手、能力协商或 thread
  创建，预期中断不误报启动失败、不能在 STOP 后上线且只发布一个停止终态。Launcher
  net8/net45 各 `65/65`，Host MVP `61/61`，PowerShell 7 与 Windows PowerShell 5.1 Phase 2
  均为 `418/418`；R20.1/.NET Framework 4.5/x64 Host A/B 五文件输出逐字节一致，Host SHA-256
  为 `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`，Autodesk DLL
  复制数为 `0`。真实 Codex/AgentHost/AutoCAD 正常退出、强杀和分阶段启动中断仍未验证，不能把
  M4.15.3 或 M4.15 标为完成。
- M4.15.4 受限账户与执行控制矩阵：普通受限用户、RestrictedToken 或预配置 AppContainer、
  AppLocker、WDAC、代码签名及杀毒/EDR 阻止分别记录候选哈希、稳定错误码和系统事件证据。
- M4.15.5 持久化故障矩阵：系统断电、磁盘满、审计保留中断、未知 control artifact 和企业
  默认保留策略必须可恢复或明确转人工归档，不得自动修复、猜测删除或弱化 fail-closed。
  当前自动化准备 M4.15.5a 已完成：`audit-retention-plan` 增加无路径 `controlStatus`，合法
  journal/temp 报告 `recovery_required`；未知文件/目录、reparse、超限/不可读和严格 schema
  无效控制 artifact 报告 `manual_review_required`。`audit-retention-apply` 在持锁后复检，未知、
  危险或 inventory 不完整时使用稳定同名原因码拒绝且不删除原证据；合法命名但内容损坏仍保留
  更具体的既有拒绝码。M4.15.5b 自动化准备也已完成：受控模拟审计流写入和独立锚点持久化失败
  后永久 fail-closed，Bridge 会话被终止且不会尝试写入第二终态；审计保留执行器在 journal 临时
  文件已耐久但尚未提交、journal 已提交但尚未删除、以及 artifact 已删除但 receipt 尚未提交等
  阶段，将受控 I/O 故障统一映射为稳定 `cleanup_failed`。journal 提交前不删除审计 artifact，
  提交后保留 `recovery_required` 状态；同一 plan ID 重试只收敛为一次 `applied/recovered`，再次
  重试固定为 `already_applied`，不会状态回退、重复删除或重复累计。公共 CLI 只输出稳定错误码、
  阶段、`Environment` 分类和数值脱敏标志，不输出原始异常、路径或文件内容。当前 Bridge
  `83/83`，PowerShell 7 与 Windows PowerShell 5.1 Phase 2 均为 `421/421`，AgentLauncher
  net8/net45 各 `65/65`。这些均为明确标注的受控模拟故障，不等同于真实磁盘满、卷离线或断电。
  真实磁盘满、系统断电、企业默认保留和
  人工归档流程仍未验证，因此 M4.15.5 不能标为完成。
- M4.15.6 收口证据：双 Shell 自动化、目标 R20.1 构建、候选哈希、PATH/秘密扫描、进程残留、
  真实机器观察和脱敏 evidence 全部绑定；未执行的真实策略项必须明确标为未验证。
  当前自动化收口已完成，但只标记为 `automated_readiness_only`：`verify-phase2.ps1` 现可输出
  动态九项目计数和安全边界 JSON；新增 R20.1/.NET Framework 4.5/x64 双隔离构建门禁，当前
  Host `0.4.2.0` 五文件输出逐字节一致、0 warning/0 error、Autodesk DLL 复制数为 `0`，Host
  SHA-256 为 `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`；新增
  readiness 汇总器严格绑定 PowerShell 7 与 Windows PowerShell 5.1 Phase 2 `421/421`、
  AgentHost SHA-256 `780D3CD57786CC624D8A033B2069E41095F7119EE4E695110D7E94E8CCB399D2`、
  认证/bootstrap evidence、跟踪锁文件、源码 manifest、用户 PATH 长度/哈希、秘密/API 扫描和
  相关进程残留 `0`。汇总器双 Shell 自检与输出语义等价通过，不持久化本机路径、环境内容、
  凭据或 `TRUSTEDPATHS`。真实 Credential Manager/Codex keyring/RestrictedToken 产品链、固定
  容量卷、磁盘满、断电、异常退出、AppLocker/WDAC/EDR 和企业保留/归档仍全部为 `false`；
  `M4Complete=false`、`M416Frozen=false`，因此本项不等同于 M4.16 冻结。详见
  `handoff/autocad2016/M4_15_AUTOMATED_READINESS_20260726.md`。
- 完成条件：失败可诊断、默认拒绝、进程无残留、日志不泄密。
- 实机矩阵处置：9 项真实机器/企业矩阵必须在
  `handoff/autocad2016/live-matrix-results.json` 中逐项记录明确处置，不允许留空。
  处置只有两种：`verified`（在绑定候选上实测通过）或 `deferred`（写明延后理由与
  重新评估时点）。
- 其中 `RealAbnormalExitMatrixVerified` 为 M5 的硬前置，不得延后：它验证真实强杀
  AutoCAD / AgentHost / Codex 之后终态唯一、无孤儿进程、后续请求 fail-closed，
  而这正是 M5 写入过程中崩溃时唯一的保护。
- 其余 8 项允许记为 `deferred`，但必须在 M9 发布门禁和 M10 企业部署前重新评估；
  未重新评估不得进入 M12 GA 验收。

### M4.16 冻结安全前置候选

- 交付：从已提交源码构建的候选、双 Shell 门禁、资源/身份 evidence 和回滚点。
- 完成条件：M4 全部必选项通过前，Host.2016 的 CAD 写入开关保持编译期或策略级硬禁用。
- M4 必选项定义：全部自动化门禁通过，且 M4.15 的实机矩阵处置已按上述规则完成
  （`RealAbnormalExitMatrixVerified` 为 `verified`，其余允许 `deferred` 并写明理由）。
- 冻结 evidence 必须分别列出 verified 与 deferred 两组，不得合并为单一布尔结论。

## M5：`create_line` 安全写入最小闭环

大目标：只开放一个纵向能力，即“Codex 提议创建一条 Line，用户看到确定性预览后只批准
这一次操作，Host 在锁内重验并以一个 Transaction 和一个 Undo 边界执行”。任何未知、
过期、断线、重放或状态不确定都必须拒绝；绝不自动保存。

开始条件：M1 稳定状态机、M3 Line 语义、M4 安全前置全部完成。未满足时禁止把现有
Security 核心原语或 Host.2025 原型称作 M5 已完成。

当前状态：主线约 10%–15%，仅有部分 `CadApprovalGate`、强类型 proposal 和
`create_line` 安全核心原语；Host.2016 产品调用链、审批 UI 和真实事务尚未接通。

### M5.1 冻结强类型操作契约

- 交付：`CadOperationBatch`、`CreateLineOperation`、`CadOperationResult` 的版本化协议。
- 字段：system session/turn/request/tool-call ID、document identity、revision、space、
  layer policy、start/end、units、selection binding、created-at、expiry、plan hash。
- 技术：只允许结构化 DTO；禁止 AutoCAD 命令字符串、LISP、脚本、任意 API 名称和动态类型。
- 规范化：固定字段顺序、数值格式、坐标精度和 SHA-256/HMAC 输入，未知字段 fail-closed。
- 自动化：固定向量、版本不兼容、字段缺失/重复、未知 operation、NaN/Infinity 和超大 payload。
- 完成条件：Contracts 不引用 Autodesk，也不暴露可绕过审批的执行字段。

### M5.2 接通 AgentHost 提案工具

- 交付：Codex 侧唯一写入相关工具 `cad.propose_operations`，第一版只接受一个 create_line。
- 技术：AgentHost 只解析/初检并经认证 Bridge 发送 proposal，不直接执行 CAD、不引用 Autodesk。
- 绑定：proposal 必须绑定当前 request、turn、tool-call；模型不能提供 document revision、
  selection hash、审批 token 或最终计划哈希，这些由受信 Host 填充。
- 自动化：伪造绑定、重复 call ID、并发 tool call、超时、取消和 Bridge 断线。
- 完成条件：工具返回只表示“已提交/被拒绝”，不得把提案接受误报为 CAD 已修改。

### M5.3 定义 Bridge 写入协议

- 交付：proposal、preview-ready、approval-resolved、execution-started、execution-terminal 消息。
- 技术：复用现有 HMAC、seq、nonce、防重放和大小限制；所有消息含 request ID 和 correlation ID。
- 状态：协议只允许单向转换，重复或乱序消息不得触发二次执行。
- 自动化：坏 MAC、重放、乱序、截断、超限、断线和迟到终态。
- 完成条件：Bridge 只传强类型计划和结构化结果，不传 UI 控件或 Autodesk 对象。

### M5.4 在 Host.2016 实现 `CadProposalBroker`

- 交付：Host 应用服务接收 proposal，切换到 AutoCAD 合法执行上下文，读取当前文档状态。
- 技术：Bridge 回调不得直接调用 Autodesk API；通过 R20.1 已验证的 dispatcher/command
  context 进入主线程，整个等待过程不阻塞 AutoCAD UI。
- 边界：文档为空、命令繁忙、锁不可得、文档切换或 shutdown 立即结构化拒绝。
- 自动化：抽象调度器测试；R20.1 API Probe 固定实际调用成员。
- 完成条件：AgentHost 和 Bridge 项目仍无 Autodesk 引用。

### M5.5 实施提案验证与策略

- 交付：Schema、操作数、坐标、几何、空间、图层和风险策略验证器。
- 规则：第一版每批只能 1 条 Line；端点必须有限且在配置边界内；拒绝零长度、超长线、
  非法单位、未知空间、锁定/冻结/不可写图层和模型指定任意 Handle。
- 技术：所有数值和策略在审批前验证一次，锁内执行前再验证一次。
- 自动化：边界值、恶意 JSON、数值溢出、坐标炸弹、大小写/Unicode 图层混淆。
- 完成条件：验证失败时 DBMOD 不变且不显示批准按钮。

### M5.6 生成确定性只读预览

- 交付：端点、长度、图层、空间、包围盒、操作数、风险和预期 DBMOD 变化摘要。
- 技术：预览由 Host 从规范化计划生成，不信任 Agent 自述；必要时使用 Side Database
  只能作一致性预演，不得冒充进程沙箱或真实提交。
- 隐私：UI 不显示 Handle、真实路径、选择哈希、审批 token 或内部 plan hash。
- 自动化：同一计划预览一致；locale/DPI 不改变计划哈希。
- 实机：打开/关闭预览和拒绝操作时 DBMOD 必须不变。

### M5.7 实现一次性审批 UI

- 交付：Palette 只提供“拒绝”和“本次允许”，默认焦点和超时结果均为拒绝。
- 技术：不提供“始终允许”、会话级授权或记住决定；审批状态由应用状态机驱动。
- 交互：明确显示动作、对象数、图层、空间、风险和过期倒计时；流式回答与审批区分开。
- 自动化：双击允许、键盘重复、窗口隐藏、Palette Reset、文档切换和超时竞争。
- 完成条件：一次用户动作最多解决一个 approval，重复动作不执行第二次。

### M5.8 签发并消费单次能力 token

- 交付：`CadApprovalGate` 签发短时、一次使用、不可序列化泄露的 token。
- 绑定：规范化计划哈希、document identity、revision、selection policy、space、layer、
  request/turn/tool-call、nonce 和 expiry。
- 技术：秘密使用可清零缓冲区；token 不转字符串、不进日志、不通过 Agent。
- 自动化：过期、重放、篡改、跨图、跨 request、并发消费和失败后再次消费。
- 完成条件：只有审批门内部可生成有效 token。

### M5.9 获取 `DocumentLock` 后锁内重验

- 交付：执行前在锁内重新读取文档、revision、空间、图层和计划绑定条件。
- 技术：锁外审批不能保证锁内状态；任何变化都拒绝并要求重新提案/审批。
- 自动化：审批后切图、修改图层、Undo、revision 增长、关闭文档和锁竞争。
- 实机：审批后人工改变图纸，确认不会写入。
- 完成条件：没有“状态变化后仍沿用旧批准”的路径。

### M5.10 使用单 Transaction 和单 Undo 边界执行

- 交付：在一个 DocumentLock、一个 Transaction 和一个 Undo mark 中创建 Line。
- 技术：通过强类型 R20.1 API 创建实体并追加到目标空间；失败统一 abort/rollback。
- 禁止：Save、SaveAs、DwgOut、DxfOut、SendStringToExecute、LISP、脚本、动态加载。
- 自动化：事务中每个可能失败点注入异常，验证无部分提交。
- 实机：批准后只新增一条 Line；一次 Undo 完整撤销；插件不自动保存。

### M5.11 定义取消、断线与不可中断区

- 交付：提案/预览/等待审批阶段可取消；进入短事务临界区后确定性提交或回滚。
- 技术：事务开始后不因网络取消留下未知半状态；绝不自动重试写操作。
- 自动化：在每个阶段注入取消、断线、shutdown 和 timeout。
- 完成条件：若无法确认结果，返回 `unknown` 并关闭本会话后续写入。

### M5.12 返回结构化执行终态

- 交付：`completed`、`failed`、`cancelled`、`unknown`，以及 error code/stage/retryable。
- 技术：created ObjectId/Handle 不传播到 Agent 或普通 UI；只返回有限计数和结果摘要。
- 自动化：唯一终态、迟到消息、重复结果和 unknown 锁死。
- 完成条件：Agent 的 tool result 与 UI/审计使用同一受信终态。

### M5.13 建立写入审计链

- 交付：提案、验证、预览、审批展示、用户决定、token 消费、锁内重验、事务、Undo 和终态。
- 技术：记录 IDs、时间、风险、规则版本和脱敏摘要；必须复用 M4.12 已冻结的 CAD 执行事件schema、字段白名单和哈希链，不得另建审计通道或改写既有链格式。
- 自动化：拒绝、超时、重放、失败和 unknown 均有完整但不泄密的记录。
- 完成条件：可证明“谁在何时批准了哪个摘要”，但无法从日志复现 token 或敏感图纸。

### M5.14 完成自动化安全矩阵

- 覆盖：恶意参数、协议重放、并发审批、重复 token、revision 变化、图层变化、
  事务异常、取消、断线、unknown 和全部禁止 API。
- 技术：Contracts/Bridge/Security/Host MVP 分层测试；Host 禁用 API 扫描增加写入白名单例外，
  例外只允许审计过的强类型执行器。
- 完成条件：Release、Specs、安全门禁全绿，新增测试计数由脚本动态读取。

### M5.15 完成 AutoCAD 2016 实机矩阵

- 场景：批准、拒绝、超时、过期 token、重复允许、取消、断线、审批后 revision 变化、
  Undo、关闭文档、退出 AutoCAD。
- 验证：批准只增加一条 Line；其余场景 DBMOD 不产生非预期变化；一次 Undo 可撤销；
  不自动保存；无残留 AgentHost/Codex。
- 证据：精确 Host/AgentHost/manifest 哈希、脱敏结果和回滚点。

### M5.16 发布首个写入候选

- 交付：写入默认关闭，用户显式启用；管理员策略可强制永久关闭。
- 技术：能力协商仅在 M4/M5 门禁满足时声明 `create_line`；旧只读候选保持可回滚。
- 完成条件：未启用时 Agent 看不到写工具；启用后也只能执行本文唯一纵向闭环。

## M6：扩展 CAD 写入白名单

大目标：在不复制安全逻辑的前提下扩展常用写操作；所有操作复用 M5 的验证、预览、
一次审批、锁内重验、事务、Undo 和审计管线。

当前状态：未开始。M5 未完成前不得并行开放新写类型。

### M6.1 建立操作注册表

- 交付：operation type、schema version、capability、risk、数量上限和 handler 映射。
- 技术：默认拒绝未知操作；每个 handler 只处理一种强类型 DTO。

### M6.2 抽取公共执行管线

- 交付：validate -> preview -> approve -> revalidate -> execute -> audit。
- 自动化：证明新增 handler 无法绕过任何阶段。

### M6.3 增加 Circle、Arc、Polyline

- 技术：逐类型冻结有限数、半径、角度、顶点/bulge 和数量限制。
- 验收：每类独立完成恶意输入、API Probe、实机审批、Undo 和回滚。

### M6.4 增加 DBText 与 MText

- 技术：限制文字长度、格式、样式、高度和旋转；拒绝字段/表达式和外部引用注入。
- 验收：中文、换行、超长文字和样式缺失。

### M6.5 增加已有 BlockReference

- 技术：只允许插入已存在且策略允许的块定义；禁止任意路径导入 DWG。
- 验收：动态块、属性、比例、旋转和缺失块。

### M6.6 增加 Dimension 与 Hatch

- 技术：只开放最小确定字段；边界、样式和图层策略受限。
- 验收：复杂边界失败必须全批回滚。

### M6.7 开放有限属性修改

- 交付：图层、颜色、文字和少量几何属性的白名单修改。
- 技术：必须提供 before/after 预览和目标稳定引用；revision 变化后失效。

### M6.8 增加 Move、Copy、Rotate、Scale

- 技术：矩阵参数强类型化，限制对象数、范围和结果包围盒。
- 验收：不可见/锁定层、跨空间和极端 scale 拒绝。

### M6.9 最后开放删除和批量替换

- 技术：更高风险等级、恢复检查点、二次确认或管理员策略；默认关闭。
- 完成条件：恢复点创建与可读性验证失败时不得执行。

### M6.10 实现批操作依赖与原子性

- 交付：显式依赖图、最大操作数、拓扑排序和全有或全无事务。
- 自动化：循环依赖、部分失败、重复目标和资源上限。

### M6.11 建立逐操作证据

- 每种操作必须单独完成：契约、测试、R20.1 API Probe、实机批准/拒绝/Undo/回滚。
- 未完成类型不得出现在 capability 或 UI。

### M6.12 冻结写入白名单版本

- 交付：白名单版本、策略版本、兼容矩阵、迁移规则和管理员禁用入口。

## M7：会话、长期记忆与安全恢复

大目标：提供按 DWG 隔离的系统会话集合、安全长期记忆和重启恢复，但不恢复未审批、正在执行
或结果不确定的 CAD 写入。

当前状态：约 5%，只有单个内存会话和 Codex thread 映射；尚无完整会话集合、切换和持久恢复。

### M7.1 冻结系统身份模型

- 交付：conversation、turn、request、tool-call、operation、document-scope ID。
- 技术：Codex thread 仅作 provider metadata，不作为系统主键。
- 会话模型：维护会话集合、当前会话、标题、创建/更新时间和终态；同一系统会话中的消息按
  `Conversation -> Messages -> Items` 组织。
- 第一阶段：允许先实现进程内多会话，但必须使用未来 SQLite 持久化所需的同一系统 ID 和状态模型。

### M7.2 引入进程外 SQLite 存储

- 交付：schema version、WAL 策略、单写者服务和事务化迁移。
- 技术：数据库不由 AutoCAD UI 线程直接访问。

### M7.3 定义最小持久化数据

- 保存：脱敏摘要、任务终态、用户偏好、索引引用、策略版本，以及每会话独立草稿、模型和
  思考强度偏好。
- 禁止：完整 CAD JSON、真实路径、Handle、审批 token、API token、完整 stderr。

### M7.4 实现记忆控制

- 交付：启用、暂停、查看摘要、脱敏导出、按对话清除和全部清除。
- 技术：清除是可审计且有界完成的事务。

### M7.5 容量与保留策略

- 交付：每用户/每图/全局容量、保留天数和到期清理。
- 自动化：容量耗尽、并发清理和时钟变化。

### M7.6 按图隔离

- 技术：使用脱敏文档身份；禁止同名或路径变化导致跨图记忆混淆。
- 范围：会话集合、当前会话、草稿、模型/思考强度偏好、索引引用和长期记忆均按 DWG 隔离。
- 验收：A/B 图、未保存图、切换图纸和 Palette Reset 矩阵。

### M7.7 数据完整性与备份

- 交付：完整性检查、受控备份、迁移前快照、损坏隔离。
- 技术：迁移失败自动回滚到上一个 schema。

### M7.8 定义恢复规则

- 可恢复：已完成对话、用户草稿、安全偏好、最后选中的安全模型/思考强度和可验证索引引用。
- 不可恢复：等待审批、运行中任务、执行中操作、unknown 写入和旧 approval token。
- 技术：跨 AutoCAD 重启的历史、草稿和偏好恢复必须依赖 M7 SQLite；进程内多会话不得冒充
  持久恢复，Codex thread ID 仍只作为可丢弃、可重建的 provider metadata。

### M7.9 完成隐私与迁移测试

- 覆盖：升级、降级拒绝、崩溃、损坏、清除、跨图隔离和秘密扫描。

### M7.10 冻结记忆候选

- 实机：重启 AutoCAD/AgentHost 后恢复对话；未确认写入不会自动继续。

## M8：VS Code Codex 风格正式 Palette UI、设置与可用性

大目标：形成类似 VS Code Codex 插件的紧凑深色聊天工作台，同时保留 AutoCAD 特有的选择
上下文、整图索引、审批和操作结果能力。普通用户不需要理解 JSON、thread、AgentHost 或实体
内部类型。

当前状态：主线约 20%–25%。基础 Palette 与 100% DPI 已通过；用户已于 2026-07-25
确认、并于 2026-07-26 再次确认当前 Kimi 侧边栏视觉方案暂时冻结，不再继续视觉迭代。它仍是
隔离预览，必须修完审查项并接入真实状态机后才能合并；视觉冻结不等于 M8 功能完成。

### M8.1 收敛 Kimi UI 预览

- 修复：候选缺 AgentHost 包、流式期间 Send 过早启用、清掉新草稿、Handle 脱敏不完整、
  剪贴板异常捕获过窄、索引取消失败不可重试、空索引 HasIndex 语义错误。
- 验收：目标 UI specs、完整候选包和 AutoCAD 实机均通过。
- 边界：继续使用 AutoCAD 2016 programmatic WPF、net45/x64 和 R20.1；不引入 WebView、
  React、WinUI、Avalonia 或新增网络依赖。

### M8.2 建立单一 Presentation State

- 交付：会话集合、当前会话、消息、请求、上下文、索引、审批、执行和错误状态聚合模型。
- 数据结构：UI 使用系统 `Conversation -> Messages -> Items`，按 system conversation/turn/item
  累积流式 delta；Codex 原始事件和 thread ID 不进入控件模型。
- 技术：控件只渲染状态并发命令，不直接持有进程或 Bridge。
- 流式：30–60 ms 合并 Dispatcher 刷新；消息列表虚拟化；用户向上阅读时不强制滚底，并提供
  “回到最新”；只显示安全 reasoning summary/status，不展示内部思维链。

### M8.3 完成正式聊天工作台

- 交付：顶部会话栏、会话标题/切换、新建会话、真实消息列表、固定输入器，以及 Start、Stop、
  Send、Cancel 和 Clear 控件。
- 技术：新建和切换会话复用 M7 的唯一会话状态；M7 与 M8 禁止各建一套会话集合或终态逻辑。
- 验收：流式终态前不能再次 Send；offline/failed 不允许误发送。

### M8.4 保护用户草稿与 IME

- 技术：发送时记录提交文本版本；只在输入框仍等于已提交文本时清空。
- 交互：Enter 发送、Shift+Enter 换行；每个会话独立草稿；Palette Reset 后保留当前进程内草稿。
- 自动化：等待期间继续输入、会话切换、中文 IME、粘贴、剪贴板锁定和异常。

### M8.5 区分四类数据

- 交付：紧凑 CAD 上下文条，清晰区分当前选择快照、整图索引、当前会话和长期记忆，并提供各自
  独立的查看、刷新和清除动作。
- 验收：用户不会把“清上下文”误认为“新建对话”。

### M8.6 接入整图索引体验

- 交付：范围、进度、取消、重试、partial/limited、分类和 revision。
- 技术：进度限频并合并 UI 更新；取消失败后按钮恢复；空索引表示“已成功建立但实体数为 0”，
  不得与 not_built 或 failed 混淆。

### M8.7 接入写入审批体验

- 交付：预览、风险、一次允许/拒绝、过期、执行进度、结果和 Undo 指引。
- 技术：审批区与聊天输出隔离，默认拒绝且无永久授权。
- 前置：M5 安全写入闭环和 M4 安全门禁完成前，控件不得启用、暗示或模拟 CAD 写入能力。

### M8.8 接通模型与思考强度纵向能力

- 交付：会话级模型和思考强度选择，选项由后端 capability/允许列表驱动，并显示 AgentHost
  实际接受值；新会话使用受控默认值，已有会话按策略决定是否允许切换。
- 调用链：M4.1 配置 -> Contracts -> Bridge -> AgentHost -> Codex runtime -> 接受值回传 ->
  Presentation State；任何一层拒绝均返回结构化错误并保持原设置。
- 降级：后端尚未开放能力时控件禁用并显示“使用 Codex 默认值”，禁止硬编码选项、仅改变标签
  或制造已经生效的假功能。
- 自动化：模型白名单、非法思考强度、管理员锁定、断线、会话切换和实际接受值回显。

### M8.9 完成设置与诊断

- 设置：Codex 路径、健康、超时、沙箱状态、记忆、日志和写入总开关；敏感值使用引用/状态
  显示，不回显 token，管理员锁定项只读。
- 诊断：一键导出脱敏版本、错误码、状态、候选哈希和有限日志。
- 禁止：Handle、真实路径、用户名、完整环境、完整 CAD JSON、token、原始思维链和完整 stderr。

### M8.10 完成布局与 DPI

- 视觉：采用紧凑深色 Codex 风格，保持 AutoCAD 宿主可读性，不机械复制 VS Code 私有控件。
- 实机：300/360/520 DIP、100%/125%/150%、多显示器、停靠/浮动/隐藏/Reset。
- 验收：文字和按钮不重叠、不裁切，不用 viewport 宽度缩放字体，长消息和工具结果不破坏布局。

### M8.11 完成键盘与无障碍

- 交付：Tab 顺序、焦点、快捷键、Tooltip、屏幕阅读名称、高对比度和错误可感知性。
- 验收：仅键盘可完成启动、发送、取消、审批拒绝和清除。

### M8.12 冻结正式 UI 候选

- 交付：视觉基线截图、完整候选、状态矩阵、UI specs、精确候选和 DPI live evidence。
- 完成条件：模型/思考强度真实生效，多会话与草稿按 DWG 隔离，流式和取消终态正确，
  300/360/520 DIP 与 100%/125%/150% 实机通过后才冻结。

## M9：CI、测试、性能与供应链

大目标：任何提交都不能绕过关键构建、安全、兼容、性能和依赖门禁。

### M9.1 建立 Windows CI 矩阵

- 覆盖：net45/x64、net8.0-windows、PowerShell 7、Windows PowerShell 5.1。
- 技术：CI 不假定开发机 PATH，不访问用户 NuGet 配置。

### M9.2 锁定工具链

- 交付：SDK、NuGet、离线 net45 reference assemblies 和 R20.1 API Probe 输入锁。
- 验收：干净缓存构建可复现。

### M9.3 汇总必过门禁

- 覆盖：Contracts、IPC、Bridge、Launcher、AppServer、Runtime、Host MVP、Security、
  禁用 API、秘密扫描、manifest 和候选 doctor。
- 技术：测试总数动态汇总，不硬编码陈旧数字。

### M9.4 覆盖率门禁

- 目标：关键状态机不低于 90% 行覆盖、80% 分支覆盖。
- 重点：唯一终态、取消、断线、审批 token、锁内重验和事务结果。

### M9.5 属性与模糊测试

- 对象：JSON、cursor、认证帧、审批 token、操作契约和审计记录。
- 验收：崩溃、无限循环和超大分配为 0。

### M9.6 并发与耐久测试

- 场景：重复请求、并发文档、会话快速切换、Palette Reset、断线、迟到事件、delta 洪泛、
  500 次启停、长会话和至少 8 小时 soak。
- 验收：唯一终态、状态不回退、迟到事件不污染新会话、无残留进程、无密钥延迟泄露，
  长会话内存无持续增长。

### M9.7 性能回归门禁

- 交付：1k/10k/50k CAD 基线，以及消息虚拟化、delta 合并、Dispatcher 占用、会话切换和
  Palette Reset 状态保持基线。
- 规则：关键 p95、总耗时、UI 响应时间或峰值内存退化超过 15% 阻止发布。
- 配置门禁：覆盖模型白名单、非法思考强度、管理员锁定和后端未开放时的禁用状态。

### M9.8 安全静态门禁

- 交付：秘密扫描、禁止 API、依赖漏洞、SBOM、许可证检查。
- 技术：源码词法扫描明确其局限，并配合人工/IL 审查。

### M9.9 可复现候选构建

- 交付：相同提交和输入得到可解释的 manifest；时间戳等非确定字段受控。

### M9.10 测试数据治理

- 交付：所有 CAD fixture、日志和截图均脱敏、版本化、有来源说明。

### M9.11 失败证据保留

- 交付：CI 失败只上传脱敏摘要和必要 artifacts；令牌与本地路径不出现在附件。

### M9.12 发布门禁策略

- 交付：分支保护、必过检查、代码审查和例外审批流程。

## M10：签名、安装、升级与企业部署

大目标：用户不依赖源码目录、手工环境变量或开发工具即可安装、运行、升级、回滚和卸载。

### M10.1 生成 Autodesk `.bundle`

- 交付：标准目录、`PackageContents.xml`、2016/2025 分版本组件和命令注册。
- 验收：AutoCAD 只加载匹配版本二进制。

### M10.2 固定安全默认配置

- 默认：CAD 写入关闭、MCP/插件为空、日志脱敏、最小权限、无自动保存。

### M10.3 Authenticode 签名

- 对象：DLL、EXE、安装器、PowerShell 脚本和发布 manifest。
- 验收：安装前后签名链、时间戳和哈希验证通过。

### M10.4 当前用户安装

- 交付：无需管理员权限的安全安装路径、ACL 和 AutoCAD bundle 注册。

### M10.5 全机安装

- 交付：管理员控制的 ProgramData/Program Files 部署和策略。
- 技术：用户可写配置与签名程序文件分离。

### M10.6 升级、修复和卸载

- 交付：幂等安装、修复缺失文件、版本升级和干净卸载。
- 规则：保留/删除用户数据必须显式选择。

### M10.7 配置与数据库迁移回滚

- 技术：迁移前检查和备份；失败自动恢复上一个可运行版本。

### M10.8 企业策略

- 交付：Codex 路径/版本、写入开关、资源配额、审计、保留期和诊断导出策略。

### M10.9 干净机矩阵

- 环境：无源码、无 SDK、普通用户、管理员、离线/受限网络和组策略机器。
- 验收：安装、首次启动、升级、修复、卸载和回滚。

### M10.10 完成部署文档

- 交付：管理员手册、用户手册、故障排查、应急禁用和证据导出流程。

## M11：AutoCAD 2025 正式适配

大目标：2016 与 2025 共享 Agent、索引、安全、审批和审计核心，版本差异只在 Autodesk
Host 适配层；当前未提交 Host.2025 原型不能直接算产品实现。

### M11.1 冻结可复用核心边界

- 技术：先在 2016 GA 后固定 Contracts/Application Services/Bridge 边界。
- 验收：核心项目不引用任一 AutoCAD 版本程序集。

### M11.2 固定 2025 工具链

- 交付：原版 AutoCAD 2025 managed assemblies、`net8.0-windows/x64` 和 API Probe。

### M11.3 重整 Host.2025 为薄宿主

- 技术：删除与 2016 核心重复的 Agent、索引、安全和状态逻辑。
- 规则：保护用户当前未提交原型，不直接覆盖或误合并。

### M11.4 接入统一只读链

- 交付：Palette、选择 v2、整图索引、查询和 Codex 多轮对话。

### M11.5 接入统一写入链

- 交付：同一提案、审批、token、锁内重验、事务、Undo 和审计协议。

### M11.6 验证 2025 文档线程语义

- 覆盖：DocumentLock、dispatcher、Transaction、Undo、MDI 切换和退出。

### M11.7 验证 2025 对象语义

- 覆盖：M3 强类型/受限对象与版本差异，不降低 2016 契约。

### M11.8 生成独立签名 bundle

- 技术：2016 和 2025 组件路径、RuntimeRequirements 和签名明确隔离。

### M11.9 完成双版本干净机矩阵

- 验收：两个版本不能互相加载错误二进制；功能和安全证据分别绑定哈希。

## M12：最终 GA 验收与发布

大目标：通过可追溯、可复核、可回滚的双版本发布矩阵；P0/P1 缺陷为零后才标记最终完成。

### M12.1 建立需求追踪矩阵

- 交付：每项需求对应源码、测试、候选哈希、实机证据、文档和负责人。

### M12.2 独立安全复核

- 范围：威胁模型、进程隔离、凭据、审批、防重放、审计、隐私和供应链。
- 完成条件：所有 P0/P1 安全问题关闭或明确阻断发布。

### M12.3 冻结最终候选

- 技术：2016/2025 各自从精确提交构建，冻结签名、manifest、SBOM 和回滚包。
- 规则：冻结后不混入无关修改。

### M12.4 故障恢复演练

- 场景：Codex 不可用、AgentHost 崩溃、Bridge 断线、磁盘满、审计损坏、迁移失败和回滚。

### M12.5 双版本功能矩阵

- 覆盖：安装、读取、50k 索引、分页查询、多轮对话、审批写入、Undo、退出和卸载。

### M12.6 性能与耐久终验

- 覆盖：冻结性能阈值、500 次启停、8 小时 soak、多文档和高 DPI。

### M12.7 隐私与证据终验

- 验收：发布包、日志、诊断导出、evidence 和文档无秘密及敏感本地信息。

### M12.8 文档与支持就绪

- 交付：用户、管理员、安装、升级、故障排查、应急禁用、隐私和已知限制。

### M12.9 发布决策

- 条件：P0/P1 缺陷为 0；所有必选 CI、实机、安全、签名和安装门禁通过。
- 未满足：保持 RC，不得改称 GA 或“最终产品完成”。

### M12.10 发布与回滚观察

- 交付：分阶段发布、错误遥测的脱敏汇总、回滚触发阈值和首个稳定观察窗口。

## 2. 实际执行顺序

阶段依赖不是简单编号串行，实际顺序如下：

1. 先在 M1 Worktree 完成总超时覆盖 `starting_provider` 的 RED/GREEN 修复、双 Shell
   Phase 2 和 Host.2016 专项门禁，并拆成独立提交。
2. 在干净集成分支按依赖顺序受控吸收 M1 的 9 个原提交和总超时修复；不得整分支硬合并，
   不得触碰主工作区未提交的 Host.2025/Kimi UI 原型。
3. 完成 M1 的 AutoCAD 2016 实机矩阵：取消、断线、超时、按图隔离、正常退出和
   125%/150% DPI，随后冻结稳定只读候选。
4. M2 DrawingIndex 与 M3 读取语义可并行开发，但必须在同一 Host 线程/DTO/限制契约上汇合。
5. M4.4/M4.5 已完成；继续 M4.6–M4.16。它们可与 M2/M3 并行。M5 的阻断条件为
   M4.16 完成条件所定义的 M4 必选项，而不是 9 项实机矩阵全部 verified。
6. M5 只做 `create_line` 一条纵向闭环，不同时扩展多种写操作。
7. M5 实机通过后进入 M6；M7 与 M8 可在稳定状态模型上并行，但不能各建一套状态。
8. M9 从现在开始持续建设，M10 在功能/安全接口稳定后冻结。
9. AutoCAD 2016 达到 GA 后才正式执行 M11；最后由 M12 统一发布验收。
10. 正式聊天 UI 以 M1 稳定状态机为前置；Kimi 可先完成 M8.1–M8.6、M8.10–M8.11 的
    视觉和 Presentation 层，但不得复制任务状态机或直接持有 Bridge/进程。
11. M4.1、Contracts、Bridge 和 AgentHost 先补齐模型/思考强度字段、capability、白名单和
    接受值回传，再接 M8.8；在此之前 UI 必须显示“使用 Codex 默认值”并禁用选择器。
12. 进程内多会话可先落地；跨 AutoCAD 重启的历史、草稿和偏好恢复必须等待 M7 SQLite。
    M7 与 M8 始终共享同一套系统会话 ID、当前会话和消息状态。

关键路径：

`M0 -> M1 -> M2/M3 -> M4 -> M5 -> M6 -> M7/M8 -> M9 -> M10 -> M11 -> M12`

## 3. 下一批可执行目标

下一批不是开始 CAD 写入，而是以下顺序：

1. [已完成] 运行 `HOST2016_TURN_START_TIMEOUT_FAILS_CLOSED`，确认 Provider start 阶段
   也受 Host 总截止时间保护，晚到 turn ID 不会复活请求。
2. [已完成] `MvpAgentClient` 在登记 turn 后立即启动 timeout monitor，并删除 start 响应后的重复启动。
3. [已完成] 重跑 Host MVP、双 Shell Phase 2、R20.1 v2 API stage 和 M1 candidate freeze；
   当前证据位于 M1 Worktree 的 `handoff/autocad2016/evidence/`，核心二进制哈希可复现。
4. [待用户授权] 在干净集成分支按依赖顺序受控吸收 M1 的 10 个提交；不得整分支硬合并，
   不夹带 M4 堆叠提交，不触碰主工作区未提交的 Host.2025/Kimi UI 原型。当前 merge-tree
   审计无冲突，但本项目约束禁止我未经明确授权执行 merge/cherry-pick/commit。
5. [待实机] 用户使用精确候选哈希执行 M1 矩阵：取消、断线、超时、按图隔离、正常退出、
   Palette Reset、125%/150% DPI；证据必须绑定候选，不继承旧版本运行时结论。
6. [后续] M1 受控吸收且实机通过后，开始 M2.1–M2.5 DrawingIndex/CadQuery 协议、
   主线程分片、DTO 边界和基础索引；同时继续收口 M4.6/M4.8/M4.9。

遇到以下情况必须暂停并请求用户决策：

- 需要启用 CAD 写入但 M4 必选项尚未完成，或 `RealAbnormalExitMatrixVerified`
  仍未 `verified`。
- 需要覆盖用户未提交的 Host.2025/Kimi UI 修改。
- 候选哈希、源码提交和 evidence 无法一一对应。
- 需要记录真实图纸、路径、Handle、令牌或完整环境变量才能继续。
- AutoCAD 2016 原版 API 与目标设计冲突，且替代方案会扩大安全边界。
- 需要把任何 `deferred` 矩阵改写为 `verified` 而没有对应的实机 evidence。

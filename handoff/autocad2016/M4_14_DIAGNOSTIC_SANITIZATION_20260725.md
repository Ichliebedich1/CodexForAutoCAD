# M4.14 统一诊断脱敏已完成纵切（最后复验：2026-07-26）

## 本轮结论

当前只完成 M4.14 的若干真实生产边界，不代表 M4.14 或 M4 已完成：

- Contracts 新增 `DiagnosticSanitizer`，调用方必须先指定诊断来源分类。
- 输入最多 4096 字符，公开输出最多 512 字符。
- 清除 Bearer、access/refresh token、API key、Authorization、password、secret、credential、
  带引号 JSON 敏感键值、Windows/UNC 路径、URI、域账号、邮箱身份、控制字符和双向格式字符。
- 正则匹配超时返回固定 `[redacted-diagnostic]`，不回退原始文本。
- `AgentBridgeClientException` 和 `AgentBridgeRemoteException` 保留稳定错误码，但公开消息在构造
  时统一清洗；原始 inner exception 不再保留，避免其 Message、Data 或 StackTrace 泄露。
- Bridge 服务端 `BridgeRemoteException` 与客户端 `AgentBridgeClientException` 的错误码也已
  收口：合法闭集码保持兼容，含路径、令牌或其他非法字符的任意远端码分别归一为
  `remote_error`/`internal_error`；消息、错误码和嵌套异常的来源分类与数值脱敏证据合并公开，
  不保留原始 inner exception。
- `RunDrawingQueryHandlerAsync` 的真实跨进程错误响应已验证只传递清洗后的有界文本。
- AppServer stderr 继续保持只有字节数与截断位的无文本摘要。RPC 异常保留数值 code，但原始
  JSON data 不再保留，只公开 `DataWasPresent`、脱敏 flags 和清洗后的消息；通用/协议异常也
  不再保留任意原始 inner exception。
- AppServer 公开异常现在显式携带 `DiagnosticClassification` 与数值
  `DiagnosticRedactions`；配置与版本预检为 `Configuration`，RPC 为 `RemoteError`，
  通用/协议异常为 `Exception`。
- AgentHost 未知命令不再原样回显任意首参数；输出仅包含按 `Configuration` 分类后的清洗命令、
  数值脱敏计数和固定 usage。
- Contracts 已覆盖 `\\?\`、`\\.\`、`\??\` 设备路径、带空格/引号路径、转义 JSON secret、
  一字符 scheme 及 URI userinfo/query/fragment；异常图最多遍历 `16` 个节点、深度 `8`，按
  引用去重，超深/超宽统一标记 `Truncated`，不保留异常对象、堆栈或 `Data`。
- `AgentBootstrapLaunchException` 已接入真实 Launcher 失败边界：配置/凭据、进程环境、
  stderr 和其余异常分别映射到稳定分类；公开仍只有固定消息和错误码，只附带数值脱敏证据，
  直接诊断和嵌套/AggregateException 原文均不保存。
- AgentHost `doctor` 与 `run` 的成功 JSON 已改为最小公共状态模型；不再公开 App Server 原始
  `userAgent`、`platformOs`、`platformFamily` 或 `codexHome`，只保留兼容性、配置存在性、
  Codex 版本/允许区间、可执行来源和固定沙箱状态。
- Host.2016 的 Palette 与命令行公共错误边界已统一收口：`MvpAgentFailure.FormatForUser`
  在结构化格式化后强制执行一次有界 `DiagnosticSanitizer`；Bridge 断线提示保留稳定语义但在
  最外层脱敏，`CODEX16QUERY`/`CODEX16QUERYNEXT` 不再直接显示未经处理的异常消息。
- 身份脱敏正则已覆盖邮箱或域账号与中文相邻的 Unicode 边界，避免依赖 ASCII `\b` 造成漏网。
- AppServer `ProtocolFaulted` 公共事件不再转交任意观察者或处理器抛出的原始 `Exception`；
  兼容 `Exception` 投影现在是新建的固定消息安全快照，原对象、StackTrace、`Data` 和 inner
  exception graph 均不保留，事件另行公开稳定分类和数值脱敏标志。
- AppServer 的服务端请求失败响应已在唯一 `WriteErrorAsync` 出站边界统一收口：保留
  JSON-RPC 数值 code，message 按 `RemoteError` 分类执行有界脱敏，处理器提供的任意原始
  JSON data 不再写回本机 Codex 子进程，只回传 `diagnosticClassification`、数值
  `diagnosticRedactions` 和 `sourceDataWasPresent`。
- AgentHost `doctor/run` 的通用 CLI 失败不再把 `ArgumentException` 等 CLR 实现类型写进
  公共 JSON；统一使用稳定错误、`errorStage=agenthost_cli`、分类和数值脱敏标志。已有配置、
  版本预检和健康检查错误码保持不变，只补充同一结构化元数据。
- AgentHost 协议故障 stderr 只输出固定 `appserver_protocol_fault`、分类和数值脱敏标志；
  `bootstrap-doctor/bootstrap-serve` 失败也改为稳定错误码，不改变认证 frame、stdout 或
  Launcher 只保留 stderr 字节数/截断位的边界。
- `audit-export`、`audit-retention-plan` 和 `audit-retention-apply` 现在统一经过 AgentHost
  审计 CLI 最外层失败边界；未预期异常只输出固定 `agenthost_audit_failure`、稳定 error code、
  `errorStage=agenthost_audit`、分类和数值脱敏标志。已有 `invalid_arguments`、
  `audit_*_rejected` 和闭集 ReasonCode 保持不变。
- AppServer Client 与底层 `CodexProcessTransport` 的 stderr 摘要事件现在都逐观察者隔离：
  一个诊断观察者抛异常不会阻断后续观察者、stderr 排空或退出传播；Client 侧观察者异常只经
  既有 `ProtocolFaulted` 固定安全快照报告，底层 transport 不保留或外抛观察者异常。
- AgentRuntime 的 `ProjectionFailed` 与 `EventObserverFailed` 不再把原始异常对象、
  StackTrace、`Data` 或 inner graph 交给公共诊断订阅者；兼容 `Exception` 投影改为新建固定
  消息快照，并单独公开来源分类和数值脱敏标志。
- AgentRuntime 的失败 turn 不再保留完整 Provider `turn` JSON；公共事件只保留 `id`、
  `status` 和已经按 `RemoteError` 分类脱敏的 `error.message`。observer 失败诊断也不再持有
  原始 `AgentEvent`，只公开事件类型安全快照，Provider thread/turn、delta、工具数据和原始
  payload 均被省略。
- Codex 动态工具参数校验失败的拒绝原因在写入内部事件和回传本机 Codex 前统一按
  `RemoteError` 分类脱敏；未知属性名或非法类型值不能借错误消息回显令牌、路径或身份。
- Bridge 的公共 `Completion`/`TerminalError` 不再返回 notification handler 或 transport
  抛出的原始异常；统一使用固定消息 `BridgeTerminalException`，只保留来源分类和数值脱敏
  标志。既有 authentication/capacity/protocol 强类型语义保持，迟到 handler fault 仍被观察。
- Host.2016 的 DrawingIndex 启动、CadQuery 和 CadQuery 下一页三个通用 catch 分支不再输出
  CLR 类型名；统一返回 `internal_error`、稳定 `error_stage`、`Exception` 分类和数值脱敏
  标志，原始异常图在计算标志后立即丢弃。
- `CodexLocalAppServerConfigurationRequest` 与 `AppServerClientOptions` 不再使用会展开
  Codex 路径、工作/临时/home 目录、完整 PATH、启动参数或环境字典的编译器 record
  `ToString()`；公共字符串只报告配置存在性、条目数量和数值限制。
- AgentRuntime 的 runtime/thread/turn options、thread handle，以及 text/local-image/mention
  输入不再通过 record `ToString()` 展开工作区路径、Provider 标识、DeveloperInstructions、
  用户提示词、输出 schema 或本地文件路径；实际字段和 wire 序列化保持不变。
- `BridgeRequest` 与 `BridgeNotification` 的公共字符串不再展开 request/notification id、
  method 或 `BodyJson`，避免完整 CAD 上下文、查询结果和错误 JSON 被普通对象日志旁路输出。
- AppServer transport/control 包装器的公共字符串不再递归展开 CodexHome、Provider
  thread/turn/request ID、method、完整 params/data JSON、错误正文、任意 result 或审批
  payload；覆盖 initialize response、notification、server request、RPC error、request
  resolution、turn interrupt 和 approval event。实际属性、wire JSON、事件分发与审批处理
  保持不变。
- AgentRuntime 的 turn handle、item snapshot，以及消息增量、工具状态/进度、turn/review、
  CAD proposal/rejection 和四类审批事件不再通过 record `ToString()` 展开 Provider IDs、
  回复内容、工具 JSON、错误正文或审批 payload；只报告稳定类型、枚举和字段存在性，事件属性、
  投影、流式内容与审批转发不变。
- AppServer 四类审批请求、权限/网络/文件系统子模型、审批响应、CAD 文档身份、变更摘要与
  预览对象的公开 `ToString()` 也已收口；不能再展开命令、工作目录、授权路径、Provider ID、
  理由、策略修订、图纸身份或预览 JSON，只报告类型、布尔存在性、枚举和数量。wire JSON、
  审批字段和决策联合类型保持不变。
- AppServer initialize 请求侧的 client info、capabilities 和 params 也不再由 record 默认
  `ToString()` 展开任意客户端名称、标题、版本或方法列表；只报告配置存在性、布尔能力和数量，
  initialize wire JSON 保持不变。
- AgentRuntime 的 CAD 点、`create_line` 提案、提案批次与 Broker 结果也已加入同一公共 record
  字符串门禁；坐标、图层、Provider IDs 和结果正文不再因普通对象日志旁路展开，实际强类型
  属性、提案解析和 Broker 结果语义保持不变，CAD 写入仍禁用。
- `AgentHostAuditException` 的 raw inner exception 路径已从 `Record/CreateForCurrentUser`
  追到生产 Bridge、CLI、导出和 UI：请求响应固定为 `handler_error`，Bridge terminal 使用安全
  快照，bootstrap/audit CLI 使用结构化脱敏边界，导出只复制白名单字段；当前没有原始 inner
  外逃路径，因此没有仅因 public 构造器存在而机械重构。

## RED → GREEN 证据

新增 Host.2016 公共行为规格先复现 `AgentBridgeClientException` 保留原始 inner exception：

- RED：Host.2016 MVP `56/57`，唯一失败为原始 inner exception 仍可观察。
- 中间 GREEN：Host.2016 MVP `57/57`。
- Host.2016 最外层公共边界规格先 RED `57/58`，最终 GREEN `58/58`。
- Contracts：`97/97`。
- Bridge.Client 跨进程规格：由 `30/30` 增至 `31/31`，新增反向图纸查询错误脱敏。
- AppServer：先以 `32/33` 复现原始 RPC data 泄漏，再以 `33/34` 复现原始协议 inner 泄漏，
  最终新增配置/版本预检分类规格并达到 `35/35`；协议故障事件纵切随后达到 `36/36`。服务端
  请求失败响应的真实传输规格再以 `36/37` 复现原始 message/data 出站，最终达到 `37/37`。
- AgentHost：未知命令泄漏规格先以 Bridge `71/72` 复现受保护标记泄漏，修复后为 `72/72`。
- Contracts：设备路径/转义 JSON/URI 变体先以 `97/98` 复现泄漏，异常图入口再以缺少 API 的
  编译 RED 复现，最终 `99/99`。
- AgentLauncher：分类与数值脱敏证据先以缺少公开属性的编译 RED 复现，最终 bootstrap
  net8/net45 各 `63/63`。
- AgentHost 通用 CLI 先 RED `73/74`；协议故障 stderr 以缺少稳定格式化入口的编译 RED
  复现；bootstrap CLI 再 RED `75/76`，最终 Bridge `76/76`。
- AgentHost 审计 CLI 以缺少共同捕获入口的编译 RED 复现，修复后 Bridge `77/77`。
- AppServer Client stderr 观察者隔离先 RED `37/38`、后 GREEN `38/38`；底层真实子进程
  transport 观察者隔离再 RED `38/39`、后 GREEN `39/39`。
- AgentRuntime 公共诊断事件先以缺少结构化安全投影的编译 RED 复现并达到 `35/35`；动态工具
  校验错误出站泄漏再 RED `35/36`、后 GREEN `36/36`。
- AgentRuntime 失败 turn 的原始 Provider JSON 泄漏与 observer 持有原始 Agent 事件分别由
  真实 RED 复现，最终专项由 `36/36` 增至 `38/38`。
- Bridge notification handler 的原始异常可从公共 `Completion`/`TerminalError` 观察到，修复
  后公共终态只保留安全异常快照；Bridge 完整回归保持 `77/77`。
- Host.2016 命令诊断先以缺少统一格式化入口的编译 RED 复现，三个真实命令 catch 分支接入后
  Host.2016 MVP 由 `58/58` 增至 `59/59`；目标 R20.1/.NET Framework 4.5/x64 产品构建及四个
  net45 依赖均为 `0 warning / 0 error`。
- 配置 record 的默认字符串先真实输出 7 处路径和完整 PATH 片段，AppServer RED 为 `39/40`；
  `AppServerClientOptions` 再真实输出可执行路径和工作目录，RED 为 `40/41`，最终
  AppServer `41/41`。
- AgentRuntime options/handle/input 默认字符串泄漏路径、提示词和 Provider 标识，先 RED
  `38/39`，后 GREEN `39/39`；Bridge request/notification 默认字符串泄漏完整 `BodyJson`，
  专项先 `0/1`，后完整 Bridge `78/78`。
- AppServer transport wrappers 先真实输出 CodexHome、method、JSON、错误正文和任意 result，
  RED `41/42` 后 GREEN `42/42`；turn interrupt 与 approval event 再真实输出 Provider ID、
  命令、工作目录和审批原因，RED `42/43` 后 GREEN `43/43`。测试同时确认 wire JSON 与事件
  payload 未因诊断字符串收口而改变。
- AgentRuntime 既有公开 record 规格扩展到 turn handle、item snapshot 和全部公开事件后，
  真实 RED `38/39` 复现 Provider IDs、流式内容、工具 JSON、错误正文与审批 payload 泄漏；
  GREEN `39/39`，既有事件投影、流式、审批和只读查询回归保持通过。
- Bridge 服务端远端异常的原始 message/非法 code，以及 Bridge Client 公共异常的非法 code
  与 inner exception 数值证据分别由新增规格真实覆盖；最终 Bridge 从 `78/78` 增至 `80/80`。
  两处错误码验证的 net45 nullable 流分析问题以显式非空检查关闭，四个 net45 依赖和
  R20.1/.NET Framework 4.5/x64 Host 产品构建均为 `0 warning / 0 error`，Autodesk DLL
  复制数为 `0`。
- AppServer 审批 payload 默认 record 字符串先以 `43/44` 真实复现 `CommandAction` 展开受保护
  标记；整组请求/响应与嵌套权限模型接入安全字符串投影后为 `44/44`，AgentRuntime 回归
  `39/39`，测试同时证明序列化 wire JSON 仍保留原协议字段。
- AppServer initialize 请求 records 随后以 `44/45` 真实复现 client info 任意配置文本泄漏，
  GREEN 后为 `45/45`；测试同时证明 initialize wire JSON 未改变。
- AgentRuntime 既有公开 record 规格补入 CAD 点、`create_line` 提案、提案批次和 Broker 结果后，
  真实 RED `38/39` 复现坐标、图层、Provider IDs 与结果正文泄漏，GREEN 恢复 `39/39`；修改
  仅限 `ToString()`，不启用 CAD 写入，也不改变工具或 Broker 数据。

完整门禁在 Windows PowerShell 5.1 与 PowerShell 7 均通过：

- 动态规格总数：`416/416`。
- AppServer：`45/45`。
- Bridge：`80/80`。
- AgentRuntime：`39/39`。
- Host.2016 MVP：`59/59`。
- AgentLauncher bootstrap net8/net45：各 `63/63`，含连续 `500` 次启停回收。
- Release：`0 warning / 0 error`。
- Host 禁用 API、AgentHost doctor、敏感信息扫描和 `git diff --check`：通过。
- AgentHost、FakeAgentHost、Bridge Client TestServer 和强杀恢复工作器残留：`0`。
- R20.1/.NET Framework 4.5/x64 Host A/B 输出逐字节一致，当前 Host SHA-256 为
  `866566D5B53AC53047316E0696613EFD5B9DB8C8B0EA4BA44C75EEFE12DC8B8B`；
  Autodesk DLL 复制数为 `0`。
- User PATH：长度 `661`，SHA-256
  `05df0d2ffc86d41186216560d37cc16fa0159ed5cef9a89f61042964c196be59`，项目可疑条目 `0`。

## 收口结论与后续边界

M4.14 的代码、自动化和静态公共出口审计已经收口：

1. 已复核 AgentRuntime、Bridge、Host、AgentHost 审计导出/保留、CLI JSON、Doctor/Run、
   Host BuildInfo、DrawingIndex/CadQuery 和剩余公共 record/EventArgs 字符串出口；未发现新的
   可复现公共泄漏。
2. `AgentHostAuditException` 与 `AgentEventProjectionException` 当前均未证明存在生产公共
   外逃；后续不得仅因公开构造器存在而机械改动。
3. `Replace`/`Sanitize` 静态复核未发现另一套诊断清洗器；CAD 文字摘要、cursor、命令行引用、
   哈希和原子文件替换继续保留各自业务语义。
4. 编码、转义、超限和新公共出口继续作为回归纪律；只有发现真实泄漏旁路时才重新打开 M4.14。

真实 Codex、AutoCAD、组策略、EDR、受限账户、系统断电和企业保留策略故障验证属于 M4.15，
不再作为 M4.14 未完成代码审计。M4 整体仍未完成，M5 CAD 写入继续保持禁用。

本轮没有启动或控制 AutoCAD，没有启用 CAD 写入、保存、命令、LISP、Shell、文件或网络 Agent
工具，也没有提交、合并、cherry-pick、push、reset 或清理 Git 工作树。

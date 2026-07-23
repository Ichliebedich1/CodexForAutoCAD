# M4：进程隔离与诊断基线

最后更新：2026-07-23（北京时间）

## 状态

本文件记录 M4 已完成的十二个小切口：Codex 子进程 stderr/AgentHost 诊断脱敏、本机 Codex
启动配置、AgentHost 进程树的 Job Object 边界、该 Job 的进程数/总提交内存硬限制，以及
AgentHost 只读会话的内容脱敏 JSONL 运行审计与本地 SHA-256 哈希链、Codex 子进程父环境白名单和
CPU/累计用户时间/会话墙钟限制、已认证 AgentHost 异常退出后的 retained-Job 清理、workspace/audit
私有 ACL 与有界保留、Codex 版本/App Server 健康预检、默认空 MCP；不是完整沙箱候选，不代表已完成每会话 `CODEX_HOME`、磁盘硬配额、凭据隔离、外部不可篡改
审计或 CAD 写入终态审计。
本轮没有启动、关闭或控制 AutoCAD，也没有加载 DLL、保存或修改图纸。

当前代码调用链仍为：

```text
AutoCAD Host.2016
  -> authenticated AgentHost bootstrap
     (unnamed Windows Job Object; kill-on-close + process/memory/CPU/user-time limits)
  -> AgentHost Program
  -> CodexVersionPreflight (codex --version; product-owned compatibility range)
  -> CodexChildEnvironmentPolicy
  -> CodexProcessTransport (clear parent environment + fixed allowlist)
  -> codex app-server --stdio -c mcp_servers={} (inherits AgentHost Job membership)
```

现有受限 bootstrap、批准的 AgentHost EXE 哈希、受限继承句柄和有界直接子进程终止继续保留。
启动器在恢复 AgentHost 前创建未命名 Job Object、设置 `KILL_ON_JOB_CLOSE`、`ACTIVE_PROCESS`、
`JOB_MEMORY`、`JOB_TIME` 和 CPU hard cap 并完成进程分配；该 Job handle 随受认证的 service
session 保存。因此正常停止、拥有它的 Host 进程结束，或已认证 AgentHost 自行退出都会关闭
该边界。最后一条由后台退出监视器接管并走同一有界 cleanup；普通后代会继承 Job membership。
真实 Codex 的配额耗尽和异常退出矩阵仍需另做验证。

## 本轮完成

- `AppServerClientOptions.MaximumStandardErrorBytes` 默认限制为 `16 KiB`，范围为 `1 KiB` 至
  `1 MiB`。
- `CodexProcessTransport` 不再以 `ReadLineAsync` 保留或广播 Codex stderr 原文；它按固定字节
  缓冲区持续排空、清零缓冲区，并只产生 `bytes`/`truncated` 摘要。
- 进程退出事件在异步 stderr 排空完成后才发布摘要；这不会阻塞进程事件线程，也不会把原文
  重新引入诊断链路。
- AppServer stderr 事件、进程退出事件和退出异常均不再携带原始 stderr 文本。
- AgentHost 控制台诊断不再输出 Codex stderr 原文、协议异常正文、工作目录或 `CODEX_HOME`
  路径；正常 doctor 仅报告工作区已就绪与 Codex home 是否已配置。
- AppServer 规格新增“有界无内容 stderr 摘要”和“无效 stderr 限额被拒绝”两项。
- `WindowsInheritedBootstrapProcess` 在校验 AgentHost 映像后、恢复主线程前，将其加入具有
  `KILL_ON_JOB_CLOSE` 的未命名 Windows Job Object。关闭 session/拥有者进程时该边界会回收
  AgentHost 和其普通后代；分配失败 fail-closed，沿用既有结构化启动失败路径。
- 若当前 Windows 版本或企业策略不允许嵌套 Job，进程分配会安全失败；不得回退为无 Job 的
  AgentHost。该受限环境兼容矩阵仍未实测。
- AgentHost/Codex 进程树默认最多 `16` 个进程、Job 总提交内存最多 `4 GiB`。可接受范围分别
  为 `2..64` 和 `512 MiB..16 GiB`；非法值在创建子进程前以 `InvalidConfiguration`
  fail-closed。当前 Host 使用这两个安全默认值，尚未提供面向用户的配置入口。
- 同一 Job 工厂创建后会通过 `QueryInformationJobObject` 读回限制标志和值；该检查证明
  Windows 接受配置，不等于故意耗尽进程槽或内存后的行为测试。
- CPU hard cap 默认 `75%`，累计 Job user-time 默认 `8` 小时；允许范围分别为 `1..100%` 与
  `100 ms..7 d`。Job user-time 是 AgentHost/Codex 全进程树累计用户态 CPU 时间，不是墙钟时间。
  CPU-busy synthetic child 已验证 user-time 耗尽后由 Windows 终止整个 Job；CPU 节流性能未测量。
- 认证完成后的 service session 另有默认 `24` 小时墙钟截止，允许 `1 s..7 d`；截止后调用既有
  有界 Stop 清理，首次终止失败会在 `100 ms` 后重试一次，连续失败会记录 late failure 并阻止
  后续启动。`RuntimeExpired` 保留终止原因状态。
- 已认证 AgentHost 的后台退出监视器会在根进程自行退出、或等待根进程发生不可恢复错误时触发同一
  有界 Stop 收口；自动清理失败会在 `100 ms` 后重试一次，再失败才 poison 后续启动。
- AgentLauncher net45/net8 规格各 `37/37`：隔离的 `bootstrap-serve` 假 AgentHost 会启动一个
  已知挂起的后代；`StopAsync` 返回后、拥有 Job 的启动器不调用停止逻辑而直接退出后，以及已建立
  session 的根 AgentHost 自行退出而启动器仍存活时，父/后代 PID 都必须消失。最后一条在检查后代
  PID 已消失前不会调用 `STOP`。规格还验证默认/自定义限额、Windows 读回值和非法配置。专用
  引导门禁还验证 Job user-time 真实终止、墙钟终止、显式 STOP 胜过已撤销截止、清理重试和
  连续失败后阻断后续启动；相关进程基线/终态均为 `0`，没有启动或操作 AutoCAD。
- `AgentHostAuditLog` 已成为 `bootstrap-serve` 真实会话的必需依赖。它在当前用户的本地固定
  磁盘目录 `%LOCALAPPDATA%\OpenAI\CodexForAutoCAD\audit\agenthost` 创建每会话独占
  `CreateNew` JSONL 文件；目录拒绝 UNC、设备路径、非固定盘和任一已发现的重解析点。
- 每条记录按 UTF-8 单行 JSON 立即落盘，并有单调 sequence；默认上限为 `10,000` 条记录和
  `4 MiB`。`/2` 使用首行零哈希、逐行 `previousRecordHash`/`recordHash` 的 canonical SHA-256
  链，并由有界内部验证器检查字段、删行、序号、前序哈希和终态。字段只允许 schema、UTC 时间、
  AgentHost session ID、系统 conversation/request ID、
  Bridge request ID、Provider thread/turn ID、方法、审批种类、稳定 outcome/error code。提示词、
  canonical CAD JSON、图名/路径、实体内容、命令文本、工作目录、环境变量、异常正文、token 和
  Provider 原始 payload 没有可写字段。
- 已接入 `session_started/stopped/failed`、`bridge_connected/disconnected`、请求收发/失败、
  thread/turn 创建、取消请求/已分派、审批请求和 turn 完成/取消/失败。审计容量或写入失败会
  取消并释放认证 Bridge 会话，不会静默丢弃记录后继续运行。
- 生产 Codex client 强制关闭父环境继承，先清空 `ProcessStartInfo.Environment`，再注入固定 `16`
  个变量名；`TEMP`/`TMP` 指向 `AgentWorkspace.Temp`，`PATH` 只含批准的 Windows 系统目录。
  `CODEX_HOME`、token/API key、代理、父 `PATH`、`PSModulePath` 和自定义调试变量均不自动传入。
- 生产本机配置还固定附加 `-c mcp_servers={}`，用 Codex TOML 配置覆盖默认用户 profile 的 MCP
  server 表；项目代码不直接读取或复制 profile 内容，但不构成插件、`CODEX_HOME` 或凭据隔离。
- synthetic child 已证明父哨兵不可见、显式允许变量可见、`null` 删除继承值；真实 doctor 与
  两轮认证 Codex v2 对话继续通过，清理后 AgentHost/app-server 均为 `0`。当前仍使用默认用户
  Codex home 兼容文件登录，不应写成凭据或插件配置隔离。
- workspace session 根、四个子目录、lease、audit 根和 JSONL 现在关闭 ACL 继承，仅允许当前
  用户、SYSTEM 与内置 Administrators；设置后读回 owner 和完整规则集，偏差即 fail-closed。
- 每个 session 使用独占 lease。正常退出删除当前 workspace；残留按 `24` 小时和最多 `64` 个
  session 有界清理。审计按 `30` 天和最多 `512` 个受管理文件清理；活动 lease/日志不会被删除。
- 私有路径拒绝 UNC、设备路径、ADS 和重解析根；目录树清理不跟随重解析点，单次最多访问
  `50,000` 项。正常 STOP 先给 AgentHost `1` 秒自然退出以完成审计/workspace 收口，随后才进入
  既有 `5` 秒强制回收。
- `doctor`、`run` 和认证 `bootstrap-serve` 现先在与 App Server 相同的受控 child allowlist 中执行
  `codex --version`。产品冻结范围为 `>=0.144.4 <0.145.0`，当前本机 `0.144.4` 已通过；输出必须
  是单行严格 UTF-8 三段版本且最多 `4 KiB`。不兼容、非文本、超限、启动失败、退出错误或超时会以
  路径无关的稳定代码 fail-closed，版本通过后仍需完成 App Server `initialize`。认证 Bridge 在
  runtime start 完成前不会可用。详见 `M4_CODEX_VERSION_PREFLIGHT_20260723.md`。

## 已验证

```text
dotnet build src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj --configuration Release
Result: 0 warnings, 0 errors

dotnet run --project tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj --configuration Release --no-build
Result: 27/27 specs passed

scripts\verify-autocad2016-agent-bootstrap.ps1 -Configuration Release
Result: isolated net45/net8 builds 0 warnings / 0 errors; net45 37/37; net8 37/37;
bit-for-bit runnable output match; relevant processes 0 -> 0.

scripts\verify-phase2.ps1 -Configuration Release
Result: Release 0 warnings / 0 errors; dynamic specs 342/342 in PowerShell 5.1 and 7; Host disabled-API and basic
sensitive-information scans passed; local AgentHost doctor handshake passed.

tests\Codex.AutoCAD.AgentHost.Live.Specs
Result: real Codex v2 2/2; concurrent Bridge/AgentHost STOP removed the current session workspace;
managed session directories 2 -> 2; AgentHost residual processes 0.
```

专用 Job 限制证据见
`evidence/m4-agenthost-job-resource-limits-20260723.json`，SHA-256 为
`A6E22226423B2339EFE46034500D491E46829B651BDA9885B6F55194498AD8DD`。这些离线检查
不证明真实 Codex 配额耗尽、AutoCAD 异常退出或 AutoCAD 实机行为。

CPU/运行时间限制证据见
`evidence/m4-agenthost-cpu-runtime-limits-20260723.json`，SHA-256 为
`B6F8546CC9410D172E501BAF217B1C7B7FF0D52195E14AEC9322FB1709788207`。它证明 synthetic
CPU user-time 与墙钟终止，不证明真实 Codex CPU 节流性能、内存/进程槽耗尽或 AutoCAD 行为。

AgentHost 异常退出 retained-Job 清理证据见
`evidence/m4-agenthost-unexpected-exit-cleanup-20260723.json`，对应说明为
`M4_AGENTHOST_UNEXPECTED_EXIT_CLEANUP_20260723.md`。它证明 synthetic 根 AgentHost 退出后
普通后代不会因为启动器仍持有 Job 而残留；不证明 AutoCAD 异常退出或真实 Codex 故障矩阵。

私有存储与保留证据见
`evidence/m4-agenthost-private-storage-retention-20260723.json`，对应说明为
`M4_PRIVATE_STORAGE_RETENTION_20260723.md`。它不证明磁盘硬配额、每会话凭据或 AutoCAD
异常退出矩阵。

## 明确未完成

- Codex 路径、工作目录、启动/关闭超时和当前 `0.144.x` 兼容版本硬门槛已在
  `M4_LOCAL_CODEX_CONFIGURATION_20260723.md` 与 `M4_CODEX_VERSION_PREFLIGHT_20260723.md` 完成；
  将来版本升级仍需显式协议复核和新证据。
- 每会话独立 `CODEX_HOME`，以及不复制/泄露用户凭据的登录和恢复方案。
- 插件配置隔离和独立凭据；默认空 MCP 已完成，但默认用户 Codex home 仍可被访问。
- Windows Job Object 的进程数、Job 总提交内存、CPU hard cap 和累计 user-time 已应用，service
  session 墙钟截止也已实现；工作目录磁盘硬配额仍未实现，CPU 节流性能及内存/进程数的真实
  Codex 耗尽行为也未验证。
- 受限令牌或 AppContainer。
- 已处于企业 Job/受限桌面环境时的嵌套 Job 兼容性与用户可理解的诊断。
- 工作目录与审计目录的最小 ACL/有界保留清理已完成；synthetic AgentHost 异常退出的 Job 回收已
  覆盖。可靠磁盘硬配额、真实 Codex/AutoCAD 故障注入和完整僵尸进程矩阵仍未完成。
- 审批解决、CAD 写入提案/执行终态和未来日志导出尚未进入当前审计。当前 CAD 写入仍禁用，
  不得把只读 `approval_requested` 记录解释为 CAD 审批审计闭环。
- 当前 SHA-256 链不是签名、HMAC、远端锚定或 WORM 存储；能够替换整个文件并重算链的主体仍可
  生成自洽结果，因此外部不可篡改审计仍未完成。详见 `M4_AUDIT_HASH_CHAIN_20260723.md`。
- 对 AgentRuntime、Bridge、Host 与导出日志的统一错误/配置脱敏。

## 下一顺序

1. 配置、版本/App Server 健康预检和 Codex 子进程显式环境白名单已完成，当前保留默认用户文件
   登录行为。
2. 独立 `CODEX_HOME` 需先迁移到 OS keyring 或受信 token；不复制用户 profile 中的配置或
   `auth.json` 作为临时方案，并补插件配置隔离。
3. 已完成 Job Object 进程数/总提交内存/CPU/user-time、session 墙钟限制、synthetic AgentHost
   异常退出回收和 workspace/audit 私有 ACL/有界保留；继续补可靠磁盘硬配额，并以真实 Codex、
   AutoCAD 异常退出和僵尸进程矩阵验证。
4. 将当前只读审计扩展到审批解决和 M5 强类型 CAD 写入终态，并完成受保护审计锚点、凭据/环境边界和
   实机矩阵后，才允许开始 M5 CAD 写入。

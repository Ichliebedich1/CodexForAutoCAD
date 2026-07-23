# M4：进程隔离与诊断基线

最后更新：2026-07-23（北京时间）

## 状态

本文件记录 M4 已完成的四个小切口：Codex 子进程 stderr/AgentHost 诊断脱敏、本机 Codex
启动配置、AgentHost 进程树的 Job Object 边界，以及该 Job 的进程数/总提交内存硬限制；
不是完整沙箱候选，不代表已完成每会话 `CODEX_HOME`、环境隔离、CPU/运行时/磁盘配额或
凭据隔离。
本轮没有启动、关闭或控制 AutoCAD，也没有加载 DLL、保存或修改图纸。

当前代码调用链仍为：

```text
AutoCAD Host.2016
  -> authenticated AgentHost bootstrap
     (unnamed Windows Job Object; kill-on-close + process-count/job-memory limits)
  -> AgentHost Program
  -> CodexProcessTransport
  -> codex app-server --stdio (inherits AgentHost Job membership)
```

现有受限 bootstrap、批准的 AgentHost EXE 哈希、受限继承句柄和有界直接子进程终止继续保留。
启动器在恢复 AgentHost 前创建未命名 Job Object、设置 `KILL_ON_JOB_CLOSE`、`ACTIVE_PROCESS`
和 `JOB_MEMORY` 并完成进程分配；该 Job handle 随受认证的 service session 保存。因此正常停止
或拥有它的 Host 进程结束都会关闭该边界。普通后代会继承 Job membership；真实 Codex 的
配额耗尽和异常退出矩阵仍需另做验证。

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
- AgentLauncher net45/net8 规格各 `30/30`：隔离的 `bootstrap-serve` 假 AgentHost 会启动一个
  已知挂起的后代；`StopAsync` 返回后，以及拥有 Job 的启动器不调用停止逻辑而直接退出后，
  父/后代 PID 都必须消失。规格还验证默认/自定义限额、Windows 读回值和非法配置。专用
  引导门禁确认相关进程基线/终态均为 `0`，没有启动或操作 AutoCAD。

## 已验证

```text
dotnet build src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj --configuration Release
Result: 0 warnings, 0 errors

dotnet run --project tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj --configuration Release --no-build
Result: 15/15 specs passed

scripts\verify-autocad2016-agent-bootstrap.ps1 -Configuration Release
Result: isolated net45/net8 builds 0 warnings / 0 errors; net45 30/30; net8 30/30;
bit-for-bit runnable output match; relevant processes 0 -> 0.

scripts\verify-phase2.ps1 -Configuration Release
Result: Release 0 warnings / 0 errors; dynamic specs 319/319; Host disabled-API and basic
sensitive-information scans passed; local AgentHost doctor handshake passed.
```

专用 Job 限制证据见
`evidence/m4-agenthost-job-resource-limits-20260723.json`，SHA-256 为
`A6E22226423B2339EFE46034500D491E46829B651BDA9885B6F55194498AD8DD`。这些离线检查
不证明真实 Codex 配额耗尽、AutoCAD 异常退出或 AutoCAD 实机行为。

## 明确未完成

- Codex 路径、工作目录和启动/关闭超时的正式配置已在
  `M4_LOCAL_CODEX_CONFIGURATION_20260723.md` 完成；Codex 兼容版本硬门槛仍未冻结。
- 每会话独立 `CODEX_HOME`，以及不复制/泄露用户凭据的登录和恢复方案。
- 空 MCP/插件配置、子进程环境白名单和最小继承策略。
- Windows Job Object 的进程数和 Job 总提交内存限制已应用；CPU、运行时和工作目录磁盘配额
  仍未实现，内存/进程数的真实 Codex 耗尽行为也未验证。
- 受限令牌或 AppContainer。
- 已处于企业 Job/受限桌面环境时的嵌套 Job 兼容性与用户可理解的诊断。
- 工作目录 ACL、清理策略、结构化运行审计和故障注入/僵尸进程实测。
- 对 AgentRuntime、Bridge、Host 与导出日志的统一错误/配置脱敏。

## 下一顺序

1. 配置模型与只允许本地绝对 Codex 可执行文件的预检已完成，保留当前默认登录行为。
2. 将 Codex 子进程的环境构建为显式白名单；独立 `CODEX_HOME` 需先确定安全登录和凭据边界，
   不复制用户 profile 中的配置文件作为临时方案。
3. 已完成 Job Object 进程数/总提交内存限制；继续补 CPU、运行时和工作目录磁盘配额，并以
   真实 Codex、异常退出和僵尸进程矩阵验证。
4. 完成结构化审计、ACL、凭据/环境边界和实机矩阵后，才允许开始 M5 CAD 写入。

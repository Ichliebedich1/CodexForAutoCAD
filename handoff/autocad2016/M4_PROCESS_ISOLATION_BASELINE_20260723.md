# M4：进程隔离与诊断基线

最后更新：2026-07-23（北京时间）

## 状态

本文件记录 M4 的第一个小切口。它只收紧 Codex 子进程 stderr 和 AgentHost 诊断输出；不是
完整沙箱候选，不代表已完成每会话 `CODEX_HOME`、环境隔离、Job Object、资源配额或凭据隔离。
本轮没有启动、关闭或控制 AutoCAD，也没有加载 DLL、保存或修改图纸。

当前代码调用链仍为：

```text
AutoCAD Host.2016
  -> authenticated AgentHost bootstrap
  -> AgentHost Program
  -> CodexProcessTransport
  -> codex app-server --stdio
```

现有受限 bootstrap、批准的 AgentHost EXE 哈希、受限继承句柄和有界直接子进程终止继续保留。
它们不等价于对 AgentHost 所有后代进程的 Job Object 管理。

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

## 已验证

```text
dotnet build src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj --configuration Release
Result: 0 warnings, 0 errors

dotnet run --project tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj --configuration Release --no-build
Result: 10/10 specs passed

scripts\verify-phase2.ps1 -Configuration Release
Result: Release 0 warnings / 0 errors; dynamic specs 313/313; Host disabled-API and basic
sensitive-information scans passed; local AgentHost doctor handshake passed.
```

这些离线检查不证明真实 Codex 认证回合、AgentHost 后代进程清理或 AutoCAD 实机行为。

## 明确未完成

- Codex 路径、兼容版本、超时和工作目录的正式配置与迁移。
- 每会话独立 `CODEX_HOME`，以及不复制/泄露用户凭据的登录和恢复方案。
- 空 MCP/插件配置、子进程环境白名单和最小继承策略。
- Windows Job Object、CPU/内存/进程数/运行时配额、受限令牌或 AppContainer。
- 工作目录 ACL、清理策略、结构化运行审计和故障注入/僵尸进程实测。
- 对 AgentRuntime、Bridge、Host 与导出日志的统一错误/配置脱敏。

## 下一顺序

1. 先建立正式的 M4 配置模型与只允许本地绝对 Codex 可执行文件的预检，保留当前默认行为。
2. 将 Codex 子进程的环境构建为显式白名单；独立 `CODEX_HOME` 需先确定安全登录和凭据边界，
   不复制用户 profile 中的配置文件作为临时方案。
3. 加入 Job Object 和资源限制，再以故障注入验证整棵进程树清理。
4. 完成结构化审计、ACL 和实机矩阵后，才允许开始 M5 CAD 写入。

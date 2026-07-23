# M4 AgentHost 异常退出进程树清理

最后更新：2026-07-23（北京时间）

## 目标

补足一个已知 Job Object 生命周期缺口：`KILL_ON_JOB_CLOSE` 的 Job 句柄由启动器的
`AgentHostServiceSession` 持有。此前 `STOP` 和启动器自身退出都会关闭该句柄，但若已认证的
AgentHost 自行退出、启动器仍存活，普通后代可能继续属于一个仍被持有的 Job。

## 实现

- 只有真实 `WindowsInheritedBootstrapProcess` 构造的 service session 会启动根进程退出监视器；
  注入委托的纯内存规格构造器不受影响。
- 监视器在后台等待已认证 AgentHost 的进程句柄。根进程退出，或进程等待本身发生不可恢复错误时，
  都会走既有有界 `StopAsync` 收口路径。
- 收口先完成 AgentHost/通道清理，随后释放由 session 持有的 Job。Windows 的
  `KILL_ON_JOB_CLOSE` 因而终止同一 Job 内仍存活的普通后代。
- 自动收口最多尝试两次，间隔 `100 ms`；两次都不能证明清理完成时写入
  `AgentBootstrapLateFailureRegistry`，后续启动 fail-closed。显式 `STOP` 已开始时监视器不重复接管。
- 该路径不阻塞 AutoCAD UI 线程，也没有新增 CAD API、CAD 写入或保存调用。

## 自动化证据

`AGENTHOST_UNEXPECTED_EXIT_KILLS_PROCESS_TREE` 使用仅限测试的 FakeAgentHost：它完成真实
认证 bootstrap、启动一个已知挂起的受监管后代，随后等待测试信号并自行以非零码退出。测试在
确认 service session 已建立后才发送该信号，并且在验证后代 PID 已消失之前绝不调用 `STOP`。

2026-07-23 的隔离门禁结果：

```text
AgentHost Release: 0 warnings / 0 errors
AgentLauncher Specs: net45 37/37; net8 37/37
Required new spec: AGENTHOST_UNEXPECTED_EXIT_KILLS_PROCESS_TREE = passed
Isolated builds: 2; bit-for-bit runnable output match = true
Relevant AgentHost/FakeAgentHost process baseline/final: 0 -> 0
AutoCAD started/controlled: false
```

脱敏摘要见
`evidence/m4-agenthost-unexpected-exit-cleanup-20260723.json`。完整门禁脚本为
`scripts/verify-autocad2016-agent-bootstrap.ps1`，其生成证据 schema 已升至 `/8` 并单独输出
`ProcessTreeCleanupOnUnexpectedAgentHostExitLiveVerified=true`。

## 边界

- 这证明的是 Windows 上 synthetic AgentHost 根进程退出时的 retained-Job 回收，不是 AutoCAD
  崩溃实机矩阵。
- 它不证明真实 Codex app-server 在所有故障模式下的后代行为，也不证明嵌套 Job、受限桌面、
  AppContainer、EDR 或企业策略兼容性。
- 它不替代每会话 `CODEX_HOME`/凭据/插件配置隔离、磁盘硬配额、受保护审计锚点或 CAD 写入审计。
- CAD 写入和插件保存继续禁用。

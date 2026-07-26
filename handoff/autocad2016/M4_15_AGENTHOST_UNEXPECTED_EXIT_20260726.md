# M4.15.3a AgentHost 意外退出的唯一结构化终态

最后验证：2026-07-26（北京时间）

## 本轮结论

正式 Launcher 原本能够在 AgentHost 自行退出后关闭所持有的 Job handle，并用
`KILL_ON_JOB_CLOSE` 回收仍存活的后代进程；但 Host 只能等待随后出现的泛化 Bridge 断线，
无法稳定区分“AgentHost 已意外退出”和普通连接丢失。本轮把该退出事实接入正式运行时：

- `AgentHostServiceSession` 新增独立的 `ProcessExitFailureTask`，系统会话、资源限制和进程
  退出终态不再混为同一状态。
- 正常 STOP 发布 `None`；进程数、内存、累计用户时间、会话墙钟等资源终态优先并使退出终态
  保持 `None`；只有无资源终态的根 AgentHost 自行退出才发布 `UnexpectedExit`。
- 稳定错误码为 `agenthost_unexpected_exit`，`error_stage=agenthost_runtime`，不可自动重试；
  用户可以显式停止并重新启动 AgentHost。
- Host 同时监听资源限制和进程退出，并在 Bridge fault 的同一有界归因窗口内先检查资源终态、
  再检查进程退出，最后才回退为泛化 Bridge 断线。
- 当前活动请求只进入一次 `failed`；`request_id` 保持 Host 所有，后续 ASK fail-closed，迟到
  Bridge fault 不能覆盖已经提交的 AgentHost 退出终态。
- 公开 UI/命令行不包含 stderr、路径、原始 Bridge 诊断或 inner exception graph。
- 退出监视器自身失败会成为结构化运行时失败并触发既有两次有界清理，不再无限等待资源
  终态。

这完成的是 M4.15.3 的自动化准备纵切，不等于真实 AutoCAD/Codex 强杀矩阵已经通过。

## 正式调用链

```text
AgentHostServiceSession
  -> Job resource monitor
     -> resource terminal: ProcessExit=None + bounded cleanup
     -> RootProcessExited: ResourceLimit=None
  -> root process exit watcher
     -> wait for authoritative resource terminal
     -> no resource failure: ProcessExit=UnexpectedExit
     -> resource failure/fault: ProcessExit=None
     -> existing bounded process-tree cleanup

MvpAgentClient
  -> monitor ResourceLimitFailureTask
  -> monitor ProcessExitFailureTask
  -> Bridge fault attribution window
     -> resource failure wins
     -> otherwise unexpected process exit wins
     -> otherwise generic Bridge disconnect
  -> one TransitionOfflineForAgentHostFailure state transition
```

不存在 Provider、UI 或 AutoCAD 插件直接读取 Win32 退出细节的旁路。

## RED → GREEN 证据

- Launcher RED 强化既有 `AGENTHOST_UNEXPECTED_EXIT_KILLS_PROCESS_TREE`：真实 FakeAgentHost
  在建立服务会话后自行退出，后代必须由 retained Job handle 回收，同时
  `ProcessExitFailureTask=UnexpectedExit`；随后 STOP 不得把终态改回 `None`。
- Host RED 新增 `HOST2016_AGENTHOST_EXIT_WINS_BRIDGE_FAULT_RACE`：先注入带敏感标记的 Bridge
  connection-lost，再发布进程退出；最终必须只有一个 `agenthost_unexpected_exit`，活动请求
  为 `failed`，后续 ASK 保持拒绝且敏感标记不可见。
- GREEN 后两个规格均通过，既有资源限制竞态规格继续通过，证明资源终态没有被新的退出终态
  降级。

## 最终自动化结果

- AgentLauncher bootstrap net8：`65/65`，包含连续 `500` 次启停回收。
- AgentLauncher bootstrap net45：`65/65`，包含连续 `500` 次启停回收。
- Host.2016 MVP：`60/60`。
- PowerShell 7 Phase 2：`417/417`。
- Windows PowerShell 5.1 Phase 2：`417/417`。
- Release：`0 warning / 0 error`。
- Host 禁用 API、敏感信息扫描和 AgentHost doctor：通过。
- R20.1/.NET Framework 4.5/x64 Host A/B 五文件输出逐字节一致。
- 当前 Host SHA-256：
  `DA5C6D100E4B8CEDCEEB1C4389E09A77667F6879C05A64EF4EC1A0EF43275255`。
- R20.1 产物中的 Autodesk DLL 复制数：`0`。
- R20.1 验证产物：
  `artifacts/m4-15-agenthost-exit-r201-host-a6b8b8a95bea469aa5bcc170854ada55/`。
- 条件 net45 还原造成的临时 `packages.lock.json` 变化已恢复，最终无实际差异。
- AgentHost、FakeAgentHost、Bridge Client TestServer 和保留恢复工作器残留：`0`。

旧的 `verify-autocad2016-host.ps1` 仍绑定更早的 Host 项目哈希，本轮按设计拒绝执行；没有放宽
该冻结门禁。R20.1 A/B 使用与上一 M4 检查点相同的“条件 net45 恢复、同一依赖闭包、禁用
ProjectReference 重建、两个独立输出目录”口径。该结果证明当前源码可构建且 A/B 一致，不等于
M9 的完整独立依赖闭包可重复构建已经完成。

## 仍需真实机器验证

1. 真实 Codex app-server 正常退出、崩溃和强制终止。
2. 真实 AgentHost 在空闲、流式回答、取消、STOP 和启动握手阶段被终止。
3. AutoCAD 正常退出、任务管理器终止和启动中断时 AgentHost/Codex 残留为 0。
4. 每种故障只显示一个终态，并记录稳定错误码、请求身份和脱敏审计证据。
5. 资源限制与强杀同时发生时资源终态仍优先。
6. 真实企业父 Job、AppLocker/WDAC、EDR、受限账户和系统事件证据。

本轮没有启动或控制 AutoCAD，没有启用 CAD 写入、保存、命令、LISP、Shell、文件或网络 Agent
工具，也没有提交、合并、cherry-pick、push、reset 或清理 Git 工作树。M4.15、M4 和 M4.16
仍未完成，M5 CAD 写入继续硬禁用。

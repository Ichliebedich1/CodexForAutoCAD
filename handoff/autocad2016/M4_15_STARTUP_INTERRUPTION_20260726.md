# M4.15.3b AgentHost 启动中断主动取消

最后验证：2026-07-26（北京时间）

## 本轮结论

Host 原先在收到 `CODEX16AGENTSTOP` 或 AutoCAD 退出清理请求时，会等待正在进行的
AgentHost 启动任务自行结束，但不会主动取消它。若启动停在 bootstrap、Bridge 握手、能力协商
或 thread 创建阶段，STOP 可能一直等待到原启动超时。

本轮把启动令牌纳入 Host 生命周期：

- 首次 `StartAsync` 创建调用方令牌与 Host 生命周期相连的 `CancellationTokenSource`。
- `StopCoreAsync` 在后台清理线程先取消该令牌，再等待启动任务收口，然后复用既有
  `MvpAgentStopCoordinator` 清理已经越过 Host 边界的 Bridge 与 AgentHost 资源。
- STOP 触发的预期 `OperationCanceledException` 不再误显示为“启动 AgentHost 失败”；最终只发布
  一次“AgentHost 已停止”。
- 启动任务不能在 STOP 后切换为 online；重复 STOP 保持幂等。
- 调用方自身取消仍按启动失败路径报告，并允许显式重试；没有把所有启动取消都伪装成 STOP。
- 取消回调自身异常不能跳过后续进程和 Bridge 清理。

这完成的是 M4.15.3 的自动化准备，不等于真实 AutoCAD/Codex 启动中断已经通过。

## 正式调用链

```text
MvpAgentClient.StartAsync
  -> linked startup CancellationTokenSource
  -> AgentHostBootstrapService.StartAsync
  -> Bridge Start / capability negotiation / thread start

MvpAgentClient.StopAsync / AutoCAD termination cleanup
  -> background StopCoreAsync
  -> cancel linked startup token
  -> await startup cleanup
  -> stop/dispose Bridge and stop AgentHost
  -> one stopped terminal
```

Provider、Palette 和 AutoCAD API 均不直接管理该令牌或 AgentHost 进程。

## 自动化证据

- 新增 `HOST2016_STOP_CANCELS_INFLIGHT_START`：受控启动检查点保持未完成，STOP 必须在
  `2 s` 门限内取消启动；启动任务进入 cancelled，Host 不上线，错误回调为 `0`，停止终态为
  `1`，重复 STOP 不新增终态。
- Host.2016 MVP：`61/61`。
- PowerShell 7 Phase 2：`418/418`。
- Windows PowerShell 5.1 Phase 2：`418/418`。
- Release：`0 warning / 0 error`；禁用 API、秘密扫描和 AgentHost doctor 通过。
- AgentLauncher net8/net45 仍为 `65/65`，各包含连续 `500` 次启停。
- R20.1/.NET Framework 4.5/x64 Host A/B 五文件逐字节一致。
- Host SHA-256：
  `9827DC321B7D458594B007085C78C54505CBE09CEF1BDEFB616D2ABFDFCFB5E8`。
- R20.1 产物：
  `artifacts/m4-15-startup-interrupt-r201-host-bb2d13bb18594442980a31064c61e650/`。
- Autodesk DLL 复制数：`0`。

条件 net45 还原只用于 R20.1 编译，跟踪锁文件已恢复为无差异。没有启动或控制 AutoCAD，
没有启用 CAD 写入、保存、命令、LISP、Shell、文件或网络 Agent 工具。

## 仍需真实机器验证

1. `CODEX16AGENTSTART` 的 bootstrap、Bridge 握手、能力协商和 thread 创建阶段分别执行 STOP。
2. AutoCAD 在上述每个阶段正常退出，退出不超过既定门限且 AgentHost/Codex 残留为 `0`。
3. 任务管理器终止 AutoCAD 或 AgentHost 时只出现一个结构化终态。
4. 启动中断与资源限制、Bridge 断线同时发生时，终态优先级保持确定。
5. 真实 Codex stderr、路径、环境和异常图不出现在 Palette、命令行或审计导出。

M4.15.3、M4.15、M4 和 M4.16 仍未完成，M5 CAD 写入继续硬禁用。

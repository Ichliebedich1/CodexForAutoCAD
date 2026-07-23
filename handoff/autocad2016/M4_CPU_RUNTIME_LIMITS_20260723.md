# M4：AgentHost CPU 与运行时间限制

最后更新：2026-07-23（北京时间）

## 结论

AgentHost/Codex 进程树现在同时受 Windows Job Object CPU hard cap 和累计 Job user-time 限制，
认证后的 AgentHost service session 另有独立墙钟截止。三个概念不能互换：

- CPU hard cap 限制整个 Job 可使用的处理器周期比例。
- Job user-time 累加 Job 内全部进程的用户态 CPU 时间；达到上限时 Windows 终止整个 Job。
- session runtime 从 AgentHost 完成认证后按经过时间计时；到期后执行现有有界 Stop 清理。

本切片没有启动、关闭或控制 AutoCAD，没有加载 DLL、修改或保存 DWG。CAD 写入与插件保存继续
禁用。

## 默认值与边界

| 限制 | 默认值 | 接受范围 | 作用域 |
| --- | ---: | ---: | --- |
| 最大进程数 | 16 | 2..64 | Job 进程树 |
| Job 总提交内存 | 4 GiB | 512 MiB..16 GiB | Job 进程树 |
| CPU hard cap | 75% | 1..100% | Job 进程树 |
| 累计 Job user-time | 8 h | 100 ms..7 d | Job 进程树 |
| session 墙钟时间 | 24 h | 1 s..7 d | 认证后的 service session |

非法配置在 AgentHost 进程创建前以 `InvalidConfiguration` fail-closed。当前 Host 使用安全默认值，
尚无面向用户的设置页；正式配置 UI 属于后续 M8。

## 实现

- `JOBOBJECT_EXTENDED_LIMIT_INFORMATION` 同时设置 `KILL_ON_JOB_CLOSE`、`ACTIVE_PROCESS`、
  `JOB_MEMORY` 和 `JOB_TIME`。
- `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION` 设置 `ENABLE | HARD_CAP`，`CpuRate` 使用百分比乘
  `100` 的 Windows 单位。
- Job 创建后分别通过 information class `9` 和 `15` 读回限制，验证 Windows 接受的标志和值。
- service session 的墙钟 timer 只在认证成功后启动；正常 Stop 会取消 timer。
- 墙钟到期使用既有 `StopCore`：终止并等待进程、收口 stderr、取消 I/O、释放进程包装。
- 首次清理失败后等待 `100 ms` 再重试一次；第二次仍失败会写入 late-failure registry，使后续
  AgentHost 启动 fail-closed。`RuntimeExpired` 供上层区分墙钟到期。

## 自动化证据

```text
Direct net8 AgentLauncher Specs: 35/35
Windows PowerShell 5.1 isolated gate:
  net45 35/35
  net8 35/35
  build-a/build-b runnable output byte-identical
  relevant processes 0 -> 0
Windows PowerShell 5.1 Phase 2: 329/329, 0 warnings, 0 errors
PowerShell 7 Phase 2: 329/329, 0 warnings, 0 errors
AgentHost doctor: passed, Codex stderr bytes 0
```

新增规格证明：

- Windows 读回进程数、内存、Job user-time、CPU enable/hard-cap 和精确 CpuRate。
- CPU-busy synthetic AgentHost 在 `1` 秒累计 Job user-time 耗尽后被 OS 终止。
- 挂起 service 在 `1` 秒墙钟截止后被终止。
- 显式 Stop 在截止前完成后，已撤销 timer 不会反转 `RuntimeExpired` 状态。
- 墙钟清理第一次终止失败时自动重试并成功。
- 墙钟清理连续两次失败后，当前测试进程中的后续 AgentHost 启动 fail-closed。
- net8 apphost、`dotnet <dll>` 和 net45 EXE 三种测试 helper 启动形式均可工作。

脱敏 evidence：
`evidence/m4-agenthost-cpu-runtime-limits-20260723.json`

evidence SHA-256：
`B6F8546CC9410D172E501BAF217B1C7B7FF0D52195E14AEC9322FB1709788207`

## 未完成边界

- 没有测量 `75%` CPU hard cap 的实际吞吐或调度公平性；只证明 Windows 接受精确配置。
- 没有故意耗尽真实 Codex 的进程槽或总提交内存。
- synthetic user-time 终止不等价于真实 Codex 长时间压力测试。
- 没有可靠的工作目录磁盘硬配额；不得用轮询目录大小冒充硬配额。
- 每会话 `CODEX_HOME`、独立凭据、空 MCP/插件配置、受限令牌/AppContainer 仍未完成。
- 工作区/审计目录最小 ACL 与有界保留已由后续
  `M4_PRIVATE_STORAGE_RETENTION_20260723.md` 完成；AutoCAD 异常退出和僵尸进程矩阵仍未完成。

因此本切片关闭了 M4 的 CPU 与运行时间待办，但不代表 M4 或产品已经完成，也不能据此启用 CAD
写入。

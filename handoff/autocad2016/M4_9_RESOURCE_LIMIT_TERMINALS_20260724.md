# M4.9 资源限制与结构化终态

日期：2026-07-24

状态：代码与自动化切口完成；真实 Codex/AutoCAD 和企业策略矩阵未完成。

## 已进入真实调用链

- AgentHost、Codex 及普通后代位于同一 Windows Job Object。
- Job 限制最大进程数、Job 总提交内存、CPU hard cap 和累计用户时间。
- watchdog 限制认证服务墙钟时间。
- `GracefulStopTimeout` 在启动前校验为 `0–30 s`，默认 `1 s`。
- 显式 STOP 将资源终态固定为 `None`，不会被迟到定时器反转。

## 权威终态来源

Windows Job completion port 提供以下通知：

- `JOB_OBJECT_MSG_ACTIVE_PROCESS_LIMIT`
- `JOB_OBJECT_MSG_JOB_MEMORY_LIMIT`
- `JOB_OBJECT_MSG_END_OF_JOB_TIME`
- 根 AgentHost 正常或异常退出

根进程退出清理先等待 completion port 完成归因，再关闭 Job。不得根据退出码、峰值内存、
stderr 或进程是否仍存在猜测配额原因。

服务墙钟由 Launcher watchdog 提交。第一个权威资源终态胜出；组合耗尽不伪造固定优先级。

## Host 行为

稳定错误码：

- `agenthost_process_limit_exceeded`
- `agenthost_memory_limit_exceeded`
- `agenthost_user_time_limit_exceeded`
- `agenthost_session_runtime_limit_exceeded`

共同字段：

- `error_stage=agenthost_runtime`
- `retryable=false`
- 活动 request 只进入一次 `failed`
- 后续 ASK fail-closed
- 路径、环境变量、stderr 和测试秘密不进入 UI

Bridge 普通断线先进入最长一秒的有界归因窗口。资源通知在窗口内到达时，资源终态优先；
没有资源原因时沿用普通 Bridge 断线错误。

## Working-set 决策

当前产品不启用 `JOB_OBJECT_LIMIT_WORKINGSET`：

- working set 是驻留物理页，不是提交内存，受系统修剪和全机内存压力影响；
- 将其作为硬终止条件会造成 Windows 版本和负载相关抖动；
- 它不能替代 Job 总提交内存对整棵进程树的硬边界。

产品硬内存边界继续使用 Job 总提交内存。working set 保留为只读性能 telemetry 和发布预算，
沿用 M2 的外部采样/evidence 路径。企业若确实要求驻留集上限，只能作为未来可选策略，并先
完成 Windows 版本、组策略、杀毒和真实 Codex/AutoCAD 兼容矩阵。

## 自动化

- Launcher net45/net8 精确规格：`57/57`
- Host MVP：`56/56`
- Phase 2：`360/360`
- 组合耗尽：原生提交内存与 CPU 同时消耗，接受先到的 Job 内存或 Job 用户时间权威通知；
  禁止 `None`、墙钟兜底和状态回退
- 连续 service 启停：`500`
- AgentHost/FakeAgentHost 最终残留：`0`

正式阶段证据由 `scripts/verify-autocad2016-agent-bootstrap-stage.ps1` 生成。不要在本文件反向
固化 evidence SHA-256，以免形成候选 manifest 自引用。

## 尚未完成

- 真实 Codex 的内存、CPU、进程和长时间会话耗尽矩阵
- AutoCAD 2016 正常退出、异常退出、断线和资源耗尽实机矩阵
- 企业 Windows/宿主 Job/组策略/杀毒组合矩阵
- 管理员资源策略分层与锁定
- M4.10 磁盘硬配额

这些项目完成前，M4.9 不能标记为最终完成，M5 CAD 写入继续禁用。

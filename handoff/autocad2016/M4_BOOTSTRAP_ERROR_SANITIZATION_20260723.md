# M4：AgentHost Bootstrap 异常诊断脱敏

最后更新：2026-07-23（北京时间）

## 结论

`AgentBootstrapLaunchException` 过去会保留调用方传入的诊断文字和内部异常；这些值可能含有
AgentHost stderr、本地路径、凭据或 .NET 异常细节。现在异常公开面只能来自
`AgentBootstrapLaunchFailurePolicy` 的有限映射表：

- 每个已知失败类别均有固定 `error_code` 和固定安全说明；
- 未知枚举值统一归一化为 `InternalError` / `agenthost_internal_error`；
- 传入的原始诊断和内部异常不进入 `Message`、`InnerException` 或 `ToString()`；
- Host.2016 将 `InternalError` 显式映射为结构化、不可重试的 `internal_error`。

这样 Bootstrap 失败不会再凭借异常对象本身绕过已完成的 Bridge、运行时、Host UI 与本地配置
错误边界。它不增加 CAD 写入、Provider 抽象、Direct API 或第二套 Agent Loop。

## 已接入调用链

```text
AgentLauncher 失败点（启动 / 确认 / 超时 / 清理）
  -> AgentBootstrapLaunchFailurePolicy
  -> AgentBootstrapLaunchException（固定 code/message）
  -> MvpAgentFailureFormatter
  -> Host.2016 Palette / 命令结构化失败
```

原始诊断不会被公开异常对象保存；日后若需供受保护审计使用，必须另行设计受限、脱敏的记录面，
不能恢复将原始字符串塞入公开异常的做法。

## 自动化验证

- 本检查点时 Launcher Release 编译在 net8 与条件 net45 均为 `0` warning / `0` error；对应 Launcher
  规格分别为 `38/38`。后续受限 token probe 将当前组合规格扩展为 `41/41`，见
  `M4_RESTRICTED_TOKEN_BOOTSTRAP_PROBE_20260723.md`。
- Host.2016 MVP 规格为 `53/53`，覆盖未知 Bootstrap 失败必须映射为 `internal_error`，且路径形态
  `M4-SENTINEL` 不会进入用户失败文本。
- Launcher 规格为每个已知失败值及一个未知枚举值注入含路径形态的诊断和内部异常，验证公开
  `Message`、`InnerException` 和 `ToString()` 均不泄露；stderr 失败也只公开
  `agenthost_child_exited` 的固定说明。
- 额外双 Shell Bridge 阶段门通过：net45/net8 Bridge Client 各 `29/29`、Bridge `56/56`、
  Phase 2 `351/351`、Release `0` warning / `0` error、秘密扫描与 diff 检查通过；受控测试后
  AgentHost 残留为 `0`。

脱敏机器记录见：
`evidence/m4-bootstrap-error-sanitization-20260723.json`。

## 证据边界

该检查点未启动、重启或操作 AutoCAD；未执行 `NETLOAD` 或 CAD 命令。它没有证明真实
Host.2016 `NETLOAD`、从 AutoCAD 到长运行 AgentHost 的连接、真实 Codex CAD 对话或 CAD 写入。

它也没有完成磁盘硬配额、真实隔离登录/插件配置审查、受限令牌/AppContainer、受保护审计锚点
或真实 Codex/AutoCAD 异常退出矩阵。M4 仍未完成，M5 CAD 写入继续禁用。

## 后续工作

- 继续审计并设计受限令牌/AppContainer 的兼容路径，不能直接把当前启动链切到未知身份。
- 将安全日志导出和未来新增的异常出口逐项纳入固定代码/说明策略。
- 在部署提供真实硬配额能力后，再实现并验证 workspace 磁盘硬配额的 fail-closed 预检。

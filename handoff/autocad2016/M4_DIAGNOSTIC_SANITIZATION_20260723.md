# M4：Bridge、运行时与 Host 诊断脱敏边界

最后更新：2026-07-23（北京时间）

## 结论

本切口为所有已经发布的 Bridge 错误码建立了唯一的固定、安全说明。`AgentBridgeErrorSanitizer`
只接受闭合的 `AgentBridgeErrorCodes` 白名单；未知错误码统一归一化为 `internal_error`，显示文本
固定为“不公开敏感诊断”的安全说明。异常正文、Provider 输出、stderr、处理器自定义文本、路径、
工作目录和令牌均不再作为 Bridge、运行时或 Host UI 的失败文本传播。

`AgentBridgeFailure` 与失败型 `AgentBridgeEvent` 的契约验证现在同时要求：

1. 错误码属于固定白名单；
2. 错误说明与该错误码的固定安全说明完全一致。

因此错误对象不能再携带“看起来受限、但来自异常”的任意字符串跨越 IPC 边界。

## 已接入的调用链

```text
Bridge 请求处理 / 远端异常
  -> AgentBridgeErrorSanitizer
  -> AgentBridgeFailure 或失败 AgentBridgeEvent
  -> Bridge Client
  -> AgentHost 终态 / AgentRuntime 投影
  -> Host.2016 Palette 与命令失败说明
```

覆盖的现有路径包括：未注册请求处理器、请求取消、Bridge 处理器异常、远端错误、AgentHost 回合
终态、Codex 回合失败投影、动态 CAD 查询/提案校验拒绝，以及 Host 的失败格式化。CAD 写入仍禁用；
本切口不改变审批、事务或 Undo 逻辑。

## 自动化验证

- Contracts 规格验证已加入路径形态的 `M4-SENTINEL` 文本：未知码、任意异常说明及不匹配的固定
  说明都会被拒绝。
- Bridge 规格验证处理器抛出包含伪路径的异常后，客户端仅接收 `internal_error` 与固定安全说明；
  还覆盖了“未注册请求处理器”必须返回 `request_invalid` 的固定安全说明。
- Bridge Client net45/net8 规格均为 `29/29`；Bridge 为 `56/56`；完整 Phase 2 为 `350/350`；
  Release 为 `0` warning / `0` error。
- PowerShell 7.6.4 与 Windows PowerShell 5.1.19041.6456 分别完成两次隔离确定性构建；R20.1/
  net45/x64 Host.2016 连续两次构建输出的 DLL SHA-256 相同：
  `EA862CA0CEF2942EBBA9F97C68FD94F1D4A789A67FA2355BD88EB84D9B3D648A`。

完整脱敏机器证据：
`artifacts/m4-diagnostic-sanitization/bridge-client-stage-verification-20260723.json`。

## 证据边界

本阶段未启动、重启或操作 AutoCAD；没有执行 `NETLOAD`、发送 CAD 命令，也没有证明 Host.2016
与长运行 AgentHost 的真实连接或真实 Codex CAD 对话。它是 Bridge/managed core 的代码与自动化
检查点，不能替代 M1/M2/M3 的 AutoCAD 实机矩阵，也不代表 M4 已完成。

## 后续工作

- 将配置读取、日志导出和未来新增的错误出口逐项纳入同一固定代码/说明策略。
- 继续 M4 的磁盘硬配额、真实隔离登录与插件配置审查、受限令牌/AppContainer、受保护审计锚点
  及真实故障/僵尸进程矩阵。
- M4 完成前不得启用 M5 的 CAD 写入闭环。

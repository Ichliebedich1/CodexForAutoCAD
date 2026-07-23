# M4：Codex 版本预检与 App Server 健康门槛

最后更新：2026-07-23（北京时间）

## 结论

AgentHost 现在在启动本机 `codex app-server --stdio` 前，先以与 App Server 相同的受控工作目录和
子进程环境运行 `codex --version`。当前产品冻结支持范围为：

```text
>=0.144.4 <0.145.0
```

本机实测版本为 `codex-cli 0.144.4`。版本输出必须是单行、严格 UTF-8、三段数值版本；标准输出最多
`4 KiB`，不会写入控制台、审计或普通日志。预检通过后仍必须完成 App Server `initialize`，因此版本
匹配不是健康检查的替代品。

`codex app-server` 的协议是版本相关且仍带实验性质的集成面。新的 Codex 次版本会被有意拒绝，直到
重新审查协议、更新兼容范围、补测试并重新冻结证据；用户环境变量不能放宽该范围。

## 真实调用链

```text
AgentHost doctor / run / authenticated bootstrap-serve
  -> CodexLocalAppServerConfiguration
  -> CodexVersionPreflight (codex --version; same child allowlist)
  -> CodexProcessTransport (codex app-server --stdio)
  -> initialize health handshake
  -> only then expose the authenticated Bridge session as usable
```

在 `bootstrap-serve` 中，AgentHost 会在创建可接受 Bridge 请求的会话之前完成版本预检和
`CodexAgentRuntime.StartAsync()`。所以失败不会让 Palette 把 Agent 误报为在线。

## 稳定失败面

`doctor` 和 `run` 的版本预检失败返回路径无关 JSON：

```json
{"ok":false,"error":"codex_version_preflight","errorCode":"UnsupportedVersion"}
```

`errorCode` 只会是以下受控值之一：

- `ProcessStartFailed`
- `TimedOut`
- `ProcessExitedWithError`
- `VersionOutputTooLarge`
- `InvalidVersionOutput`
- `UnsupportedVersion`

审计会把它们进一步归类为稳定、无正文的 `codex_version_*` 代码。真实可执行文件路径、工作目录、
版本原文、stderr、完整环境和令牌均不进入该输出。

## 已验证

```text
AppServer Specs: 27/27
AgentHost Release build: 0 warnings / 0 errors
AgentHost doctor: current local Codex 0.144.4 passed version preflight and initialize
Real AgentHost/Codex v2 live: 2/2
AgentLauncher bootstrap gate: net45 36/36; net8 36/36; related processes 0 -> 0
Full Phase 2: Windows PowerShell 5.1 341/341; PowerShell 7 341/341
```

规格覆盖隔离环境、严格格式、兼容补丁接受、未审查次版本拒绝、非 UTF-8、超限输出和超时。真实
doctor 与两轮 live 均未启动或控制 AutoCAD；CAD 写入、插件保存和自动保存仍禁用。

脱敏机器可复现摘要见 `evidence/m4-codex-version-preflight-20260723.json`。

## 未完成边界

- 这不是每会话 `CODEX_HOME` 或独立凭据；当前仍是默认用户文件登录兼容模式。
- 没有实现插件配置隔离、磁盘硬配额、受限令牌/AppContainer 或完整 Codex 僵尸进程矩阵；
  默认空 MCP 已由 `M4_EMPTY_MCP_BOUNDARY_20260723.md` 的结构化配置覆盖完成。
- 版本范围只覆盖当前验证的 `0.144.x` 窗口，不承诺未来 Codex 版本兼容。
- 本切口不授权 CAD 写入，也不替代 M4 的受保护审计锚点/签名、M5 的审批/事务/Undo 闭环；本地
  SHA-256 链的已完成和边界见 `M4_AUDIT_HASH_CHAIN_20260723.md`。

## 升级规则

1. 先读取新版本对应的官方 App Server 协议/生成 schema，确认请求、事件和初始化字段没有未审查变化。
2. 在隔离环境补齐版本解析、协议契约、启动失败和真实本机 live 回归。
3. 明确更新 `CodexVersionCompatibility.Default`、本文件、脱敏 evidence 和支持范围。
4. 重新运行双 Shell Phase 2、bootstrap、doctor/live 门禁后，以独立提交冻结。

不得通过用户环境变量、PATH 顺序或字符串前缀匹配绕过上述步骤。

# M4：默认空 MCP 边界

最后更新：2026-07-23（北京时间）

## 结论

生产 AgentHost 创建本机 Codex App Server client 时，固定传入以下追加参数：

```text
-c
mcp_servers={}
```

因此实际启动形态为：

```text
codex app-server --stdio -c mcp_servers={}
```

`codex app-server --help` 将 `-c/--config` 定义为 TOML 配置覆盖。该固定覆盖把 Codex 默认用户
profile 中的 MCP server 表置为空，使其中配置的 MCP server 不会进入本项目的 AgentHost/Codex
调用链。参数由 `CodexLocalAppServerConfiguration.CreateClientOptions()` 生产，并由
`CodexProcessTransport` 使用参数列表启动；UI、AutoCAD Host 和 Bridge 均不解析 Codex 原始配置。

## 已验证

```text
AppServer Specs: 27/27
AgentHost Release build: 0 warnings / 0 errors
Real AgentHost v2 live: 2/2
Phase 2 Release gate: 341/341
```

规格断言生产 `AppServerClientOptions` 的额外参数严格等于 `-c|mcp_servers={}`。真实 live gate
完成能力协商与同一 Codex thread 的两轮 v2 上下文对话，随后正常清理该测试启动的 AgentHost。
完整 Phase 2 门禁同时通过 Host 禁用 API、基础敏感信息扫描和 AgentHost doctor 活体握手。上述
检查没有启动、关闭或控制 AutoCAD，没有加载 DLL、修改或保存 DWG。

## 明确未完成

- 默认用户 `CODEX_HOME` 仍可被 Codex 使用，当前文件登录兼容路径仍存在。
- 这不是每会话凭据、OS keyring、受信 token 或插件配置隔离。
- 这不会禁止默认 profile 中除 MCP server 表以外的配置、技能、日志、会话或其他用户状态。
- 它不替代工作目录硬配额、受限令牌/AppContainer、审计防篡改、故障注入或 AutoCAD 异常退出矩阵。

不得把此切口表述为“整个用户配置已隔离”“每会话凭据已隔离”或“完整 M4 沙箱已完成”。将来启用
每会话 `CODEX_HOME` 前，用户必须明确完成受支持的 OS 凭据登录或等价安全认证恢复；不得复制、链接、
解析或记录现有用户 profile、`auth.json`、token 或完整环境变量。

## 后续

1. 保持当前默认空 MCP 覆盖，不允许回退为隐式用户 MCP 配置。
2. 在不复制用户 profile 的前提下，设计并验证每会话 `CODEX_HOME` 与凭据恢复。
3. 单独审查插件配置和其余默认 profile 读取面，不能把空 MCP 误当成插件隔离。

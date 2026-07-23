# M4：子进程环境与凭据边界审计

最后更新：2026-07-23（北京时间）

## 结论

Codex 子进程现已进入显式环境白名单：生产 AgentHost 清空完整父环境，只注入固定的 Windows
运行时、用户 home、`AgentWorkspace.Temp`、最小系统 `PATH` 和非交互 Git 设置。synthetic child、
真实 app-server doctor 与两轮认证 Codex 对话均已通过。

这仍不是每会话凭据、插件或用户配置隔离。为兼容现有文件登录，白名单保留
`USERPROFILE`/`HOME`，Codex 继续使用默认用户 `~/.codex`；本项目没有复制、移动、链接、解析或
记录任何用户 Codex profile、令牌或配置文件。精确白名单与证据见
`M4_CODEX_CHILD_ENVIRONMENT_ALLOWLIST_20260723.md`。

## 已核对的调用面

```text
AutoCAD Host.2016
  -> AgentHost bootstrap-serve
  -> CodexLocalAppServerConfigurationResolver
  -> CodexChildEnvironmentPolicy
  -> CodexProcessTransport
  -> ProcessStartInfo.Environment.Clear()
  -> fixed allowlist
  -> codex app-server --stdio -c mcp_servers={}
```

- AgentHost 为每个认证 session 创建独立 `AgentWorkspace`；它用于 Agent 工作目录，不等于
  `CODEX_HOME`。
- `CodexLocalAppServerConfigurationResolver` 只读取 `CODEX_EXECUTABLE` 和父 `PATH` 用于启动前
  定位绝对 `codex.exe`；这些发现值不会整体进入子进程。它不读取、设置或记录 `CODEX_HOME`。
- 生产 `AppServerClientOptions` 强制 `InheritParentEnvironment=false`；通用 transport 的兼容
  默认值仍为 `true`。环境键值在启动前拒绝空名称、`=` 和 NUL，`null` 明确表示删除。
- 白名单不含 `CODEX_HOME`、access token、API key、代理、父 `PATH`、`PSModulePath` 或任意
  自定义/调试变量；`TEMP`/`TMP` 绑定每会话 workspace temp。
- app-server `initialize` 会返回 Codex home，但 AgentHost 正常 doctor 只公开“已配置”布尔值，
  不公开路径。
- 生产本机 Codex 配置固定传入 `-c mcp_servers={}`，覆盖默认用户 profile 的 MCP server 表；
  项目代码不复制、直接读取或记录 profile 内容；该覆盖也不形成空插件目录、独立 `CODEX_HOME`
  或独立凭据。
  默认用户 Codex home 仍可被 app-server 访问。

## 风险

- 父环境泄漏已由白名单关闭，但企业代理、额外证书或工具目录不会自动继承；未来如需支持，
  必须通过独立强类型配置加入，不能恢复完整父环境。
- 现有全局 Codex profile 可能同时承载登录状态和用户自定义配置。把 `CODEX_HOME` 直接改到空
  session 目录可能使会话失去登录能力；复制 profile 则可能复制令牌、MCP 或插件配置，均不可接受。
- 已有 Job Object 覆盖 AgentHost 及普通后代的关闭边界；隔离规格已验证显式停止及拥有 Job
  的启动器直接退出均会回收该树。但它不最小化环境、不能隔离凭据，也尚未替代真实 Codex
  异常退出与僵尸进程实测。

## 必须遵守的实现门槛

1. 不复制、硬链接、符号链接、导出或记录全局 Codex profile、API key、刷新令牌或完整环境变量。
2. 不以“先清空 `CODEX_HOME` 再看是否能启动”的方式修改生产 Agent 启动路径。
3. 在采纳每会话 home 前，先确认受支持的认证恢复方式；候选仅限用户明确授权的交互登录，或
   经过审计的 OS 安全凭据引用。不能猜测私有 Codex 文件布局或令牌格式。
4. 环境白名单已通过 synthetic child、doctor、真实 bootstrap-serve 两轮对话和停止清理后成为
   默认生产路径；后续不得无测试扩大名单。
5. 白名单中只允许经过逐项理由说明的操作系统运行时变量、明确的工作目录、批准的 `CODEX_HOME`
   和必要的受控配置；不得把整个父环境或 PATH 重新整体注入。

## 后续顺序

1. 当前采用“全局文件登录兼容 + 子进程父环境白名单 + 默认空 MCP”的只读过渡模式；下一步验证
   OS keyring 或受信 access token，确定每会话认证策略与插件配置边界。
2. child-environment builder、synthetic child 和真实 Codex 默认路径已完成；后续补企业代理、
   证书和无全局登录矩阵，不得回退为父环境继承。
3. 仅在安全认证路径被证实后，将每会话 `CODEX_HOME` 接到 AgentWorkspace 的受限子目录；
   使用最小 ACL 和有界清理策略。
4. 加入真实 Codex doctor、bootstrap-serve、停止/崩溃和僵尸进程矩阵，再在现有 Job Object
   边界上叠加资源配额。

该审计不是完整安全验收，也不取代 AutoCAD 实机测试。

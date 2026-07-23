# M4：子进程环境与凭据边界审计

最后更新：2026-07-23（北京时间）

## 结论

截至本审计，Codex 子进程尚未处于环境白名单或每会话凭据隔离中。当前行为可完成已验证的
本机 app-server doctor，但不能被描述为凭据、MCP/插件或用户配置隔离。为避免破坏现有用户登录，
本轮**不**复制、移动、链接或解析任何用户 Codex profile、令牌或配置文件。

## 已核对的调用面

```text
AutoCAD Host.2016
  -> AgentHost bootstrap-serve
  -> CodexLocalAppServerConfigurationResolver
  -> CodexProcessTransport
  -> ProcessStartInfo.Environment (当前继承父环境，再应用 options.Environment 覆写)
  -> codex app-server --stdio
```

- AgentHost 为每个认证 session 创建独立 `AgentWorkspace`；它用于 Agent 工作目录，不等于
  `CODEX_HOME`。
- `CodexLocalAppServerConfigurationResolver` 只读取 `CODEX_EXECUTABLE` 以定位可执行文件；它
  不读取、设置或记录 `CODEX_HOME`。
- `CodexProcessTransport` 当前没有调用 `startInfo.Environment.Clear()`；`AppServerClientOptions.Environment`
  仅叠加/移除指定键，因此父进程其他环境变量仍会继承。
- app-server `initialize` 会返回 Codex home，但 AgentHost 正常 doctor 只公开“已配置”布尔值，
  不公开路径。
- 当前没有代码为 Codex 指定空 MCP、空插件目录或独立凭据引用。

## 风险

- 从 AutoCAD/AgentHost 继承的环境可能含有代理、模型、工具或其他敏感配置；它们尚未被最小化。
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
4. 环境白名单须在独立测试路径中证明 doctor、app-server、正常 bootstrap-serve、取消和停止都
   正常后，才能成为默认路径。
5. 白名单中只允许经过逐项理由说明的操作系统运行时变量、明确的工作目录、批准的 `CODEX_HOME`
   和必要的受控配置；不得把整个父环境或 PATH 重新整体注入。

## 后续顺序

1. 与用户确定每会话认证策略（显式交互登录 / 受支持的 OS 凭据引用 / 暂时保持全局只读模式）。
2. 在不读取敏感值的前提下，创建可测试的 child-environment builder，并用 synthetic child
   process 验证白名单、移除和日志脱敏。
3. 仅在第 1 步的认证路径被证实后，将每会话 `CODEX_HOME` 接到 AgentWorkspace 的受限子目录；
   使用最小 ACL 和有界清理策略。
4. 加入真实 Codex doctor、bootstrap-serve、停止/崩溃和僵尸进程矩阵，再在现有 Job Object
   边界上叠加资源配额。

该审计不是完整安全验收，也不取代 AutoCAD 实机测试。

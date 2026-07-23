# M4：Codex 子进程环境白名单基线

最后更新：2026-07-23（北京时间）

## 结论

生产 AgentHost 启动 `codex app-server --stdio` 时不再复制或继承完整父进程环境。
`CodexLocalAppServerConfiguration.CreateClientOptions()` 强制
`InheritParentEnvironment=false`，`CodexProcessTransport` 先清空子进程环境，再应用固定白名单。
通用 AppServer transport 保留兼容默认值 `true`，因此测试或其他调用者不会被暗中改变。

这完成的是**父环境最小化**，不是每会话 `CODEX_HOME` 或独立凭据隔离。当前白名单仍设置
`USERPROFILE`/`HOME`，Codex 因而继续使用官方默认的用户 `~/.codex`，现有文件登录可以工作，
但该 home 中的配置、认证、日志、会话、技能和插件仍属于全局用户 profile。本切片没有读取、
复制、链接、解析或记录其中的 `auth.json`、配置或 token。

官方 Codex 文档确认：`CODEX_HOME` 默认是 `~/.codex`，并同时承载 config、auth、logs、sessions、
skills 和插件元数据；文件凭据位于 `CODEX_HOME/auth.json`，也可采用 OS keyring 或受信的
`CODEX_ACCESS_TOKEN`。因此在 keyring/受信 token 尚未准备好之前，不把生产路径切换到空的每会话
home。参考：

- <https://learn.chatgpt.com/docs/config-file/environment-variables>
- <https://learn.chatgpt.com/docs/auth>

生产本机配置还固定传入 `-c mcp_servers={}`，覆盖 default Codex profile 的 MCP server 表；它只
限制 MCP server 配置，不会使整个 profile、插件或凭据独立。

## 生产调用链

```text
AgentWorkspace.Create
  -> workspace.Work + workspace.Temp
  -> CodexLocalAppServerConfigurationResolver
  -> CodexChildEnvironmentPolicy
  -> AppServerClientOptions(InheritParentEnvironment=false)
  -> ProcessStartInfo.Environment.Clear()
  -> fixed allowlist
  -> codex app-server --stdio -c mcp_servers={}
```

工作目录和临时目录都必须是已存在、固定本地磁盘、非 UNC/设备路径且不经过已发现重解析点的
目录。无效临时目录返回稳定的 `InvalidTemporaryDirectory`；无法生成必需的系统环境时返回
`InvalidChildEnvironment`，均不在消息中公开真实路径。

## 白名单

| 变量 | 来源与用途 |
| --- | --- |
| `SystemRoot`, `WINDIR` | 由 Windows special folder API 获取，供系统运行时定位。 |
| `ComSpec` | 固定为系统目录内现存的 `cmd.exe`。 |
| `USERPROFILE`, `HOME` | 由用户 profile special folder 获取；当前用于兼容现有 Codex 登录。 |
| `APPDATA`, `LOCALAPPDATA` | 由 Windows special folder API 获取，供本机 Codex 状态和缓存使用。 |
| `TEMP`, `TMP` | 强制指向当前 `AgentWorkspace.Temp`。 |
| `PATH` | 仅由 Windows、System32、Wbem 和 Windows PowerShell 系统目录组成；不复制父 `PATH`。 |
| `PATHEXT` | 固定为 `.COM;.EXE;.BAT;.CMD`。 |
| `RUST_LOG` | 固定为 `error`，避免外部调试设置扩大诊断内容。 |
| `GIT_CONFIG_NOSYSTEM` | 固定为 `1`，不加载系统级 Git 配置。 |
| `GIT_CONFIG_GLOBAL` | 固定为 `NUL`，不加载用户全局 Git 配置。 |
| `GIT_TERMINAL_PROMPT` | 固定为 `0`，禁止子进程交互式凭据提示。 |
| `GCM_INTERACTIVE` | 固定为 `Never`，禁止 Git Credential Manager 交互。 |

明确不自动传入：`CODEX_HOME`、`CODEX_ACCESS_TOKEN`、任何 API key、代理变量、父 `PATH`、
`PSModulePath`、任意自定义变量和用户调试变量。将来确需代理或受信 token 时，必须通过独立的
强类型配置和脱敏审计加入，不能重新开放父环境继承。

## 自动化证据

```text
AppServer Specs: 20/20
  - synthetic child cannot see a parent sentinel
  - explicitly approved variable is visible
  - null override removes an inherited variable
  - empty/'='/NUL environment names and NUL values fail before start
  - production local Codex options force the exact allowlist

AgentHost Release build: 0 warnings / 0 errors
AgentHost doctor under allowlist: passed (Codex 0.144.4, stderr bytes 0)
Real AgentHost v2 live gate: 2/2 (capabilities + two authenticated Codex turns)
Windows PowerShell Phase 2: 329/329, Release 0 warnings / 0 errors
PowerShell 7 Phase 2: 329/329, Release 0 warnings / 0 errors
Relevant AgentHost / codex app-server processes after cleanup: 0 / 0
```

最终 Git 提交结果以本切片 evidence 和提交历史为准。以上检查没有启动、关闭或控制 AutoCAD，
也没有加载 DLL、修改或保存 DWG。

## 未完成边界

- 每会话独立 `CODEX_HOME`、OS keyring/受信 token 认证恢复和独立凭据边界。
- 插件配置隔离和不读取全局 Codex 配置的启动配置；默认空 MCP 已由
  `M4_EMPTY_MCP_BOUNDARY_20260723.md` 中的结构化覆盖完成。
- 代理、企业证书或额外工具目录的强类型配置入口与兼容矩阵。
- 工作目录/临时目录最小 ACL、有界清理和磁盘配额。
- 受限令牌或 AppContainer、CPU/运行时配额和真实故障注入。
- AutoCAD 异常退出、企业代理环境和用户无全局 Codex 登录时的实机矩阵。

在这些边界关闭前，不得把本基线描述为完整进程沙箱、凭据隔离或 M4 完成，也不得据此启用
AutoCAD 2016 CAD 写入。

# M4：本地 Codex 配置与 AgentHost CLI 错误脱敏

最后更新：2026-07-23（北京时间）

## 结论

本切口将本地 Codex 配置解析失败收敛为有限的稳定错误码和固定安全说明。配置解析器不再把
路径、令牌、环境值或任意异常正文放入 `CodexLocalConfigurationException.Message`；未知枚举值
会归一化为 `invalid_configuration`。

`AgentHost` 的 `doctor`、`run` 和命令行用法错误也不再回显调用者提供的命令文本或 .NET
异常类型：

- 配置失败返回 `codex_configuration` 与稳定的 `errorCode`；
- 未知命令固定返回 `unknown_command` 且 `command` 为 `unknown`；
- 未预期失败固定返回 `agenthost_internal_error` / `unexpected_failure`；
- bootstrap 失败只显示已知 bootstrap 命令和 `failed`。

这与已完成的 `AgentBridgeErrorSanitizer` 边界相邻，但不混为同一个契约：前者保护
Bridge/运行时/Host 已发布失败事件，后者保护本地启动配置和 AgentHost CLI 诊断。

## 实现范围

`CodexLocalConfigurationFailurePolicy` 是配置失败的唯一映射表。它仅允许以下类别的稳定码：
平台、配置的可执行文件、可执行文件发现、workspace、temporary directory、child environment、
session isolation、超时及兜底的 `invalid_configuration`。安全说明不含绝对路径、工作目录、
私有目录、token、原始环境值、stderr 或异常类型。

本切口未增加 Provider 抽象、Direct API、第二套 Agent Loop、CAD 命令或 CAD 写入；CAD 写入
和插件保存仍禁用。

## 自动化验证

- AppServer 规格为 `30/30`，覆盖每个配置失败类型和未知枚举归一化；路径形态
  `M4-SENTINEL` 不会出现在配置失败代码或说明中。
- AgentHost Release 编译为 `0` warning / `0` error。
- Release CLI 直接验证：未知命令返回退出码 `2`、不回显 sentinel；无效 `--codex` 返回
  `codex_configuration` / `invalid_configured_executable`、不回显 sentinel。
- 完整 Phase 2 为 `351/351`；包含 Release 构建、九个规格项目、Host 禁用 API、敏感信息扫描、
  diff 检查和 AgentHost doctor 握手。

脱敏记录见：
`evidence/m4-configuration-error-sanitization-20260723.json`。

## 证据边界

这次验证没有启动、重启或操作 AutoCAD，没有执行 `NETLOAD` 或 CAD 命令。Phase 2 的
AgentHost doctor 握手仅验证本机 Codex 的受控健康路径，不构成 AutoCAD 实机证据。

本切口也没有实现磁盘硬配额、真实 Credential Manager 隔离登录、插件配置隔离、日志导出、
受保护审计锚点、受限令牌/AppContainer 或 CAD 写入终态。特别是，不能用目录大小轮询冒充
工作目录硬配额。

## 后续工作

- 将未来安全日志导出及新增外部错误出口逐项接入同一固定代码/说明策略。
- 单独评估可真正强制的 workspace 磁盘硬配额；当前桌面环境不可因此宣称硬配额已完成。
- 继续 M4 的真实隔离登录、插件配置审查、受保护审计锚点及故障/僵尸进程矩阵；M4 完成前
  不启用 M5 CAD 写入。

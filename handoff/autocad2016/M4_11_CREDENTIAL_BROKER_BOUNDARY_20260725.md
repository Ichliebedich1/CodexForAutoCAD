# M4.11 凭据 Broker 边界

日期：2026-07-25

状态：自动化凭据 Broker 纵切已完成；真实 Windows Credential Manager、真实 Codex
登录、keyring 后端和生产受限身份仍未完成。

## 已完成

- `AgentHostBootstrapOptions` 新增默认禁用的凭据配置。
- 启用配置只接受产品专属目标：
  `OpenAI/CodexForAutoCAD/credential/<name>`。
- 当前只声明 Windows Credential Manager access-token 模式。
- 禁用模式携带 target、未知模式、空 target、外部命名空间和非法 target 字符均在启动前
  fail-closed。
- Windows Credential Manager 读取只接受 Generic Credential。
- blob 在复制前执行非空、指针、类型和 `4 KiB` 上限校验。
- 凭据只作为有界二进制数据进入 Launcher，不创建秘密字符串。
- 原生凭据记录在有界复制后立即释放；Launcher 持有的二进制缓冲区在幂等 `Dispose` 时
  原位清零。
- 缺失、错误类型、空值、超限和原生读取失败统一映射为
  `agenthost_credential_unavailable`，公开异常不包含目标、Win32 错误或秘密内容。
- 凭据 Broker 通过认证命名管道向已绑定 PID/创建时间的 AgentHost 一次性发送有界帧；
  服务端写完后立即关闭 pipe，接收端以 EOF 收口，避免双方互相等待。
- AgentHost 在凭据交付后只通过 `codex login --with-access-token` 的 stdin 写入秘密；
  token 不进入 argv、普通环境变量、日志或工作区，stdout/stderr 只排空不保存。
- 每会话 `CODEX_HOME` 写入 `cli_auth_credentials_store = "keyring"`，登录前拒绝已有
  `auth.json`，登录后再次拒绝产生 `auth.json`；超时、取消、非零退出和异常均杀进程树
  并映射为结构化凭据失败。
- 生产 AgentHost bootstrap 已按“创建隔离 home -> 接收凭据 -> stdin 登录 -> bootstrap
  confirmation -> Codex preflight -> app-server”顺序接入；默认凭据模式仍为 Disabled。

## 仍未完成

- 尚未用真实 Windows Credential Manager 凭据执行本机端到端登录。
- 尚未在真实安装的 Codex CLI/keyring 后端中证明 `auth.json` 不产生及 keyring 可用。
- 尚未在 RestrictedToken 下证明 Credential Manager、keyring、Codex、Pipe 和 STOP 全链成功。
- 尚未完成撤销、过期、重复读取、并发启动和失败清理矩阵。
- 尚未完成企业/AutoCAD 真实配额、磁盘硬配额和完整故障矩阵。

因此 Credential Broker 仍默认禁用；只有经过真实凭据和受限身份矩阵后才可启用生产隔离
`codex-home`。M4.7、M4.10、M4.15/M4.16 仍未完成，M5 CAD 写入继续禁用。

## 支持边界

当前受支持设计只允许产品专属 Windows Credential Manager 凭据经受保护继承通道进入
AgentHost，再由 AgentHost 通过 Codex CLI 的 stdin 登录入口注入隔离 home。不得：

- 复制、链接、解析或记录用户全局 `auth.json`；
- 复用随机 `CODEX_HOME` 无法匹配的全局 Codex keyring account；
- 将 token 放入命令行、普通环境变量、配置文件、工作区或日志；
- 使用 Codex internal-only external-auth RPC；
- 在失败时回退 CurrentUser 或用户全局 profile。

浏览器 OAuth 的安全跨会话复用目前没有公开、受支持的产品机制。当前路线首先支持管理员
预置的 access token；API key 模式须在独立 RED/GREEN 切片中加入，不能仅增加枚举冒充完成。

## 自动化

- AgentLauncher net8：`63/63`
- AgentLauncher net45：`63/63`
- Bridge Specs：`53/53`
- Bridge fake Codex login：成功、非零退出、`auth.json`、超时、取消和秘密不泄露：`2/2`
- 两份隔离构建：逐文件 SHA-256 一致
- 连续 service 启停：每个目标框架 `500`
- PowerShell 7 Phase 2：`364/364`
- Windows PowerShell 5.1 Phase 2：`364/364`
- Release：`0 warning / 0 error`
- Host 禁用 API、AgentHost doctor、`git diff --check` 和基础秘密扫描：通过
- AgentHost/FakeAgentHost 最终残留：`0`
- 安全引导门禁必须从真实 worktree 路径运行，不得从 Junction/符号链接别名运行；
  否则 Windows 进程映像真实路径与批准路径不同，身份绑定会按设计 fail-closed。

脱敏证据见 `evidence/m4-credential-reader-verification-20260725.json`。该证据证明
认证凭据帧、隔离 home、fake stdin 登录失败矩阵和自动化门禁；不证明真实 Credential
Manager 凭据、真实 Codex/keyring、AutoCAD 实机或生产受限身份。

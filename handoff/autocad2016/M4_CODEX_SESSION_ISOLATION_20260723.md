# M4: 可选 Codex 会话状态与凭据隔离

最后更新：2026-07-23（北京时间）

## 结论

认证 AgentHost 的 `bootstrap-serve` 现在具有一个**可选**的每会话 Codex 状态隔离路径。只有当
AgentHost 进程环境中存在非秘密引用 `CODEX_AUTOCAD_CREDENTIAL_TARGET` 时才启用；没有该引用时，
行为保持原有用户 profile 兼容模式，避免未准备好安全认证恢复的用户突然失去本地 Codex 登录。

启用时，引用必须严格是下列形式：

```text
CodexForAutoCAD/<letters-digits-dot-underscore-hyphen>
```

它只用作 Windows Credential Manager 的 **Generic Credential Target**。token 本身不能放入环境变量、
命令行、bootstrap frame、审计 JSONL、普通配置文件或本说明。AgentHost 通过 `CredRead` 读取该
Generic Credential 后，在已租约的私有 session workspace 内创建：

```text
codex-home
codex-sqlite
```

这两个目录通过既有私有 ACL 规则创建，并随 session 正常结束或旧 session 清理一并删除。运行时
`codex app-server` 子进程只获得以下三项新增环境变量：

```text
CODEX_HOME
CODEX_SQLITE_HOME
CODEX_ACCESS_TOKEN
```

版本预检 `codex --version` 使用同一隔离目录，但明确移除 `CODEX_ACCESS_TOKEN`；token 只在真正启动
app-server 时进入受控 child environment。父环境继承仍关闭，默认空 MCP 覆盖
`-c mcp_servers={}` 仍保留。

## 生产调用链

```text
Host authenticated bootstrap
  -> AgentHost bootstrap-serve
  -> optional CODEX_AUTOCAD_CREDENTIAL_TARGET
  -> Windows Generic Credential CredRead
  -> private leased workspace/codex-home + codex-sqlite
  -> CodexLocalAppServerConfiguration validation
  -> version preflight without token
  -> app-server runtime with private state and token
```

`doctor` 与独立 `run` 故意没有接入这个可选路径，继续用于旧兼容诊断。UI、AutoCAD Host、Bridge 和
审计不会解析或持有 Credential Manager Target 以外的凭据配置；它们也不会直接访问 Codex profile。

## 失败关闭与审计

以下情况在 app-server 启动前失败：

- 空白、前缀错误、非法字符或过长的 credential target；
- Credential Manager 中找不到/无法安全读取该 Generic Credential；
- 空白、含控制字符、首尾空白或超过上限的 token；
- 私有 session 状态目录创建或验证失败；
- 三个隔离输入不完整、目录不是现有固定本地非重解析目录，或两个目录相同。

AgentHost 只记录稳定脱敏错误码：

```text
codex_credential_reference_invalid
codex_credential_unavailable
codex_credential_rejected
codex_session_workspace_unavailable
```

目录、target、token、Credential Manager 原始错误和全局 profile 内容不应进入错误消息或审计记录。

## 自动化证据

本切片的当前受控验证：

```text
AppServer Specs: 29/29
Bridge Specs: 55/55
```

覆盖范围包括：运行时仅收到隔离三变量、版本预检排除 token、部分/无效隔离输入 fail-closed 且不泄露
目录或 token、私有目录 ACL 与清理、未配置引用保持兼容、无效 target 在读凭据和创建目录之前拒绝、
任意 credential reader 故障转换为稳定脱敏错误。

完整脱敏记录见 `evidence/m4-codex-session-isolation-20260723.json`。测试使用 fake credential
reader；没有创建、读取或枚举真实 Windows Credential Manager 项。

## 明确未完成

- 尚未在真实 Windows Generic Credential、真实 token 和空私有 `CODEX_HOME` 上验证 Codex 登录和
  app-server 初始化。
- 尚未验证真实隔离路径的 AutoCAD Host/NETLOAD、Palette、图纸读写或异常退出；本切片没有启动或
  控制 AutoCAD，也没有加载 DLL、修改或保存 DWG。
- 未配置引用时，全局用户 profile 仍是兼容登录来源；不能把默认路径称为已隔离。
- `mcp_servers={}` 仅清空 MCP server 表；Codex 的插件、技能和其它默认 profile 读取面仍需单独审查。
- 这不是 AppContainer、受限令牌、工作目录硬配额、受保护审计锚点或完整 OS 沙箱。

## 后续人工验证前置条件

在单独批准的测试会话中，人工创建一个不含生产 token 的专用 Windows Generic Credential，并仅把其
**target 名称**作为 `CODEX_AUTOCAD_CREDENTIAL_TARGET` 传给 AgentHost。验证前后都不得把 token、
目标的实际名称、全局 `.codex` 内容或完整环境变量粘贴进聊天、Git 或审计。验证应确认：认证成功、
每次 session 目录被删除、版本预检未带 token、默认未配置路径仍正常、失败路径不泄露秘密。该人工
步骤尚未获得执行授权或证据。

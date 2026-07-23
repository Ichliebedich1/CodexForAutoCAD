# M4：受限令牌与 AppContainer 进程身份隔离可行性审计

最后更新：2026-07-23（北京时间）

## 结论

当前候选**不能安全地直接切换到 AppContainer**，也不能把一个“去掉少量 privilege 的同用户
token”描述为完整文件系统沙箱。源代码和本机能力审计表明，现有启动链、命名管道、私有 workspace
ACL、Credential Manager 读取和 Codex 运行时文件都以当前用户身份为边界；未经改造直接换身份会造成
启动失败，或更糟地为恢复功能而放宽 ACL。

仅 Launcher 内部可见、fail-closed 的受限自身 token 启动探针现已实现：它验证真正的 restricted token
和 private desktop 原语，并将机器结果限制为受限成功或两类结构化失败，始终清理且不回退。它**没有**建立
可成功运行的受限 runtime/ACL 组合，详见 `M4_RESTRICTED_TOKEN_BOOTSTRAP_PROBE_20260723.md`。生产
`bootstrap-serve` 在最小 runtime/workspace/pipe ACL、真实 Codex 子树和 AutoCAD 共存矩阵完成前继续
使用现有启动链。AppContainer 保留为后续部署级方案，不是本次可直接上线的替换项。

## 已核对的当前链路

```text
Host.2016 / Launcher（当前用户）
  -> CreateProcessW + 受限继承 stdin/stdout/stderr 句柄
  -> Job Object（进程树、CPU、内存、时间、关闭即终止）
  -> AgentHost（当前用户）
  -> CurrentUserOnly 命名管道
  -> 当前用户/SYSTEM/Administrators 专有 workspace + CredRead
  -> Codex 子进程
```

审计发现：

1. 生产路径仍是同用户 `CreateProcessW`。公开 `AgentHostBootstrapOptions`、Doctor 和 Service 没有
   身份选择入口；Launcher 内部 friend Specs 的 `RestrictedToken` probe 调用
   `CreateRestrictedToken`、`CreateProcessAsUser` 和 private desktop，并在 `IsTokenRestricted`
   失败时拒绝。没有 `CreateProcessWithTokenW` 或 AppContainer profile/token 实现。
2. 现有 Job Object 在子进程创建后、恢复主线程前分配，因此它可继续作为受限进程树的资源边界；
   但不能替代身份隔离或磁盘配额。
3. Bridge 使用 `PipeOptions.CurrentUserOnly`，不是显式的 logon-SID/restricted-SID/AppContainer-SID DACL。
   受限 token 是否可连接必须实测；AppContainer 则必须设计专用命名空间和最小权限管道 ACL。
4. `AgentHostPrivateStorage` 精确验证 ACL 只能为当前用户、SYSTEM、Administrators；任何受限 SID 或
   AppContainer SID 都会被当前检查拒绝。必须先把“允许什么 SID、允许哪些目录/文件、什么权限”变成
   显式、最小化的产品契约，不能绕过验证器。
5. 可选 session isolation 在 AgentHost 内使用 `CredRead`，然后才把 token 放入受控 child environment。
   AppContainer 不能假定可直接读取同一用户的 Credential Manager；需要保留当前受控 broker 或建立新的
   受限凭据交付设计，绝不能复制 token 到普通 workspace。
6. 本机调用方具有 `SeIncreaseQuotaPrivilege` 与 `SeImpersonatePrivilege`，未发现
   `SeAssignPrimaryTokenPrivilege` 或 `SeTcbPrivilege`。这只说明“受限自身 token”的受控探针值得做，
   不是任何生产启动 API 已被验证的证明。

## Windows API 约束

Microsoft 的 [CreateRestrictedToken](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken)
说明允许用调用方自身 token 的受限版本配合 `CreateProcessAsUser`，此路径不要求
`SeAssignPrimaryTokenPrivilege`；但受限 token 应使用独立 desktop，避免与非受限进程共享默认 desktop。
`CreateProcessWithTokenW` 则要求调用方具有
[`SeImpersonatePrivilege`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw)。

命名管道访问受 DACL 约束，Microsoft 的
[named-pipe security guidance](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
建议以 logon SID 收紧同一终端会话访问；AppContainer 对命名对象还需要在创建时授予其 SID 访问权，
并处理隔离命名空间，见 [AppContainer guidance](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)。
这些要求与当前 `CurrentUserOnly`/严格三 SID 文件 ACL 不等价。

## 分阶段方案

### A. 受限自身 token 探针（已完成的原语/fail-closed 切口）

已新增仅程序集内部可调用的测试入口，不改生产路径：

- 从当前进程 primary token 创建受限 token，并明确移除/禁用 privilege；若 API、desktop、句柄或
  Job 分配任一失败，返回固定结构化错误，绝不回退到未受限启动。
- 为受限子进程建立非交互 private desktop；bootstrap 继续只使用继承的单次句柄，不能转用命令行或
  环境变量传递机密。
- 当前 fake AgentHost 在 runtime/desktop/ACL 组合中无法完成认证，固定失败并 `0` 残留；因此没有为
  可执行映像、必要 runtime、临时 workspace 或命名管道扩大 ACL。
- 成功启动、认证、STOP、超时、Job kill-on-close 与拒绝非 allowlist 访问仍是下一子阶段；不启动
  AutoCAD，不连接真实 Codex。

探针通过后仍不能宣称完整沙箱：它还需要真实 Codex 子树、网络、私有 workspace、Credential Manager、
嵌套 Job/企业策略与 AutoCAD 共存验证。

### B. AppContainer（部署级后续方案）

只有在安装器或受管部署能预配稳定的 profile/SID、安装目录与 runtime ACL、私有 workspace、命名管道
ACL/namespace、最小 network capability 以及凭据 broker 后，才可评估 AppContainer。不得由插件在用户
绘图会话中静默创建 profile、扩大 `Everyone` ACL 或把凭据落盘以“修复”兼容性。

## 证据边界

本审计的原始 source/local capability 结论现已由单独的 restricted-token probe 补充：该 probe 创建了
受限 token、private desktop 和受控 FakeAgentHost 子进程，但没有启动 AutoCAD、未执行 `NETLOAD` 和 CAD
命令，也没有完成受限进程的成功认证运行。它不完成 M4 或授权 M5 CAD 写入。

脱敏记录见：
`evidence/m4-process-identity-isolation-feasibility-20260723.json`。

# M4：受限自身 token Bootstrap 探针（默认关闭）

最后更新：2026-07-23（北京时间）

## 结论

本切口完成了一个**默认关闭、不会回退**的 Windows 受限自身 token 探针，但没有把它作为
AgentHost/Codex 的生产启动方式。

公开的 `AgentHostBootstrapOptions` 不再包含进程身份选择器；公开
`AgentHostBootstrapDoctor.RunAsync` 和 `AgentHostBootstrapService.StartAsync` 固定使用
`CurrentUser`。Host.2016、插件配置及其他产品调用方无法选择 `RestrictedToken`。只有 Launcher
程序集内部、通过 `InternalsVisibleTo` 限定的受控规格可调用该能力探针。

探针已经在本机证明两项 Windows 原语可用：

- 从当前 primary token 创建的 token 由 `IsTokenRestricted` 确认为真正的 restricted token；仅禁用
  privilege 并不足以构成这个结论，因此实现同时提供 restricting SID。
- 可创建并关闭独立 private desktop。

受限 FakeAgentHost 随后尝试走同一套已认证、受限继承句柄、SHA-256/PID/映像校验和 Job Object
启动链。跨机器门禁不再规定它必须以某个特定阶段失败，只接受三类结构化结果：受限认证成功、
`agenthost_process_isolation_failed`，或受限子进程启动后的 `agenthost_child_exited`。任一结果都
必须证明没有向 `CurrentUser` 回退并完成进程清理。本机 net45/net8 结果均为 `child_exited`；这只是
本机能力记录，不是产品契约。

Microsoft 对 [CreateRestrictedToken](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken)
说明 restricting SID 会参与第二次访问检查，并建议受限程序使用非默认 desktop；
[CreateProcessAsUser](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasusera)
也要求 token 对目标可执行映像拥有读/执行访问。因此成功创建 token 不等价于已获得一个可运行的
生产 sandbox。

## 已接入的安全行为

```text
公开 AgentHostBootstrapOptions / Doctor / Service
  -> CurrentUser（唯一产品路径）
Launcher 内部受控能力探针
  -> RestrictedToken
       -> CreateRestrictedToken + IsTokenRestricted
       -> private desktop
       -> CreateProcessAsUser, CREATE_SUSPENDED
       -> 既有映像身份校验、单次继承句柄、Job Object
       -> 成功继续；任何失败固定错误并终止/清理
```

- 公共产品配置、公开导出类型和公开 Doctor 结果均不暴露实验身份选择或原始身份 telemetry。
- 内部探针只接受 `RestrictedToken`；其他 profile 在创建子进程前以
  `agenthost_invalid_configuration` 拒绝。
- restricted token、private desktop、创建子进程或子 token 验证失败会以
  `agenthost_process_isolation_failed` 拒绝；受控 child 在确认前退出仍使用既有的
  `agenthost_child_exited` 安全错误。
- `AgentBootstrapDoctorResult` 的 profile/restricted/private-desktop telemetry 仅对 Launcher 内部和
  friend Specs 可见；探针没有把 runtime 失败细节、desktop 名称、SID、路径、stderr 或 token 写入
  公开错误或 evidence。
- 现有 CurrentUser 重载保持不变，不能因该探针悄悄走 `CreateProcessAsUser`。

## 自动化验证

- `RESTRICTED_TOKEN_PRIMITIVES_FAIL_CLOSED`：原语可用时由 Windows 验证 restricted token 并
  创建/释放 private desktop；受机器策略阻止时只接受结构化隔离失败。
- `RESTRICTED_TOKEN_BOOTSTRAP_PROBE_PORTABLE`：只接受受限认证成功、结构化隔离失败或受限 child
  退出，检查无 CurrentUser 回退、无路径/desktop 泄露且零残留。
- `EXPERIMENTAL_IDENTITY_NOT_PUBLIC`：反射验证公共配置、导出类型和结果均不暴露实验能力。
- AgentLauncher Specs 在 net8 与 net45 均为 `41/41`；Host.2016 MVP Specs 为 `53/53`。
  普通 CurrentUser 的真实 AgentHost bootstrap-doctor 仍作为同一规格集的一部分通过；它不是受限
  token 成功运行的证明。
- 正式 Launcher 门禁以两次隔离构建比较完整可运行输出树，两个输出逐文件 SHA-256 一致；net45/net8
  运行同一 `41` 个固定 ID，结束后相关 AgentHost/FakeAgentHost 残留为 `0`。该门禁没有启动、重启或
  操作 AutoCAD。
- 最新门禁 evidence schema 为 `9`，net45/net8 的 primitive 结果均为 `available`，bootstrap
  结果均为 `child_exited`；验证文件 SHA-256 为
  `7123FB7B29EB6EE37A0E8610C29CCE2DC46E3A636690C0293B5D38D1D5BD3105`。

脱敏记录见：
`evidence/m4-restricted-token-bootstrap-probe-20260723.json`。

## 明确未完成

本探针**没有**证明以下任何一项：

- restricted token 对生产 AgentHost/.NET runtime、Codex、workspace、audit、Credential Manager 或
  `CurrentUserOnly` Bridge 管道的最小权限契约；
- restricted token 的成功认证 bootstrap、`bootstrap-serve`、STOP/超时/Job 回收，或真实 Codex 子树；
- runtime/workspace/pipe ACL 的 allowlist，或默认 desktop/生产目录 ACL 的安全变更；
- AppContainer profile、命名空间、SID ACL、capability、网络或凭据 broker；
- AutoCAD 启动、`NETLOAD`、CAD 命令、CAD 写入或插件保存。

## 下一步

先单独设计一个由部署拥有的、最小化的 restricted runtime fixture：运行时文件、workspace、审计目录、
desktop/window station 与管道的 allowlist 必须逐项可审计、可撤销并在受控测试目录中验证。只有该
fixture 完成成功 bootstrap、STOP/超时、Job 回收和拒绝非 allowlist 访问后，才可评估实际
AgentHost/Codex 兼容矩阵。

在此之前，`RestrictedToken` 继续是 Launcher 内部测试用 fail-closed profile，AppContainer 仍是安装/部署级后续方案；
M4 未完成，M5 CAD 写入继续禁用。

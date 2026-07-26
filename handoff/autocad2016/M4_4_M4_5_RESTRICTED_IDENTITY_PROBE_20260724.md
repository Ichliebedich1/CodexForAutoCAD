# M4.4/M4.5 RestrictedToken 公共边界与可移植探针

最后更新：2026-07-24（北京时间）

## 结论

M4.4 和 M4.5 的代码、自动化与受控 Git 集成已完成。公共产品配置、导出类型和公开结果
不再暴露实验身份选择或原始探针 telemetry；RestrictedToken 只保留为 internal-only
机器能力探针，且任何结果都禁止回退到 CurrentUser。

这不等于 M4.7 生产身份隔离完成。本机结果为：受限令牌与 private desktop 原语可用，但
受限 FakeAgentHost 在完成认证 bootstrap 前退出，结构化结果为 `child_exited`。因此
`authenticatedRestrictedBootstrapVerified=false`，CAD 写入继续禁用。

## 身份

- 冻结目标 SHA-256：
  `164333019801590C57C21A74AE40F7E5AC8A677B1E1D60FE6D4D5E0C884B8DB5`
- 分支：`codex/m4-integration`
- 源码提交：`0763022f34bdd7ac09e4411f8d9f4ddcd97c0c33`
- 提交说明：`feat(agenthost): probe restricted bootstrap without fallback`
- 脱敏 evidence：
  `evidence/m4-restricted-identity-probe-verification-20260724.json`

## M4.4 已实现

- 公共 `AgentBootstrapLaunchOptions` 不允许调用方选择实验进程身份。
- 公共启动结果和 doctor 不返回实验身份选项、原始 Win32 错误、路径或私有 telemetry。
- 实验入口只存在于 Launcher 内部测试边界，不进入 Host、Palette、配置或普通业务调用链。
- 默认路径始终是当前已批准的产品启动方式；实验探针失败不会静默重启为 CurrentUser。
- bootstrap 失败统一映射为稳定错误码和无本地路径/异常正文的说明。

## M4.5 已实现

- 确定性规格与机器能力结果分离；测试不再固定要求某台机器必须得到 `child_exited`。
- 探针只接受三类受限结果：
  `authenticated_success`、结构化 `isolation_failure` 或受限启动后的 `child_exited`。
- net45 与 net8 分别运行并记录结果；两者均验证无 CurrentUser 回退和无残留进程。
- 本机原语结果均为 `available`，bootstrap 结果均为 `child_exited`。
- `child_exited` 只说明受限子进程已启动后退出，不证明 Pipe、workspace、凭据或 Codex
  能在生产受限身份下工作。

## 自动化证据

专项 AgentLauncher 门禁从精确提交重新生成：

```text
net45: 41/41
net8: 41/41
Release: 0 warnings / 0 errors
Relevant residual Agent processes: 0
artifacts/autocad2016-agent-bootstrap-dd40e74ecd264454976ed2d49b62c0de/verification.json
SHA-256: 9F2828286E1259BC6B3FBB32A518638BABA40769D9353E922FCA9CF448A2BEA3
```

完整双 Shell 回归：

```text
Bridge Client net45/net8: 30/30
Bridge: 49/49
Phase 2 PowerShell 7: 358/358
Phase 2 Windows PowerShell 5.1: 358/358
Release: 0 warnings / 0 errors
Source manifest SHA-256:
12649611FA8ED6F4F366CF60C06EB73A2E2958CF99000C49B976D91808DA28C5
verification.json SHA-256:
1DC258ABF33ECF9575E278352BEE103700E75EA3699ABC8FAF415A14706378D0
```

R20.1 API 双 Shell Probe：

```text
29 passed / 8 expected failed
Release: 0 warnings / 0 errors
Autodesk DLLs copied: 0
Cross-shell evidence SHA-256:
8AE4E690EEDB25AF3AA3989B5D331110850396067C6F830BE1A7D5F300C25E4F
```

上述 artifact 留在本地受控构建目录，Git 中只保存脱敏汇总与哈希。

## 未完成

M4.7 仍需在 RestrictedToken 或预配置 AppContainer 下完成真实 AgentHost/Codex
bootstrap、认证 Pipe、STOP 和异常退出，并为 runtime、workspace、Pipe、window
station/desktop 建立最小 DACL。M4.8/M4.11 还需完成工作区 ACL/lease 和凭据 Broker。

本检查点没有启动或控制 AutoCAD，没有执行 `NETLOAD`，不是 M4.16 安全候选，也不授权
进入 M5 CAD 写入。

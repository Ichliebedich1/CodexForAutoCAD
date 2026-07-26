# M4.15.1 企业策略阻止启动的结构化失败纵切

最后验证：2026-07-26（北京时间）

## 本轮结论

M4.15 的第一个非实机纵切已接入正式启动和 Host UI 调用链：

- Windows `CreateProcess` 失败不再全部归入普通 `process_start_failed`。
- 当前用户身份下的 `ERROR_ACCESS_DENIED`（5）、`ERROR_INVALID_IMAGE_HASH`（577）、
  `ERROR_ACCESS_DISABLED_BY_POLICY`（1260）及 Windows 应用阻止错误 4551–4557，会稳定映射为
  `AgentBootstrapLaunchFailure.ProcessStartBlocked` 和
  `agenthost_process_start_blocked`。
- RestrictedToken 路径上的普通访问拒绝仍归入 `ProcessIsolationFailed`，避免把身份/ACL
  隔离失败误报为企业执行策略；显式策略错误 577、1260、4551–4557 在两种身份下都归入
  `ProcessStartBlocked`。
- 其他普通进程创建错误继续归入 `ProcessStartFailed`；RestrictedToken 下的其他创建错误继续
  归入 `ProcessIsolationFailed`。
- Host.2016 将策略阻止显示为不可自动重试的脱敏提示：
  “Windows 或企业策略已阻止 AgentHost 启动；请让管理员检查 AppLocker、WDAC、杀毒/EDR
  和代码签名策略。”
- 原始 Win32 正文、可执行文件路径和 inner exception graph 不进入 Palette 或命令行公共错误。

这只证明分类规则、脱敏边界和正式调用链接线正确；不等于已经在真实 AppLocker、WDAC、
杀毒/EDR、组策略或企业代码签名环境中验证。

## 正式调用链

```text
CODEX16AGENTSTART / Palette Start
  -> MvpAgentRuntime
  -> AgentHostBootstrapService
  -> WindowsInheritedBootstrapProcess.CreateProcess
  -> Marshal.GetLastWin32Error()
  -> AgentBootstrapLaunchFailurePolicy.ClassifyProcessCreationFailure
  -> AgentBootstrapLaunchException
  -> MvpAgentFailureFormatter
  -> 脱敏、结构化、不可自动重试的 Host UI 错误
```

Provider、AutoCAD UI 和 CAD 工具没有获得原始 Win32 错误文本或本地路径。

## 失败分类

| 条件 | 内部失败 | 公共错误码 | Retryable |
|---|---|---|---|
| 当前用户，错误 5 | `ProcessStartBlocked` | `agenthost_process_start_blocked` | `false` |
| 任意身份，错误 577/1260/4551–4557 | `ProcessStartBlocked` | `agenthost_process_start_blocked` | `false` |
| RestrictedToken，普通错误 5 | `ProcessIsolationFailed` | `agenthost_process_isolation_failed` | `false` |
| 当前用户，其他进程创建错误 | `ProcessStartFailed` | `agenthost_process_start_failed` | `true` |
| RestrictedToken，其他进程创建错误 | `ProcessIsolationFailed` | `agenthost_process_isolation_failed` | `false` |

## RED → GREEN 证据

- RED 规格先证明缺少 `ProcessStartBlocked` 枚举和进程创建错误分类入口。
- AgentLauncher 规格覆盖错误 5、577、1260、4551–4557、普通非策略错误，以及
  CurrentUser/RestrictedToken 的差异。
- Host.2016 MVP 规格验证稳定错误码、中文可操作提示、`Retryable=false` 和原始诊断不泄漏。
- `verify-autocad2016-agent-bootstrap.ps1` 将
  `PROCESS_POLICY_BLOCK_CLASSIFIED` 纳入 bootstrap 必选规格。

## 最终自动化结果

- AgentLauncher bootstrap net8：`64/64`，包含连续 `500` 次启停回收。
- AgentLauncher bootstrap net45：`64/64`，包含连续 `500` 次启停回收。
- PowerShell 7 Phase 2：`416/416`。
- Windows PowerShell 5.1 Phase 2：`416/416`。
- Host.2016 MVP：`59/59`。
- Release：`0 warning / 0 error`。
- Host 禁用 API、敏感信息扫描和 AgentHost doctor：通过。
- R20.1/.NET Framework 4.5/x64 Host A/B 逐字节一致，SHA-256：
  `B1BF3287338115C5986A3424A689BFF45867C1C4F9EF0F69A85A6822E072683C`。
- R20.1 产物中的 Autodesk DLL 复制数：`0`。
- 本轮验证产物：
  `artifacts/m4-15-policy-block-r201-host-5a8e27c341354156aa17a371ef55f0a0/`。
- User PATH 长度仍为 `661`，SHA-256：
  `05df0d2ffc86d41186216560d37cc16fa0159ed5cef9a89f61042964c196be59`；
  项目可疑 PATH 条目 `0`。
- AgentHost、FakeAgentHost、Bridge Client TestServer 和强杀恢复工作器残留：`0`。

Phase 2 的动态汇总仍为 `416/416`，因为 AgentLauncher bootstrap 是独立专项，不计入其中九个
动态规格项目；Launcher 专项由 `63/63` 增至 `64/64`。

## 尚未完成

M4.15 仍需完成：

1. 真实父进程已在 Job 中时的嵌套 Job 企业矩阵。
2. 真实 Codex 和 AutoCAD 正常/异常退出及僵尸进程检查。
3. 普通受限账户、RestrictedToken 或预配置 AppContainer 的生产全链。
4. 真实 AppLocker、WDAC、组策略、代码签名、杀毒/EDR 拦截矩阵。
5. 系统断电、磁盘满和企业保留策略的恢复与人工归档流程。
6. 每个真实故障的候选哈希、事件日志、稳定错误码、无残留和日志不泄密证据。

当前不得把本纵切解释为 M4.15 或 M4 已完成；M4.16 安全前置候选尚未冻结，M5 CAD 写入继续
保持硬禁用。

本轮没有启动或控制 AutoCAD，没有启用 CAD 写入、保存、命令、LISP、Shell、文件或网络 Agent
工具，也没有提交、合并、cherry-pick、push、reset 或清理 Git 工作树。

# M4.15.4 受限账户与执行控制实机矩阵

最后更新：2026-07-26（北京时间）

M4.15 的其他子项都各有交接文件，M4.15.4 此前没有任何一份，因此它的实机范围、预期错误码
和证据要求一直没有落到纸面。本文件补齐这个缺口，是 M4.15.4 的唯一执行入口。

## 1. 本项的性质

M4.15.4 **没有可以先做的自动化部分**。它要验证的不是分类规则，而是「真实的执行控制机制
确实会让 `CreateProcess` 返回我们已经分类的那些 Win32 错误」。这一步只能在装有真实
AppLocker / WDAC / 代码签名策略 / 杀毒 EDR 的机器上，由用户执行。

已由 M4.15.1 自动化覆盖、**本文件不再重复验证**的部分：

- Win32 错误 → `AgentBootstrapLaunchFailure` → 公共错误码的映射规则；
- `Retryable=false` 语义；
- Host.2016 只显示脱敏可操作提示，不泄漏原始 Win32 正文、可执行文件路径或异常链。

本文件要验证的部分：真实拦截**是否真的落进**上述已分类的错误码；以及被拦截时系统侧
是否留下可核对的事件证据。

## 2. 当前实现的分类（实机需据此核对）

| 条件 | 内部失败 | 公共错误码 | Retryable |
|---|---|---|---|
| 当前用户身份，错误 `5` | `ProcessStartBlocked` | `agenthost_process_start_blocked` | `false` |
| 任意身份，错误 `577` / `1260` / `4551`–`4557` | `ProcessStartBlocked` | `agenthost_process_start_blocked` | `false` |
| RestrictedToken，普通错误 `5` | `ProcessIsolationFailed` | `agenthost_process_isolation_failed` | `false` |
| 当前用户，其他创建错误 | `ProcessStartFailed` | `agenthost_process_start_failed` | `true` |
| RestrictedToken，其他创建错误 | `ProcessIsolationFailed` | `agenthost_process_isolation_failed` | `false` |

被阻止时 Host 固定显示：

> Windows 或企业策略已阻止 AgentHost 启动；请让管理员检查 AppLocker、WDAC、杀毒/EDR
> 和代码签名策略。

分类源码：`src/Codex.AutoCAD.AgentLauncher/AgentBootstrapLaunchModels.cs`
（`ClassifyProcessCreationFailure` / `IsExplicitExecutionPolicyError`）。

### 需要在实机上回答的一个设计问题

当前用户身份下的普通错误 `5` 被归入 `ProcessStartBlocked`，也就是会显示上面那句
「请让管理员检查 AppLocker、WDAC……」。但错误 `5` 也可能只是候选目录的一个普通 NTFS ACL
拒绝，跟企业策略毫无关系。

矩阵 A 要专门回答：**在标准用户下因纯 ACL 拒绝而失败时，这句提示是否会把管理员引向错误
的排查方向。** 如果会，这是一条需要在 M4.16 冻结前修掉的可用性缺陷，不是实现细节。
在实机结果出来之前不要改分类——现在改属于凭猜测调整安全边界。

## 3. 前置条件与边界

- 绑定 M4 候选：执行前记录候选目录、Host 与 AgentHost 的 SHA-256，所有结果都绑定这组哈希。
- 每个矩阵开始前和结束后各记录一次相关进程残留数（AgentHost、Codex app-server）。
- **不修改本机的企业策略来制造失败**。只在策略已经存在、或由管理员在受控测试机上正式配置
  的前提下执行。不导入注册表文件，不改 UAC、Defender 排除项或代码签名信任链。
- 不启动或操作 AutoCAD 进行写入；本矩阵只验证 AgentHost 启动路径。
- 不把杀毒/EDR 的告警提交给厂商云或外部分析服务。
- 结果只回报错误码、事件 ID 和计数，不回报可执行文件完整路径、账户名、域名、策略 GUID、
  证书主题、机器名或事件日志原文。

## 4. 六个子矩阵

事件 ID 为常见默认值，实际以本机日志通道为准；对不上时记录实际 ID，不要改测试结论。

### A 普通受限用户（标准账户，无管理员权限）

1. 以标准用户登录，加载候选并触发 `CODEX16AGENTSTART`。
2. 分别测试两种情形：
   - A1 候选目录对该用户可读可执行 → 预期正常启动；
   - A2 候选目录对该用户拒绝执行（由管理员用 ACL 设置） → 预期启动失败。
3. A2 记录：公共错误码、Retryable、Host 提示文本、是否出现第 2 节末尾描述的误导。

预期：A1 成功；A2 得到 `agenthost_process_start_blocked`、`Retryable=false`。

### B RestrictedToken 生产全链

1. 在 RestrictedToken 身份下完整跑一次启动 → 提问 → STOP。
2. 制造一次普通 ACL 拒绝（同 A2 方式）。

预期：普通拒绝归入 `agenthost_process_isolation_failed`，**不得**被报成
`agenthost_process_start_blocked`——这条区分是 M4.15.1 的核心不变量，实机必须确认它没有
在真实身份下失效。

### C 预配置 AppContainer

仅在环境已预配置 AppContainer 时执行；不为本测试新建容器配置。
记录启动是否成功、失败时的错误码，以及能力不足是否被误报为策略阻止。

### D AppLocker

1. 由管理员在测试机上使 AgentHost 可执行文件落入 AppLocker 拒绝规则。
2. 触发启动。
3. 采集 `Microsoft-Windows-AppLocker/EXE 和 DLL` 通道事件（阻止通常为 `8004`，
   仅审核模式为 `8003`）。

预期：`agenthost_process_start_blocked`、`Retryable=false`，且事件通道中存在对应记录。

### E WDAC 与代码签名

1. 在 WDAC 策略生效的测试机上触发启动。
2. 采集 `Microsoft-Windows-CodeIntegrity/Operational` 事件（阻止通常为 `3077`，
   审核为 `3076`）。
3. 若环境要求签名而候选未签名，记录这一事实本身——它决定 M9 是否必须引入签名步骤。

预期：错误 `577` 或 `1260` → `agenthost_process_start_blocked`。

### F 杀毒 / EDR 拦截

1. 只在拦截**自然发生**或由管理员在受控测试机上正式配置的前提下记录。
   不要为触发拦截而构造可疑行为。
2. 采集对应产品的操作日志；Windows Defender 通常在
   `Microsoft-Windows-Windows Defender/Operational`（`1116` / `1117`）。

预期：得到稳定错误码而不是超时或静默失败；无残留进程。

## 5. 每个矩阵的证据表

| 字段 | 说明 |
|---|---|
| 矩阵 | A / B / C / D / E / F |
| 结果 | 通过 / 失败 / 跳过（跳过必须写原因） |
| 公共错误码 | 例如 `agenthost_process_start_blocked` |
| Retryable | `true` / `false` |
| Host 提示是否脱敏 | 有无出现路径、账户名、Win32 原文 |
| 系统事件通道与 ID | 实际观察到的值 |
| 残留进程数 | 结束后 AgentHost + Codex app-server |
| 候选哈希 | Host 与 AgentHost SHA-256 前 8 位即可 |

## 6. 完成条件

M4.15.4 只有在 A、B、D、E 四个矩阵都有实机结果，且满足以下全部条件时才能标为完成：

1. 每种拦截都产生**稳定且不可自动重试**的结构化错误码，不出现超时或静默失败；
2. RestrictedToken 的普通拒绝与企业策略阻止在真实身份下仍然可区分；
3. Host 提示不泄漏路径、账户名、策略标识或 Win32 原文；
4. 每次失败后相关进程残留为 `0`；
5. 第 2 节末尾的误导性提示问题有明确结论。

C 与 F 允许标为「环境不具备」而跳过，但必须写明原因，且不得据此把 M4.15.4 记为通过。

在全部条件满足前，M4.15.4、M4.15 和 M4 均不得标为完成，M4.16 安全前置候选不得冻结，
M5 CAD 写入继续保持硬禁用。

## 7. 反馈格式

按第 5 节的表格逐矩阵回报。不要发送事件日志原文、完整可执行文件路径、账户名、域名、
策略 GUID、证书主题或机器名。

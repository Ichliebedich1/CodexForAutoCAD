# M4 真实环境矩阵：先做的三项

创建：2026-07-26（北京时间）

M4.16 冻结前置条件目前只剩两条，而且是同一件事：`M4Complete` 为 false，因为 9 个真实
机器/企业矩阵一项都没验证。其余机械性条件（工作树干净、回滚点、evidence 绑定当前
HEAD、`RunCorrelation=Correlated`）已全部满足。

本文件只覆盖其中**难度最低、且 M5 写入闭环真正依赖**的三项：

| 矩阵 | readiness 字段 |
|---|---|
| A 真实 Windows 凭据管理器 | `RealCredentialManagerVerified` |
| B 真实 Codex 登录与 keyring | `RealCodexLoginAndKeyringVerified` |
| C 真实异常退出 | `RealAbnormalExitMatrixVerified` |

选这三项不是因为好做，是因为**凭据和进程生命周期正是 M5 往图纸里写东西时所依赖的东
西**。这三项没有真实证据，M5 的写入闭环就建立在假设之上。

## 0. 安全边界（每一条都不要跳过）

- **凭据由你自己创建和输入。**不要把令牌、密码或凭据内容贴到聊天里，也不要让我代填。
  我不接触秘密值；本文件只描述你在 Windows 自己的界面里做什么。
- 全程只用**空白图或脱敏副本**。三项都不需要打开生产图纸。
- 强杀只针对**本次测试启动的** AgentHost / Codex / AutoCAD 进程。不要强杀你正在用、
  有未保存内容的 AutoCAD 会话。
- 不启用 CAD 写入或插件保存。三项都在只读状态下进行。
- 反馈只给错误码、状态和计数。**不要发送**：凭据目标全名、令牌、用户名、域名、
  完整路径、`TRUSTEDPATHS`、事件日志原文。

## 1. 绑定候选

三项结果必须绑定到执行时所用的候选哈希，否则重建后结论自动失效。开始前记录：

```powershell
Get-FileHash '<候选目录>\Codex.AutoCAD.Host.2016.dll' -Algorithm SHA256
Get-FileHash '<候选目录>\AgentHost\Codex.AutoCAD.AgentHost.exe' -Algorithm SHA256
```

当前 `main`（`fdcb438`）在干净 worktree 上的自动化构建结果为
Host `9827DC32…`、AgentHost `779063EB…`。实机用的候选必须与你记录的一致。

## 2. 矩阵 A：真实 Windows 凭据管理器

> **暂不可执行（2026-07-26 更正）。**
>
> 本节最初写成可执行步骤是错的。凭据模式位于 `AgentHostBootstrapOptions`，默认
> `Disabled`，而 **Host.2016 完全没有暴露任何凭据配置入口**——`src/Codex.AutoCAD.Host.2016`
> 里没有一处引用凭据。也就是说，用户没有任何办法把凭据模式打开，A1–A4 无法执行。
>
> 这是**代码缺口，不是环境缺口**：需要先在 Host.2016 增加一个凭据配置面（默认仍关闭、
> 只接受产品前缀、错误码不变），A 才能变成可执行的实机矩阵。在那之前
> `RealCredentialManagerVerified` 保持 `not_run`。
>
> 下面的实现事实与步骤保留，作为补上配置面之后的验收依据。

### 当前实现事实

- 凭据模式默认是 **`Disabled`**。禁用状态下携带 target 会在启动前 fail-closed。
- 启用后只接受产品专属目标：`OpenAI/CodexForAutoCAD/credential/<name>`，
  且只接受**普通凭据（Generic Credential）**。
- 凭据内容有 `4 KiB` 上限，只作为有界二进制进入 Launcher，不会被构造成秘密字符串；
  用完在 `Dispose` 时原位清零。
- 缺失、类型不对、空值、超限和原生读取失败，全部映射为同一个稳定错误码
  `agenthost_credential_unavailable`，公开错误里不含目标名、Win32 错误或秘密内容。

### 步骤

**A1 缺失凭据必须 fail-closed。**在**不**创建任何凭据的情况下，启用凭据模式并指定一个
目标名，启动 AgentHost。

预期：得到 `agenthost_credential_unavailable`，且提示里看不到目标名、Win32 错误码或
任何秘密片段。

**A2 错误类型必须 fail-closed。**用 Windows「凭据管理器」创建一条**域凭据**
（不是普通凭据），目标名用产品前缀。启动。

预期：同样是 `agenthost_credential_unavailable`，不因为"凭据存在"就放行。

**A3 正常路径。**删掉 A2 那条，改建一条**普通凭据（Generic Credential）**，目标名为
`OpenAI/CodexForAutoCAD/credential/<你自己取的名字>`，内容填一个你自己准备的测试值。
启动。

预期：AgentHost 正常启动并可以问答。

**A4 外部命名空间必须拒绝。**再建一条目标名**不带**产品前缀的普通凭据，配置指向它。

预期：启动前就被拒绝，错误里不出现该目标名。

### 记录

A1/A2/A4 的错误码是否都是 `agenthost_credential_unavailable`（A4 可能是配置层拒绝，
记录实际码）；A3 是否成功；四次是否都没有泄漏目标名或秘密。

## 3. 矩阵 B：真实 Codex 登录与 keyring

**这一项完全由你在 Codex 自己的登录流程里完成。**我不参与，也不需要知道任何凭据。

**B1 未登录状态。**在一个干净的 `CODEX_HOME` 下，不登录直接从 Host 启动 AgentHost 并
提问。

预期：得到稳定、脱敏的失败，不泄露路径或原始 stderr；AutoCAD 仍可操作；`DBMOD` 不变。

**B2 完成真实登录。**你用 `codex` 自己的登录方式完成登录。

**B3 登录后正常路径。**重新 `CODEX16AGENTSTART`，完成两轮问答。

预期：两轮都返回，第二轮记得第一轮内容；CAD 写入与插件保存仍显示 disabled。

**B4 会话隔离的 CodexHome。**如果启用了每会话 `CODEX_HOME`，确认登录态是否按预期在
会话间隔离或共享——**记录你实际观察到的行为**，不要按预期填写。这一条的目的就是发现
文档与现实不符的地方。

### 记录

B1 的错误码与是否脱敏；B3 两轮是否成功；B4 观察到的实际隔离行为。

## 4. 矩阵 C：真实异常退出

目标：真实强杀之后必须产生**唯一终态、无僵尸进程、不泄露 stderr**，并且后续 ASK
fail-closed。

每一小节开始前用空白图，结束后记录残留进程数：

```powershell
Get-Process -Name 'Codex.AutoCAD.AgentHost' -ErrorAction SilentlyContinue
```

**C1 强杀 AgentHost。**启动 AgentHost，提交一个较长的问题，在回答进行中强制结束
`Codex.AutoCAD.AgentHost.exe`。

预期：Palette 显示稳定错误码（`agenthost_unexpected_exit` 或等价终态），当前回合结束，
后续 `CODEX16ASK` fail-closed 要求重新启动；无残留 Codex 子进程。

**C2 强杀 Codex 子进程。**重新启动，提交长问题，强制结束 Codex app-server 子进程
（**不是** AgentHost）。

预期：得到稳定终态而不是无限等待；AgentHost 自身按设计收口。

**C3 强杀 AutoCAD。**新开一个 AutoCAD 进程，加载候选、启动 AgentHost，然后强制结束
AutoCAD 本身。

预期：AutoCAD 退出后 AgentHost 与 Codex 子进程**残留为 0**——这条验证的是
`KILL_ON_JOB_CLOSE` 进程树边界在真实强杀下确实生效，而不只是在自动化夹具里生效。

**C4 强杀后重新启动。**C1–C3 各自之后，重开干净进程，确认可以正常重新启动并问答。

预期：前一次的强杀不会永久毒化后续启动。

### 记录

每一小节：错误码、终态是否唯一、后续 ASK 是否 fail-closed、残留进程数、`DBMOD` 是否
变化。**C3 的残留进程数是这一项里最关键的数字。**

## 5. 证据表

| 字段 | 说明 |
|---|---|
| 矩阵 | A1–A4 / B1–B4 / C1–C4 |
| 结果 | 通过 / 失败 / 跳过（跳过写原因） |
| 稳定错误码 | 实际观察到的值 |
| 是否脱敏 | 有无出现目标名、路径、用户名、Win32 原文 |
| 残留进程数 | 结束后 AgentHost + Codex 子进程 |
| DBMOD | 前后是否变化 |
| 候选哈希 | Host 与 AgentHost SHA-256 前 8 位 |

## 6. 完成之后

三项各自的小节全部有结果后，才可以把对应的 readiness 字段置为 true。

**注意：当前 `verify-m4-automated-readiness.ps1` 把这 9 个字段硬编码为 `$false`，
没有任何输入通道。**也就是说，即使你今天做完这三项，汇总器也无法记录。这个缺口必须
先补上，且补法必须要求结果绑定候选哈希——否则重建候选后旧结论会被无声继承，重演
今天早上"陈旧 evidence 被当成本次结果"的同一类错误。

完成三项后仍然是 `M4Complete=false`：还有 6 项未做（受限身份全链、固定容量卷、
磁盘写满、系统断电、AppLocker/WDAC/EDR、企业保留归档）。它们各自的处置——做、延后、
还是明确划出范围——需要单独决定，且目标文件只能由你修改。

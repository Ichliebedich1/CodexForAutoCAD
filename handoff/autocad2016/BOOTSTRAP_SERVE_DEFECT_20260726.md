# `bootstrap-serve` 启动缺陷调查记录

创建：2026-07-26（北京时间）

`CODEX16AGENTSTART` 在真机上失败，`error_code=agenthost_timeout`、
`error_stage=starting_agenthost`。本文件记录当日调查的**确定结论**与**未确定部分**，
以及下一步唯一该做的事。

## 1. 已确定（有可复现证据）

### 1.1 生产启动路径坏了，且与候选无关

`CODEX16AGENTSTART` 走 `AgentHostBootstrapService.StartAsync` → AgentHost 的
`bootstrap-serve`。同一台机器、同一个二进制：

```text
bootstrap-doctor   168 ms 通过
bootstrap-serve    ~10 s 超时（10011 / 10016 / 10019 / 10023 / 10027 / 10043 ms）
```

M1 候选（`8E6B26FD…`）与汇合后候选（`EF079C01…`）的 AgentHost **表现相同**，因此不是
某个候选引入的问题。复现程序 `BootProbe` 位于会话临时目录，可在 AutoCAD 之外稳定复现，
无需启动 AutoCAD。

### 1.2 bootstrap 门禁从未覆盖生产路径

`verify-autocad2016-agent-bootstrap.ps1` 的 scope 是
`autocad2016-live-agenthost-inherited-handle-bootstrap-doctor`，evidence 字段为
`RealAgentHostBootstrapDoctorCompleted`，规格实现调用
`AgentHostBootstrapDoctor.RunAsync`——**全部是 `bootstrap-doctor`**。

生产使用的 `bootstrap-serve` 没有任何门禁覆盖。这就是「7/7 全绿而生产起不来」的原因，
也是当日最严重的一处假绿。

`bootstrap-serve` 比 `bootstrap-doctor` 多出：每会话隔离 `CODEX_HOME`
（`CodexSessionHomeLease`）、凭据管道投递与接收（两侧各带 10 秒超时）、以及确认帧交换。
这些差异全部未被自动化验证过。

### 1.3 真实错误被 `Timeout` 盖住

`AgentHostBootstrapService.WaitForConfirmationAsync`：

```csharp
var completed = await Task.WhenAny(confirmationTask, controller.AbortCompletion);
if (completed == controller.AbortCompletion) { ... throw controller.GetTerminalFailure(); }
// 下面这条能给出真实退出码的分支，只有在 confirmationTask 抛错时才可达
if (child.WaitForExit(1000, out exitCode) && exitCode != 0) {
    throw new AgentBootstrapLaunchException(ChildExitedWithError, ...);
}
```

确认帧读取在子进程异常结束时**没有结束**，于是永远走 abort 分支报 `Timeout`，
`ChildExitedWithError` 分支不可达。**这是独立于根因的第二个缺陷**：即使根因修好，
任何未来的早期子进程故障仍会被报成笼统的超时。

### 1.4 会话租约创建成功

每次尝试都在 `%LOCALAPPDATA%\OpenAI\CodexForAutoCAD\workspace\sessions\` 下留下一个
会话目录，含 `audit`、`codex-home`、`workspace`、`.active`、`.codex-autocad-session`。
因此 Launcher 侧的 `AgentSessionWorkspaceLease.CreateForCurrentUser` 是通过的。

所有会话的 `codex-home` **均为空**——没有任何一次走到 codex 初始化。

## 2. 未确定（不要在此基础上继续推理）

**子进程的生命周期尚无一致观测。**当日两次测量互相矛盾：

- 1.1 秒间隔采样：t=1s..9s 全部为 0 个 AgentHost 进程
- 25 毫秒间隔采样：401 ms 抓到进程，随后 `WaitForExit(8000)` 未取得退出码

两者不能同时为真。**子进程的退出码、存活时长和死亡原因都未确定。**

当日在这条线上先后提出并被推翻的假设：冷启动开销、汇合引入的回归、隔离 CODEX_HOME
初始化慢、子进程从未创建。**四次都是在证据不足时下的结论。**记录于此是为了让后来者
不要重复它们，也不要把它们当作线索。

## 3. 根本原因：子进程没有被许可的失败上报渠道

> 本节更正了本文件先前写的「下一步是把 stderr 带进失败信息」。那个方向是错的，
> 它等于推翻 M4.14 的脱敏边界。

`CaptureStandardErrorAsync` 读取子进程 stderr 后**立即清零并丢弃内容**，只保留字节数：

```csharp
while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
{
    capturedBytes += Math.Min(read, remaining);
    Array.Clear(buffer, 0, read);          // 内容丢弃
}
return new AgentHostStandardErrorCapture(capturedBytes, truncated);
```

`AgentHostStandardErrorCapture` 只有 `Bytes` 与 `Truncated` 两个字段。这是 M4.14 的
有意设计（「stderr 仍为无文本摘要」），用于防止子进程原始错误文本把路径或令牌带给父进程。
**该决定本身是正确的，不应推翻。**

但它的直接后果是：

> **AgentHost 在送出确认帧之前失败时，没有任何被许可的渠道说明失败原因。**
> stderr 按设计被丢弃，确认通道尚未建立，进程只是退出。

因此当前这个生产缺陷**通过产品自身的通道不可诊断**——所缺的信息在设计上就不存在，
不是没找对地方。

### 正确的修复方向

让子进程通过**本就安全的通道**上报失败，而不是放宽 stderr。

`FormatBootstrapFailureForStandardError` 产出的是稳定错误码、阶段、诊断分类和数值脱敏
标志——**闭集，无自由文本**。这类内容完全可以走继承的确认通道（或一个受保护的、
定长的状态字段）回传，既保持 M4.14 的边界，又让早期失败可诊断。

这是协议层的缺口，不是加一行日志能解决的。修改需要同时改动 AgentHost 的
`bootstrap-serve` 失败路径与 Launcher 的确认读取，并配套一条覆盖 `bootstrap-serve`
的门禁（当前门禁只覆盖 `bootstrap-doctor`，见 1.2）。

在此之前，不要再靠进程采样猜测机制。

## 4. 阻塞影响

- 矩阵 C（`RealAbnormalExitMatrixVerified`，目标文件规定的 M5 硬前置）无法执行。
- M1 实机矩阵无法重测——其当日记录已因未实际执行而撤回，见
  `M1_READONLY_STABILITY_RUNTIME_TEST_20260722.md` 第 15 节。
- `M4Complete` 保持 `false`，M4.16 无法冻结，M5 保持阻断。

代码、门禁与文档在 `main` 上是干净的；被阻塞的是实机验证，不是代码集成。

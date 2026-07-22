# AutoCAD 2016 MVP 高 DPI、文档关闭与退出生命周期测试

本清单只在 `MVP_AGENT_RUNTIME_TEST_20260721.md` 的 `0.3.1.0` Agent 在线及两轮对话
通过后执行。各样本必须独立记录，不能把不同缩放、不同图纸或不同 AutoCAD 进程的
`DBMOD` 和事件计数拼成一条时间线。

候选 ID：`autocad2016-mvp-agent-v031-a7bff46f-8c74b95e`

Host SHA-256：
`A7BFF46F1BA4970818ACB03F51C09EEBF1DDB8A7093D0C4C615E2D877D9236D1`

## 固定边界

- 只使用脱敏测试图、空白图或用户认可的副本。
- 由用户手工启动、关闭和切换 AutoCAD；Codex 不控制 CAD 进程。
- 不脚本化修改注册表、`SECURELOAD`、`TRUSTEDPATHS`、`SAVETIME` 或企业策略。
- CAD 写入和插件保存保持禁用。
- AutoCAD 原生 `.sv$` 自动保存若落在 `DBMOD` 观察窗口内，该窗口作废后重测；不得
  把它归因于插件，也不得为了本测试自动修改用户的保存设置。
- 不提交图纸名称、路径、canonical JSON、选择哈希、上下文哈希或真实坐标。

## 1. 125% DPI 独立样本

1. 用户在 Windows 显示设置中手工选择 `125%`。若 Windows 要求注销或重新登录，由
   用户自行决定并完成；不要通过注册表或脚本修改。
2. 手工启动一个未加载旧候选的 AutoCAD 2016 进程。
3. `NETLOAD` 精确 `0.3.1.0` Host，然后执行：

```text
DBMOD
CODEX16PAL
CODEX16PALINFO
CODEX16PALRESET
CODEX16PALINFO
DBMOD
```

4. 实际检查停靠、浮动、隐藏重开、两个上下文标签页、assistant 区、问题输入框和发送
   按钮；输入两行中文但不必发送。

通过标准：

- `Module version: 0.3.1.0`。
- 面板文字、按钮和输入框无不可用裁切、重叠或无法滚动区域。
- 中文输入、换行、停靠、浮动、隐藏重开与 RESET 正常。
- generation/reset/release 按一次 RESET 递增。
- INFO 的 DPI 若报告约 `120 x 120`，记录为 DPI telemetry 通过；若仍为 `96 x 96`、
  `unavailable` 或其他数值，记录实际值并将 telemetry 单项标为未通过，即使视觉可用也
  不得把整个 125% DPI 项表述为完整通过。
- 无原生自动保存污染时，窗口首尾 `DBMOD` 相同。

## 2. 150% DPI 独立样本

在另一个干净 AutoCAD 2016 进程中按第 1 节重复，Windows 显示缩放改为 `150%`。

通过标准相同，但 DPI telemetry 预期约为 `144 x 144`。测试完成后由用户自行恢复原来的
Windows 缩放设置；插件不修改显示配置。

## 3. 文档关闭与缓存失效

准备两个脱敏测试图 A、B。只在图 A 发布上下文：

```text
保持未选择状态
DBMOD
预选测试图元
CODEX16CTX
CODEX16CTXINFO
```

确认 `published=true` 后，由用户通过 AutoCAD 正常界面关闭图 A，并切换到图 B。随后在
图 B 执行：

```text
CODEX16CTXINFO
CODEX16PALINFO
DBMOD
```

通过标准：

- AutoCAD 不崩溃、不卡死。
- 旧上下文为 `published=false`、`selected=0`，不得跨图保留。
- `Anonymous DocumentToBeDestroyed events` 至少增加一次；最终状态可能随后被图 B 的
  DocumentActivated 更新为 `cleared-document-activated`，因此以事件计数和未发布状态
  共同判断，不强制最终只出现一个原因字符串。
- Palette 仍可隐藏、重开和 RESET。
- 未发生 CAD 写入或插件保存。
- 图 B 的 `DBMOD` 只与图 B 自身关闭后样本比较，不能与图 A 的数值直接比较。

## 4. AutoCAD 正常退出与 AgentHost 清理

本项专门验证插件终止回调，不先执行 `CODEX16AGENTSTOP`：

1. 在干净 AutoCAD 2016 进程中加载精确候选、打开 Palette、发布一个只读上下文。
2. 执行 `CODEX16AGENTSTART`，确认 `AgentHost 在线；只读 Codex 会话已建立。`
3. 通知 Codex 准备退出测试；Codex 只读记录相关进程基线，不发送 CAD 命令。
4. 由用户通过 AutoCAD 正常界面退出程序。若 AutoCAD 因用户图纸状态询问是否保存，
   由用户自行决定；插件不得代替用户回答。
5. AutoCAD 完全关闭后通知 Codex，由 Codex 只读检查残留进程。

通过标准：

- AutoCAD 正常退出，无持续卡死或崩溃提示。
- `Codex.AutoCAD.AgentHost.exe` 在有界清理后不存在。
- 与本轮 AgentHost 关联的 Codex app-server 不成为新增孤儿进程；既有 Codex Desktop
  app-server 只作为基线，不得误杀或误报。
- 无插件主动保存、无 CAD 写入、无自动重试。

若 AutoCAD 超过约 30 秒仍无法退出，或 AgentHost 仍残留，立即把本项记为失败并保留
现场；不要反复强制结束进程掩盖生命周期问题。

## 5. 证据结果

每项只允许填写：`通过`、`失败`、`阻塞`、`未测`。

在四项全部取得实际运行证据前，以下值必须保持：

```text
HighDpi125Verified=false
HighDpi150Verified=false
DocumentCloseLifecycleVerified=false
AutoCadExitLifecycleVerified=false
```

视觉可用、DPI telemetry、文档关闭和进程清理是不同证据，任何一项缺失都不能用另一项
代替。

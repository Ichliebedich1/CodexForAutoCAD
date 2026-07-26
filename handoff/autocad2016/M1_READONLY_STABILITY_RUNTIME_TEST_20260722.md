# AutoCAD 2016 M1 只读稳定化实机测试

状态：`0.3.3.0` 代码、自动化和候选冻结已完成；本文件是当前唯一的 M1 实机验收入口。
2026-07-26 已在集成分支 `4667a48` 重新验真并重建候选（Phase 2 `276/276`、Host MVP
`41/41`、R20.1 A/B 逐字节一致）；旧候选目录 `…-4b602965-561c6af3` 已不存在，实机必须
使用下方当前候选。

## 1. 候选身份与加载文件

```text
Candidate directory:
C:\tmp\CodexForAutoCAD-m1-integration\artifacts\autocad2016-m1-readonly-v033-e6701a77-8e6b26fd-7e69cb73

NETLOAD only:
Codex.AutoCAD.Host.2016.dll

Host SHA-256:
E6701A771D17EC3EC8B2CA7DA78B553E27897639DC48B3BC0435F07249C9B5F6

AgentHost:
AgentHost\Codex.AutoCAD.AgentHost.exe

AgentHost SHA-256:
8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A

Manifest SHA-256:
49E6DAE400DDC25DDBB538FD1CD36858325A9A866114E43A81B14AEE48A2E2CE
```

Host `E6701A77…` 与 2026-07-22 冻结的 v033 候选逐字节一致，R20.1/net45 构建可复现。
AgentHost 哈希随 .NET 8 SDK 版本变化（`7A3ABCEA` 初版 → `4B602965` 集成冻结 →
`8E6B26FD` 当前，与 M2/M3 候选一致），属可解释的 framework-dependent 发布差异，
不是篡改信号；实机 evidence 必须绑定本节当前哈希。

只对 `Codex.AutoCAD.Host.2016.dll` 执行 `NETLOAD`。不要 NETLOAD 其他依赖 DLL，也不要
直接运行 AgentHost；其余根目录 DLL、`AgentHost` 子目录和 `.sha256` sidecar 必须保持原
布局。

如果当前 AutoCAD 进程曾加载 `0.3.2.0` 或其他 Host，先正常关闭并新开一个 AutoCAD
2016 进程；.NET Framework 程序集不能在同一进程中可靠卸载后替换。

## 2. 测试纪律

- 只使用空白图或脱敏测试图，不修改生产图。
- 每个小节记录开始和结束 `DBMOD`；数值可以不是 0，但插件操作不应改变它。
- 不把完整 canonical JSON、图纸路径、图名、Handle、选择/上下文哈希、TRUSTEDPATHS、
  用户名、许可证、API Key、token 或完整环境变量发到聊天或写入 Git。
- 不为制造故障而删除候选文件、修改注册表或强杀生产会话。
- CAD 写入和插件保存必须始终显示 disabled。

## 3. 加载、诊断和 Palette

在干净 AutoCAD 2016 进程中执行：

```text
DBMOD
NETLOAD
DBMOD
CODEXCADDOCTOR
CODEXCAD
CODEX16PAL
CODEX16PALINFO
DBMOD
```

检查：

- `Module version: 0.3.3.0`；若不是，立即停止。
- `CadContextJson: codex.autocad.cad-context/2`。
- R20.1 / managed `20.1.0.0` / x64。
- CAD write、plugin-initiated save 均 disabled，SAVETIME 未修改。
- Palette 出现四个按钮：新建对话、清除全部、取消回合、发送给 Codex。
- 各次 `DBMOD` 不因插件命令改变。

## 4. 建立精确候选的基础上下文和对话

预选一个已知可读取的 Line，预选后不要插入其他命令：

```text
DBMOD
CODEX16CTX
CODEX16CTXINFO
CODEX16AGENTSTART
```

等待 Palette 显示 AgentHost 在线，然后发送两轮短问题。第一轮要求记住一个由你现场临时
选择的 8 位数字并在末尾写 `M1-A`；第二轮要求复述该数字并写 `M1-B`。

预期：

- v2 上下文发布成功，`DBMOD before` 与 `DBMOD after` 相同。
- 两轮均返回，第二轮能记住第一轮数字。
- 流式显示正常，没有 CAD 写入或自动保存。

## 5. 新建对话：保留 CAD 上下文，隔离旧聊天

保持上述上下文已发布，执行：

```text
CODEX16NEWCHAT
CODEX16CTXINFO
CODEX16PALINFO
DBMOD
```

也可以点击 Palette 的“新建对话”。随后询问“上一轮让我记住的 8 位数字是什么；不知道就
明确回答不知道，并写 `M1-C`”。

预期：

- Palette 旧回答立即清空，并显示新的只读 Codex 对话已建立。
- `Published=true`、选择数量和 v2 上下文保持不变。
- 新对话不应可靠复述旧 8 位数字；若偶然猜中，换一个随机数字重做一次。
- `DBMOD` 不变。

## 6. 只清 CAD 上下文：保留当前聊天

在当前新对话中先让 Codex 记住另一个现场随机 8 位数字。然后执行：

```text
CODEX16CTXCLEAR
CODEX16CTXINFO
CODEX16PALINFO
DBMOD
```

预期：

- `Status: cleared-user-command`、`Published: false`、`Selected count: 0`。
- 回答文本和当前 Codex 对话不被清除。
- 未重新捕获时实际提交 `CODEX16ASK` 必须提示先执行 `CODEX16CTX`。

在同一图纸重新预选 Line 并执行 `CODEX16CTX`，再询问刚才的 8 位数字。预期当前聊天仍能
记住它，证明“清 CAD 上下文”不等于“新建对话”。

## 7. 清除全部：CAD 上下文、回答和对话一起清除

执行：

```text
CODEX16CLEARALL
CODEX16CTXINFO
CODEX16PALINFO
DBMOD
```

也可以点击 Palette 的“清除全部”。

预期：

- CAD 上下文未发布、选择数为 0、JSON 字节为 0。
- Palette 回答区为空。
- 状态说明下一次提问将建立新对话。
- 重新捕获同一 Line 后询问上一节的 8 位数字，新对话不应可靠复述。
- `DBMOD` 不变。

## 8. 图 A / 图 B 对话隔离

在图 A 捕获安全对象，让 Codex 记住第三个随机 8 位数字并取得回答。正常切换到图 B，立即
执行：

```text
CODEX16CTXINFO
CODEX16PALINFO
DBMOD
```

预期：

- `Status: cleared-document-activated`、`Published: false`。
- 图 A 的可见回答立即清空。
- Palette 提示旧对话已隔离，下一次提问将建立新对话。

在图 B 不重新捕获，执行 `CODEX16ASK` 并实际提交问题。预期 Host fail-closed，要求重新
执行 `CODEX16CTX`，不得显示图 A 回答。

然后在图 B 捕获一个对象并询问图 A 的随机数字。预期图 B 新对话不应可靠复述。再切回图 A
时也应重新建立对话，不恢复先前图 A thread。

## 9. 取消、重复取消和 busy

提交一个足够长、能观察到流式输出的问题，在回答尚未结束时立即执行两次：

```text
CODEX16CANCEL
CODEX16CANCEL
```

也可连续点击“取消回合”。若回答已经自然完成，本轮不算取消测试，重新提交较长问题。

预期：

- 取消请求幂等，只得到一个稳定取消终态。
- 重复取消不使状态回到 running，不产生重复回答。
- 取消后可以正常提交新问题。

另起一个活动回合，在尚未完成时分别尝试“新建对话”和“清除全部”。预期均显示结构化
`error_code=busy` 或等价受限状态，不清空或替换正在运行的回合；随后用取消结束该回合。

## 10. Palette Reset、停止和重新启动

先发布一个 v2 上下文，再执行：

```text
CODEX16PALRESET
CODEX16PALINFO
CODEX16CTXINFO
DBMOD
```

预期 Palette 实例重建，但已发布上下文仍保留，回答/状态布局正常，`DBMOD` 不变。

然后执行：

```text
CODEX16AGENTSTOP
CODEX16AGENTSTOP
CODEX16PALINFO
CODEX16AGENTSTART
CODEX16PALINFO
DBMOD
```

预期重复 STOP 幂等，停止后明确显示离线/已停止，重新启动后可再次提问且不修改图纸。

## 11. AutoCAD 正常退出清理

本节必须使用另一个干净 AutoCAD 进程：

1. 加载同一精确候选，打开 Palette，捕获一个上下文并启动 AgentHost。
2. 不执行 `CODEX16AGENTSTOP`。
3. 通过 AutoCAD 正常界面退出。
4. AutoCAD 完全关闭后，只读检查 `Codex.AutoCAD.AgentHost.exe` 及其 Codex app-server
   子进程残留数。

预期约 30 秒内正常退出，无卡死或崩溃，相关残留为 0，插件没有保存 DWG。

## 12. 125% 与 150% DPI

分别由用户在 Windows 显示设置中切换到 125% 和 150%，每个缩放使用新的 AutoCAD
进程，重复：

```text
DBMOD
NETLOAD
CODEX16PAL
CODEX16PALINFO
CODEX16PALRESET
CODEX16PALINFO
DBMOD
```

检查停靠、浮动、隐藏重开、四按钮 2×2 布局、中文输入、换行、摘要、JSON 和回答区均不
重叠、不裁切。测试后由用户恢复原显示缩放；插件不得修改注册表或显示设置。

## 13. 启动失败、断线和超时观察

当前候选已通过自动化故障回归，但真实环境故障只在自然发生或已有受控注入条件时记录。
不要通过删除文件、破坏安装或强杀生产会话制造故障。

若自然出现 AgentHost 启动失败、Bridge 断线、Codex 异常退出或 10 分钟回合超时，检查：

- 显示稳定的 `error_code`、`error_stage`、request_id 和终态，不泄露原始路径或异常。
- 当前回合结束，后续 ASK fail-closed，不重复发送。
- AutoCAD 仍可操作，CAD 写入和保存仍禁用。
- STOP/正常退出后无 AgentHost 或 Codex 残留。

## 14. 反馈格式

只反馈每节“通过 / 失败 / 跳过”，以及：

- `Module version` 是否为 `0.3.3.0`。
- Host/AgentHost 哈希是否匹配。
- DBMOD 是否保持不变。
- 失败时的受限 `error_code`、`error_stage` 和 Palette 状态。
- 正常退出后的相关进程数量。

不要发送完整 canonical JSON、真实图纸路径、图名、Handle、选择/上下文哈希或敏感配置。

## 15. 实机结果记录

本节只记录用户在真实 AutoCAD 2016 上亲自执行并回报的结果。自动化门禁的结论不写进本节，
也不得据此把任何一节标为通过。

### 2026-07-26 冒烟（第 3、4 节，部分项）

用户加载当前候选后回报：

| 检查项 | 结果 |
| --- | --- |
| `Module version` = `0.3.3.0` | 通过 |
| CAD write / plugin-initiated save 显示 disabled | 通过 |
| `DBMOD` 前后未变化 | 通过 |
| 两轮问答正常返回 | 通过 |

结论：候选可在真机加载并完成一次只读问答闭环，且未改动图纸修改标志。

同两节中尚未单独回报、因此仍未验证的项：`CadContextJson: codex.autocad.cad-context/2`、
R20.1 / managed `20.1.0.0` / x64 三项标识、Palette 四按钮布局、第二轮是否真的复述出第一轮
的 8 位数字（记忆连续性）。

### 2026-07-26 第 5–9 节

用户在同一个 AutoCAD 进程中连续执行第 5 节（新建对话）、第 6 节（只清 CAD 上下文）、
第 7 节（清除全部）、第 8 节（图 A/图 B 对话隔离）、第 9 节（取消、重复取消与 busy），
回报全部通过。

这意味着三种清除语义的区分、按图隔离和取消幂等在真机上成立。**回报为整体通过，未逐项
拆分**；因此各节内部的细目（例如新对话是否可靠地复述不出旧的 8 位数字、`error_code=busy`
的具体取值、`Status: cleared-document-activated` 字面值）没有单独确认。若后续要把这些作为
逐条 evidence 引用，需要重新逐项回报。

### 2026-07-26 第 10–12 节

用户执行第 10 节（Palette Reset、重复 STOP 与重新启动）、第 11 节（不执行 STOP 直接正常
退出 AutoCAD 后的残留清理，使用另一个干净进程）、第 12 节（125% 与 150% DPI，各用新
进程），回报全部通过。

与前一轮同样为整体回报，未逐项拆分；因此「正常退出后 AgentHost 与 Codex 残留数为 0」
这一具体计数、以及各 DPI 下四按钮 2×2 布局的逐项目视检查结果，没有单独的数值记录。

### M1 实机验收状态

第 3–12 节全部由用户在真实 AutoCAD 2016 上执行并回报通过，**M1 实机验收完成**。
第 13 节按设计只在故障自然发生时记录，不构成阻塞项。

因此 M1 只剩最后一件事：把 `codex/m1-integration` 受控吸收进 `main`。该操作需要用户
明确授权，本项目约束禁止未经授权执行 merge。

# Kimi Palette UI 第二阶段交接（视觉与交互收敛）

任务书：`KIMI_PALETTE_UI_REFINEMENT_TASK.md`（2026-07-24）+ 用户补充的 M8 范围说明。
工作树：`C:\tmp\CodexForAutoCAD-kimi-palette-ui`，分支 `codex/kimi-palette-ui`
UI 基线：`4f7c32aa4c11d8766f5f5bd737679f0aafa1a54b`（已验证 2efd40b 为 HEAD 祖先）

## 1. 视觉决策（用户指定方向）

整体为 VS Code Codex 侧边插件式**紧凑深色聊天工作台**，自上而下：

1. **会话栏**（30 DIP）：状态点 + Agent 状态文本（单行省略，ToolTip 全文）+
   启动 / 停止 / 新建会话；“切换会话”按钮**禁用**并标注当前仅一个进程内会话（M7 未开放）。
2. **只读边界微文案**：一行低对比灰字“只读 · CAD 写入禁用 · 不会自动保存 DWG”，
   融入层级不喧宾夺主。
3. **紧凑 CAD 上下文条**：选择行（计数/完整性 + 清除选择上下文 + 清除全部）、
   索引行（状态 + 开始/取消 + 详情）、元信息行（会话/长期记忆四语义分离标注）。
   “详情”展开选择摘要、索引统计、原始状态与范围单选；Canonical JSON 移入底部诊断区。
4. **消息列表**（独占伸缩行）：用户（蓝底“你”）/ 助手（“Codex”，流式时“Codex…”）/
   状态（灰）/ 错误（红）四类消息；有界渲染（500 条上限裁剪，streaming 项受保护）；
   用户向上阅读不强制滚底，脱离底部时出现“回到最新”。
5. **固定输入区**：多行输入（Enter 发送 / Shift+Enter 换行 / IME 组字 Enter 不发送）、
   模型与思考强度**禁用**并显示“使用 Codex 默认值”（后端无 capability，不伪造）、
   取消回合 / 发送（蓝色主操作）。
6. **诊断 Expander**（默认收起）：匿名指标 + Canonical JSON + 复制。

颜色：底 #1E1E1E、面 #252526、正文 #D4D4D4、次级 #9D9D9D、主操作 #0E639C；
状态点绿 #57A64A / 琥珀 #D7A000 / 红 #F14C4C，且状态**必同时有文字**。
圆角 4 DIP；按钮 30 DIP 高；不按宽度缩放字体。

## 2. Presentation State 与调用链

```text
MvpAgentClient（唯一状态机）
  └─ 新增只读 AgentClientSnapshot（connection/turn/conversationEpoch，无 Codex ID）
       ├─ SnapshotChanged 事件 → MvpAgentRuntime → UnifiedPaletteRuntime
       └─ GetSnapshot()（离线/停止后回退 Offline）
UnifiedPaletteController
  ├─ PaletteConversationStore（Conversation → Messages → Items，500 条有界）
  │    ├─ TextChanged("") → NoteStreamReset（惰性开流，杜绝幽灵空气泡）
  │    ├─ TextChanged(delta) → AppendAssistantDelta（终态后迟到增量拒绝并计数）
  │    ├─ 终态/错误级状态行 → AddStatus/AddError（只收 Host 冻结格式前缀）
  │    └─ EnsureEpoch(conversationEpoch) → 会话切换/清除即重置（迟到事件不污染新会话）
  ├─ drafts[epoch]：每会话进程内草稿；Palette Reset 只重建 Panel，状态全保留
  └─ UpdatePanel() → Panel：状态、快照、消息快照、草稿
UnifiedPalettePanel（只渲染 + 发命令）
  ├─ 按钮可用性 = PaletteCommandAvailability.FromSnapshot（无控件本地状态机）
  └─ 消息刷新 40 ms 节流合并（PaletteDeltaCoalescer 同源窗口常量）
```

索引：开始/取消后 `UnifiedPaletteRuntime.RefreshDrawingIndexView()` 重新拉取真实
descriptor 重推视图，按钮永远从 descriptor 派生——取消失败可立即重试。

## 3. 缺陷修复对照（M8.1）

| 缺陷 | 修复 |
|---|---|
| Send 过早恢复 | `AskAsync` 语义为"Provider 已接受"；Send 改由快照回合态派生，starting_provider/running/cancelling 期间禁止，终态或稳定离线恢复 |
| 等待期新草稿被清除 | `PaletteDraftGuard.ShouldClearAfterSend`：仅当输入仍等于已提交文本才清空 |
| 剪贴板捕获过窄 | 全异常映射到固定脱敏提示（`PaletteClipboardFeedback`），不显示异常内容，可重试 |
| 索引取消失败不可重试 | 不预判禁用；finally 重新拉取 descriptor；scanning 即保持可取消 |
| 空索引判为未建立 | `Established`（Ready/Partial/Limited）与 `HasIndex` 分离；0/0 Ready 显示"已建立：0 / 0"，null descriptor 才是"未建立" |
| 敏感信息进 UI | 复用 Host 冻结 sanitizer；上下文哈希行剔除；JSON 移入诊断区；面板异常只显示类型名 |
| 候选缺 AgentHost 错误 | 后端已有脱敏错误（"MVP AgentHost 包不完整"），经错误消息条呈现 |

## 4. 修改文件

首选范围内：

- `UnifiedPalettePanel.cs`（重写为深色工作台，约 1050 行）
- `UnifiedPaletteController.cs`（快照缓存、消息存储、草稿、状态归类）
- `UnifiedPaletteRuntime.cs`（快照/草稿/用户消息/错误转发、索引视图刷新）
- `PalettePresentationModels.cs`（+命令可用性矩阵、草稿保护、剪贴板反馈、delta 合并器、
  会话消息存储、模型 capability 视图与选择门禁、布局常量；索引视图 Established）
- `tests/.../Mvp.Specs/Program.cs`（+10 条规格）

越界（理由已述）：

- `MvpAgentClient.cs`：**只读** `AgentClientSnapshot` + `SnapshotChanged` + `GetSnapshot()`。
  理由：Send/Cancel 必须派生自真实状态机；状态字符串面向人类不能当协议解析。
  未改任何状态迁移、wire contract 或生命周期语义，仅新增观察者出口。
- `MvpAgentRuntime.cs`：订阅/退订并转发快照事件；停止后发布 Offline 快照。

未修改：Contracts/Bridge/AgentHost/AppServer/CadContext/DrawingIndex/CadQuery、
Host.2025、任何协议与进程控制路径。未新增 NuGet/网络/WebView 依赖。

## 5. 自动化测试

新增 10 条规格（Mvp.Specs，56 → 66）：

```text
HOST2016_PALETTE_SEND_MATRIX_FOLLOWS_REAL_SNAPSHOT   离线/启动中/停止中/在线空闲/三种进行态/三种终态/ null
HOST2016_PALETTE_DRAFT_SURVIVES_IN_FLIGHT_TYPING     等待期输入不清空（IME/粘贴等价路径）
HOST2016_PALETTE_CLIPBOARD_FAILURE_IS_BOUNDED        COM/InvalidOperation/ThreadState/UnauthorizedAccess/通用异常
HOST2016_PALETTE_INDEX_CANCEL_RETRY_STATE            scanning 可取消、失败后可重试、cancelled 可重启
HOST2016_PALETTE_EMPTY_INDEX_IS_ESTABLISHED          0/0 Ready=已建立；null=未建立；scanning 未建立
HOST2016_PALETTE_MODEL_GATE_CLOSED_WITHOUT_CAPABILITY 禁用+默认文案+任意字符串/空白名单拒绝+白名单精确放行
HOST2016_PALETTE_DELTA_COALESCER_LIMITS_FLUSHES      100 delta/40ms 只刷一次、边界精确
HOST2016_PALETTE_STORE_REJECTS_LATE_DELTAS           终态后/重置后迟到增量拒绝计数；epoch 切换清空
HOST2016_PALETTE_STORE_BOUNDED_AND_ORDERED           600 消息裁到 500、顺序单调、streaming 存活
HOST2016_PALETTE_LAYOUT_POLICY_FITS_NARROW_WIDTH     300/520 DIP 与控件最小尺寸常量审查
```

真实运行结果（全部本次实跑）：

```text
dotnet run --project tests/Codex.AutoCAD.Host.2016.Mvp.Specs -c Release
→ 66/66 specs passed

Host.2016 Release/net45/x64（D:\AutoCAD 2016 R20.1 原版程序集，流程同
verify-autocad2016-context-v2-candidate.ps1）：
→ 0 警告 / 0 错误；输出仅 5 个 Codex.*.dll，Autodesk DLL = 0
产物：artifacts/kimi-palette-refinement-build/Codex.AutoCAD.Host.2016.dll
SHA-256：7e5ff9707cb379c0d357c16df96d9982d0ecb3496a2521170a53843faae4c811
Module version：0.4.2.0（未提升）

scripts/verify-phase2.ps1 -Configuration Release（UTF-8 控制台）：
→ 托管核心 Release 构建 0 警告 0 错误；9 个规格项目动态汇总 342/342
  （Contracts 87、Ipc 35、Security 19、AppServer 20、Bridge 44、
   Bridge.Client 29、AgentRuntime 33、Chat 9、Host.2016.Mvp 66）；
  Host 禁用 API 词法扫描通过；AgentHost doctor 活体握手 ok=true；
  git diff（未暂存/已暂存）--check 通过；敏感信息基础扫描通过。

git diff --check → 通过；新增代码禁用 API/敏感信息/假数据扫描 → 0 命中
```

## 6. 尚缺的后端能力（UI 已按禁用态落地）

- **模型/思考强度 capability**（M4.1→Contracts→Bridge→AgentHost 全链缺失）：
  选择器禁用显示"使用 Codex 默认值"；`PaletteModelSelectionGate` 已就绪，
  后端开放后接白名单即可，任意字符串当前一律拒绝。
- **会话集合与切换**（M7）：后端仅单进程内会话（conversationEpoch 为真实边界）；
  切换入口禁用标注；草稿按 epoch 隔离，恢复历史属 M7 SQLite，未伪造。
- **长期记忆**（M7）：上下文条标注"未提供"。

## 7. 实机测试清单（用户执行）

1. 干净 AutoCAD 2016 NETLOAD 上述 DLL（核对 SHA-256 与 0.4.2.0）→ CODEX16PAL /
   重复打开 / CODEX16PALRESET：深色工作台出现；Reset 后消息与草稿保留。
2. 300 / 360 / 520 DIP 与停靠/浮动/隐藏重开：会话栏、上下文条、按钮不裁切不重叠；
   100% / 125% / 150% DPI 与多显示器切换。
3. 中文 IME 组字 Enter 不发送、Enter 发送、Shift+Enter 换行；流式期间继续输入的
   草稿在回合结束后保留；发送后原样文本被清空。
4. Agent 离线（发送自动启动）→ 启动中（Send 禁用）→ 在线 → 流式（Send 禁用、
   可取消）→ 完成 / 取消 / 失败（Send 恢复）；断线后错误条脱敏；停止后全禁用。
5. 无选择/完整选择/含不支持对象；清除选择上下文（对话与索引不变）与清除全部。
6. 空图扫描：显示"已建立：0 / 0、100%、完整"；普通图扫描中取消、取消失败重试、
   完成 / 部分 / 受限 / 失效状态切换。
7. 长会话：快速多轮问答，列表滚动向上停留不跳底，"回到最新"出现并工作。
8. 全流程 DBMOD 仅因用户图纸操作变化；插件不保存 DWG。

## 8. 未完成项与风险

- M8.7（写入审批）、M8.9（设置页）不在本阶段；M8.12 冻结需实机矩阵后评估。
- 消息列表为有界裁剪（500 条）而非真虚拟化；满足"虚拟化或等价有界渲染"。
- 会话栏"切换会话"与模型/思考强度为显式禁用态，非功能缺失隐瞒。
- 快照事件从状态机各迁移点手工插入；若有新增迁移路径需同步通知（编译期无法强制）。
- AutoCAD 实机未运行（按任务边界）；IME、DPI、多显示器结论待用户验证。

## 9. 提交

待填。

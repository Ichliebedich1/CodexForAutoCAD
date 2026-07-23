# Kimi 任务：AutoCAD 2016 Palette UI 第一阶段

最后更新：2026-07-23（北京时间）

## 1. 开始前先确认

你只在以下工作树和分支工作：

```text
Worktree: C:\tmp\CodexForAutoCAD-kimi-palette-ui
Branch: codex/kimi-palette-ui
Required starting commit: 47c8faa8dd9eaf74747f15aff7a34edced7b4ce0
```

先执行并记录：

```powershell
git status --short --branch
git rev-parse HEAD
```

如果分支、提交或工作树不符，立即停止，不要自行 reset、rebase、merge、删除或覆盖文件。
不要触碰主工作区，也不要修改其他 worktree。

## 2. 项目现状

这是 AutoCAD 2016 R20.1 x64 的进程内 WPF Palette，目标框架是 .NET Framework 4.5。
当前真实 UI 位于：

- `src/Codex.AutoCAD.Host.2016/UnifiedPalettePanel.cs`
- `src/Codex.AutoCAD.Host.2016/UnifiedPaletteController.cs`
- `src/Codex.AutoCAD.Host.2016/UnifiedPaletteRuntime.cs`

真实动作由以下运行时提供，不得复制第二套状态或伪造结果：

- `MvpAgentRuntime.StartAsync()`
- `MvpAgentRuntime.StopAsync()`
- `MvpAgentRuntime.AskAsync(...)`
- `MvpAgentRuntime.CancelAsync()`
- `MvpAgentRuntime.NewConversationAsync()`
- `MvpAgentRuntime.ClearAll()`
- `UnifiedReadOnlyContextRuntime.Clear(...)`
- `DrawingIndexRuntime.Start(...)`
- `DrawingIndexRuntime.Cancel()`

当前已经存在 CadContextJson v2、DrawingIndex/CadQuery、流式 Codex 回答、取消、Agent 启停、
新建对话和清理动作。UI 只能消费 Host 已经整理好的状态，不得解析 Codex 原始 JSON、IPC 帧、
Provider payload 或审计 JSONL。

`handoff/autocad2016/MVP_PUBLIC_CONTRACT_V1.md` 是形成过程中的历史契约说明；当前实现已经使用
CadContextJson v2 和 DrawingIndex。不得把历史 v1 限额或字段重新写回当前 UI。

## 3. 本任务的交付目标

完成一个真实接入现有调用链的 Palette UI 纵切，使普通用户无需理解 thread、JSON、AgentHost
或实体内部类型，也能看清以下三种不同状态：

1. 当前对话：Agent 是否可用、回答内容、正在运行/取消/失败/完成状态、输入与发送动作。
2. 当前选择：是否已捕获、总数、已解析数、不支持/受限数、完整性、可读摘要与 canonical JSON。
3. 整张图纸：DrawingIndex 是否未建立、扫描中、已完成、部分完成、取消或失效；只显示真实
   状态，不伪造进度百分比。

这只是 M8 UI 的第一阶段，不实现设置页、长期记忆、CAD 写入审批界面或完整产品皮肤。

## 4. 必须实现的界面

### 4.1 整体布局

- 保持安静、紧凑、工作型界面，不做落地页、Hero、渐变背景、装饰光球或大面积卡片。
- 最小宽度约 `320` DIP、默认宽度约 `520` DIP；窄侧栏下所有中文文字、按钮和状态不得重叠。
- 使用清晰的三块信息架构：`对话`、`当前选择`、`整图索引`。可以使用主 TabControl，也可以
  使用等价但更清晰的紧凑导航；不要把三类状态混成一段诊断文本。
- 调试指标、Palette generation、DPI、事件计数等移入默认收起的“诊断”区域，不占据主流程。
- 保留明确、常驻的安全边界：`只读`、`CAD 写入禁用`、`不会自动保存 DWG`。表达简洁，不能
  暗示当前已具备写入能力。

### 4.2 对话区

- 显示 Host 已提供的真实 Agent 状态和流式回答文本。
- 提供真实接线的：启动、停止、新建对话、取消当前回合、发送问题。
- 输入框支持中文输入和多行文字；`Enter` 发送、`Shift+Enter` 换行，发送空白内容必须拒绝。
- 发送或启动/停止进行中必须防止重复触发；异步处理不得阻塞 AutoCAD UI 线程。
- 发送失败、超时、取消和断线使用现有 `MvpAgentFailureFormatter`/运行时状态，不显示堆栈、
  本地路径、token、环境变量或原始异常正文。
- 回答区内容变化不能导致整个 Palette 跳动或改变输入区稳定尺寸。

### 4.3 当前选择区

- 显示 `Published`、`SelectedCount`、`ParsedEntityCount`、`UnsupportedEntityCount`、`Complete`
  和 `ReadIssueSummary` 的用户可读结果。
- 不支持、超限和读取失败对象必须以现有脱敏统计显示，不能静默隐藏，也不能显示 Handle、
  图纸路径或选择/context 哈希。
- 可读摘要作为默认视图；canonical JSON 放在次级 Tab，并提供明确的复制动作。
- 增加“清除 CAD 上下文”动作，只调用现有清理入口；它不得清除整图索引或当前对话。
- “清除全部”必须继续调用 `MvpAgentRuntime.ClearAll()`，并与“清除 CAD 上下文”有明显区别。

### 4.4 整图索引区

- 显示现有 `DrawingIndexRuntime`/controller 传入的真实状态和统计。
- 可以增加真实接线的扫描开始与取消动作，但必须使用现有合法 scope 和运行时入口。
- 当前 controller 只有字符串状态时，先完整、可读地展示该真实状态；禁止通过字符串猜测协议
  状态、编造百分比或另建第二套扫描状态机。
- 如果为了 UI 需要结构化展示，可在 Host.2016 内增加最小、只读的 presentation model，并从
  `DrawingIndexRuntime.GetDescriptor()`/现有快照生成；不得修改 wire contract。

## 5. 视觉和可用性要求

- 使用中性灰白背景、深色正文、蓝色主操作，并用绿色/琥珀/红色表达成功、等待和失败；不要
  做成单一蓝色或紫色主题。
- 控件圆角不超过 `6px`；不要在卡片内再套卡片。
- 命令按钮允许图标加文字；仓库没有现成图标库时不要新增网络依赖、emoji、手绘 SVG 或字体
  图标包。清晰的文字命令优先。
- 操作控件至少约 `32` DIP 高，状态/正文/诊断形成明确字号层级；紧凑面板内不要使用 Hero 字号。
- 所有布局使用稳定的 Grid 行列、Min/Max 尺寸和滚动边界；动态文本不能挤压或遮挡后续控件。
- 支持 100%、125%、150% DPI 的布局逻辑；本任务可以先完成代码和离线构建，不能把未进行的
  AutoCAD 实机 DPI 检查写成已通过。
- 键盘焦点顺序合理，按钮有可读 ToolTip，颜色不是唯一的状态表达方式。

## 6. 硬性技术约束

- 保持 .NET Framework 4.5、x64、AutoCAD R20.1 和当前 programmatic WPF 架构。
- 不新增 NuGet、npm、WebView、浏览器、React、WinUI、Avalonia 或联网依赖。
- 不启动、关闭或控制 AutoCAD；不发送 CAD 命令。实机 UI 验收由用户之后单独完成。
- 不修改 CadContext、DrawingIndex、CadQuery、IPC、Bridge、AgentHost 或 AppServer 协议。
- 不修改 `src/Codex.AutoCAD.Host.2025`。
- 不启用 CAD 写入，不添加 AutoCAD 命令字符串、LISP、脚本、反射调用或自动保存。
- 不显示或记录真实图纸路径、图名、API key、token、完整环境变量、selection/context hash。
- 不让 UI 直接控制 Codex 子进程；只能调用现有 Host 运行时入口。
- 不增加假进度、假聊天数据、静态演示响应或第二套独立任务/连接状态。
- 不进行与 UI 纵切无关的重构。

## 7. 允许修改的范围

首选修改：

- `src/Codex.AutoCAD.Host.2016/UnifiedPalettePanel.cs`
- `src/Codex.AutoCAD.Host.2016/UnifiedPaletteController.cs`
- `src/Codex.AutoCAD.Host.2016/UnifiedPaletteRuntime.cs`

仅在确有必要时：

- 新增少量 Host.2016 presentation-only 类型。
- 更新 `src/Codex.AutoCAD.Host.2016/Codex.AutoCAD.Host.2016.csproj` 的显式 Compile 列表。
- 增加针对纯 presentation model/状态映射的自动化规格。
- 新增交接文档 `handoff/autocad2016/KIMI_PALETTE_UI_HANDOFF.md`。

除上述范围外的文件默认禁止修改。确需越界时，先在交接文档中说明理由，保持最小变更。

## 8. 测试与验收

至少完成：

1. `git diff --check`。
2. 使用目标机原版 `D:\AutoCAD 2016` R20.1 托管程序集完成 Host.2016 Release/x64 编译；禁止
   将 Autodesk DLL 复制进输出目录。
3. 运行受影响的 Host MVP/源码门禁；如果增加纯 presentation model 规格，net45/net8 适用边界
   必须明确。
4. 运行 `scripts/verify-phase2.ps1 -Configuration Release`，并记录真实通过数。
5. 搜索确认没有 CAD 保存 API、写入 API、命令字符串、真实路径、token 或调试假数据进入新增代码。

如果某项因环境原因不能运行，必须明确写“未验证”，不能用源码检查代替实机结论。

需要为后续用户实机测试提供一份简洁清单，至少覆盖：

- 320/520 DIP 左右宽度下的停靠、浮动、隐藏重开和 Palette Reset。
- 中文多行输入、Enter/Shift+Enter、连续发送与取消。
- Agent 离线、连接中、在线、回答中、完成、取消、失败和停止。
- 无选择、完整选择、不支持/受限选择、清除上下文和清除全部。
- 整图索引未建立、扫描、取消、完成、部分/失效状态。
- 100%、125%、150% DPI；文字、按钮、输入区和滚动区无重叠裁切。
- 全流程 DBMOD 只因用户自己的图纸操作变化，插件 UI 不保存 DWG。

## 9. 交付格式

完成后：

1. 审阅完整 diff，删除临时诊断和未使用代码。
2. 更新 `handoff/autocad2016/KIMI_PALETTE_UI_HANDOFF.md`，写明：实现内容、修改文件、真实测试
   输出、未验证边界、实机步骤、提交 ID。
3. 创建一个独立提交，建议提交信息：

```text
feat(host2016): redesign readonly palette workflow
```

4. 不要 merge、push、删除 worktree、生成正式候选或宣称 M8 完成。

## 10. 明确不做

- 不做 Provider 抽象、Direct API、自研 Agent Loop。
- 不做设置页、SQLite 长期记忆、审计查看器或日志导出。
- 不做 CAD 写入计划、预览、审批和执行 UI；这些要等 M5 安全写入闭环完成。
- 不做 AutoCAD 2025 UI。
- 不以截图好看代替真实调用链、编译和状态验收。

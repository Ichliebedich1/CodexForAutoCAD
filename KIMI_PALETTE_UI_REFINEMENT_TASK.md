# Kimi 任务：AutoCAD 2016 Palette UI 第二阶段视觉与交互收敛

最后更新：2026-07-24（北京时间）

## 1. 工作位置与基线

只在以下工作树和分支工作：

```text
Worktree: C:\tmp\CodexForAutoCAD-kimi-palette-ui
Branch: codex/kimi-palette-ui
Required UI baseline: 4f7c32aa4c11d8766f5f5bd737679f0aafa1a54b
First UI implementation: 2efd40b98b2aa731d8d95ac56a08d5f414c30edf
```

开始前执行：

```powershell
git status --short --branch
git rev-parse HEAD
git merge-base --is-ancestor 2efd40b98b2aa731d8d95ac56a08d5f414c30edf HEAD
```

`.workbuddy/` 是本地工具目录，不得提交、删除或改名。不要 reset、rebase、merge、push，
不要触碰主工作区或其他 worktree。

## 2. 用户提供视觉方向

用户会在对话中直接告诉你希望的视觉效果、参考产品、颜色、密度和布局偏好。视觉设计以用户
最新说明为准。本任务书不替用户发明审美方向，但以下技术、安全和交互约束始终有效。

收到视觉要求后，先用简短文字复述以下内容再实施：

1. 页面信息层级和主要操作位置。
2. 颜色、间距、字体层级和控件风格。
3. 300 DIP 窄侧栏和 520 DIP 常用宽度下如何响应。
4. 哪些现有功能保持原位，哪些只改变视觉呈现。

如用户只描述观感而没有指定每个像素，按其目标做一致的专业设计，不要反复追问细枝末节。

## 3. 已有实机证据

用户已在 AutoCAD 2016 x64、R20.1、100% DPI 中加载当前 UI 分支产物：

```text
Module version: 0.4.2.0
Host target: .NET Framework 4.5
AcMgd / AcDbMgd: 20.1.0.0
Palette open: passed
Repeated CODEX16PAL: passed
CODEX16PALRESET: passed
Generation: 1 -> 2
Reset count: 0 -> 1
Release count: 0 -> 1
Observed DIP size after reset: 300 x 890
DBMOD: 21 -> 21
CAD write: disabled
Plugin save: disabled
```

这只证明加载、打开、重复打开、Reset 和只读边界正常。不得把中文 IME、Agent 流式状态、
整图索引、125%/150% DPI 或完整 UI 矩阵写成已通过。

## 4. 本阶段目标

在现有真实调用链上完成第二版正式工作面板：

- 视觉达到用户直接描述的效果。
- 保持“对话 / 当前选择 / 整图索引”三类信息清楚分离。
- 300 DIP 到 520 DIP 宽度均可用，动态文本不遮挡按钮或输入框。
- 常用工作流更直接，诊断信息继续默认收起。
- 所有按钮继续调用现有 Host 运行时入口，不增加演示数据或第二套状态机。
- 同时修复第 5 节列出的已知交互缺陷。

## 5. 必须修复的交互缺陷

### 5.1 发送按钮必须跟随真实回合终态

当前 `SendCurrentPrompt()` 在 `AskAsync()` 返回后立即重新启用 Send。必须确认
`AskAsync()` 的真实语义；如果它只代表 Provider 已接受请求，Send 不能在流式回合终态前恢复。

- Send 可用性由现有 Host request/turn/presentation 状态派生。
- running、waiting、cancelling 期间禁止重复发送。
- completed、failed、cancelled 或明确 offline 后才按状态矩阵恢复。
- 不得仅用一个局部布尔值伪造第二套 Agent 状态。

### 5.2 不得清除用户在等待期间输入的新草稿

当前成功路径无条件 `prompt.Clear()`。改为：

- 发送前记录已提交文本及输入版本。
- 仅当输入框仍等于该次已提交文本时清空。
- 用户在请求期间继续输入的新草稿必须保留。
- 覆盖中文 IME、粘贴、快速发送和迟到终态。

### 5.3 剪贴板失败必须有界且可重试

当前只捕获 `COMException`。应处理 WPF 剪贴板实际可能抛出的预期异常，并显示固定、脱敏、
可重试的提示。

- 不捕获或显示路径、堆栈、原始异常正文。
- 不使用无限重试，不阻塞 AutoCAD UI 线程。
- 不用空成功提示掩盖失败。

### 5.4 索引取消失败后必须可以再次操作

当前取消按钮先禁用，异常后可能一直禁用到下一次状态通知。

- 取消成功、失败、迟到状态和重复点击都必须依据真实 descriptor/presentation state 恢复。
- 失败后仍处于可取消状态时允许重试。
- 不得伪造扫描终态或百分比。

### 5.5 空图纸的有效索引不能被判为“无索引”

当前 `HasIndex` 只依赖非空 `IndexId`。检查协议语义，并确保零实体但成功完成的合法索引能显示：

- 已建立；
- `0 / 0`；
- 真实完成度和完整性；
- 可按真实状态重新扫描。

null descriptor 与合法空索引必须明确区分。

### 5.6 可见文本的隐私边界

主工作界面和复制反馈不得显示：

- 图纸真实路径或用户名；
- AutoCAD Handle；
- selection/context hash；
- token、完整环境变量或原始 stderr；
- 异常堆栈和内部 Provider 标识。

复用已有 sanitizer/格式化器。不要在 UI 内另写一套容易漏项的字符串替换器。Canonical JSON
若属于开发诊断能力，必须放在明确的次级诊断位置，并遵循当前产品的脱敏契约。

## 6. 视觉和布局硬约束

- 这是高频使用的 CAD 工作面板，不是营销页。
- 不使用 Hero、渐变背景、装饰光球、大面积卡片或卡片套卡片。
- 不新增 WebView、React、WinUI、Avalonia、网络资源、字体包或 NuGet 依赖。
- 使用现有 programmatic WPF、.NET Framework 4.5、x64、AutoCAD R20.1。
- 300 DIP 宽度是已观测实机边界；最长中文文字必须换行或合理省略，不能越界。
- 回答增长不能挤压输入区；工具栏和主要按钮应有稳定尺寸。
- 使用符号/图标时只能复用仓库现有资源；没有可靠图标资源时使用清楚的文字按钮。
- 颜色不能是唯一状态表达，状态必须同时有文字。
- 保留常驻只读边界，但应融入界面层级，不要喧宾夺主。
- 诊断指标默认折叠，普通用户不需要理解 AgentHost、thread、JSON 或内部类型。

## 7. 架构和安全边界

只能使用现有入口：

```text
MvpAgentRuntime.StartAsync
MvpAgentRuntime.StopAsync
MvpAgentRuntime.AskAsync
MvpAgentRuntime.CancelAsync
MvpAgentRuntime.NewConversationAsync
MvpAgentRuntime.ClearAll
UnifiedReadOnlyContextRuntime.Clear
DrawingIndexRuntime.Start
DrawingIndexRuntime.Cancel
```

禁止：

- UI 直接启动或停止 Codex/AgentHost 进程；
- 解析 Codex 原始 JSON、IPC 帧或审计 JSONL；
- 修改 Bridge、AgentHost、AppServer、CadContext、DrawingIndex 或 CadQuery wire contract；
- 启用 CAD 写入、保存、导出、LISP、脚本或任意 AutoCAD 命令字符串；
- 新增假进度、假聊天、静态演示状态或第二套任务状态；
- 修改 `src/Codex.AutoCAD.Host.2025`；
- 为了美化进行无关的大规模重构。

如果现有 Host 状态不足以正确驱动 Send/Cancel，可增加最小的只读 presentation state，并在交接
文档中说明数据来源；不得让控件拥有 Provider 或进程生命周期。

## 8. 建议修改范围

首选：

```text
src/Codex.AutoCAD.Host.2016/UnifiedPalettePanel.cs
src/Codex.AutoCAD.Host.2016/UnifiedPaletteController.cs
src/Codex.AutoCAD.Host.2016/UnifiedPaletteRuntime.cs
src/Codex.AutoCAD.Host.2016/PalettePresentationModels.cs
tests/Codex.AutoCAD.Host.2016.Mvp.Specs/Program.cs
```

仅在编译或测试需要时更新对应 `.csproj`。任何越界修改必须先说明理由。

## 9. 自动化与构建验收

至少完成：

1. 对 presentation state 和第 5 节每个缺陷增加 RED/GREEN 规格。
2. Host.2016 使用原版 `D:\AutoCAD 2016` R20.1 程序集完成 Release/net45/x64 构建。
3. Release 构建 0 warning / 0 error，输出目录 Autodesk DLL 为 0。
4. 运行受影响的 Host MVP Specs。
5. 运行 `scripts/verify-phase2.ps1 -Configuration Release`。
6. `git diff --check`。
7. 禁用 API 和敏感信息扫描通过。
8. 不启动、关闭或控制 AutoCAD；实机验收仍由用户执行。

测试必须覆盖：

- running 时 Send 不可用，唯一终态后正确恢复；
- 等待期间新输入草稿不被清除；
- 剪贴板失败提示并可再次复制；
- 索引取消失败后可重试；
- null descriptor 与完成的零实体索引；
- 未知状态保持原文且默认安全；
- 300/520 DIP 下关键控件的最小尺寸和布局约束可审查。

## 10. 实机交接清单

完成后给用户一份短清单，至少包括：

1. 干净 AutoCAD 2016 中加载精确 DLL，记录 SHA-256 和模块版本。
2. 300/349/520 DIP 左右宽度的停靠、浮动、隐藏重开和 Reset。
3. 中文 IME、Enter、Shift+Enter、发送期间继续输入草稿。
4. Agent 离线、启动、在线、流式、完成、取消、失败、停止。
5. 当前选择完整/不完整、清上下文、清全部。
6. 空图、普通图、扫描中取消、取消失败重试、完成和失效索引。
7. 100%/125%/150% DPI。
8. 全流程 DBMOD 不因插件变化，插件不保存 DWG。

## 11. 交付

1. 新增 `handoff/autocad2016/KIMI_PALETTE_UI_REFINEMENT_HANDOFF.md`。
2. 记录视觉决策、修改文件、真实测试数、构建结果、未验证边界、产物路径和 SHA-256。
3. 删除临时诊断与未使用代码。
4. 创建一个独立提交，建议：

```text
feat(host2016): refine palette visual workflow
```

5. 不 merge、不 push、不删除 worktree、不生成正式发布候选、不宣称 M8 完成。


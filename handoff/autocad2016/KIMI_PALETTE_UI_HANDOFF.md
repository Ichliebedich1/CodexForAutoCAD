# Kimi Palette UI 第一阶段交接

任务书：`KIMI_PALETTE_UI_TASK.md`（2026-07-23）
工作树：`C:\tmp\CodexForAutoCAD-kimi-palette-ui`，分支 `codex/kimi-palette-ui`
实现基线：`47c8faa8dd9eaf74747f15aff7a34edced7b4ce0`（已验证为 HEAD 祖先）

## 1. 实现内容

将 `UnifiedPalettePanel` 从单段诊断文本重构为三块信息架构的只读工作面板，全部消费
Host 已整理状态，动作只调用现有运行时入口：

- **整体布局**：主 `TabControl` 三个 Tab（对话 / 当前选择 / 整图索引）；顶部常驻安全边界
  横幅“只读 · CAD 写入禁用 · 不会自动保存 DWG”；调试指标（Palette generation、DPI、
  事件计数等）移入默认收起的“诊断”`Expander`。PaletteSet 最小 320 DIP、默认 520 DIP
  为既有配置，未改动。
- **对话 Tab**：状态行（状态点 + Host 原文状态文本）+ 流式回答区（固定输入区尺寸，回答
  区独占伸缩行并自动滚到底）+ 多行输入框（`Enter` 发送、`Shift+Enter` 换行、中文 IME
  组字期间的 Enter 不触发发送、空白拒绝）+ 启动 Agent / 停止 Agent / 新建对话 /
  取消回合 / 发送。发送与生命周期动作均有防重复触发（`sendInFlight` 与按钮禁用），
  异常经 `MvpAgentFailureFormatter` 脱敏后只进状态行。
- **当前选择 Tab**：状态行（已捕获 / 未捕获 / 捕获未完成）+ 计数与完整性
  （Selected/Parsed/Unsupported/Complete/CanonicalBytes + ReadIssueSummary）+
  “可读摘要”与“Canonical JSON”两个次级 Tab，JSON 带“复制 JSON”按钮与复制反馈；
  可读摘要剔除上下文 SHA-256 行。“清除 CAD 上下文”只调
  `UnifiedReadOnlyContextRuntime.Clear("palette-user-clear")`；“清除全部”继续调
  `MvpAgentRuntime.ClearAll()`，两个按钮文字与 ToolTip 明确区分。
- **整图索引 Tab**：状态行（未建立/准备中/扫描中/已完成/部分完成/受限完成/已取消/
  已失效/失败）+ 真实统计（范围、已索引/总数、descriptor 真实 ProgressPercent、不支持
  与读取失败计数、完整性、限制原因）+ controller 传入的原始字符串状态全文展示 +
  范围下拉（整张图纸/模型空间/当前空间/所有布局/当前选择）+ 开始扫描 / 取消扫描，
  分别调 `DrawingIndexRuntime.Start(scope)` 与 `DrawingIndexRuntime.Cancel()`。
  未建立或扫描中以外状态才允许开始；仅扫描中允许取消。不伪造任何百分比。

新增最小只读 presentation model（Host.2016 内）：

- `PalettePresentationModels.cs`
  - `PaletteStatusTone`：Neutral/Busy/Success/Warning/Failure，只驱动状态点颜色，
    Host 原文始终是唯一状态表达。
  - `PaletteAgentStatusView.FromHostStatus()`：只对已知 Host 状态文本分类着色，未知
    文本保持 Neutral 且逐字显示，不发明状态。
  - `PaletteDrawingIndexView.FromDescriptor()`：从 `DrawingIndexRuntime.GetDescriptor()`
    快照生成；计数、ProgressPercent、Limited/LimitReason 全部逐字来自 descriptor；
    null descriptor 回退为“未建立”空视图。
  - 该文件只依赖 BCL + Contracts，同时被 Host.2016（net45）与 Mvp.Specs（net8）编译，
    net45/net8 边界即此文件。

接线方式：`UnifiedPaletteRuntime.UpdateDrawingIndexStatus()` 在现有字符串状态通知
链路上同步抓取 descriptor 生成视图（抓取失败静默保留上一份真实视图，绝不让快照异常
逃出通知链）；`UnifiedPaletteController` 缓存视图并在 `UpdatePanel()` 时推给
`panel.UpdateDrawingIndex(rawStatus, view)`。

## 2. 修改文件

- `src/Codex.AutoCAD.Host.2016/UnifiedPalettePanel.cs`（重写为三 Tab 布局，935 行）
- `src/Codex.AutoCAD.Host.2016/UnifiedPaletteController.cs`（缓存并下发动画索引视图）
- `src/Codex.AutoCAD.Host.2016/UnifiedPaletteRuntime.cs`（descriptor → 视图接线）
- `src/Codex.AutoCAD.Host.2016/PalettePresentationModels.cs`（新增，presentation-only）
- `src/Codex.AutoCAD.Host.2016/Codex.AutoCAD.Host.2016.csproj`（显式 Compile 列表 +1）
- `tests/Codex.AutoCAD.Host.2016.Mvp.Specs/Codex.AutoCAD.Host.2016.Mvp.Specs.csproj`
  （链接 presentation model 源文件）
- `tests/Codex.AutoCAD.Host.2016.Mvp.Specs/Program.cs`（+3 条 presentation 规格）

未修改范围外文件；未修改 wire contract、CadContext、DrawingIndex、IPC、Bridge、
AgentHost、AppServer 与 Host.2025。

## 3. 新增规格

- `HOST2016_PALETTE_INDEX_VIEW_MAPS_REAL_STATES`：9 种协议状态 → 标签/色调/可否开始/
  可否取消映射，null descriptor 回退。
- `HOST2016_PALETTE_INDEX_VIEW_KEEPS_REAL_PROGRESS`：descriptor 计数与 42% 真实进度
  逐字保留，scope 标签与统计文本包含真实计数。
- `HOST2016_PALETTE_AGENT_STATUS_CLASSIFIES_KNOWN_MESSAGES`：14 条真实 Host 状态文本
  的色调分类，未知文本保持 Neutral，显示文本逐字不变。

## 4. 真实测试输出

### 4.1 git diff --check

通过（无输出）。

### 4.2 Host.2016 Release/x64 编译（D:\AutoCAD 2016 R20.1 原版程序集）

流程与 `verify-autocad2016-context-v2-candidate.ps1` 相同（依赖项目 net45 还原 +
`-p:EnableAutoCad2016=true` + `FrameworkPathOverride` 指向
Microsoft.NETFramework.ReferenceAssemblies.net45 1.0.3，A 组输出目录暂存依赖 DLL）：

```text
dotnet build src/Codex.AutoCAD.Host.2016/Codex.AutoCAD.Host.2016.csproj \
  --configuration Release --no-restore -p:Platform=x64 \
  -p:AutoCad2016Dir="D:\AutoCAD 2016" -p:EnableAutoCad2016=true \
  -p:FrameworkPathOverride=<net45 ref assemblies 1.0.3> \
  -p:BuildProjectReferences=false -p:ContinuousIntegrationBuild=true

Codex.AutoCAD.Host.2016 -> artifacts\kimi-palette-ui-build\Codex.AutoCAD.Host.2016.dll
0 个警告 / 0 个错误（TreatWarningsAsErrors=true）
```

输出目录仅含 `Codex.AutoCAD.{Host.2016,AgentLauncher,Bridge.Client,Contracts,Ipc}.dll`，
无 accoremgd/acdbmgd/acmgd（csproj 内 `RejectAutodeskCopyLocal` 目标亦会强制失败）。

### 4.3 源码安全扫描（新增代码）

对全部新增 diff 行扫描 CAD 保存/写入 API、命令字符串、进程/反射/IPC/网络/文件写入/
注册表、真实路径、token、假数据标记：0 命中（`IsNullOrEmpty` 内嵌 "lOrEm" 为误报，
已人工排除）。

### 4.4 scripts/verify-phase2.ps1 -Configuration Release

以 UTF-8 控制台编码运行，`-CodexExecutable` 指向本机 npm 安装的 codex.exe：

```text
解决方案组成验证通过；.NET SDK 固定版本验证通过：8.0.319
托管核心解决方案 Release 构建：0 警告 / 0 错误
规格动态计数汇总：332/332
  Contracts.Specs 87/87；Ipc.Specs 35/35；Security.Specs 19/19；
  AppServer.Specs 20/20；Bridge.Specs 44/44；Bridge.Client.Specs 29/29；
  AgentRuntime.Specs 33/33；Chat.Specs 9/9；Host.2016.Mvp.Specs 56/56
  （含新增 3 条 palette presentation 规格全部 PASS）
AutoCAD Host 禁用 API 词法扫描通过；AgentHost doctor 活体握手 ok=true；
git diff --check 与 git diff --cached --check 通过；敏感信息基础扫描通过。
```

## 5. 未验证边界

- **AutoCAD 实机**：未启动 AutoCAD；停靠/浮动/隐藏重开/Palette Reset、IME 实际组字、
  连续发送/取消、Agent 各真实状态、选择与索引实机流转、100%/125%/150% DPI 与
  DBMOD 不变均需用户按第 7 节清单实机验证。
- **scripts/verify-autocad2016-host.ps1 与 verify-autocad2016-unified-host.ps1**：
  两个脚本仍为诊断时代门禁（钉住 3 个 Compile 项、禁止 ProjectReference/WPF 引用/
  PaletteSet/async、钉死三份诊断源文件哈希、主 sln 名称检查误伤
  `Codex.AutoCAD.Host.2016.Mvp.Specs`）。自 7f10d60（2026-07-21，MVP 接线提交）起
  在基线上即失败，与本任务无关，未验证、未修改（曾临时评估修复，已还原全部改动）。
  建议后续单独任务更新这两个脚本以匹配 MVP 时代 Host.2016。
- `scripts/verify-phase2.ps1` 在 GBK 控制台下会因 Security.Specs 中文摘要行乱码而
  误判失败；需以 UTF-8 控制台编码运行（`[Console]::OutputEncoding = UTF8`）。

## 6. 构建注意事项

- Host.2016 直接 `dotnet build` 会因锁文件 RID（win;win-arm64;win-x64;win-x86）与
  ProjectReference 图报 NU1004；必须先对 4 个依赖项目执行
  `dotnet restore -p:EnableAutoCad2016=true --force --no-cache`（并恢复各自
  packages.lock.json 字节），再以 `--no-restore` + `FrameworkPathOverride` 编译。
  与 context-v2-candidate 脚本流程一致。

## 7. 实机测试清单（供用户执行）

1. 干净 AutoCAD 2016 进程 NETLOAD 新 DLL → CODEX16PAL：
   - 320 DIP 左右最小宽度与 520 DIP 默认宽度下，停靠左右侧、浮动、隐藏重开、
     CODEX16PALRESET；三个 Tab 所有中文文字、按钮、状态不重叠不裁切。
2. 对话：
   - 中文多行输入、Enter 发送、Shift+Enter 换行、IME 组字中 Enter 不发送、空白被拒；
   - Agent 离线 → 启动（连接中）→ 在线 → 提问 → 回答中流式文本 → 完成；
     取消回合；断线/失败只显示脱敏状态行；停止 Agent；新建对话保留 CAD 上下文；
     发送/启动进行中重复点击无效。
3. 当前选择：
   - 无选择、完整选择、含不支持/受限对象的选择：计数、完整性、ReadIssueSummary 真实显示；
   - 可读摘要不含上下文哈希；Canonical JSON 复制成功；
   - “清除 CAD 上下文”只清选择上下文（对话与整图索引不变）；“清除全部”调 ClearAll。
4. 整图索引：
   - 未建立 → 开始扫描（各 scope）→ 扫描中（开始禁用/取消可用）→ 取消 → 完成/
     部分完成/受限完成/失效；统计数字与 descriptor 一致，无编造百分比。
5. 100%、125%、150% DPI：文字、按钮、输入区、滚动区无重叠裁切。
6. 全流程前后 DBMOD 只因用户自己的图纸操作变化；插件不保存 DWG。

## 8. 提交

提交 ID：`2efd40b98b2aa731d8d95ac56a08d5f414c30edf`
提交信息：`feat(host2016): redesign readonly palette workflow`
分支：`codex/kimi-palette-ui`（未 merge、未 push；交接文档在提交后回填本提交 ID，
工作树中该文件与提交内容仅差本节文字）

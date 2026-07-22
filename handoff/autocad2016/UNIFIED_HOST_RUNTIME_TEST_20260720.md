# AutoCAD 2016 统一 Host 只读 MVP 实机验收

日期：2026-07-20
候选 ID：`autocad2016-unified-host-frozen-20260720-F5D80075`
文件名：`Codex.AutoCAD.Host.2016.dll`
大小：`87552` 字节
SHA-256：`F5D8007526467ED77A240596633892258ADC5CDC6F4B57A47B5578818AD172E0`

## 1. 本轮范围

本轮只验证：

- 一个统一 `net45/x64` DLL 能否由原版 AutoCAD 2016 R20.1 人工 `NETLOAD`。
- 诊断、Palette、六类只读选择读取是否在同一 DLL 中工作。
- 真实坐标、图层、文字、半径、顶点和块名是否进入 CadContextJson v1。
- Palette 是否显示可读摘要和 canonical JSON。
- 捕获、重建、清除和文档切换期间 `DBMOD` 是否保持不变。

本轮不验证 Agent、IPC、Codex 对话、CAD 写入或插件保存；这些能力在候选中均为禁用。
125%/150% DPI、文档关闭和 AutoCAD 退出生命周期另列后续测试，不与本轮混为一次证据。

## 2. 必须使用干净 AutoCAD 进程

旧诊断宿主与统一候选具有相同程序集名称，旧 Palette/ReadOnlyContext sidecar 还包含重复
命令。首次统一候选验证必须由用户手工完成以下操作：

1. 正常关闭所有现有 AutoCAD 2016 进程。
2. 用户手工重新打开一个干净 AutoCAD 2016。
3. 本轮只 `NETLOAD` 上述统一 DLL；不要再加载旧的诊断、Palette 或 ReadOnlyContext DLL。

Codex 不启动、唤醒、关闭或重启 AutoCAD。

不需要关闭 AutoCAD 自带的自动保存，也不要修改 `SAVETIME`。插件不会保存 DWG，也不会
更改自动保存设置。建议使用脱敏测试图或图纸副本；如果 AutoCAD 自身自动保存恰好打断
一个观察窗口，请记录该事实并在状态稳定后重复该小段，不要把它归因于插件。

## 3. 加载、诊断与 Palette

在干净会话中依次执行：

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

`NETLOAD` 必须在文件选择器中选择冻结目录里的精确 `Codex.AutoCAD.Host.2016.dll`。

预期：

- 加载消息包含“统一只读 MVP 候选已加载”。
- Doctor 显示 `.NET Framework 4.5`、x64、R20.1/托管 `20.1.0.0`。
- `Palette capability` 和 `Read-only selection capability` 为 `enabled`。
- CadContextJson 为 `codex.autocad.cad-context/1`。
- Agent/IPC、CAD 写入、插件保存均为 `disabled`。
- Palette 能打开，显示“可读摘要”和“Canonical JSON”两个页签。
- `CODEX16PALINFO` 显示模块版本 `0.2.0.0`、Agent/CAD 写入/插件保存禁用。
- 三次 `DBMOD` 数值应相同；不要求具体数值必须为 `0` 或 `4`。

## 4. 六类真实图元到 JSON

准备以下对象各一个：

- Line
- Circle
- Polyline
- DBText
- MText
- BlockReference

先在 AutoCAD 属性面板或 `LIST` 中本地记下需要对照的字段。对照完成并退出其他命令后：

```text
保持未选择状态
DBMOD
鼠标预选上述六个对象
CODEX16CTX
CODEX16CTXINFO
```

首个 `DBMOD` 必须在未选择状态执行，因为它会清除已有预选。执行完该 `DBMOD` 后重新
预选六个对象，并立即执行 `CODEX16CTX`；不要在预选和 `CODEX16CTX` 之间插入其他命令。

命令行预期：

- `status=published-read-only-json-v1`
- `published=true`
- `selected=6`
- `jsonBytes` 大于 `0`
- `DBMOD=<同一数值>-><同一数值>`
- `unchanged=true`

Palette 中逐项本地核对：

| 图元 | 必查字段 |
| --- | --- |
| Line | layer、start、end |
| Circle | layer、center、radius、normal |
| Polyline | layer、closed、elevation、normal、全部 vertices 及 bulge |
| DBText | layer、text、position、height、rotation |
| MText | layer、text、location、textHeight、rotation |
| BlockReference | layer、position、rotation、scale、effectiveName、isDynamic、isExternalReference |

“可读摘要”允许为了阅读截短很长文字或只展示前八个多段线顶点；“Canonical JSON”必须
包含完整白名单字段。不要把真实 canonical JSON、图纸路径、图名、选择哈希或上下文哈希
粘贴到聊天或提交到 Git，只需反馈字段是否逐项一致。

## 5. Palette 重建、上下文清除与文档切换

捕获成功后依次执行：

```text
DBMOD
CODEX16PALRESET
CODEX16PALINFO
CODEX16CTXINFO
DBMOD
CODEX16CTXCLEAR
CODEX16CTXINFO
DBMOD
```

预期：

- `CODEX16PALRESET` 后 Palette 实例重建，但已发布的摘要和 JSON 仍保留。
- reset/release/generation 计数按一次操作递增。
- `CODEX16CTXCLEAR` 后状态为 `cleared-user-command`、`published=false`、`selected=0`，
  Palette JSON 区为空。
- 该窗口中的各次 `DBMOD` 数值保持相同。

文档切换另做一个独立样本：在图纸 A 再次捕获后切换到图纸 B，执行：

```text
CODEX16CTXINFO
CODEX16PALINFO
```

预期状态为 `cleared-document-activated`，旧摘要和 JSON 不得跨图纸保留。

## 6. 反馈模板

请只反馈以下脱敏结果，不需要发送完整 JSON 或真实图纸截图：

```text
已手工关闭旧 CAD 并打开干净 AutoCAD 2016：是/否
NETLOAD 选择了指定冻结候选完整路径：是/否
加载消息正常：是/否
Doctor 版本/架构/API 正常：是/否
Palette 两个页签正常：是/否
CODEX16CTX status：
selected：
jsonBytes：
DBMOD 捕获前后：
Line 字段一致：是/否
Circle 字段一致：是/否
Polyline 字段一致：是/否
DBText 字段一致：是/否
MText 字段一致：是/否
BlockReference 字段一致：是/否
Palette reset 后上下文保留：是/否
clear 后 JSON 清空：是/否
文档切换后旧上下文清除：是/否/未测
异常或界面问题：无/说明
```

只有精确冻结候选通过本文件范围内的人工 `NETLOAD` 和字段核对后，才能将本阶段标记为
运行通过并产生独立 Git 提交；在此之前仍不得宣称统一 Host 或完整 AutoCAD 2016 支持。

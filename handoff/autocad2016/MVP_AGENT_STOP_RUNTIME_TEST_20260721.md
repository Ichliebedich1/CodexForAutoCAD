# AutoCAD 2016 AgentHost 停止生命周期 0.3.2 实机测试

候选 ID：`autocad2016-mvp-agent-v032-884413f0-8c74b95e`

本轮只验证 `CODEX16AGENTSTOP` 的有界清理、连续两次启动/停止和 Palette 最终状态。
CadContextJson 仍为 v1；CAD 写入、插件保存和自动重试继续禁用。

## 1. 精确候选

只加载以下 DLL：

```text
C:\tmp\CodexForAutoCAD-bridge-client2016\artifacts\autocad2016-mvp-agent-v032-884413f0-8c74b95e\Codex.AutoCAD.Host.2016.dll
```

- Host assembly version：`0.3.2.0`
- Host SHA-256：`884413F0E7ACD64974F5F42B0251F8BEFCA361FA5C59057C5136C79E9AD33928`
- AgentHost SHA-256：`8C74B95ECD6680F9A35824DB1C2C543D42B52AB1E4D3565F5B7EE8DBB1DC900E`
- 候选包文件均已设为只读。

## 2. 会话前置条件

当前 AutoCAD 进程已加载 `0.3.1.0`，该程序集不能在同一进程卸载；因此本轮必须由用户
在方便时自行关闭当前 AutoCAD，并自行重新打开一个干净 AutoCAD 2016 进程。Codex 不会
启动、关闭、唤醒或重启 AutoCAD。

是否保存当前图纸完全由用户决定。插件不会调用保存，也不会修改 `SAVETIME`。

## 3. 加载与版本确认

在脱敏测试图或空白图中执行：

```text
DBMOD
NETLOAD
CODEX16PAL
CODEX16PALINFO
```

`NETLOAD` 选择第 1 节的精确 DLL。必须看到：

- `Module version: 0.3.2.0`
- Agent 初始为离线/手动启动
- CAD write 与 plugin-initiated save 均为 disabled

若版本仍为 `0.3.1.0`，停止本轮测试。

## 4. 连续两次启动与停止

第一轮：

```text
CODEX16AGENTSTART
```

等待 Palette 显示 `AgentHost 在线；只读 Codex 会话已建立。`，然后执行：

```text
CODEX16AGENTSTOP
```

等待 Palette 最终显示 `AgentHost 已停止；CAD 写入仍禁用。`。

第二轮重复：

```text
CODEX16AGENTSTART
CODEX16AGENTSTOP
```

每条命令都必须等待 Palette 出现上一条命令的最终状态后再执行下一条。若清理失败，
Palette 必须明确显示 `停止 AgentHost 失败：<异常类型>`，不能只停留在“请求已提交”。

## 5. 最终只读检查

执行：

```text
CODEX16PALINFO
DBMOD
```

通过标准：

- 两轮都能从离线进入在线，再进入已停止状态。
- AutoCAD 始终可操作。
- 本轮开始和结束的 `DBMOD` 相同。
- 没有 CAD 写入、插件保存或未授权工具执行。
- Codex 只读进程检查确认该候选 AgentHost 数量回到 `0`。

用户无需发送 canonical JSON、命令全文、图纸路径或哈希；只需确认两轮 Palette 最终状态
均为成功，或反馈显示的异常类型。进程残留由 Codex 在本机只读检查。


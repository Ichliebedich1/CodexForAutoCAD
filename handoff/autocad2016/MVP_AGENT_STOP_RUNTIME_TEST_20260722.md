# AutoCAD 2016 AgentHost 停止生命周期 0.3.2 实机测试

本轮只验证停止失败后的重试清理、重复停止幂等、连续两轮启动/停止，以及最终无残留。
CAD 写入与插件保存继续禁用。

## 唯一允许加载的候选

候选 ID：`autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd`

只加载：

```text
C:\tmp\CodexForAutoCAD-bridge-client2016\artifacts\autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd\Codex.AutoCAD.Host.2016.dll
```

- Host version：`0.3.2.0`
- Host SHA-256：`1CC9D2943F1AB3C37395927B0E2EAF4189A0B3BE4B2E8FA4A61AE8470D3478DC`
- AgentHost SHA-256：`8E6B26FD7B20925A1CE53CAB0DBEE093C58B9AF0935219DF75FC8A7CB5C4FA2A`

候选中的 `AgentHost` 是 framework-dependent apphost，必须保留整个 `AgentHost` 子目录，不能只复制或只替换 EXE。

旧候选 `autocad2016-mvp-agent-v032-884413f0-8c74b95e` 已撤销，禁止加载。

## 前置条件

若当前 AutoCAD 进程已加载旧版 DLL，托管程序集不能在同一进程卸载。请由你自行关闭并
重新打开一个干净的 AutoCAD 2016；Codex 不会启动、关闭或操作 AutoCAD。是否保存图纸
完全由你决定，插件不会修改 `SAVETIME` 或主动保存。

## 加载和版本确认

在空白或脱敏测试图中执行：

```text
DBMOD
NETLOAD
CODEX16PAL
CODEX16PALINFO
```

确认：

- `Module version: 0.3.2.0`
- Agent 初始为离线/手动启动
- CAD write 与 plugin-initiated save 均为 disabled

## 连续两轮启停

每条命令必须等 Palette 显示上一条命令的最终状态后再继续：

```text
CODEX16AGENTSTART
CODEX16AGENTSTOP
DBMOD

CODEX16AGENTSTART
CODEX16AGENTSTOP
DBMOD
```

随后再执行一次重复停止：

```text
CODEX16AGENTSTOP
CODEX16PALINFO
DBMOD
```

通过标准：

- 两轮均从离线进入在线，再进入已停止状态。
- 第三次重复 STOP 明确幂等，不重新启动或误报仍在线。
- 若某次停止失败，Palette 必须显示异常类型；再次 STOP 必须重试未完成清理阶段，不能
  直接误报“已停止”。
- AutoCAD 始终可继续执行普通命令。
- 开始与结束的 `DBMOD` 相同。
- 没有 CAD 写入、插件保存或自动保存设置修改。
- 测试完成后由 Codex 只读检查候选 AgentHost 残留数量为 `0`。

无需发送 canonical JSON、图纸路径或完整日志。请只反馈三次 STOP 的 Palette 最终状态、
三次 `DBMOD` 数值是否一致，以及 AutoCAD 是否仍可正常使用。

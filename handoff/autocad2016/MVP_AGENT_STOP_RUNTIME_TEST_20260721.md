# AutoCAD 2016 AgentHost 停止阶段运行时测试

候选 ID：`agent-stop-{git-head-short}-{timestamp}`

本文档描述 AgentHost 停止阶段的手动运行时测试流程。
停止阶段验证器不会启动、控制或发送命令到 AutoCAD。

## 1. 精确候选

只加载以下 DLL：

```text
{candidate-package-dir}\Codex.AutoCAD.Host.2016.dll
```

Host 预期：

- Assembly version：`{host-assembly-version}`
- SHA-256：`{host-sha256}`
- AgentHost SHA-256：`{agenthost-sha256}`

必须使用未加载过旧候选的干净 AutoCAD 2016 进程。验证器不会启动、关闭或重启
AutoCAD；若当前进程曾加载旧候选，请由用户自己关闭后重新打开。

## 2. 加载与身份确认

在安全测试图或脱敏副本中依次执行：

```text
DBMOD
NETLOAD
CODEX16PAL
CODEX16PALINFO
CODEXCADDOCTOR
DBMOD
```

`NETLOAD` 时选择第 1 节的精确 DLL。必须确认：

- `CODEX16PALINFO` 显示正确的模块版本；若版本不匹配，立即停止。
- Doctor 显示 `.NET Framework 4.5`、x64、R20.1/`20.1.0.0`。
- Agent 初始为离线/手动启动。
- CAD write 与 plugin-initiated save 均为 disabled。
- 前后 `DBMOD` 数值相同。

## 3. 捕获只读上下文

先保持未选择状态执行 `DBMOD`，然后重新预选一个或多个测试图元，并立即执行：

```text
CODEX16CTX
CODEX16CTXINFO
DBMOD
```

必须看到 `published=true`、JSON bytes 大于 `0`、`DBMOD` 前后相同。不要把 canonical
JSON、选择哈希、图纸名称或路径粘贴到聊天。

## 4. 启动 AgentHost

执行：

```text
CODEX16AGENTSTART
```

等待侧边栏状态更新。通过标准是出现：

```text
AgentHost 在线；只读 Codex 会话已建立。
```

若失败，只反馈侧边栏中的错误类型和简短消息；不要粘贴 canonical JSON 或本机路径。

## 5. 同一 thread 两轮对话

可以在侧边栏输入并点击"发送给 Codex"，也可两次执行 `CODEX16ASK`。

建议问题：

1. `只根据当前 CAD 上下文，用一句话概括选区，并在末尾写标记731。`
2. `上一轮末尾要求写的三位标记是什么？只回答数字。`

通过标准：

- 第一轮出现流式或完整 assistant 文本并正常完成。
- 第二轮回答 `731`，证明复用了同一 Codex thread。
- 整个过程无 CAD 写入、无插件保存、无未授权工具执行。

## 6. 停止与 DBMOD

执行：

```text
CODEX16AGENTSTOP
CODEX16PALINFO
DBMOD
```

通过标准：

- 侧边栏显示 AgentHost 已停止。
- AutoCAD 保持可操作。
- `DBMOD` 与本轮开始时相同。
- 不出现插件保存提示或 CAD 写入。

## 7. 证据标志说明

停止阶段验证器生成的证据包含以下标志：

- `paletteSourceWiringInspected: true` - 调色板源布线已检查
- `paletteBehaviorAutomatedVerified: false` - 调色板行为未自动化验证
- `paletteRuntimeVerified: false` - 调色板运行时未验证
- `netLoadVerified: false` - NETLOAD 未验证
- `runtimeToArtifactBindingVerified: false` - 运行时到工件绑定未验证

这些标志表明停止阶段验证器仅进行构建和规格验证，不进行 AutoCAD 运行时集成验证。

## 8. 验证器限制

停止阶段验证器：

- 不启动 AutoCAD 进程
- 不控制 AutoCAD 进程
- 不发送 CAD 命令
- 不创建实时证据
- 不验证 NETLOAD 行为
- 不验证运行时到工件绑定

验证器仅验证：

- 两次隔离构建的一致性
- 规格测试的通过
- 候选包的完整性
- Git 绑定的正确性
- 证据结构的完整性

## 9. net45 依赖说明

候选包包含以下 net45 依赖（精确匹配 AutoCAD 2016 Host 需求）：

- `Codex.AutoCAD.AgentLauncher.dll`
- `Codex.AutoCAD.Bridge.Client.dll`
- `Codex.AutoCAD.Contracts.dll`
- `Codex.AutoCAD.Ipc.dll`

注意：`Codex.AutoCAD.Bridge.dll` 和 `Codex.AutoCAD.AgentRuntime.dll` 是 net8-only 依赖，
不包含在候选包中。

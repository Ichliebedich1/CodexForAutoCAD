# AutoCAD 2016 统一只读 AI MVP 0.3.1 实机测试

候选 ID：`autocad2016-mvp-agent-v031-a7bff46f-8c74b95e`

本轮只验证截图中 `CODEX16AGENTSTART` 的修复、真实 Codex 两轮只读对话和有界停止。
CAD 写入、插件保存和自动重试仍全部禁用。

## 1. 精确候选

只加载以下 DLL：

```text
C:\tmp\CodexForAutoCAD-bridge-client2016\artifacts\autocad2016-mvp-agent-v031-a7bff46f-8c74b95e\Codex.AutoCAD.Host.2016.dll
```

Host 预期：

- Assembly version：`0.3.1.0`
- SHA-256：`A7BFF46F1BA4970818ACB03F51C09EEBF1DDB8A7093D0C4C615E2D877D9236D1`
- AgentHost SHA-256：`8C74B95ECD6680F9A35824DB1C2C543D42B52AB1E4D3565F5B7EE8DBB1DC900E`

必须使用未加载过旧候选的干净 AutoCAD 2016 进程。Codex 不会启动、关闭或重启
AutoCAD；若当前进程曾加载 `0.2.0.0`/旧 Agent 候选，请由用户自己关闭后重新打开。

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

- `CODEX16PALINFO` 显示 `Module version: 0.3.1.0`；若仍显示 `0.2.0.0`，立即停止。
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

可以在侧边栏输入并点击“发送给 Codex”，也可两次执行 `CODEX16ASK`。

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

本轮通过前，manifest 中 `autoCadLiveEvidence` 与 `netLoadVerified` 必须继续为 `false`，
不得宣称 AutoCAD 2016 MVP 已完成。

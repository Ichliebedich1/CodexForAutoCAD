# AutoCAD 2016 MVP 公共契约 v1

状态：冻结候选；只有本阶段双框架门禁和 Git 提交完成后，才成为正式冻结基线。

适用范围：

- AutoCAD 2016 进程内 `net45/x64` Host。
- 进程外 `.NET 8` AgentHost、Agent Runtime 与 Codex App Server 适配层。
- Codex 与 Kimi 并行实现的侧边栏 UI。
- 当前 MVP 的只读链路；CAD 写入仍保持关闭。

本契约不证明 Host.2016 已经接入 Agent，也不证明 AutoCAD 2016 完整支持。任何运行能力仍需真实编译、冻结候选 SHA-256、用户人工 `NETLOAD` 和对应实机记录。

## 1. 三个版本号必须分离

| 版本 | 当前值 | 作用 |
| --- | ---: | --- |
| IPC `protocolVersion` | `1` | HMAC、sequence、nonce 和认证信封 |
| Host/Agent/UI `contractVersion` | `1` | 方法、请求、响应、事件、错误和能力协商 |
| CadContextJson `schemaVersion` | `1` | CAD 只读上下文 JSON |

三者不能互相代替。任一版本未知或不兼容时必须 fail-closed；禁止静默降级、猜测字段或回退到未认证 IPC。

## 2. CadContextJson v1 顶层

固定 schema：

```text
codex.autocad.cad-context
```

固定顶层字段和顺序：

| 顺序 | JSON 字段 | 类型 | 规则 |
| ---: | --- | --- | --- |
| 1 | `schema` | string | 必须为 `codex.autocad.cad-context` |
| 2 | `schemaVersion` | integer | 必须为 `1` |
| 3 | `capturedAtUtc` | string | `yyyy-MM-ddTHH:mm:ss.fffZ` |
| 4 | `source` | string | 当前必须为 `autocad.readonly-selection` |
| 5 | `egressRisk` | string | 当前必须为 `context-egress` |
| 6 | `document` | object | 脱敏文档身份 |
| 7 | `selection` | object | 强类型选择快照 |

未知字段、缺失字段、`null`、非有限数和超限值必须拒绝。

### 2.1 document

固定字段和顺序：

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| `documentId` | string | Host 生成的当前打开文档不透明 ID；不得包含图名或路径 |
| `drawingFingerprint` | string | 64 位小写 ASCII 十六进制 SHA-256 |
| `revision` | integer | 非负；由受信 Host 维护 |
| `currentSpace` | string | 仅 `model` 或 `paper` |
| `drawingVersion` | string | 例如 `AC1027`；最多 64 字符 |
| `units` | string | 例如 `millimeters` 或 `unitless`；最多 64 字符 |

v1 明确不包含：

- DWG 文件名。
- 本地或网络路径。
- `pathHash`。
- `TRUSTEDPATHS`。
- 用户名、许可证、企业服务器或账号信息。

### 2.2 selection

固定字段和顺序：

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| `snapshotHash` | string | 64 位小写 ASCII 十六进制 SHA-256 |
| `entityCount` | integer | 必须与 `entities.length` 完全一致 |
| `entities` | array | 1–64 个白名单图元 |

AutoCAD 2016 当前来源的 `snapshotHash` 继续使用已经实机验证的 `binary-v1` 选择快照哈希。UI 不得重新发明或替换它。

## 3. 公共图元字段

每个图元固定包含：

| 顺序 | 字段 | 类型 | 规则 |
| ---: | --- | --- | --- |
| 1 | `handle` | string | 1–16 位大写 ASCII 十六进制 |
| 2 | `ownerSpaceHandle` | string | 1–16 位大写 ASCII 十六进制 |
| 3 | `entityType` | string | 六类白名单值之一 |
| 4 | `stateHash` | string | 64 位小写 ASCII 十六进制 SHA-256 |
| 5 | `layer` | string | 1–255 字符，严格 UTF-8，禁止危险格式和控制字符 |
| 6 | 强类型 payload | object | 必须且只能有一个，并与 `entityType` 完全匹配 |

图元在 canonical JSON 中按 `handle` 的无符号数值升序排列，而不是按选择顺序或字符串顺序排列。重复 Handle 必须拒绝。

## 4. 六类强类型 payload

### 4.1 line

```json
{
  "entityType": "line",
  "line": {
    "start": { "x": 0, "y": 0, "z": 0 },
    "end": { "x": 100, "y": 0, "z": 0 }
  }
}
```

`start`、`end` 为 WCS 三维坐标。

### 4.2 circle

```json
{
  "entityType": "circle",
  "circle": {
    "center": { "x": 10, "y": 20, "z": 0 },
    "radius": 5,
    "normal": { "x": 0, "y": 0, "z": 1 }
  }
}
```

半径必须为正有限数，法向量不能为零。

### 4.3 polyline

```json
{
  "entityType": "polyline",
  "polyline": {
    "closed": true,
    "elevation": 0,
    "normal": { "x": 0, "y": 0, "z": 1 },
    "vertices": [
      { "position": { "x": 0, "y": 0 }, "bulge": 0 },
      { "position": { "x": 10, "y": 0 }, "bulge": 0.25 }
    ]
  }
}
```

顶点位置为 OCS 二维坐标，并与 `elevation`、`normal` 一起解释；顶点数为 1–256。

### 4.4 dbText

```json
{
  "entityType": "dbText",
  "dbText": {
    "text": "设备A",
    "position": { "x": 10, "y": 20, "z": 0 },
    "height": 2.5,
    "rotation": 0
  }
}
```

### 4.5 mText

```json
{
  "entityType": "mText",
  "mText": {
    "text": "第一行\n第二行",
    "location": { "x": 10, "y": 20, "z": 0 },
    "textHeight": 3,
    "rotation": 0
  }
}
```

`dbText.text` 与 `mText.text` 最多 2048 个 UTF-16 字符、8192 个 UTF-8 字节；只允许 `CR`、`LF`、`TAB` 三种文本控制字符。文字是敏感上下文，UI 必须明确显示将发送的选择摘要。

### 4.6 blockReference

```json
{
  "entityType": "blockReference",
  "blockReference": {
    "position": { "x": 10, "y": 20, "z": 0 },
    "rotation": 0,
    "scale": { "x": 1, "y": 1, "z": 1 },
    "effectiveName": "设备块_A",
    "isDynamic": true,
    "isExternalReference": false
  }
}
```

缩放分量必须为有限非零数。负缩放允许用于表达镜像块。

## 5. canonical JSON 和上下文身份

规范化规则：

1. 严格 UTF-8，无 BOM。
2. 不输出任何无意义空白。
3. 字段顺序按本文件及 `CadContextJsonV1Codec` 固定。
4. 不输出 `null` 或未知字段。
5. 图元按 Handle 数值升序。
6. JSON 字符串按 JSON 规则转义；合法中文和配对代理项保留为 UTF-8。
7. 浮点数使用 invariant `G17`，指数统一为小写 `e`、去掉 `+` 和指数前导零；正负零均输出 `0`。
8. 所有数必须有限，绝对值不超过 `1,000,000,000`。
9. canonical JSON 最大 `262144` 字节。

当前固定向量：

```text
canonical UTF-8 bytes = 2225
SHA-256 = c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423
```

`contextSha256` 定义为完整 canonical CadContextJson v1 UTF-8 字节的 SHA-256，小写十六进制。

身份绑定要求：

- `AgentTurnStartRequest.contextSha256` 必须与请求中的 `context` 完全一致。
- `AgentTurnStartResponse.acceptedContextSha256` 必须原样回显。
- 属于该 turn 的 assistant、tool、approval 和 terminal 事件必须携带同一 `contextSha256`。
- Thread、Turn 或上下文哈希任一不一致，Host 必须拒绝结果。

## 6. Host/Agent/UI contract v1

### 6.1 方法白名单

| 方法 | 方向 | 用途 |
| --- | --- | --- |
| `agent.capabilities.get` | Host → Agent | 冻结版本和能力协商 |
| `agent.thread.start` | Host → Agent | 创建真实 Codex thread |
| `agent.turn.start` | Host → Agent | 创建 turn，并携带提示词和 CAD 上下文 |
| `agent.turn.interrupt` | Host → Agent | 中断当前 turn；不表示重试 |
| `agent.approval.resolve` | Host → Agent | 拒绝或一次允许 |
| `cad.line.propose` | Agent → Host | 后续写入阶段的直线提案；MVP 只读阶段必须禁用 |
| `agent.event` | Agent → Host | 规范化事件通知 |

未知方法必须拒绝，不能映射为任意 AutoCAD 命令或 API 名称。

### 6.2 能力协商

`AgentCapabilitiesRequest` 必须包含：

- `contractVersion = 1`
- `clientName`
- `clientVersion`
- `hostTarget`，当前目标为 AutoCAD R20.1 / net45 / x64

`AgentCapabilitiesResponse` 必须包含：

- `contractVersion = 1`
- `minimumCompatibleVersion = 1`
- 不透明 `agentInstanceId`
- `cadContextSchema = codex.autocad.cad-context`
- `cadContextSchemaVersion = 1`
- 方法、事件和审批闭集
- `cadWriteAvailable`

`cadWriteAvailable=true` 只表示能力存在，绝不构成写入授权。

### 6.3 thread / turn

- Host 先调用 `agent.thread.start`，保存 Agent 返回的真实 `threadId`。
- 同一对话的后续问题必须复用该 `threadId`。
- 每个 turn 使用新的 `clientTurnId`。
- 每轮都附带当时最新的 CadContextJson v1 与 `contextSha256`。
- 文档切换或上下文失效后，不得继续发送旧上下文。
- 至少两轮连续对话通过前，不得把“连续上下文对话”标记为已验证。

## 7. 事件契约

事件必须包含：

- `contractVersion = 1`
- 白名单 `kind`
- 稳定 `eventId`
- 严格递增且为正的 `sequence`
- `occurredAtUtc`
- 适用时的 `threadId`、`turnId` 和 `contextSha256`

白名单事件：

```text
connection.changed
thread.started
turn.started
message.user
message.assistant.started
message.assistant.delta
message.assistant.completed
tool.started
tool.progress
tool.completed
tool.failed
approval.requested
approval.resolved
turn.completed
turn.failed
turn.cancelled
```

UI 只消费具体 `IAgentBridgeClient` 产生的强类型/规范化事件，不直接解析原始 IPC JSON。重复 `eventId` 只能忽略一次；旧 sequence、跳线或身份不匹配必须 fail-closed。

当前 MVP 至少实现并验证：

- `thread.started`
- `turn.started`
- `message.assistant.delta`
- `message.assistant.completed`
- `turn.completed`
- `turn.failed`
- `turn.cancelled`
- `connection.changed`

## 8. 审批契约

v1 只允许：

```text
allow_once
decline_and_continue
decline_and_cancel_turn
```

禁止：

- `allow_for_session`
- 永久允许
- 自动批准
- UI 自行扩大 Agent 返回的 `allowedDecisions`

CAD 写入后续仍必须满足“计划 → 预览 → 一次审批 → `DocumentLock` 内重校验 → 单事务”，失败回滚且不自动保存 DWG。

## 9. 连接状态和错误闭集

连接状态：

```text
offline
connecting
online
degraded
closed
```

错误码：

```text
offline
contract_mismatch
authentication_failed
replay_rejected
request_invalid
context_invalid
context_hash_mismatch
agent_unavailable
connection_lost
timeout
busy
turn_not_found
approval_invalid
approval_expired
approval_already_consumed
result_identity_mismatch
internal_error
```

失败语义：

- AgentHost 离线、认证失败、断线或超时必须显示明确状态。
- 禁止回退到未认证管道或进程内 Agent。
- `retryable=true` 只是 UI 提示；Host 不得自动重试 turn，更不得自动重试 CAD 写入。
- 用户重新发送只读问题时必须产生新的显式请求和 `clientTurnId`。
- CAD 写入结果不确定时必须停止后续写入，不得猜测成功或自动补偿。

## 10. Codex 与 Kimi 的并行边界

两端 UI 可以自由决定：

- 视觉风格。
- 布局、字体、颜色和动画。
- 摘要卡片的展现方式。
- JSON 折叠、复制和搜索交互。

两端 UI 不得改变：

- JSON 字段、类型、大小写、顺序和限额。
- 方法、事件、错误或审批字符串。
- thread/turn/context 身份绑定。
- fail-closed 行为。
- “一次允许”边界。
- 不自动保存 DWG。

任何 wire 变更必须提出 v2；禁止在 v1 中以“可选 UI 字段”隐式扩展协议。

## 11. 实现和证据位置

实现：

- `src/Codex.AutoCAD.Contracts/CadContextJsonV1Contracts.cs`
- `src/Codex.AutoCAD.Contracts/CadContextJsonV1Codec.cs`
- `src/Codex.AutoCAD.Contracts/AgentBridgeContracts.cs`

双框架 Specs：

- `tests/Codex.AutoCAD.Contracts.Specs/Program.cs`
- `tests/Codex.AutoCAD.Contracts.Specs/Codex.AutoCAD.Contracts.Specs.csproj`

冻结判断只覆盖公共契约和 canonical 向量，不覆盖：

- 统一 Host.2016。
- AutoCAD 2016 内 JSON 生成。
- Palette 显示摘要/JSON。
- 具体 `IAgentBridgeClient`。
- 长运行 AgentHost live Bridge。
- 真实 Codex thread/turn。
- AutoCAD 内连续对话或写入。

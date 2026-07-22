# AutoCAD 2016 CadContextJson v2 实机测试记录

状态：**P1 候选已取得 AutoCAD 2016 live 基线。加载/Doctor、100% DPI Palette、50 对象
混合选择与 placeholder、v2 Codex 两轮对话、显式清除和文档激活清除已通过。**

脱敏结果见
`evidence/cad-context-v2-live-observation-20260722.json`。尚未完成的稳定性项目已移至
`READONLY_MVP_REMAINING_LIVE_TESTS_20260722.md`；不得把延期项目写成通过。

本手册对应候选：

```text
C:\tmp\CodexForAutoCAD-context-v2\artifacts\autocad2016-mvp-context-v2-v032-0d72edc3-10bea363-af580c30\Codex.AutoCAD.Host.2016.dll
Host SHA-256: 0D72EDC38A30E7BF33AAEE4DCB1D50D341C4C883146677537C4BB5E7551D0AD7
AgentHost/Codex.AutoCAD.AgentHost.exe SHA-256: 10BEA363AC80C856FA513F4312B60410DB62BBF4917CE634B589CBA59DA65442
```

以下条件已经完成，不需要为 M0 重复实测：

1. P0 `0.3.2` 停止生命周期候选已人工通过并形成独立提交 `8a4ee57`。
2. P0 提交已受控引入 P1，候选冻结提交为 `c174166`；当前线另含采集器和门禁证据提交。
3. 重新冻结后完整门禁为 `259/259`，已生成上述候选 ID、Host/AgentHost SHA-256 和只读包。

## 已执行范围与证据边界

### A. 加载与只读边界：通过

```text
保持未选择状态
DBMOD
NETLOAD
DBMOD
CODEXCADDOCTOR
CODEXCAD
CODEX16PAL
CODEX16PALINFO
DBMOD
```

要求 Doctor 显示 `codex.autocad.cad-context/2`；CAD 写入、插件保存和 SAVETIME 修改均禁用，
各次 DBMOD 不因插件命令改变。

### B. 支持对象混合选区：通过有限样本

用户完成了 50 对象混合选区：44 个强类型实体、6 个受限 placeholder，整体发布成功，
`jsonBytes=23142`、`DBMOD 21 -> 21`。该样本证明混合发布与 placeholder，不证明 19 类
对象均已逐类核对字段。

```text
保持未选择状态
DBMOD
鼠标预选测试对象
CODEX16CTX
CODEX16CTXINFO
DBMOD
```

要求：

- schema/version 为 `codex.autocad.cad-context/2`。
- `entityCount = parsedEntityCount + unsupportedEntityCount`。
- 全部支持且未超限时 `complete=true`。
- Palette 摘要、计数和 canonical JSON 可见。
- DBMOD 前后保持一致。

不要把真实 canonical JSON、图纸路径、图名、Handle、选择哈希或上下文哈希粘贴到聊天。

### C. 未知对象或受限占位：通过

只在已有安全测试对象中选择一个当前不属于 19 类的实体；不要为测试安装垂直产品或读取
代理对象私有数据。与一个已支持对象组成混合选区后执行 `CODEX16CTX`。

要求：捕获整体成功，未知对象形成受限 placeholder，`unsupportedEntityCount >= 1`、
`complete=false`，已支持对象仍被解析；不得回退为 v1，也不得泄露异常堆栈或外部路径。

### D. v2 Agent 对话：通过

除非 M1 改动 Agent/Bridge 请求路径，否则不需要重复 happy path。用户已确认真实 v2
CAD 上下文可回答且第二轮保留前文标记。

```text
CODEX16AGENTSTART
```

在线后连续提问两轮：第一轮要求仅按当前选区概括对象类型和完整性；第二轮追问上一轮中的
一个对象。要求同一会话连续返回，AgentHost 接受 v2 上下文，不出现 v1 回退提示。

### E. 旧上下文失效：缓存清除通过，实际发送拒绝未验证

图纸切换后的 `cleared-document-activated` 已实测。用户随后调用 `CODEX16ASK` 但在输入
提示阶段取消，没有真正提交问题，因此发送前 fail-closed 仍在剩余实机手册中。

另做一个受控样本：提交问题后立即切换文档，只记录是否出现“上下文已失效”或正常完成；
不得为了制造竞态反复高速切换生产图纸。

### F. 清除、停止和残留：清除通过；残留以 P0 证据为准

```text
CODEX16CTXCLEAR
CODEX16CTXINFO
CODEX16AGENTSTOP
CODEX16AGENTSTOP
CODEX16PALINFO
DBMOD
```

要求上下文清除、重复 STOP 幂等、AutoCAD 可继续使用、DBMOD 不变；随后由用户在任务管理器
或只读进程检查中确认本候选 AgentHost 残留为 0。

## 反馈格式

只需反馈：候选 ID、各分段通过/失败、对象类型与字段是否一致、完整性计数、DBMOD 是否
不变、是否出现错误类型、AgentHost 残留数量。不要提供真实图纸内容、路径、JSON 或哈希。

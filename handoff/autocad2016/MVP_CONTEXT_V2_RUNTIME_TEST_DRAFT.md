# AutoCAD 2016 CadContextJson v2 实机测试草案

状态：**P1 候选已自动化冻结，允许用户人工执行；当前仍未取得 live 证据。**

本手册对应候选：

```text
C:\tmp\CodexForAutoCAD-context-v2\artifacts\autocad2016-mvp-context-v2-v032-4d3386d9-751b97c7-7216527a\Codex.AutoCAD.Host.2016.dll
Host SHA-256: 4D3386D9A825B2842290ACB51376FBA6BE6603F49295E606F8C9F3F92B538C08
AgentHost/Codex.AutoCAD.AgentHost.exe SHA-256: 751B97C7B17B970D01D625DDD197E1868150AAB5C235812C662AB70B919B0C67
```

执行前请确认 AutoCAD 2016 已由用户自行启动，且不要把 P0 候选与本 P1 候选混用。
以下条件已满足，不需要再次审查：

1. P0 `0.3.2` 停止生命周期候选已人工通过并形成独立提交 `8a4ee57`。
2. P0 提交已受控引入 P1，候选冻结提交为 `c174166`；当前线另含采集器和门禁证据提交。
3. 合并后完整门禁为 `235/235`，已生成上述候选 ID、Host/AgentHost SHA-256 和只读包。

## 预期测试范围

### A. 加载与只读边界

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

### B. 支持对象混合选区

优先准备实际可安全创建或从现有测试图取得的对象：Line、Circle、Arc、Polyline、DBText、
MText、BlockReference、Dimension、Hatch、Leader/MLeader、Table。无需为了凑齐 19 类修改
生产图纸；每一类只报告是否存在和字段是否与 AutoCAD 属性/LIST 一致。

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

### C. 未知对象或受限占位

只在已有安全测试对象中选择一个当前不属于 19 类的实体；不要为测试安装垂直产品或读取
代理对象私有数据。与一个已支持对象组成混合选区后执行 `CODEX16CTX`。

要求：捕获整体成功，未知对象形成受限 placeholder，`unsupportedEntityCount >= 1`、
`complete=false`，已支持对象仍被解析；不得回退为 v1，也不得泄露异常堆栈或外部路径。

### D. v2 Agent 对话

```text
CODEX16AGENTSTART
```

在线后连续提问两轮：第一轮要求仅按当前选区概括对象类型和完整性；第二轮追问上一轮中的
一个对象。要求同一会话连续返回，AgentHost 接受 v2 上下文，不出现 v1 回退提示。

### E. 旧上下文失效

在图纸 A 捕获成功后切换到图纸 B，确认 Palette/`CODEX16CTXINFO` 已清除旧上下文；不重新
捕获直接发送问题，必须 fail-closed。然后在图纸 B 捕获新选区并确认可正常提问。

另做一个受控样本：提交问题后立即切换文档，只记录是否出现“上下文已失效”或正常完成；
不得为了制造竞态反复高速切换生产图纸。

### F. 清除、停止和残留

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

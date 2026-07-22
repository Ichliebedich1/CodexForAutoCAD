# M2-A：AutoCAD 2016 图纸级索引实机测试

最后更新：2026-07-22（北京时间）

这是 M2-A 的唯一人工入口。测试必须由用户在自己的 AutoCAD 2016 进程中执行；Codex 不
启动、关闭、重启或操作 AutoCAD。请使用脱敏测试图或副本，不要在生产图纸上启动整图索引。

## 0. 候选和 DLL

候选目录：

```text
C:\\tmp\\CodexForAutoCAD-m2-drawing-index\\artifacts\\autocad2016-m2-drawing-index-v040-2cfbadd8-4028850a-8af00fa8
```

加载前核对：

```powershell
Get-FileHash 'C:\\tmp\\CodexForAutoCAD-m2-drawing-index\\artifacts\\autocad2016-m2-drawing-index-v040-2cfbadd8-4028850a-8af00fa8\\Codex.AutoCAD.Host.2016.dll' -Algorithm SHA256
```

应为：

```text
2CFBADD8FF57F6DAAA4727F1B6DE871D509B92E47A680ECCA669A024CBA786A5
```

在 AutoCAD 中只需 `NETLOAD` 这一份：

```text
Codex.AutoCAD.Host.2016.dll
```

不要单独 NETLOAD `Contracts.dll`、`Bridge.Client.dll`、`Ipc.dll` 或 AgentHost 目录中的
DLL/EXE；它们是候选包的依赖。`AcMgd.dll` 和 `AcDbMgd.dll` 使用 AutoCAD 2016 自带的
20.1.0.0，不从仓库复制。只有测试既有 Codex 对话时，Host 才会按候选包的相对目录启动
AgentHost；本 M2-A 索引命令本身不需要手动启动 AgentHost。

若版本不是 `0.4.0.0`、哈希不一致或候选目录缺文件，停止测试并记录“候选不匹配”。

## 1. 加载和诊断

在干净 AutoCAD 2016 R20.1 进程和脱敏测试图中依次执行：

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

预期：

- Doctor/Palette 显示模块版本 `0.4.0.0`。
- 显示 `CadContextJson: codex.autocad.cad-context/2`、
  `DrawingIndex: codex.autocad.drawing-index/1`、`CadQuery: codex.autocad.cad-query/1`。
- 明确显示 `Codex drawing-query tool: not connected in this host slice`、CAD write disabled、
  plugin save disabled。
- 每次插件命令前后 DBMOD 相同。AutoCAD 自己造成的修改要另行记录，不要归因于插件。

## 2. Palette 和空索引

执行：

```text
CODEX16PAL
CODEX16PALINFO
CODEX16INDEX
```

在范围提示中选择 `Model`；等待 AutoCAD Idle 自然运行后反复执行：

```text
CODEX16INDEXINFO
```

预期过程为 `preparing -> scanning -> ready/partial/limited`。空模型空间最终应为
`ready`、总数和已索引数均为 0、`complete=true`、进度 100。面板不能出现重叠、异常堆栈
或真实路径。

## 3. 五种扫描范围

在不修改图纸的前提下分别建立新索引；每次开始前记录 DBMOD，完成后再次记录：

| 范围提示 | 预期内容 |
| --- | --- |
| `Selection` | 只索引当前预选集；可超过 v2 的 64 实体选择快照上限 |
| `Current` | 当前空间 |
| `Model` | 模型空间 |
| `Layouts` | 所有布局空间 |
| `Drawing` | 模型空间和布局空间 |

`Selection` 流程：保持对象预选状态，立即执行 `CODEX16INDEX`，选择 `Selection`，不要在
预选和命令之间插入会清除选择集的命令。完成后检查 `entityCount`、`indexedEntityCount`、
`unsupportedEntityCount`、`failedEntityCount`、`complete` 和 `status`。

每次均要求：

```text
DBMOD
CODEX16INDEXINFO
DBMOD
```

索引是只读的，DBMOD 不应变化。

## 4. 查询和分页

索引达到 `ready`、`partial` 或 `limited` 后执行：

```text
CODEX16QUERY
```

先选 `All`，记录 `status`、`matches`、`returned`、`complete` 和是否存在 `next`。若有下一
页，执行：

```text
CODEX16QUERYNEXT
```

直到 `next=false`。不得把全部结果或真实图纸信息粘贴到聊天；只反馈页数、计数、状态和
字段是否合理。

再分别验证 `Type`、`Layer`、`Space`、`Block`、`Text`、`Object`：输入精确值（Text 为
包含匹配），确认结果集合符合属性面板或 LIST。查询应只返回受限摘要，不执行 AutoCAD
命令、不写图、不保存。

当前命令 UI 暴露的是单一过滤器；契约层已支持过滤条件组合、包围盒和稳定游标，这些会
在 M2-B 的结构化 Agent 工具中接入并另行测试。

## 5. 大选择集和未知对象

准备 100 个以上已知对象，混合一个或多个代理/未知对象（如测试图中自然存在的对象），
一次预选后执行 `Selection` 索引。预期：

- 不因数量超过 64 而回退到 CadContext v1 或整体失败。
- `published` 概念在 DrawingIndex 中体现为最终 descriptor；状态为 `partial` 时，
  `complete=false`、`unsupportedEntityCount > 0`，已读取实体仍可查询。
- 受限项显示 `actualType`/`readStatus` 的安全摘要；不显示异常堆栈、路径或私有配置。

若选择包含代理对象导致 AutoCAD 本身拒绝打开，请记录对象类型和命令返回的 `status`，不要
为了测试安装额外垂直产品。

## 6. 取消、失效和游标拒绝

### 取消

在足够大的脱敏图纸上启动 `CODEX16INDEX`，扫描仍为 `preparing` 或 `scanning` 时执行：

```text
CODEX16INDEXCANCEL
CODEX16INDEXCANCEL
CODEX16INDEXINFO
```

预期第一次和第二次都安全返回，最终 `status=cancelled`，已发布实体为空；DBMOD 不变。
如果索引在第一条命令前已完成，记录为“取消窗口未捕获”，不要伪造通过。

### 修改、撤销和切换图纸

索引建立后，在测试副本中用 AutoCAD 正常界面做一次可撤销修改，然后执行
`CODEX16INDEXINFO` 和 `CODEX16QUERYNEXT`。预期旧索引变为 `stale`，旧游标拒绝，不能返
回修改前结果。撤销后必须重新建立索引，不能假设旧索引自动恢复。

对 `Current` 范围切换布局后重复检查；对 `Drawing` 范围切换布局不应单独造成失效，但
文档 revision/DBMOD 变化仍必须失效。

### 关闭和退出

在索引正在准备或扫描时，使用 AutoCAD 正常关闭测试图/进程，不先强制结束进程。观察：

- 不崩溃、不死锁、不弹出未处理异常堆栈。
- `Codex.AutoCAD.AgentHost.exe`（如未启动则无需检查）和相关管道无残留。
- 下次启动可重新加载 DLL，不复用旧索引。

这是本切片的重点风险项，失败时保留命令行和时间，不要立即强制结束进程。

## 7. 1k/10k/50k 脱敏基准记录

每张图分别记录：对象总数、最终状态、已索引数、占位数、开始到完成耗时、最长主界面
卡顿体感、AutoCAD 是否持续可操作、峰值工作集（可观察则记录）和 DBMOD 前后值。

```text
样本：1k / 10k / 50k
范围：Drawing
status：
entityCount / indexedEntityCount：
unsupported / failed：
扫描耗时：
最长卡顿：
查询 All 首页/总页数：
AutoCAD 可操作：是/否
DBMOD before -> after：
```

没有真实样本或没有计时数据时标为“未验证”。自动化 50,000 个合成实体契约测试不能替
代这里的 AutoCAD 运行时证据。

## 8. 证据和隐私规则

只反馈：候选版本/哈希前缀、命令状态、计数、页数、DBMOD 是否保持、是否崩溃或残留。

不要发送：完整 canonical JSON、完整查询实体列表、真实图纸路径/图名、Handle/选择哈希、
上下文哈希、用户名、TRUSTEDPATHS、API Key、token、完整环境变量或异常堆栈。

## 9. 暂不计入 M2-A 通过的项目

- M2-B Codex 动态 `drawing.query` 工具和 AgentHost/Bridge 请求。
- 19 类对象逐字段实机语义核对（M3）。
- 125%/150% DPI、M1 故障矩阵和精确 `0.3.3.0` 候选实机绑定。
- 50k 的冻结性能预算、长时间 soak、沙箱、CAD 写入和自动保存验证。

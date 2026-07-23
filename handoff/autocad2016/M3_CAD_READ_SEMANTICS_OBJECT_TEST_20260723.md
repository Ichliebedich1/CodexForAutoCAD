# M3：CAD 读取语义与对象覆盖测试目录

最后更新：2026-07-23（北京时间）

## 当前状态与边界

本文件是 M3 的中文对象目录和实机核对模板。`0.4.2.0` 自动化候选已经冻结，但尚未取得
AutoCAD `NETLOAD` 证据；因此本文件不能把任何 M3 项目写成实机通过。

精确候选身份：

```text
Candidate directory:
C:\tmp\CodexForAutoCAD-m3-highvalue-limited\artifacts\autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7

NETLOAD target:
Codex.AutoCAD.Host.2016.dll

Host version: 0.4.2.0
Host SHA-256: B5081C63DD11BD36706B529EC28C58BB1DEA22FEF6D50BA0E76C5E3E4CE67879
AgentHost SHA-256: E3DBE95546D193D9AF451A0420E648085F9E2AF9ECCC6E956BD85BC26ACDA615
Manifest SHA-256: 2633642C2F993FC320A0662FD95D4BC900CD4A453ABCDD6B7BEB7C596EF30348
Evidence SHA-256: EA27EC4E9E9CE95D8CB488AB42B39260AD5EA71766907FEF56C0F36C630DD2B4
```

对应的脱敏冻结证据是
`evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7.json`。
它明确记录 `NetLoadVerified=false`、`AutoCadLiveEvidence=false`，并记录冻结过程没有启动、
重启或操作 AutoCAD。

这一纵切没有开启 CAD 写入、插件保存、命令字符串、LISP、脚本或反射式 CAD 调用。它只在
既有只读调用链中增加受限对象统计和块详情：

- `CODEX16TYPEINFO` 输出 19 类现有强类型对象的中文名称和人工创建入口。
- `CODEX16CTX` 的可读摘要、`CODEX16CTXINFO` 和 Palette 对选择快照显示未支持、数据超限、
  读取失败的原因总数及实际类型/数量。
- `CODEX16INDEXINFO` 和整图索引 Palette 状态对索引占位显示同一类统计。
- 类型名只保留受限的 DXF/CLR 类型标识；不拼接图层、Handle、图纸路径、对象文字或其他
  实体数据。最多保留 `4,096` 个类型桶，超过时只显示未记录的对象数量。
- `BlockReference` 的 `blockDetails` 仅走 DrawingIndex → CadQuery → 认证 Bridge → Agent
  工具，不修改冻结的 CadContextJson v2。它有界保留属性/动态属性、嵌套块计数与深度、布局
  标志和安全 Xref 布尔元数据；不读取外部 Xref 定义或真实路径，受限时标记 `limited`。
- Region、Solid、Mesh、Surface、RasterImage、Underlay、Proxy 和 Wipeout 只在
  DrawingIndex/CadQuery 中取得受限分类；它们使用既有通用摘要字段，始终标记
  `Unsupported=true`、`data_limited`，不成为 CadContextJson v2 的新增强类型 payload。

选择快照仍是兼容的 `CadContextJson v2`，最多 `64` 个实体和 `256 KiB` canonical JSON；
整图或大数量级的测试应使用独立的 DrawingIndex/CadQuery 调用链，不应放大选择快照限制。

## 当前真实调用链

```text
只读选择捕获
  -> CadContextJson v2 + 受限 placeholder
  -> CadReadTypeStatistics
  -> 可读摘要 / CODEX16CTXINFO / Palette

Idle 分片 DrawingIndex
  -> CadQueryEntity 受限 placeholder
  -> DrawingIndexAccumulator + CadReadTypeStatistics
  -> CODEX16INDEXINFO / Palette
```

统计是显示层和诊断层的补充；它不改变 v2、DrawingIndex 或 CadQuery 的 wire schema，也不让
未知对象伪装为已完整解析。

## 19 类强类型对象目录

在上述精确 M3 候选中，先执行 `CODEX16TYPEINFO` 核对此目录。下表的“首要核对字段”
是当前可读摘要/Canonical JSON 应有的重点，不是对完整语义已完成的承诺。

| # | 中文名称（强类型） | 人工创建或取得方式 | 首要核对字段 |
| --- | --- | --- | --- |
| 01 | 直线（Line） | 绘图功能区 → 直线 | layer、start、end |
| 02 | 圆（Circle） | 绘图功能区 → 圆 | layer、center、radius、normal |
| 03 | 轻量多段线（Polyline） | 绘图功能区 → 多段线 | layer、closed、elevation、normal、vertices、bulge |
| 04 | 单行文字（DBText） | 注释功能区 → 单行文字 | layer、text、position、height、rotation |
| 05 | 多行文字（MText） | 注释功能区 → 多行文字 | layer、text、location、textHeight、rotation |
| 06 | 块参照（BlockReference） | 先定义块，再从插入功能区放置块 | layer、position、rotation、scale、effectiveName |
| 07 | 圆弧（Arc） | 绘图功能区 → 圆弧 | layer、center、radius、startAngle、endAngle、normal |
| 08 | 椭圆（Ellipse） | 绘图功能区 → 椭圆 | layer、center、majorAxis、radiusRatio、normal |
| 09 | 样条曲线（Spline） | 绘图功能区 → 样条曲线 | layer、degree、controlPoints、fitPoints、closed |
| 10 | 点（DBPoint） | 绘图功能区 → 多点 | layer、position、normal |
| 11 | 射线（Ray） | 绘图功能区 → 射线 | layer、basePoint、secondPoint |
| 12 | 构造线（Xline） | 绘图功能区 → 构造线 | layer、basePoint、secondPoint |
| 13 | 旧式二维多段线（Polyline2d） | 使用已有旧格式测试图；普通多段线通常不是此类型 | layer、closed、vertices、normal |
| 14 | 三维多段线（Polyline3d） | 三维建模功能区 → 三维多段线 | layer、closed、vertices |
| 15 | 标注（Dimension） | 注释功能区 → 标注 | layer、dimensionType、measurement、dimensionText |
| 16 | 图案填充（Hatch） | 绘图功能区 → 图案填充 | layer、patternName、patternScale、loopTypes |
| 17 | 旧式引线（Leader） | 使用已有旧格式引线测试图 | layer、vertices、annotationType |
| 18 | 多重引线（MLeader） | 注释功能区 → 多重引线 | layer、leaderLines、text |
| 19 | 表格（Table） | 注释功能区 → 表格 | layer、rows、columns、position、display text |

块属性、动态块、嵌套块、布局/空间和安全 Xref 元数据已经进入精确候选的自动化调用链，
但仍没有实机字段证据。复杂块、复杂标注、复杂 Hatch、复杂 MLeader 与复杂 Table 仍是 M3
后续语义工作，不能由“强类型名称出现”或 `blockDetails` 出现推断为完全支持。

## 离线核心示例测试图

仓库现在提供可重复生成的脱敏 `AC1015` DXF 核心 fixture，而不是提交真实 DWG 或要求测试人员
从零手工创建每个基础对象：

```text
生成器：scripts/new-autocad2016-m3-core-read-fixture.ps1
离线校验器：scripts/verify-autocad2016-m3-core-read-fixture.ps1
冻结期望：handoff/autocad2016/m3-fixtures/M3_CORE_READ_FIXTURE_V1.expected.json
输出：m3-core-read-fixture-v1.dxf + m3-core-read-fixture-v1.manifest.json
```

它固定覆盖以下 14 个实体变体：Line、Circle、Arc、Ellipse、Spline、DBPoint、Ray、Xline、
Polyline、DBText、MText、带属性且含嵌套定义的 BlockReference、Polyline2d、Polyline3d。
输出只使用 ASCII 测试文字和 `M3_CORE`/`M3_LEGACY`/`M3_BLOCKS` 图层；生成器不启动、控制
或向 AutoCAD 发送命令。它的确定性、文件集、实体顺序、层和 2D/3D 多段线标志由离线校验器
锁定为 `6/6`。

Dimension、Hatch、Leader、MLeader 和 Table 没有被伪造为通用 DXF 样本，因为这些对象的
R20.1 语义依赖样式、关联或对象图结构。它们仍须在脱敏的专用测试图中由测试人员通过正常
界面创建；高价值受限对象和 Xref 也仍须单独实机核对。

## 未支持与高价值受限对象

下列对象不是本纵切新增的完整强类型 payload。选择快照继续将它们作为既有受限 placeholder
处理；只有整图索引与 CadQuery 会将它们归入下列有界类别，并保留通用的类型、图层、空间和
范围摘要。两条路径都会降低完整性，而不是让整次捕获失败：

- 面域（Region）、二维/三维实体（Solid）、三维面/旧式网格/细分网格（Mesh）、曲面（Surface）。
- 光栅图像、PDF/DWF/DGN 参考底图（Image/Underlay）。
- 垂直产品代理对象（Proxy Entity）和遮罩（Wipeout）。

Xref 的外部真实路径不允许进入可读摘要、Canonical JSON、日志或人工测试回报。长文字、
复杂 Hatch、Table 和 Spline 发生限额时必须标记 `data_limited`，不能假装完整。

## 精确候选的实机核对模板（当前待用户执行）

使用上方的精确目录、版本、SHA-256 和 manifest。请在脱敏副本或专用测试图中人工操作；
本项目不会启动、关闭或控制 AutoCAD，也不会保存图纸。先人工 `NETLOAD` 该候选根目录中的
`Codex.AutoCAD.Host.2016.dll`，再执行下列测试。

1. 对上述 14 类基础对象，可在 AutoCAD 外先生成一个新目录：

   ```powershell
   .\scripts\new-autocad2016-m3-core-read-fixture.ps1 -OutputDirectory 'C:\tmp\CodexM3CoreFixture'
   ```

   然后由测试人员在 AutoCAD 正常界面打开生成的 `m3-core-read-fixture-v1.dxf`；不要保存或
   覆盖该 fixture。其余 5 类复杂对象和受限对象仍在独立脱敏测试图中手工准备。
2. 人工准备一类或少量同类对象后，先执行 `DBMOD` 记下此时的值。对象创建本身可以改变
   `DBMOD`；本步骤只验证随后插件只读捕获不再改变它。
3. 鼠标预选不超过 64 个对象，预选完成后不要插入其他命令，立即执行：

   ```text
   CODEX16CTX
   CODEX16CTXINFO
   CODEX16PALINFO
   DBMOD
   ```

4. 用 AutoCAD 属性面板或 `LIST` 本地核对表中字段。对块参照另核对 `blockDetails` 的属性、
   动态属性、嵌套计数/深度、布局和 Xref 布尔值；外部 Xref 不应显示真实路径。不要把完整 JSON、真实图名、路径、
   Handle、选择哈希或上下文哈希贴入聊天或提交 Git；只记录“字段一致/不一致”和命令
   `status`。
5. 若选择快照中出现未支持或受限对象，确认捕获仍发布，`complete=false`，且信息区出现例如：

   ```text
   Placeholder reasons: 未支持类型 1，数据超限 0，读取失败 0
   Placeholder actual types: 代理对象(ACAD_PROXY_ENTITY) x1
   ```

6. 对整图或多于 64 个对象的样本，使用 M2 的 `CODEX16INDEX` / `CODEX16INDEXINFO`，而不是
   用选择快照规避上限。对本节列出的高价值对象，确认索引/查询结果使用上述类别，且每项均为
   `Unsupported=true` 与 `data_limited`；不要把这项核对记为完整字段读取。

如果任何对象让整次捕获返回 `published=false`、抛出未处理异常、泄露路径/实体内容，或读取
后让 `DBMOD` 改变，应保留脱敏的命令行 `status`、版本和精确候选 SHA-256，报告为失败。

## 当前自动化证据与未完成项

本开发纵切已具备以下源码级覆盖：

- 选择快照：实际类型按大小写归并、原因分类、中文显示、无图层/Handle 泄露。
- DrawingIndex：实际类型统计有界，超过类型桶上限时保留总数而不扩大内存。
- 真实 mapper 调用链：CanonicalSelectionHash v2 → CadContextJsonV2Mapper → 可读摘要。
- 中文目录：恰好 19 个编号条目，且不包含本机路径。
- 块详情：有界属性/动态属性、嵌套块、布局和安全 Xref 元数据通过 Contracts、Host、Bridge
  和 Agent 工具的真实 JSON 传输；深拷贝、内存预算、IPC 以及无 Xref 路径字段均有回归。
- 核心示例图：14 个安全直接编码的实体变体通过确定性 DXF generator/validator 固定；它不
  取代 AutoCAD 内加载、字段读取或剩余 5 类复杂对象的实机证据。
- 高价值受限类别：8 个 DrawingIndex/CadQuery 分类在 Contracts 和 Host v2 规格中验证可查询、
  可按 `includeUnsupported=false` 排除，且不能伪装为完整读取；R20.1 Probe 还编译检查其
  12 个关联实体类型。

当前自动门禁结果：

- Contracts `87/87`；Bridge Client net45/net8 各 `29/29`；Bridge `39/39`；AgentRuntime
  `33/33`；Host MVP `53/53`；完整 Phase 2 `319/319`。
- R20.1 API 双 Shell Probe 为 `29 passed / 8 expected failed`，两个 Shell 的成员集合和
  Probe DLL 哈希一致。脱敏聚合 evidence 为
  `evidence/v2-api-surface-probe-m3-cross-shell-20260723.json`。
- 禁止 API、AgentHost doctor、敏感信息和 `git diff --check` 均通过；candidate manifest 与
  evidence 已冻结。
- 当前 R20.1/net45/x64 Host A/B 输出逐字节一致，Autodesk DLL 复制数为 `0`。Host
  `0.4.2.0` SHA-256 为
  `B5081C63DD11BD36706B529EC28C58BB1DEA22FEF6D50BA0E76C5E3E4CE67879`。
- 自动化候选目录为
  `artifacts/autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7/`；manifest SHA-256
  为 `2633642C2F993FC320A0662FD95D4BC900CD4A453ABCDD6B7BEB7C596EF30348`，冻结 evidence 为
  `evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-b5081c63-e3dbe955-0b06bcf7.json`。
  该自动化候选仍没有 AutoCAD `NETLOAD` 证据。

仍未完成：全部 19 类对象与块详情的实机字段证据、5 类复杂对象及高价值受限对象的脱敏
示例图资产、复杂对象语义、复杂块/Xref 边界，以及高价值受限类别的实机读取证据。M2 的
1k/10k/50k 实机性能和查询证据仍独立待办，不因本文件而完成。

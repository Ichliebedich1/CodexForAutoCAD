# M3：CAD 读取语义与对象覆盖测试目录

最后更新：2026-07-24（北京时间）

## 当前状态与边界

本文件是 M3 的中文对象目录和实机核对模板。

> **停止：下面这组候选身份已于 2026-07-26 失效，不要按它执行实机测试。**
>
> M2、M3 和 M4 线已全部吸收进 `main`（`77a6cdf`），汇合改变了 Host.2016 的源码，
> 这组哈希描述的是一个不会出货的构建。执行 19 类字段矩阵前必须先在 `main` 上重新
> 构建候选并替换本节。当前 `main` 的 Phase 2 尚未全绿，详见 `CURRENT_STATE.md` 的
> 「主线汇合与门禁状态」一节。
>
> 第 1 节起的中文对象目录和逐类核对模板本身不受影响，仍然适用。

历史记录——汇合前的 source-bound `0.4.2.0` 自动化候选：

- 候选 ID：
  `autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc`
- 源码提交：`00fe879a0ac056fab48c955e71d63c51ef3577d9`
- Host SHA-256：
  `467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D`
- AgentHost EXE SHA-256：
  `44CD544883F7BA7B790044220FAE3C5DDD2515C589CE3CC6910260F6C6795EF5`
- manifest SHA-256：
  `02B5AE218CAFC19892F7CF086330D46EB237131A67BA61700D644E6A7E74D520`
- 候选目录：
  `artifacts/autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc`

它尚未取得 AutoCAD `NETLOAD`、19 类字段矩阵或复杂块/Xref 实机证据；因此本文件不能把
任何 M3 实机项目预先写成通过。

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

加载上述精确 M3 候选后，先执行 `CODEX16TYPEINFO` 核对此目录。下表的“首要核对字段”
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
但仍没有实机字段证据。复杂块、复杂标注、复杂 Hatch、复杂 MLeader 与复杂 Table 仍是
M3 后续语义工作，不能由“强类型名称出现”或 `blockDetails` 出现推断为完全支持。

## 未支持与高价值受限对象

下列对象不是本候选新增的完整强类型 payload。若出现在整图索引中，应以可查询但明确
`data_limited` 的安全类别保留受限类型和范围摘要，而不是伪装为完整读取：

- 面域（Region）、三维实体（Solid）、网格（Mesh）、曲面（Surface）。
- 光栅图像（RasterImage）、PDF/DWF/DGN 参考底图（Underlay）。
- 垂直产品代理对象（Proxy Entity）、区域覆盖（Wipeout）。

这 8 类已经进入自动化分类和查询门禁，但仍需 AutoCAD 实机确认对象类型、范围摘要、降级
状态和无路径泄露。Xref 的外部真实路径不允许进入可读摘要、Canonical JSON、日志或人工
测试回报。长文字、复杂 Hatch、Table 和 Spline 发生限额时必须标记 `data_limited`，不能
假装完整。

## 精确候选实机核对模板（待执行）

请只加载本文件顶部的精确 M3 候选，在脱敏副本或专用测试图中人工操作；本项目不会启动、
关闭或控制 AutoCAD，也不会保存图纸。

1. 人工准备一类或少量同类对象后，先执行 `DBMOD` 记下此时的值。对象创建本身可以改变
   `DBMOD`；本步骤只验证随后插件只读捕获不再改变它。
2. 鼠标预选不超过 64 个对象，预选完成后不要插入其他命令，立即执行：

   ```text
   CODEX16CTX
   CODEX16CTXINFO
   CODEX16PALINFO
   DBMOD
   ```

3. 用 AutoCAD 属性面板或 `LIST` 本地核对表中字段。对块参照另核对 `blockDetails` 的属性、
   动态属性、嵌套计数/深度、布局和 Xref 布尔值；外部 Xref 不应显示真实路径。不要把完整 JSON、真实图名、路径、
   Handle、选择哈希或上下文哈希贴入聊天或提交 Git；只记录“字段一致/不一致”和命令
   `status`。
4. 若出现未支持或受限对象，确认捕获仍发布，`complete=false`，且信息区出现例如：

   ```text
   Placeholder reasons: 未支持类型 1，数据超限 0，读取失败 0
   Placeholder actual types: 代理对象(ACAD_PROXY_ENTITY) x1
   ```

5. 对整图或多于 64 个对象的样本，使用 M2 的 `CODEX16INDEX` / `CODEX16INDEXINFO`，而不是
   用选择快照规避上限。确认 `Placeholder actual types` 与实测对象类别相符。

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

当前自动门禁结果：

- Contracts `96/96`；Bridge Client net45/net8 各 `30/30`；Bridge `39/39`；AgentRuntime
  `34/34`；Host MVP `54/54`；完整 Phase 2 `323/323`。
- R20.1 API 双 Shell Probe 为 `29 passed / 8 expected failed`，两个 Shell 的成员集合和
  Probe DLL 哈希一致。脱敏聚合 evidence 为
  `evidence/v2-api-surface-probe-m3-cross-shell-20260723.json`。
- 禁止 API、AgentHost doctor、敏感信息和 `git diff --check` 均通过。
- 当前 R20.1/net45/x64 Host A/B 输出逐字节一致，Autodesk DLL 复制数为 `0`。Host
  `0.4.2.0` SHA-256 为
  `467BC9711F6BD9598D7E788CB211A39D8DEE47428748CB0BDB3AF81F6322428D`。
- 14 实体记录的确定性脱敏核心 DXF fixture 已通过双次生成、独立解析和哈希门禁 `6/6`。
- source-bound 候选 evidence 为
  `evidence/m3-read-semantics-candidate-autocad2016-m3-read-semantics-v042-467bc971-44cd5448-f5ab78bc.json`。

仍未完成：19 类对象与块详情的实机字段证据、无法由核心 DXF 表达的脱敏 AutoCAD 测试图、
复杂对象语义、复杂块/Xref 边界，以及 8 类高价值对象的实机受限读取证据。M2 的
1k/10k/50k 实机性能和查询证据仍独立待办，不因本文件而完成。

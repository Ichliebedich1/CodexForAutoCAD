# CadContextJson v2 冻结候选

状态：契约已冻结，Host.2016 真实对象捕获为构建候选。net45/net8 固定向量和目标机
R20.1 原版程序集编译已经通过；Runtime、Palette、Bridge v2 协商和 AutoCAD 2016 实机
混合选区仍未完成，因此不得标记为运行验证完成。

## 1. 不可破坏的 v1 边界

- v1 schema 保持 codex.autocad.cad-context/1。
- 不修改 CadContextJsonV1Contracts.cs、CadContextJsonV1Codec.cs 的线格式和字段顺序。
- v1 固定向量继续是 2225 字节，SHA-256
  c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423。
- v2 使用独立类型、验证器、Codec、固定向量和 Bridge 请求类型。
- 不允许通过 Host 版本字符串猜测上下文版本。

## 2. R20.1 审计来源

本轮只读审计使用目标机原版文件：

    D:\AutoCAD 2016\acdbmgd.dll
    Assembly: Acdbmgd, Version=20.1.0.0
    File version: 20.1.49.0.0
    SHA-256: 9C27F4A71E4DFAEC393B53AB15A657FA37CA9F8A7B09E0522894AB3B354603BB
    Authenticode: Valid, Autodesk, Inc.

通过 .NET Framework SDK ildasm /pubonly 核对公开 getter 和只读方法，不依赖网络文档，
不启动或操作 AutoCAD。

## 3. 顶层和选择完整性

顶层字段顺序保持：

    schema
    schemaVersion
    capturedAtUtc
    source
    egressRisk
    document
    selection

v2 固定：

    schema = codex.autocad.cad-context
    schemaVersion = 2

selection 固定字段：

    snapshotHash
    entityCount
    parsedEntityCount
    unsupportedEntityCount
    complete
    entities

必须满足：

    entities.length == entityCount
    parsedEntityCount + unsupportedEntityCount == entityCount
    parsedEntityCount == entityType != unsupported 的数量
    unsupportedEntityCount == entityType == unsupported 的数量
    complete == (unsupportedEntityCount == 0)

任何未知类型、已知类型读取失败或实体数据超过 v2 限额，都必须进入 entities，不得静默
丢弃，也不得让同组选区中其余合法实体失败。

## 4. 图元公共字段

每个图元固定包含：

    handle
    ownerSpaceHandle
    entityType
    stateHash
    layer
    一个且仅一个强类型 payload

图元按数值 Handle 排序。几何数组保持 CAD 原始顺序。

## 5. v2 支持类型及字段

| entityType | R20.1 类型 | v2 payload 字段 |
| --- | --- | --- |
| line | Line | start, end |
| circle | Circle | center, radius, normal |
| polyline | Polyline | closed, elevation, normal, vertices(position, bulge) |
| dbText | DBText | text, position, height, rotation |
| mText | MText | text, location, textHeight, rotation |
| blockReference | BlockReference | position, rotation, scale, effectiveName, isDynamic, isExternalReference |
| arc | Arc | center, radius, startAngle, endAngle, normal |
| ellipse | Ellipse | center, majorAxis, radiusRatio, startParameter, endParameter, normal |
| spline | Spline | degree, isRational, hasFitData, controlPoints, fitPoints |
| point | DBPoint | position, normal, ecsRotation |
| ray | Ray | basePoint, secondPoint |
| xline | Xline | basePoint, secondPoint |
| polyline2d | Polyline2d + Vertex2d | closed, elevation, normal, vertices(position, bulge, startWidth, endWidth) |
| polyline3d | Polyline3d + PolylineVertex3d | closed, vertices |
| dimension | Dimension | dimensionType, measurement, dimensionText, textPosition, textRotation, normal, styleName |
| hatch | Hatch | associative, isGradient, isSolidFill, patternName, patternAngle, patternScale, elevation, normal, loopTypes |
| leader | Leader | isSplined, hasArrowHead, annotationType, normal, vertices |
| mLeader | MLeader | contentType, normal, text, leaderLines(vertices) |
| table | Table | position, direction, rows, columns, width, height, styleName, cells(row, column, text) |
| unsupported | 任何其他 Entity 或失败的已知类型 | dxfName, reason |

派生字段不重复传输。例如 Arc 不同时传输 Length 和 TotalAngle；Ellipse 不同时传输
主/次半径和可由 majorAxis + radiusRatio 得到的值。

## 6. 受限占位

unsupported 只允许：

    dxfName
    reason

reason 是闭集：

    unknown-entity-type
    entity-read-failed
    entity-data-limit

禁止包含异常消息、堆栈、代理对象私有数据、扩展字典、图名、图纸路径、外部引用路径或任意
二进制数据。公共 handle、ownerSpaceHandle、stateHash、layer 仍保留。

## 7. 限额

    MaximumEntities = 64
    MaximumPolylineVertices = 256
    MaximumSplinePoints = 256
    MaximumHatchLoops = 128
    MaximumLeaderVertices = 256
    MaximumMLeaderLines = 64
    MaximumMLeaderVertices = 256
    MaximumTableCells = 64
    MaximumTextCharacters = 2048
    MaximumNameCharacters = 255
    MaximumCanonicalJsonBytes = 256 KiB
    MaximumCoordinateMagnitude = 1e9

超过单实体限额时，该实体转换为 unsupported/entity-data-limit，其余实体继续发布。不得只截取
部分顶点、部分表格单元格或部分引线而不标记。

## 8. R20.1 API 风险边界

- 只通过 StartOpenCloseTransaction 和 OpenMode.ForRead 读取。
- Polyline2d/3d 的顶点 ObjectId 仍在同一只读事务中打开。
- Spline.GetControlPointAt、GetFitPointAt、Leader.VertexAt、
  MLeader.GetLeaderIndexes/GetLeaderLineIndexes/VerticesCount/GetVertex 都必须受计数限额约束。
- Table 只读取 Cells[row,column].TextString；不读取字段表达式、数据链接、公式内部结构或外部源。
- Hatch 只读取 loop 类型摘要，不复制 Curve2d、关联 ObjectId 或边界实体。
- MLeader 只读取文本副本和引线顶点；不读取块属性、字段或样式内部对象。
- 任一 getter 抛出异常时，只生成脱敏占位，不将异常文本写入 JSON。

## 9. Bridge 协商

v1 与 v2 必须使用显式、独立的能力和请求类型：

- v1 客户端继续发送冻结的 AgentTurnStartRequest。
- v2 增加明确的 v2 turn 请求，携带 CadContextJsonV2。
- AgentHost 能力响应明确列出支持的 schema/version。
- 不允许把 v2 JSON 填入 v1 字段，也不允许旧 AgentHost 静默按 v1 解析。
- HMAC、sequence、nonce、防重放和请求大小限制继续复用既有认证 IPC。

## 10. 验证门禁

冻结前至少证明：

当前契约固定向量候选：

    bytes = 6678
    SHA-256 = 21cc9378a618022c5bc21cb35c58db7818272c33d0adc5b5bd8618b4a638c3b4

1. v1 固定向量字节和 SHA-256 完全不变。
2. v2 的 19 个强类型 payload 通过验证并产生固定 canonical 向量。
3. net45/net8 产生相同 UTF-8 字节和 SHA-256。
4. 输入图元顺序不改变 canonical JSON。
5. 支持对象 + unsupported 能发布，计数和 complete=false 正确。
6. 未知对象、读取失败和超限不会静默丢失。
7. 规范 JSON 不含图名、路径、异常消息或敏感配置。
8. 使用目标机原版 R20.1 程序集 Release 编译通过。
9. 用户在 AutoCAD 2016 中人工 NETLOAD 冻结候选并验证混合选区、DBMOD 不变、插件不保存。

## 11. Host 捕获构建检查点（2026-07-21）

- `ReadOnlySelectionCaptureV2` 已实现 19 个强类型 payload，并在单实体范围内隔离
  `GetObject`、身份、公共字段和 payload getter 失败。
- 未知类型、读取失败和单实体数据超限分别生成受限占位；无异常消息、堆栈或图纸路径
  进入 JSON。
- MLeader 在读取索引集合和分配顶点数组前先检查 R20.1 计数，并对总顶点数做溢出安全
  累加。
- Host v2 Specs 在 net45/net8 均为 `12/12`，stdout 完全一致；选择快照冻结向量为
  `147` 字节、SHA-256
  `0ba4970c01da7877a41c9de960f1decd090d0f6646e9eff7a979c71db5bb8990`。
- Contracts 在 net45/net8 均为 `39/39`，v1/v2 冻结向量保持不变。
- 两份独立临时源码副本使用目标机原版 R20.1 程序集 locked restore/Rebuild，Host DLL
  均为 `105984` 字节、SHA-256
  `700A0BF9CBD976625F1EF4D7BE820DD257263295466EDA13FBC8109D89F96DD0`；输出中 Autodesk
  DLL 数量为 `0`。
- 本检查点没有启动、唤醒、关闭或操作 AutoCAD，也没有执行 `NETLOAD`。该 DLL 哈希只
  表示可重复构建候选，尚未与任何 AutoCAD 运行时绑定。

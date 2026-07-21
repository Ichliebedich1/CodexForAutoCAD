# CadContextJson v2 人工实机测试包

状态：人工测试说明。仅在测试者完成全部矩阵并在本地 Palette 核对后，才可填写
`CAD_CONTEXT_V2_RUNTIME_TEST_WORKSHEET.md` 并提交脱敏 evidence。

## 1. 前置条件

- AutoCAD 2016 已安装并可正常启动
- 已通过 `verify-autocad2016-contract-v1.ps1` 的契约门禁
- 已获取冻结候选 DLL（commit `0ceb123` 或后续经审核的候选）
- 已确认 Host DLL SHA-256 与候选 manifest 一致
- 已确认模块版本和 schema v2

## 2. 测试矩阵

### 样本 A：基础强类型

| # | 图元类型 | entityType | 测试要点 |
|---|---------|------------|---------|
| A1 | Line | line | start, end 坐标 |
| A2 | Circle | circle | center, radius, normal |
| A3 | Polyline | polyline | closed, elevation, normal, vertices |
| A4 | DBText | dbText | text, position, height, rotation |
| A5 | MText | mText | text, location, textHeight, rotation |
| A6 | BlockReference | blockReference | position, rotation, scale, effectiveName, isDynamic, isExternalReference |

### 样本 B：扩展强类型

| # | 图元类型 | entityType | 测试要点 |
|---|---------|------------|---------|
| B1 | Arc | arc | center, radius, startAngle, endAngle, normal |
| B2 | Ellipse | ellipse | center, majorAxis, radiusRatio, startParameter, endParameter, normal |
| B3 | Spline | spline | degree, isRational, hasFitData, controlPoints, fitPoints |
| B4 | DBPoint | point | position, normal, ecsRotation |
| B5 | Ray | ray | basePoint, secondPoint |
| B6 | Xline | xline | basePoint, secondPoint |

### 样本 C：旧式多段线和标注

| # | 图元类型 | entityType | 测试要点 |
|---|---------|------------|---------|
| C1 | Polyline2d | polyline2d | closed, elevation, normal, vertices(position, bulge, startWidth, endWidth) |
| C2 | Polyline3d | polyline3d | closed, vertices |
| C3 | Dimension | dimension | dimensionType, measurement, dimensionText, textPosition, textRotation, normal, styleName |

### 样本 D：填充、引线和表格

| # | 图元类型 | entityType | 测试要点 |
|---|---------|------------|---------|
| D1 | Hatch | hatch | associative, isGradient, isSolidFill, patternName, patternAngle, patternScale, elevation, normal, loopTypes |
| D2 | Leader | leader | isSplined, hasArrowHead, annotationType, normal, vertices |
| D3 | MLeader | mLeader | contentType, normal, text, leaderLines(vertices) |
| D4 | Table | table | position, direction, rows, columns, width, height, styleName, cells(row, column, text) |

### 样本 E：占位类型

| # | 图元类型 | entityType | 测试要点 |
|---|---------|------------|---------|
| E1 | 未知类型 | unsupported | dxfName, reason=unknown-entity-type |
| E2 | 读取失败 | unsupported | dxfName, reason=entity-read-failed |
| E3 | 数据超限 | unsupported | dxfName, reason=entity-data-limit |

## 3. 混合选区测试

至少执行一次混合选区测试，包含：

- 至少 2 个强类型图元
- 至少 1 个 unsupported 占位
- 验证 `entityCount == entities.length`
- 验证 `parsedEntityCount + unsupportedEntityCount == entityCount`
- 验证 `complete == (unsupportedEntityCount == 0)`

## 4. 测试流程

### 4.1 DBMOD 基线

1. 确保 AutoCAD 中无选区（ESC 取消所有选择）
2. 在命令行输入 `DBMOD` 并记录返回值（应为 0）
3. **此步骤必须在未选择状态执行**

### 4.2 预选并捕获

1. 在 AutoCAD 中选择测试矩阵中的图元组合
2. 立即在命令行输入 `CODEX16CTX`
3. 等待 Palette 显示捕获的 JSON
4. **不在 DBMOD 和 CODEX16CTX 之间插入其他命令**

### 4.3 本地核对

在本地 Palette 中核对以下字段（**禁止将以下内容写入聊天或 evidence**）：

- JSON 结构完整性
- 坐标值
- 文字内容
- 图层名
- 块名
- Handle 值
- 上下文哈希

### 4.4 DBMOD 验证

1. 捕获完成后，再次执行 `DBMOD`
2. 验证返回值与步骤 4.1 相同（应为 0）
3. 确认插件未修改图纸

### 4.5 文档切换测试

1. 打开另一个 DWG 文件
2. 验证旧上下文已清除
3. 在新文档中重复步骤 4.1-4.4

## 5. 安全约束

### 5.1 禁止操作

- 不得启动/唤醒/关闭/重启 AutoCAD
- 不得使用 COM、SendKeys、UI Automation、AutoLISP
- 不得使用脚本注入、NETLOAD
- 不得读取或写入 DWG
- 不得修改注册表、SECURELOAD、TRUSTEDPATHS、SAVETIME
- 不得调用保存命令

### 5.2 敏感信息保护

以下信息**禁止**写入聊天、evidence 或任何提交文件：

- JSON 原文
- 坐标值
- 文字内容
- 图层名
- 块名
- Handle 值
- 上下文哈希
- 图纸路径
- 用户名
- TRUSTEDPATHS 配置
- API Key
- 堆栈跟踪

### 5.3 候选身份绑定

每个 evidence 必须绑定：

- candidate ID（候选标识）
- 40 位 commit SHA
- Host DLL SHA-256
- 模块版本
- schema v2

不得用后续构建回填历史。

## 6. 验收标准

- 覆盖全部 19 个强类型
- 覆盖 3 种占位原因
- 至少一次混合选区测试
- 验证 entityCount/parsed/unsupported/complete 关系
- 验证 DBMOD 不变
- 验证文档切换清旧上下文
- 不产生真实 runtime evidence
- 不修改生产代码
- 不自动操作 AutoCAD

## 7. 提交要求

完成全部测试后，填写 `CAD_CONTEXT_V2_RUNTIME_TEST_WORKSHEET.md` 并提交：

- 交接文档
- 说明
- 模板
- 验证器
- 自测

提交信息：

```text
test(host2016): add cad context v2 runtime test kit
```

status 必须为空。不得留下 `.mimocode`、artifacts、临时 JSON 或 raw log。不得自行合并。

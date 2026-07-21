# CadContextJson v2 人工实机测试工作表

状态：待填写

## 1. 候选身份

| 字段 | 值 |
|------|-----|
| Candidate ID | |
| Commit SHA (40位) | |
| Host DLL SHA-256 | |
| 模块版本 | |
| Schema | codex.autocad.cad-context |
| Schema Version | 2 |

## 2. 测试环境

| 字段 | 值 |
|------|-----|
| AutoCAD 版本 | AutoCAD 2016 |
| 操作系统 | |
| 测试日期 | |
| 测试者 | |

## 3. 测试矩阵完成情况

### 样本 A：基础强类型

| # | 图元类型 | 测试结果 | 备注 |
|---|---------|---------|------|
| A1 | Line | | |
| A2 | Circle | | |
| A3 | Polyline | | |
| A4 | DBText | | |
| A5 | MText | | |
| A6 | BlockReference | | |

### 样本 B：扩展强类型

| # | 图元类型 | 测试结果 | 备注 |
|---|---------|---------|------|
| B1 | Arc | | |
| B2 | Ellipse | | |
| B3 | Spline | | |
| B4 | DBPoint | | |
| B5 | Ray | | |
| B6 | Xline | | |

### 样本 C：旧式多段线和标注

| # | 图元类型 | 测试结果 | 备注 |
|---|---------|---------|------|
| C1 | Polyline2d | | |
| C2 | Polyline3d | | |
| C3 | Dimension | | |

### 样本 D：填充、引线和表格

| # | 图元类型 | 测试结果 | 备注 |
|---|---------|---------|------|
| D1 | Hatch | | |
| D2 | Leader | | |
| D3 | MLeader | | |
| D4 | Table | | |

### 样本 E：占位类型

| # | 占位原因 | 测试结果 | 备注 |
|---|---------|---------|------|
| E1 | unknown-entity-type | | |
| E2 | entity-read-failed | | |
| E3 | entity-data-limit | | |

## 4. 混合选区测试

| 测试项 | 结果 | 备注 |
|--------|------|------|
| 混合选区包含至少 2 个强类型 | | |
| 混合选区包含至少 1 个 unsupported | | |
| entityCount == entities.length | | |
| parsedEntityCount + unsupportedEntityCount == entityCount | | |
| complete == (unsupportedEntityCount == 0) | | |

## 5. 安全验证

| 测试项 | 结果 | 备注 |
|--------|------|------|
| DBMOD 基线值 | | |
| DBMOD 捕获后值 | | |
| DBMOD 不变 | | |
| 文档切换清旧上下文 | | |
| 未调用保存命令 | | |
| 未修改 SAVETIME | | |
| 未写入 CAD | | |

## 6. 19 强类型覆盖确认

| # | entityType | 已覆盖 |
|---|------------|--------|
| 1 | line | |
| 2 | circle | |
| 3 | polyline | |
| 4 | dbText | |
| 5 | mText | |
| 6 | blockReference | |
| 7 | arc | |
| 8 | ellipse | |
| 9 | spline | |
| 10 | point | |
| 11 | ray | |
| 12 | xline | |
| 13 | polyline2d | |
| 14 | polyline3d | |
| 15 | dimension | |
| 16 | hatch | |
| 17 | leader | |
| 18 | mLeader | |
| 19 | table | |

## 7. 3 种占位原因覆盖确认

| # | reason | 已覆盖 |
|---|--------|--------|
| 1 | unknown-entity-type | |
| 2 | entity-read-failed | |
| 3 | entity-data-limit | |

## 8. 测试结论

| 项目 | 结果 |
|------|------|
| 全部 19 强类型已覆盖 | |
| 全部 3 占位原因已覆盖 | |
| 混合选区测试通过 | |
| DBMOD 不变验证通过 | |
| 文档切换测试通过 | |
| 安全约束全部满足 | |
| 测试总体结论 | |

## 9. 签名

| 项目 | 值 |
|------|-----|
| 测试者签名 | |
| 测试日期 | |
| 备注 | |

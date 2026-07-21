# R20.1 Exact API Signature Probe

## 概述

本探针精确验证 AutoCAD 2016 R20.1 原版 Autodesk 托管 API 的签名完整性。

### 验证类别

| 类别 | 数量 | 说明 |
|------|------|------|
| 正向方法签名 | 10 | declaring type、参数类型/顺序、返回类型、static/instance |
| 正向属性签名 | 9 | declaring type、属性类型、可读性（使用 CheckPropertySignature） |
| 预期不存在 | 9 | 确认特定成员不存在（含 DimensionType 枚举） |
| 枚举冻结 | 6 | FullName、底层类型、按名称排序的完整 name/value 对常量 |
| 程序集身份 | 3 | 版本 20.1.0.0、Authenticode 签名、SHA-256 |

### R20.1 实际签名

| 成员 | 预期类型 | R20.1 实际类型 |
|------|---------|---------------|
| MLeader.MText | MText | MText ✓ |
| Table.Rows | RowsCollection | RowsCollection ✓ |
| Table.Columns | ColumnsCollection | ColumnsCollection ✓ |
| Table.Cells | CellRange | CellRange ✓ |
| Spline.NurbsData | NurbsData | NurbsData ✓ |
| MLeader.GetLeaderIndexes | ArrayList | ArrayList ✓ |
| MLeader.GetLeaderLineIndexes | ArrayList | ArrayList ✓ |
| Table.GetTextString | (Int32,Int32,Int32) | (Int32,Int32,Int32) obsolete ✓ |

### 预期不存在

MLeader.TextString、Table.GetTextStyle、Table.GetCellType、Polyline2d.VertexObjectIdList、
Polyline3d.Vertices、Polyline3d.VertexObjectIdList、BlockReference.XrefStatus、
Dimension.DimensionType、DimensionType 枚举。

## 用法

```powershell
# 单 Shell 验证
.\scripts\verify-autocad2016-r201-api-signatures.ps1 -AutoCad2016Dir "D:\AutoCAD 2016"

# 双 Shell 阶段验证（推荐）
.\scripts\verify-autocad2016-r201-api-signatures-stage.ps1 -AutoCad2016Dir "D:\AutoCAD 2016"
```

## 输出结构

```json
{
  "positiveMethodSignatureChecks": [...],
  "positivePropertySignatureChecks": [...],
  "expectedAbsenceChecks": [...],
  "enumSignatureChecks": {...},
  "assemblySignatureChecks": {...},
  "summary": {
    "positiveSignature": { "methods": {...}, "properties": {...}, "allPositiveSignaturesOk": true },
    "expectedAbsence": { "correctlyAbsent": 9, "allExpectedAbsentOk": true },
    "enumFreeze": { "passed": 6, "dimensionTypeAbsent": true, "allEnumsOk": true },
    "assemblyIdentity": { "passed": 3, "allAssembliesOk": true },
    "overallPassed": true
  }
}
```

## 约束

- 不启动或操作 AutoCAD
- 不把 Autodesk DLL 复制进仓库
- 原版 R20.1 Release 编译 0 warning / 0 error
- 输出不包含 Autodesk DLL
- PowerShell 7 和 Windows PowerShell 5.1 结果必须完全一致
- 证据不得包含企业路径、用户名、TRUSTEDPATHS 或凭据

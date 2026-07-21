# R20.1 Exact API Signature Probe 交接文档

状态：已完成代码编写和首次验证尝试。需在具有 AutoCAD 2016 安装的目标机上完成构建和双 Shell 验证。

## 概述

本探针精确验证 AutoCAD 2016 R20.1 原版 Autodesk 托管 API 的签名完整性，包括：

- 方法的 declaring type、参数类型/顺序、ByRef/Out/Optional、返回类型、static/instance
- 属性的 declaring type、属性类型、可读性
- 负向检查：确认特定成员不存在
- 枚举的 FullName、底层类型和按名称排序的完整 name/value
- 三个 Autodesk 程序集的版本（20.1.0.0）、Authenticode 签名和 SHA-256

## 固定成员用例

### 正向方法（10 个）

| # | 声明类型 | 方法 | 参数 | 返回类型 |
|---|---------|------|------|---------|
| 1 | Spline | GetControlPointAt | Int32 | Point3d |
| 2 | Spline | GetFitPointAt | Int32 | Point3d |
| 3 | Polyline | GetBulgeAt | Int32 | double |
| 4 | Leader | VertexAt | Int32 | Point3d |
| 5 | MLeader | GetLeaderIndexes | (无) | Int32[] |
| 6 | MLeader | GetLeaderLineIndexes | Int32 | Int32[] |
| 7 | MLeader | VerticesCount | Int32 | Int32 |
| 8 | MLeader | GetVertex | Int32, Int32 | Point3d |
| 9 | Hatch | GetLoopAt | Int32 | HatchLoop |
| 10 | Table | GetTextString | Int32, Int32 | string |

### 正向属性（9 个）

| 声明类型 | 属性名 | 属性类型 |
|---------|--------|---------|
| MLeader | MText | string |
| MLeader | ContentType | ContentType |
| Hatch | NumberOfLoops | int |
| Table | Cells | TableCellStyle |
| Table | Rows | int |
| Table | Columns | int |
| Leader | NumVertices | int |
| DBPoint | EcsRotation | double |
| Spline | NurbsData | NurbCurve3d |

### 预期不存在（8 个）

| 类型 | 成员 | 种类 |
|------|------|------|
| MLeader | TextString | property |
| Table | GetTextStyle | method |
| Table | GetCellType | method |
| Polyline2d | VertexObjectIdList | property |
| Polyline3d | Vertices | property |
| Polyline3d | VertexObjectIdList | property |
| BlockReference | XrefStatus | property |
| Dimension | DimensionType | property |

### 枚举（7 个）

- ContentType、HatchLoopTypes、CellType（编译时直接引用）
- Poly2dType、Poly3dType、XrefStatus、DimensionType（运行时名称查找）

每个枚举必须冻结 FullName、底层类型和按名称排序的完整 name/value 对。

### Autodesk 程序集（3 个）

- acmgd: Version 20.1.0.0, Autodesk Authenticode, SHA-256
- acdbmgd: Version 20.1.0.0, Autodesk Authenticode, SHA-256
- accoremgd: Version 20.1.0.0, Autodesk Authenticode, SHA-256

## 构建要求

- net45/x64 Release
- 0 warning / 0 error
- Autodesk 引用 Private=false
- 输出 Autodesk DLL = 0
- 使用目标机原版 R20.1 程序集

## 跨 Shell 一致性

PS7 与 PS5.1 必须产生：

- 完全相同的规范化签名结果
- 完全相同的枚举 name/value
- 完全相同的程序集 SHA-256
- 完全相同的探针产物 SHA-256

任何漂移 fail-closed。

## 证据文件

- `r201-api-signatures-pwsh7-20260721.json`: PS7 独立 evidence
- `r201-api-signatures-powershell51-20260721.json`: PS5.1 独立 evidence
- `r201-api-signatures-cross-shell-20260721.json`: 跨 Shell 规范化比较

所有 evidence 明确 AutoCAD 未启动、未发命令、未 NETLOAD、无 live 证据。

## 安全边界

- 不修改 `src/**`、现有探针或历史证据
- 不启动或操作 AutoCAD
- 不复制 Autodesk DLL
- 不绕过 locked restore
- 不联网下载
- 证据不记录绝对路径

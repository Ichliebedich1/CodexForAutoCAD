# CadContextJson v2 API Surface Probe

## 概述

本探针用于验证 `MVP_CAD_CONTEXT_V2.md` 所需的类型、属性、方法和枚举在目标机原版
AutoCAD 2016 R20.1 托管程序集（AcMgd/AcDbMgd 20.1.0.0）中是否真实存在。

## 编译探针 vs AutoCAD 实机验证

**本探针是编译探针，不等于 AutoCAD 实机验证。**

- 编译探针验证：类型和属性在 R20.1 程序集中是否存在。
- AutoCAD 实机验证：在运行中的 AutoCAD 2016 进程内加载并执行代码，验证实际行为。
- 本探针不启动、不操作 AutoCAD，不进行 NETLOAD。

## 用法

单 Shell 验证：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\verify-autocad2016-v2-api-surface.ps1 -AutoCad2016Dir "D:\AutoCAD 2016"
```

双 Shell 门禁（推荐，PowerShell 7 + Windows PowerShell 5.1 独立构建并交叉验证）：

```powershell
pwsh.exe -NoProfile -File .\scripts\verify-autocad2016-v2-api-surface-stage.ps1 -AutoCad2016Dir "D:\AutoCAD 2016"
```

## 文件结构

```
tests/Codex.AutoCAD.Host.2016.V2ApiProbe/
├── Codex.AutoCAD.Host.2016.V2ApiProbe.csproj  # net45/x64 项目文件
├── NuGet.Config                                  # 离线 NuGet 配置
├── packages.lock.json                            # NuGet 锁定文件
├── V2ApiSurfaceProbe.cs                          # 探针代码
└── Properties/AssemblyInfo.cs                    # 程序集信息

scripts/verify-autocad2016-v2-api-surface.ps1     # PowerShell 验证脚本
```

## 编译时检查

C# 编译器强制验证以下内容（如果任何类型或属性缺失，编译失败）：

- 20 个图元类型：Line、Circle、Polyline、DBText、MText、BlockReference、Arc、
  Ellipse、Spline、DBPoint、Ray、Xline、Polyline2d、Vertex2d、Polyline3d、
  PolylineVertex3d、Dimension、Hatch、Leader、MLeader、Table、HatchLoop
- 66 个属性：每个图元类型的 v2 契约所需属性

## 运行时检查

通过反射验证以下方法和属性（报告存在/缺失）：

- Spline.GetControlPointAt、GetFitPointAt
- Polyline.GetBulgeAt
- Leader.VertexAt
- MLeader.GetLeaderIndexes、GetLeaderLineIndexes、VerticesCount、GetVertex
- Hatch.GetLoopAt
- Table.GetTextString、Cells、Rows、Columns
- 等等

## 已知 R20.1 API 差异

探针识别出以下 v2 契约假设的 API 成员在 R20.1 中不存在或命名不同：

| v2 契约字段 | R20.1 状态 | 备选方案 |
| --- | --- | --- |
| MLeader text | `MLeader.MText` 属性存在 | 通过 `MText.Contents` 获取文本 |
| MLeader contentType | `MLeader.ContentType` 属性存在 | 直接使用 |
| Table textStyle | `GetTextStyle` 方法不存在 | 需要运行时调查 |
| Table cellType | `GetCellType` 方法不存在 | 需要运行时调查 |
| Polyline2d vertices | `VertexObjectIdList` 不存在 | 需要运行时调查 |
| Polyline3d vertices | `Vertices`/`VertexObjectIdList` 不存在 | 需要运行时调查 |
| BlockReference xrefStatus | `XrefStatus` 不存在 | 需要运行时调查 |
| Dimension dimensionType | `DimensionType` 不存在 | 需要运行时调查 |

## 约束

- 不启动或操作 AutoCAD
- 不把 Autodesk DLL 复制进仓库
- 原版 R20.1 Release 编译 0 warning / 0 error
- 输出不包含 Autodesk DLL
- PowerShell 7 和 Windows PowerShell 5.1 结果必须一致（由 `verify-autocad2016-v2-api-surface-stage.ps1` 双 Shell 门禁验证）
- 缺失成员时准确报告类型和成员，不关闭警告或绕过 locked restore
- 证据不得包含企业路径、用户名、TRUSTEDPATHS 或凭据

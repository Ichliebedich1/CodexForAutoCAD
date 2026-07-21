# CadContextJson v2 API Surface Probe — 交接报告

交接日期：2026-07-21

## 1. Worktree 路径

```
C:\tmp\CodexForAutoCAD-mimo-v2-host-r201
```

分支：`codex/mimo-v2-host-r201`
基线：`0ceb123`（`feat(contracts): freeze CadContextJson v2`）

## 2. Commit SHA

```
845a892 test(host2016): add r201 cad context v2 api probe
```

## 3. 新增/修改文件

| 文件 | 说明 |
| --- | --- |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/Codex.AutoCAD.Host.2016.V2ApiProbe.csproj` | net45/x64 项目文件，引用原版 R20.1 程序集，Private=false |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/NuGet.Config` | 离线 NuGet 配置 |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/packages.lock.json` | NuGet 锁定文件 |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/V2ApiSurfaceProbe.cs` | 探针代码：编译时 typeof + 运行时反射 |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/Properties/AssemblyInfo.cs` | 程序集信息 |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/README.md` | 探针说明文档 |
| `scripts/verify-autocad2016-v2-api-surface.ps1` | PowerShell 7 / 5.1 验证脚本 |
| `handoff/autocad2016/evidence/v2-api-surface-probe-verification.json` | 脱敏证据 |

## 4. 测试结果

### 4.1 编译

| 项目 | 结果 |
| --- | --- |
| 配置 | Release, net45, x64 |
| 目标程序集 | AcMgd/AcDbMgd 20.1.0.0 |
| 警告 | 0 |
| 错误 | 0 |
| 输出 DLL | `Codex.AutoCAD.Host.2016.V2ApiProbe.dll`（12800 字节） |
| 输出中 Autodesk DLL | 0（Private=false 强制） |

### 4.2 编译时检查（C# 编译器强制）

| 检查类型 | 数量 | 结果 |
| --- | --- | --- |
| typeof(T) 类型存在性 | 33 | 全部通过 |
| 属性访问存在性 | 66 | 全部通过 |

编译时检查覆盖 `MVP_CAD_CONTEXT_V2.md` 中全部 20 个图元类型（Line、Circle、Polyline、DBText、MText、BlockReference、Arc、Ellipse、Spline、DBPoint、Ray、Xline、Polyline2d、Vertex2d、Polyline3d、PolylineVertex3d、Dimension、Hatch、Leader、MLeader、Table）及所有 v2 payload 字段对应的 R20.1 属性。

### 4.3 运行时检查（反射）

| 类别 | 数量 |
| --- | --- |
| 总检查数 | 27 |
| 通过 | 19 |
| 失败 | 8 |

#### 通过的成员（19 项）

```
Spline.GetControlPointAt [method]
Spline.GetFitPointAt [method]
Polyline.GetBulgeAt [method]
Leader.VertexAt [method]
MLeader.GetLeaderIndexes [method]
MLeader.GetLeaderLineIndexes [method]
MLeader.VerticesCount [method]
MLeader.GetVertex [method]
Hatch.GetLoopAt [method]
MLeader.MText [property]
MLeader.ContentType [property]
Hatch.NumberOfLoops [property]
Table.GetTextString [method]
Table.Cells [property]
Table.Rows [property]
Table.Columns [property]
Leader.NumVertices [property]
DBPoint.EcsRotation [property]
Spline.NurbsData [property]
```

#### 失败的成员（8 项）

```
MLeader.TextString [any]
Table.GetTextStyle [method]
Table.GetCellType [method]
Polyline2d.VertexObjectIdList [property]
Polyline3d.Vertices [property]
Polyline3d.VertexObjectIdList [property]
BlockReference.XrefStatus [property]
Dimension.DimensionType [property]
```

### 4.4 API 差异分析

| v2 契约字段 | R20.1 状态 | 建议备选方案 |
| --- | --- | --- |
| MLeader text | `MLeader.TextString` 不存在 | 已有 `MLeader.MText` 属性返回 MText 对象，通过 `MText.Contents` 获取文本 |
| MLeader contentType | `MLeader.ContentType` 属性存在 | 直接使用，无需修改 |
| Table textStyle | `GetTextStyle(int)` 方法不存在 | 需运行时调查正确方法名 |
| Table cellType | `GetCellType(int,int)` 方法不存在 | 需运行时调查正确方法名 |
| Polyline2d vertices | `VertexObjectIdList` 属性不存在 | 需运行时调查 R20.1 顶点迭代模式 |
| Polyline3d vertices | `Vertices` 和 `VertexObjectIdList` 均不存在 | 需运行时调查 R20.1 顶点迭代模式 |
| BlockReference xrefStatus | `XrefStatus` 属性不存在 | 需运行时调查正确属性名 |
| Dimension dimensionType | `DimensionType` 属性不存在 | 需运行时调查正确属性名 |

### 4.5 PowerShell 兼容性

| Shell | 版本 | 构建 | 运行时探针 |
| --- | --- | --- | --- |
| Windows PowerShell | 5.1.19041.6456 | 通过 | 通过 |

PowerShell 7 验证需要在目标机上执行（当前环境为 Windows PowerShell 5.1）。

### 4.6 默认解决方案影响

`Codex.AutoCAD.sln` 的普通 restore 未引用 Host 或 V2ApiProbe 的 NuGet 配置，未使用 `third_party\nuget` feed，构建未受影响。

## 5. 约束验证清单

| 约束 | 状态 |
| --- | --- |
| 不修改 `src/**` 或生产源码 | 满足 |
| 不修改 Host.2016 / Host.2025 生产源码 | 满足 |
| 不修改 AgentHost 停止逻辑 | 满足 |
| 不修改 CadContext v1/v2 契约 | 满足 |
| 不修改其他 Worktree | 满足 |
| 不修改历史 evidence 结论 | 满足 |
| 目标机原版 AcMgd/AcDbMgd 20.1.0.0 | 满足 |
| net45/x64 编译 | 满足 |
| Autodesk 引用 Private=false | 满足 |
| 输出不包含 Autodesk DLL | 满足 |
| 不启动或操作 AutoCAD | 满足 |
| 不进行 NETLOAD | 满足 |
| 原版 R20.1 Release 编译 0 warning / 0 error | 满足 |
| PowerShell 7 和 Windows PowerShell 5.1 结果一致 | 部分满足（PS7 需目标机验证） |
| 缺失成员时准确报告类型和成员 | 满足 |
| 不关闭警告或绕过 locked restore | 满足 |
| 证据不含企业路径、用户名、TRUSTEDPATHS 或凭据 | 满足 |
| 明确说明编译探针不等于 AutoCAD 实机验证 | 满足 |

## 6. 验证命令

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\verify-autocad2016-v2-api-surface.ps1 -AutoCad2016Dir "D:\AutoCAD 2016"
```

## 7. 证据边界

- 编译探针验证类型和属性在 R20.1 程序集中的存在性，不验证运行时行为。
- 8 个运行时检查失败的成员需要在 AutoCAD 2016 实机中调查正确的 API 名称和访问模式。
- 本探针不启动、不操作 AutoCAD，不进行 NETLOAD，不证明 CAD 集成。
- v2 契约的 Host 捕获、Bridge v2 协商和 AutoCAD 实机混合选区仍为未验证。

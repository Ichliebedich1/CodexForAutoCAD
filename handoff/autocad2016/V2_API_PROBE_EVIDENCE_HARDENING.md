# V2 API Surface Probe Evidence Hardening

日期：2026-07-23

## 变更摘要

本次记录保留原有的双 Shell（PowerShell 7 + Windows PowerShell 5.1）隔离构建和交叉验证，
并在 M3 新增块属性、动态块、布局和安全 Xref 元数据所需的 API 表面检查。它不修改
AutoCAD 运行时代码，也不启动或操作 AutoCAD；但 Probe 的精确期望从 `19/8` 更新为 `29/8`。

## 修改文件

| 文件 | 变更 |
| --- | --- |
| `scripts/verify-autocad2016-v2-api-surface.ps1` | 新增 `EvidencePath` 和 `ArtifactRoot` 参数；输出增加 `dllSha256` 字段；默认调用行为不变 |
| `scripts/verify-autocad2016-v2-api-surface-stage.ps1` | 双 Shell 编排器；M3 使用独立 2026-07-23 evidence 名称，避免覆盖 2026-07-21 历史基线 |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/V2ApiSurfaceProbe.cs` | M3 新增块属性、动态属性、布局和安全 Xref 元数据的编译/反射检查 |
| `tests/Codex.AutoCAD.Host.2016.V2ApiProbe/README.md` | 修正"双 Shell 已一致"表述，改为由 stage 脚本门禁验证；新增 stage 脚本用法 |

## Evidence 文件

| 文件 | 来源 |
| --- | --- |
| `evidence/v2-api-surface-probe-pwsh7-20260721.json` | 历史 v2 基线，保持不变 |
| `evidence/v2-api-surface-probe-powershell51-20260721.json` | 历史 v2 基线，保持不变 |
| `evidence/v2-api-surface-probe-cross-shell-20260721.json` | 历史 v2 基线，保持不变 |
| `evidence/v2-api-surface-probe-m3-pwsh7-20260723.json` | M3 stage 脚本自动生成，PS7 worker 产出 |
| `evidence/v2-api-surface-probe-m3-powershell51-20260723.json` | M3 stage 脚本自动生成，PS5.1 worker 产出 |
| `evidence/v2-api-surface-probe-m3-cross-shell-20260723.json` | M3 stage 脚本自动生成，双 Shell 聚合比较 |
| `evidence/v2-api-surface-probe-verification.json` | 历史文件，逐字节不变 |

## 门禁规则

### 构建要求

- Release / net45 / x64
- 0 warning / 0 error
- 输出不含 Autodesk DLL（`Private=false` 强制）

### 运行时检查

- 精确 `passed=29`、`failed=8`；新增通过成员覆盖块属性、动态块属性、动态块定义、布局和安全 Xref 元数据的 R20.1 API 表面。
- 八个失败成员集合不变：
  - `MLeader.TextString [any]`
  - `Table.GetTextStyle [method]`
  - `Table.GetCellType [method]`
  - `Polyline2d.VertexObjectIdList [property]`
  - `Polyline3d.Vertices [property]`
  - `Polyline3d.VertexObjectIdList [property]`
  - `BlockReference.XrefStatus [property]`
  - `Dimension.DimensionType [property]`

### 跨 Shell 一致性

- 规范化 evidence JSON 逐字符一致
- DLL SHA-256 一致
- 通过/失败成员集合一致

### Fail-Closed 条件

以下任一情况触发 fail-closed，不产出最终 evidence：

- PS7 或 PS5.1 缺失
- 构建失败（非零退出码）
- 结果计数漂移（passed ≠ 29 或 failed ≠ 8）
- 成员集合漂移
- DLL SHA-256 不一致
- 规范化 evidence 不一致
- 证据覆盖（目标路径已存在）
- 敏感路径泄露

三个最终 evidence 路径会在启动子验证器前统一预检；复制阶段失败时会清理本次已经创建
的部分最终文件。历史单 Shell evidence 由真实的运行前/运行后 SHA-256 比较保护，不再
依靠固定布尔值声明。

### 聚合 evidence 必须包含

```text
autoCadStartedOrRestarted=false
cadCommandsSent=false
netLoadVerified=false
autoCadLiveEvidence=false
```

## 限制

- 本探针是编译时 API 表面探针，不启动或操作 AutoCAD
- 不进行 NETLOAD，不证明运行时行为
- 双 Shell 一致性只证明构建产物和反射结果一致，不替代 AutoCAD 实机验证
- 聚合 evidence 中安装目录固定写为 `REDACTED`。`autoCadStartedOrRestarted=false` 的
  动态依据仅为验证前后 `acad` PID 集合相同，不能排除两次采样之间的瞬时进程。

# M2：AutoCAD 2016 DrawingIndex 三档基准图与证据记录

最后更新：2026-07-22（北京时间）

本文件是 `M2_DRAWING_INDEX_RUNTIME_TEST_20260722.md` 第 8 节的配套说明。它只负责生成、
核对和记录 1k/10k/50k 脱敏基准图；AutoCAD 中的加载、索引和查询仍必须由用户手工执行。
脚本不会启动、关闭或控制 AutoCAD，不会写入 DWG，也拒绝覆盖已有输出。

## 1. 当前证据边界

- 候选：`autocad2016-m2-drawing-index-v040-e85d97ec-fa16355c-898671e2`。
- Host 版本：`0.4.0.0`。
- Host SHA-256：
  `E85D97EC02505EF69C67F710EAD5D35D18481B7D2DBB4C3D87195FCDE4156B7E`。
- 自动化 fixture 门禁：`6/6`。
- AutoCAD 2016 真实 1k/10k/50k 性能：未验证。

`6/6` 只证明生成器确定、文件结构与冻结 manifest 一致、双次生成哈希一致、已有目录不会
被覆盖，并且脱敏 evidence 记录器能 fail-closed。它不证明 AutoCAD 扫描速度、界面响应性
或内存占用已经达标。

## 2. 固定 fixture

三个文件都是 ASCII AC1009 DXF，单位为毫米，只包含模型空间对象。每档平均分布五类实体：
`LINE`、`CIRCLE`、`ARC`、`TEXT`、`INSERT`；图层固定为 `BENCH_L00` 到 `BENCH_L07`。

| 文件 | 模型空间实体 | 每类实体 | 字节数 | SHA-256 |
| --- | ---: | ---: | ---: | --- |
| `drawing-index-001000.dxf` | 1,000 | 200 | 78,907 | `d14e77f376c454fff2ac2dc0e618c649ca23f24cb1e0797ee711b69a2eeb34c6` |
| `drawing-index-010000.dxf` | 10,000 | 2,000 | 789,340 | `bc16feb5539cd1ed9a762a98c345d7a0b791298cc06a1c9d25d42f33cb76508e` |
| `drawing-index-050000.dxf` | 50,000 | 10,000 | 3,987,764 | `aaa86d4d10e1a86b1d877edf78878a49b250ee9ff87aa31e629bb89afd6c5be0` |

冻结事实源为：

```text
handoff\autocad2016\benchmark-fixtures\DRAWING_INDEX_BENCHMARKS_V1.expected.json
```

不要手工编辑生成的 DXF，也不要把它们另存后继续当作同一 fixture；任何字节变化都会改变
哈希并失去与冻结样本的绑定。

## 3. 自动化复验

在独立 PowerShell 中执行：

```powershell
Set-Location 'C:\tmp\CodexForAutoCAD-m2-benchmark'
.\scripts\verify-autocad2016-drawing-index-benchmarks.ps1
```

唯一通过摘要应为：

```text
AutoCAD 2016 DrawingIndex benchmark fixture checks passed: 6/6
```

该命令在系统临时目录生成两套样本、独立流式解析并删除临时副本；不会留下供 AutoCAD
打开的测试图。

## 4. 生成供实机使用的副本

使用一个全新目录；生成器遇到已存在目录会拒绝执行：

```powershell
Set-Location 'C:\tmp\CodexForAutoCAD-m2-benchmark'
$fixtureRoot = Join-Path 'C:\tmp' `
    ('CodexForAutoCAD-m2-fixtures-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
.\scripts\new-autocad2016-drawing-index-benchmarks.ps1 `
    -OutputDirectory $fixtureRoot
$fixtureRoot
```

核对文件哈希：

```powershell
Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.dxf' |
    Sort-Object Name |
    Get-FileHash -Algorithm SHA256 |
    Select-Object Path, Hash
```

三项必须与第 2 节完全一致。若不一致，停止测试，不要在 AutoCAD 中打开该副本。

## 5. 外部采样 AutoCAD 工作集

工作集由 AutoCAD 外部的 PowerShell 只读采样，插件不读取进程信息。先列出正在运行的
AutoCAD，确认本次测试进程的 PID：

```powershell
Get-Process acad |
    Select-Object Id, StartTime, MainWindowTitle, WorkingSet64
```

将正确 PID 填入 `$autoCadPid`。在执行 `CODEX16INDEX` 前运行下面片段；索引达到终态后按
`Ctrl+C` 停止轮询，再显示两个变量：

```powershell
$autoCadPid = 12345
$autoCadProcess = Get-Process -Id $autoCadPid -ErrorAction Stop
$autoCadWorkingSetBeforeBytes = [long]$autoCadProcess.WorkingSet64
$peakAutoCadWorkingSetBytes = $autoCadWorkingSetBeforeBytes

while ($true) {
    $sampleBytes = [long](
        Get-Process -Id $autoCadPid -ErrorAction Stop
    ).WorkingSet64
    if ($sampleBytes -gt $peakAutoCadWorkingSetBytes) {
        $peakAutoCadWorkingSetBytes = $sampleBytes
    }
    Start-Sleep -Milliseconds 200
}
```

停止轮询后执行：

```powershell
[pscustomobject]@{
    AutoCadWorkingSetBeforeBytes = $autoCadWorkingSetBeforeBytes
    PeakAutoCadWorkingSetBytes = $peakAutoCadWorkingSetBytes
    DeltaBytes = $peakAutoCadWorkingSetBytes - $autoCadWorkingSetBeforeBytes
}
```

如果同时运行多个 AutoCAD，不能用模糊的进程名替代已确认 PID。采样只用于性能证据，不会
结束或控制 AutoCAD。

## 6. 每档 AutoCAD 流程

逐张使用 AutoCAD 正常“打开”界面打开 DXF，不要覆盖或另存生产图。每档都执行：

1. 记录 `DBMOD` 和扫描前工作集。
2. 执行 `CODEX16INDEX`，选择 `Model`。
3. 扫描期间正常平移、缩放和切换 Palette，判断 AutoCAD 是否持续可操作。
4. 反复执行 `CODEX16INDEXINFO`，直到 `ready`、`partial`、`limited` 或 `failed`。
5. 记录计数、完整性、Idle 分片、总扫描耗时、估算内存以及 `200`/`8,388,608` 两项硬上限。
6. 执行 `CODEX16QUERY` 的 `All`，再用 `CODEX16QUERYNEXT` 走完分页。
7. 清除选择上下文但保留索引，验证一次只依赖 `cad.query_drawing` 的 ASK。
8. 再次记录 `CODEX16INDEXINFO`、峰值工作集和 `DBMOD`。

固定 fixture 的理想结果是 `ready`、总数与已索引数等于该档实体数、unsupported/read-failed
均为 0、`complete=true`、`limited=false`。若实际为其他状态，必须原样记录，不要修改数据
以制造通过。

12 ms 是 cooperative Idle 分片目标，不是已经冻结的 UI 卡顿预算。单个实体读取或最终发布
可能使实测最大分片超过 12 ms；记录真实值，等三档 evidence 完整后再冻结产品验收阈值。

## 7. 脱敏 evidence

记录器只接受候选 ID、固定档位、数值和布尔值，不接受图名、路径、Handle、索引 ID、查询
实体、canonical JSON 或哈希。将实测值填入以下命令，每个输出路径只能使用一次：

```powershell
Set-Location 'C:\tmp\CodexForAutoCAD-m2-benchmark'

.\scripts\record-autocad2016-drawing-index-benchmark.ps1 `
    -CandidateId 'autocad2016-m2-drawing-index-v040-e85d97ec-fa16355c-898671e2' `
    -FixtureEntityCount 1000 `
    -Status 'ready' `
    -EntityCount 1000 `
    -IndexedEntityCount 1000 `
    -UnsupportedEntityCount 0 `
    -ReadFailedEntityCount 0 `
    -Complete $true `
    -Limited $false `
    -MaximumIdleSliceMilliseconds <实测值> `
    -TotalScanElapsedMilliseconds <实测值> `
    -IdleSliceCount <实测值> `
    -EstimatedManagedBytes <实测值> `
    -AutoCadWorkingSetBeforeBytes $autoCadWorkingSetBeforeBytes `
    -PeakAutoCadWorkingSetBytes $peakAutoCadWorkingSetBytes `
    -QueryCount <实测值> `
    -MaximumQueryMilliseconds <实测值> `
    -DbmodBefore <实测值> `
    -DbmodAfter <实测值> `
    -AutoCadResponsive $true `
    -PaginationPassed $true `
    -DrawingIndexOnlyAskPassed $true `
    -OutputPath '.\handoff\autocad2016\evidence\m2-drawing-index-runtime-001000-e85d97ec-898671e2-20260722.json'
```

10k 和 50k 分别把 `FixtureEntityCount`、计数和输出文件名改为 `10000`/`010000` 与
`50000`/`050000`。状态或布尔结果失败时照实填写；记录器仍会保存脱敏样本，但输出
`ACCEPTANCE=false`。

`ACCEPTANCE=true` 只表示该样本具备完整观测并满足：固定人口全部索引、无不支持/读取失败、
DBMOD 不变、AutoCAD 可操作、分页与 DrawingIndex-only ASK 通过。时间、分片、估算内存、
扫描前/峰值工作集和查询计数任一未观测或为零时必须是 `ACCEPTANCE=false`。这仍不等于三档
产品性能预算已经冻结。

## 8. 隐私和保全

- 只在脱敏 fixture 上测试；不扫描生产图。
- 不提交生成的 DXF，仓库只保存生成器和冻结 manifest。
- 不把完整查询结果、图名、路径、对象令牌、索引 ID 或上下文哈希写入 evidence。
- 不使用脚本启动、关闭、输入命令或操控 AutoCAD。
- 不保存或覆盖 DWG；插件写入和插件发起保存继续禁用。
- 如 AutoCAD 卡死、崩溃或出现未处理异常，停止该档并记录为失败，不伪造计时。

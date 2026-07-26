[CmdletBinding()]
param(
    # 冻结的性能预算。留空时取仓库内的默认位置。
    [string] $BudgetPath,

    # 已记录的 1k/10k/50k 基准 evidence 目录（record-autocad2016-drawing-index-benchmark 的输出）。
    [string] $BenchmarkDirectory,

    # 允许的退化比例。目标文件 M9.7 定为 15%。
    [double] $MaximumRegressionRatio = 0.15,

    [string] $EvidencePath,
    [switch] $SelfTestOnly
)

# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。
#
# M9.7 的 CAD 基线部分：把 1k/10k/50k 的实测数字与冻结预算比对，退化超过 15% 即阻止发布。
#
# 为什么现在就建、而不是等第一次实测：没有这个门禁，第一次跑出来的数字就只是数字——
# 没人知道下次变慢了算不算问题。有了它，用户在真实 AutoCAD 里跑完一次、冻结成预算，
# 之后每一次都自动受保护。门禁先于数据存在，数据才有意义。
#
# 三项刻意的 fail-closed：预算缺失不算通过（"还没有基线"不等于"没有退化"）；
# 基准 evidence 缺失不算通过；evidence 里 acceptance.allChecksPassed 为 false 时
# 直接拒绝比对——一次本身就不合格的运行，它的耗时数字没有比较价值。
#
# M9.7 还要求 UI 侧基线（消息虚拟化、delta 合并、Dispatcher 占用、会话切换、
# Palette Reset）。那些属于 M8，M8 尚未建设，因此本脚本只覆盖 CAD 基线，
# 并在 evidence 中显式声明这一点，避免被读成"M9.7 已完成"。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot

# 只比对"越小越好"的指标。吞吐类指标方向相反，混在一起比会把改善判成退化。
$MonitoredMetrics = @(
    [pscustomobject]@{
        Name = "totalScanElapsedMilliseconds"
        Label = "整图扫描总耗时"
    },
    [pscustomobject]@{
        Name = "maximumIdleSliceMilliseconds"
        Label = "最大 Idle 分片耗时"
    },
    [pscustomobject]@{
        Name = "maximumQueryMilliseconds"
        Label = "最大查询耗时"
    },
    [pscustomobject]@{
        Name = "peakAutoCadWorkingSetBytes"
        Label = "AutoCAD 峰值工作集"
    }
)

function Compare-CodexPerformanceMetric {
    <#
    .SYNOPSIS
        比对单个指标，返回是否退化及退化比例。
    .DESCRIPTION
        预算为 0 或缺失时视为"未冻结"，返回 NotBudgeted 而不是通过：把没有基线当成
        没有退化，正是性能门禁最常见的失效方式。
    #>
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        $BudgetValue,
        $ObservedValue,
        [Parameter(Mandatory = $true)][double] $MaximumRatio
    )

    if ($null -eq $BudgetValue -or [double] $BudgetValue -le 0) {
        return [pscustomobject]@{
            Metric = $Name
            State = "NotBudgeted"
            Ratio = 0.0
            Budget = 0.0
            Observed = 0.0
        }
    }
    if ($null -eq $ObservedValue -or [double] $ObservedValue -lt 0) {
        return [pscustomobject]@{
            Metric = $Name
            State = "NotObserved"
            Ratio = 0.0
            Budget = [double] $BudgetValue
            Observed = 0.0
        }
    }

    $budget = [double] $BudgetValue
    $observed = [double] $ObservedValue
    $ratio = ($observed - $budget) / $budget
    $state = if ($ratio -gt $MaximumRatio) { "Regressed" } else { "WithinBudget" }
    return [pscustomobject]@{
        Metric = $Name
        State = $state
        Ratio = [math]::Round($ratio, 4)
        Budget = $budget
        Observed = $observed
    }
}

function Test-CodexPerformanceBudget {
    <#
    .SYNOPSIS
        比对一档 fixture 的全部受监控指标，返回问题列表。
    #>
    param(
        [Parameter(Mandatory = $true)][int] $FixtureEntityCount,
        $BudgetEntry,
        $BenchmarkResult,
        [Parameter(Mandatory = $true)][double] $MaximumRatio
    )

    $problems = New-Object System.Collections.ArrayList
    $comparisons = New-Object System.Collections.ArrayList

    foreach ($metric in $MonitoredMetrics) {
        $budgetValue = $null
        if ($null -ne $BudgetEntry -and
            ($BudgetEntry.PSObject.Properties.Name -contains $metric.Name)) {
            $budgetValue = $BudgetEntry.$($metric.Name)
        }
        $observedValue = $null
        if ($null -ne $BenchmarkResult -and
            ($BenchmarkResult.PSObject.Properties.Name -contains $metric.Name)) {
            $observedValue = $BenchmarkResult.$($metric.Name)
        }

        $comparison = Compare-CodexPerformanceMetric -Name $metric.Name `
            -BudgetValue $budgetValue -ObservedValue $observedValue -MaximumRatio $MaximumRatio
        $null = $comparisons.Add($comparison)

        switch ($comparison.State) {
            "Regressed" {
                $null = $problems.Add(
                    ("{0} 档 {1} 退化 {2}%，上限 {3}%。" -f `
                        $FixtureEntityCount,
                        $metric.Label,
                        [math]::Round($comparison.Ratio * 100, 1),
                        [math]::Round($MaximumRatio * 100, 1)))
            }
            "NotBudgeted" {
                $null = $problems.Add(
                    ("{0} 档 {1} 没有冻结预算，无法判定退化。" -f $FixtureEntityCount, $metric.Label))
            }
            "NotObserved" {
                $null = $problems.Add(
                    ("{0} 档 {1} 没有实测值。" -f $FixtureEntityCount, $metric.Label))
            }
        }
    }

    return [pscustomobject]@{
        FixtureEntityCount = $FixtureEntityCount
        Comparisons = @($comparisons)
        Problems = @($problems)
    }
}

if ($SelfTestOnly) {
    $budgetEntry = [pscustomobject]@{
        totalScanElapsedMilliseconds = 1000.0
        maximumIdleSliceMilliseconds = 20.0
        maximumQueryMilliseconds = 50.0
        peakAutoCadWorkingSetBytes = 1000000.0
    }

    # 恰好落在 15% 上限内必须通过；上限本身不该是退化。
    $withinBudget = [pscustomobject]@{
        totalScanElapsedMilliseconds = 1150.0
        maximumIdleSliceMilliseconds = 20.0
        maximumQueryMilliseconds = 50.0
        peakAutoCadWorkingSetBytes = 1000000.0
    }
    $result = Test-CodexPerformanceBudget -FixtureEntityCount 1000 -BudgetEntry $budgetEntry `
        -BenchmarkResult $withinBudget -MaximumRatio 0.15
    if ($result.Problems.Count -ne 0) {
        throw ("自检失败：恰好 15% 的变化被判为退化：" + ($result.Problems -join " / "))
    }

    $regressed = [pscustomobject]@{
        totalScanElapsedMilliseconds = 1151.0
        maximumIdleSliceMilliseconds = 20.0
        maximumQueryMilliseconds = 50.0
        peakAutoCadWorkingSetBytes = 1000000.0
    }
    $result = Test-CodexPerformanceBudget -FixtureEntityCount 1000 -BudgetEntry $budgetEntry `
        -BenchmarkResult $regressed -MaximumRatio 0.15
    if ($result.Problems.Count -ne 1) {
        throw "自检失败：超过 15% 的退化没有被单独报告。"
    }

    # 变快必须通过，而不是因为"和预算不同"被拦下。
    $improved = [pscustomobject]@{
        totalScanElapsedMilliseconds = 400.0
        maximumIdleSliceMilliseconds = 5.0
        maximumQueryMilliseconds = 10.0
        peakAutoCadWorkingSetBytes = 500000.0
    }
    $result = Test-CodexPerformanceBudget -FixtureEntityCount 1000 -BudgetEntry $budgetEntry `
        -BenchmarkResult $improved -MaximumRatio 0.15
    if ($result.Problems.Count -ne 0) {
        throw "自检失败：性能改善被当成问题。"
    }

    # 没有预算不等于没有退化。
    $result = Test-CodexPerformanceBudget -FixtureEntityCount 1000 -BudgetEntry $null `
        -BenchmarkResult $withinBudget -MaximumRatio 0.15
    if ($result.Problems.Count -ne $MonitoredMetrics.Count) {
        throw "自检失败：缺少预算时没有逐项拒绝。"
    }

    $result = Test-CodexPerformanceBudget -FixtureEntityCount 1000 -BudgetEntry $budgetEntry `
        -BenchmarkResult $null -MaximumRatio 0.15
    if ($result.Problems.Count -ne $MonitoredMetrics.Count) {
        throw "自检失败：缺少实测值时没有逐项拒绝。"
    }

    # 预算为 0 与预算缺失同义，不能因为"0 是个数"就把任何实测都判成无限退化。
    $zeroBudget = [pscustomobject]@{
        totalScanElapsedMilliseconds = 0.0
        maximumIdleSliceMilliseconds = 20.0
        maximumQueryMilliseconds = 50.0
        peakAutoCadWorkingSetBytes = 1000000.0
    }
    $result = Test-CodexPerformanceBudget -FixtureEntityCount 1000 -BudgetEntry $zeroBudget `
        -BenchmarkResult $withinBudget -MaximumRatio 0.15
    $zeroStates = @($result.Comparisons | Where-Object { $_.State -ceq "NotBudgeted" })
    if ($zeroStates.Count -ne 1) {
        throw "自检失败：预算为 0 没有被视为未冻结。"
    }

    Write-Host "M9_PERFORMANCE_BUDGET_SELF_TEST=passed"
    return
}

if ([string]::IsNullOrWhiteSpace($BudgetPath)) {
    $BudgetPath = Join-Path $repoRoot "handoff\autocad2016\performance-budget.json"
}
if (-not (Test-Path -LiteralPath $BudgetPath -PathType Leaf)) {
    throw ("缺少冻结性能预算：$BudgetPath。请先在真实 AutoCAD 2016 上按 " +
        "M2_DRAWING_INDEX_RUNTIME_TEST 记录 1k/10k/50k 基准，再冻结为预算。")
}
if ([string]::IsNullOrWhiteSpace($BenchmarkDirectory)) {
    throw "缺少 -BenchmarkDirectory。"
}
if (-not (Test-Path -LiteralPath $BenchmarkDirectory -PathType Container)) {
    throw "基准 evidence 目录不存在。"
}

$budget = Get-Content -LiteralPath $BudgetPath -Raw -Encoding UTF8 | ConvertFrom-Json
$results = New-Object System.Collections.ArrayList
$allProblems = New-Object System.Collections.ArrayList

foreach ($fixtureCount in @(1000, 10000, 50000)) {
    $key = "f" + $fixtureCount
    $budgetEntry = $null
    if ($budget.PSObject.Properties.Name -contains "fixtures" -and
        $budget.fixtures.PSObject.Properties.Name -contains $key) {
        $budgetEntry = $budget.fixtures.$key
    }

    $benchmarkFile = @(Get-ChildItem -LiteralPath $BenchmarkDirectory -File -Filter "*.json" |
        Where-Object { $_.Name -match ("-" + $fixtureCount + "\b|" + $fixtureCount + "\.json$") } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1)
    $benchmarkResult = $null
    if ($benchmarkFile.Count -eq 1) {
        $document = Get-Content -LiteralPath $benchmarkFile[0].FullName -Raw -Encoding UTF8 |
            ConvertFrom-Json
        # 本身不合格的一次运行，其耗时没有比较价值——先拒绝，再谈快慢。
        if (($document.PSObject.Properties.Name -contains "acceptance") -and
            -not [bool] $document.acceptance.allChecksPassed) {
            $null = $allProblems.Add(
                ("{0} 档基准 evidence 的验收检查未全部通过，拒绝用于性能比对。" -f $fixtureCount))
            continue
        }
        if ($document.PSObject.Properties.Name -contains "result") {
            $benchmarkResult = $document.result
        }
    }

    $comparison = Test-CodexPerformanceBudget -FixtureEntityCount $fixtureCount `
        -BudgetEntry $budgetEntry -BenchmarkResult $benchmarkResult `
        -MaximumRatio $MaximumRegressionRatio
    $null = $results.Add($comparison)
    foreach ($problem in $comparison.Problems) {
        $null = $allProblems.Add($problem)
    }
}

$evidence = [ordered]@{
    SchemaVersion = 1
    RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    RunCorrelationId = Get-CodexGateRunCorrelationId
    Scope = "m9-7-performance-budget-gate"
    MaximumRegressionRatio = $MaximumRegressionRatio
    Fixtures = @($results | ForEach-Object {
        [ordered]@{
            FixtureEntityCount = $_.FixtureEntityCount
            Comparisons = @($_.Comparisons | ForEach-Object {
                [ordered]@{
                    Metric = $_.Metric
                    State = $_.State
                    Ratio = $_.Ratio
                }
            })
        }
    })
    ProblemCount = $allProblems.Count
    Problems = @($allProblems)
    UiBaselinesCovered = $false
    EvidenceBoundary = "This gate covers only the CAD side of M9.7 - the 1k/10k/50k DrawingIndex baselines. The UI baselines M9.7 also requires (message virtualisation, delta merge, dispatcher occupancy, session switching, Palette Reset) belong to M8, which is not built, so passing this gate does NOT complete M9.7. Numbers compared here come from user-observed runs in real AutoCAD; this gate does not start or measure AutoCAD itself."
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) {
        [IO.Path]::GetFullPath($EvidencePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidencePath))
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) -Force | Out-Null
    [IO.File]::WriteAllText($resolvedEvidencePath, ($evidence | ConvertTo-Json -Depth 10),
        (New-Object Text.UTF8Encoding($false)))
    Write-Host ("M9_PERFORMANCE_BUDGET_EVIDENCE=" + $resolvedEvidencePath)
}

Complete-CodexBuildSafety -State $buildSafety -Stage "m9-7-performance-budget" | Out-Null

if ($allProblems.Count -ne 0) {
    Write-Host "`nM9.7 CAD 性能预算门禁未通过，共 $($allProblems.Count) 项：" -ForegroundColor Yellow
    foreach ($problem in $allProblems) {
        Write-Host ("  - " + $problem)
    }
    Write-Host "M9_PERFORMANCE_BUDGET=failed"
    exit 1
}

Write-Host "`nM9.7 CAD 性能预算门禁通过；UI 侧基线属于 M8，尚未覆盖。" -ForegroundColor Green
Write-Host "M9_PERFORMANCE_BUDGET=passed"

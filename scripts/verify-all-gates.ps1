[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    # AgentHost 直接启动的 codex.exe 绝对路径；留空时由 Phase 2 自行探测。
    [string] $CodexExecutable,

    # R20.1 门禁需要的 AutoCAD 2016 安装目录（只读取托管 API 程序集，不启动 AutoCAD）。
    [string] $AutoCad2016Dir,

    [string] $EvidenceDirectory,

    # R20.1 门禁在构建前后比对 acad 进程集合，构建期间集合变化就失败——这是它证明
    # 「构建没碰过运行中的 AutoCAD」的方式。因此本套件默认在 AutoCAD 运行时直接拒绝
    # 启动，而不是先花十几分钟构建再必然失败。
    [switch] $IgnoreRunningAutoCad,

    [switch] $SelfTestOnly
)

# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。
#
# M9.3：把必过门禁汇总成一个可复现的入口。
#
# 在此之前，跑全套门禁靠的是会话临时目录里的一次性驱动脚本——它不在仓库里，别人复现不了，
# 会话结束就消失，而且每次重写都可能漏掉某一项。本脚本把这件事变成仓库内容。
#
# 总数一律动态汇总，不写死任何规格数字：硬编码的数字在规格增加后只会变成一个需要有人
# 记得更新的谎言。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")

function Get-CodexGateDefinitions {
    <#
    .SYNOPSIS
        必过门禁的顺序定义。Readiness 必须排在最后——它汇总前面各项的 evidence。
    #>
    return @(
        [pscustomobject]@{
            Name = "phase2-powershell7"
            Script = "scripts\verify-phase2.ps1"
            Shell = "Core"
            EvidenceFile = "phase2-ps7.json"
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "phase2-windowspowershell51"
            Script = "scripts\verify-phase2.ps1"
            Shell = "Desktop"
            EvidenceFile = "phase2-ps51.json"
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "agent-bootstrap"
            Script = "scripts\verify-autocad2016-agent-bootstrap.ps1"
            Shell = "Core"
            EvidenceFile = ""
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "auth-compat"
            Script = "scripts\verify-autocad2016-auth-compat.ps1"
            Shell = "Core"
            EvidenceFile = ""
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "r201-host-build"
            Script = "scripts\verify-m4-r201-host-build.ps1"
            Shell = "Core"
            EvidenceFile = "r201-host-build.json"
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "m9-sbom-licenses"
            Script = "scripts\verify-m9-sbom-and-licenses.ps1"
            Shell = "Core"
            EvidenceFile = "m9-sbom.json"
            IsAggregator = $false
        },
        [pscustomobject]@{
            Name = "m4-readiness"
            Script = "scripts\verify-m4-automated-readiness.ps1"
            Shell = "Core"
            EvidenceFile = "m4-readiness.json"
            IsAggregator = $true
        }
    )
}

function Get-CodexRelevantResidualProcessCount {
    $count = 0
    foreach ($name in @(
            "Codex.AutoCAD.AgentHost",
            "Codex.AutoCAD.AgentLauncher.FakeAgentHost",
            "Codex.AutoCAD.Bridge.Client.TestServer")) {
        $count += @(Get-Process -Name $name -ErrorAction SilentlyContinue).Count
    }
    return $count
}

function Find-CodexNewestStageEvidence {
    <#
    .SYNOPSIS
        某些门禁只写到自己的 stage 目录，不接受 -EvidencePath。按目录时间取最新一份。
    .DESCRIPTION
        这个回退本身有风险：门禁失败时不写 evidence，于是这里会取到上一次成功遗留的
        旧文件。RunCorrelationId 就是为此存在的——汇总器会拒绝没有本次标识的 evidence，
        所以取错文件会被下游发现，而不是静静通过。
    #>
    param(
        [Parameter(Mandatory = $true)][string] $ArtifactRoot,
        [Parameter(Mandatory = $true)][string] $Pattern
    )
    if (-not (Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
        return $null
    }
    $directories = @(Get-ChildItem -LiteralPath $ArtifactRoot -Directory -Force -Filter $Pattern `
            -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    foreach ($directory in $directories) {
        $candidate = Join-Path $directory.FullName "verification.json"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }
    return $null
}

function Get-CodexGateSuiteSummary {
    param([Parameter(Mandatory = $true)] $Results)

    $all = @($Results)
    $failed = @($all | Where-Object { $_.ExitCode -ne 0 })
    $pathChanged = @($all | Where-Object { -not $_.UserPathUnchanged })
    return [pscustomobject]@{
        Total = $all.Count
        Failed = $failed.Count
        Passed = ($all.Count - $failed.Count)
        FailedNames = @($failed | ForEach-Object { $_.Name })
        UserPathChangedCount = $pathChanged.Count
        Success = ($failed.Count -eq 0 -and $pathChanged.Count -eq 0)
    }
}

if ($SelfTestOnly) {
    $definitions = @(Get-CodexGateDefinitions)
    if ($definitions.Count -lt 2) {
        throw "自检失败：门禁定义少于两项。"
    }
    if (@($definitions | Where-Object { $_.IsAggregator }).Count -ne 1) {
        throw "自检失败：汇总器门禁必须且只能有一个。"
    }
    if (-not $definitions[$definitions.Count - 1].IsAggregator) {
        throw "自检失败：汇总器必须是最后一项，否则它读不到本次上游 evidence。"
    }
    foreach ($definition in $definitions) {
        $scriptPath = Join-Path $repoRoot $definition.Script
        if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
            throw "自检失败：门禁脚本不存在：$($definition.Script)"
        }
    }
    $duplicateNames = @($definitions | Group-Object Name | Where-Object { $_.Count -gt 1 })
    if ($duplicateNames.Count -ne 0) {
        throw "自检失败：门禁名称重复。"
    }

    $allPassed = Get-CodexGateSuiteSummary @(
        [pscustomobject]@{ Name = "a"; ExitCode = 0; UserPathUnchanged = $true },
        [pscustomobject]@{ Name = "b"; ExitCode = 0; UserPathUnchanged = $true })
    if (-not $allPassed.Success -or $allPassed.Passed -ne 2) {
        throw "自检失败：全部通过时汇总为失败。"
    }
    $oneFailed = Get-CodexGateSuiteSummary @(
        [pscustomobject]@{ Name = "a"; ExitCode = 0; UserPathUnchanged = $true },
        [pscustomobject]@{ Name = "b"; ExitCode = 1; UserPathUnchanged = $true })
    if ($oneFailed.Success -or $oneFailed.Failed -ne 1 -or $oneFailed.FailedNames[0] -cne "b") {
        throw "自检失败：单项失败没有让整套失败。"
    }
    # PATH 被改动即使门禁全绿也必须整套失败——那正是本项目两次事故的形态。
    $pathMoved = Get-CodexGateSuiteSummary @(
        [pscustomobject]@{ Name = "a"; ExitCode = 0; UserPathUnchanged = $false })
    if ($pathMoved.Success) {
        throw "自检失败：用户 PATH 变化没有让整套失败。"
    }

    Write-Host "ALL_GATES_SELF_TEST=passed"
    return
}

$runningAutoCad = @(Get-Process -Name acad -ErrorAction SilentlyContinue).Count
if ($runningAutoCad -ne 0 -and -not $IgnoreRunningAutoCad) {
    throw ("检测到 $runningAutoCad 个 AutoCAD 进程。R20.1 门禁会比对构建前后的 acad 进程" +
        "集合，构建期间启动或退出 AutoCAD 必然使该门禁失败。请先关闭 AutoCAD，" +
        "或显式传入 -IgnoreRunningAutoCad 接受这一后果。")
}

$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactRoot = $buildSafety.ArtifactRoot
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $artifactRoot "gate-evidence"
}
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null

# 一次性运行关联标识：各门禁写进自己的 evidence，汇总器要求五份携带同一个。
# 没有它，失败的门禁不写 evidence，汇总器就会读到上一次成功遗留的旧文件并报绿。
$previousRunId = $env:CODEX_GATE_RUN_ID
$env:CODEX_GATE_RUN_ID = "run-" + [Guid]::NewGuid().ToString("N")
$runCorrelationId = $env:CODEX_GATE_RUN_ID

$pwshPath = (Get-Command pwsh -ErrorAction SilentlyContinue)
if ($null -eq $pwshPath) {
    throw "找不到 pwsh。不要假设 PowerShell 7 的安装路径，必须由 Get-Command 解析。"
}
$coreShell = $pwshPath.Source
$desktopShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"

$baselinePath = Get-CodexUserPathState
$results = New-Object System.Collections.ArrayList

try {
    foreach ($definition in (Get-CodexGateDefinitions)) {
        $gateArguments = New-Object System.Collections.ArrayList

        if ($definition.IsAggregator) {
            $failedSoFar = @($results | Where-Object { $_.ExitCode -ne 0 })
            if ($failedSoFar.Count -ne 0) {
                Write-Host ("`n==> 跳过 $($definition.Name)：已有 $($failedSoFar.Count) 项上游门禁失败") `
                    -ForegroundColor Yellow
                $null = $results.Add([pscustomobject]@{
                    Name = $definition.Name
                    ExitCode = 1
                    UserPathUnchanged = $true
                    Skipped = $true
                })
                continue
            }
            $bootstrapEvidence = Find-CodexNewestStageEvidence -ArtifactRoot $artifactRoot `
                -Pattern "autocad2016-agent-bootstrap-*"
            $authEvidence = Find-CodexNewestStageEvidence -ArtifactRoot $artifactRoot `
                -Pattern "autocad2016-auth-compat-*"
            if ($null -eq $bootstrapEvidence -or $null -eq $authEvidence) {
                throw "缺少 bootstrap 或 auth evidence，无法汇总。"
            }
            $null = $gateArguments.AddRange(@(
                "-Phase2PowerShell7EvidencePath", (Join-Path $EvidenceDirectory "phase2-ps7.json"),
                "-Phase2WindowsPowerShell51EvidencePath",
                (Join-Path $EvidenceDirectory "phase2-ps51.json"),
                "-AgentBootstrapEvidencePath", $bootstrapEvidence,
                "-AuthCompatEvidencePath", $authEvidence,
                "-R201HostEvidencePath", (Join-Path $EvidenceDirectory "r201-host-build.json"),
                "-RequireRunCorrelation"))
        }
        elseif ($definition.Name -eq "r201-host-build") {
            if (-not [string]::IsNullOrWhiteSpace($AutoCad2016Dir)) {
                $null = $gateArguments.AddRange(@("-AutoCad2016Dir", $AutoCad2016Dir))
            }
            $null = $gateArguments.AddRange(@("-Configuration", $Configuration))
        }
        elseif ($definition.Name -eq "m9-sbom-licenses") {
            # 该门禁不接受 -Configuration。
        }
        else {
            $null = $gateArguments.AddRange(@("-Configuration", $Configuration))
            if (-not [string]::IsNullOrWhiteSpace($CodexExecutable)) {
                $null = $gateArguments.AddRange(@("-CodexExecutable", $CodexExecutable))
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($definition.EvidenceFile)) {
            $null = $gateArguments.AddRange(@(
                "-EvidencePath", (Join-Path $EvidenceDirectory $definition.EvidenceFile)))
        }

        $shell = if ($definition.Shell -eq "Desktop") { $desktopShell } else { $coreShell }
        $shellArguments = if ($definition.Shell -eq "Desktop") {
            @("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File")
        }
        else {
            @("-NoProfile", "-NonInteractive", "-File")
        }

        Write-Host "`n================ GATE: $($definition.Name) ================" -ForegroundColor Cyan
        & $shell @shellArguments (Join-Path $repoRoot $definition.Script) @gateArguments 2>&1 |
            ForEach-Object { Write-Output $_ }
        $exitCode = $LASTEXITCODE

        $currentPath = Get-CodexUserPathState
        $pathUnchanged = ($currentPath.Sha256 -ceq $baselinePath.Sha256)
        Write-Host ("---- $($definition.Name): exit=$exitCode pathUnchanged=$pathUnchanged")
        $null = $results.Add([pscustomobject]@{
            Name = $definition.Name
            ExitCode = $exitCode
            UserPathUnchanged = $pathUnchanged
            Skipped = $false
        })
    }
}
finally {
    $env:CODEX_GATE_RUN_ID = $previousRunId
}

$summary = Get-CodexGateSuiteSummary $results
$residual = Get-CodexRelevantResidualProcessCount
$finalPath = Get-CodexUserPathState

$suiteEvidence = [ordered]@{
    SchemaVersion = 1
    RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    RunCorrelationId = $runCorrelationId
    Scope = "codex-autocad-must-pass-gate-suite"
    Configuration = $Configuration
    GateTotal = $summary.Total
    GatePassed = $summary.Passed
    GateFailed = $summary.Failed
    FailedGates = @($summary.FailedNames)
    Gates = @($results | ForEach-Object {
        [ordered]@{
            Name = $_.Name
            ExitCode = $_.ExitCode
            UserPathUnchanged = $_.UserPathUnchanged
            Skipped = $_.Skipped
        }
    })
    UserPathLength = $finalPath.Length
    UserPathSha256 = $finalPath.Sha256
    UserPathUnchanged = ($finalPath.Sha256 -ceq $baselinePath.Sha256)
    RelevantResidualProcessCount = $residual
    AutoCadStartedOrCommanded = $false
    EvidenceBoundary = "This evidence records which must-pass gates ran in one correlated suite invocation, their exit codes, a hashed user-PATH fingerprint and the residual process count. Gate totals are counted at run time and never hardcoded. It does not start or command AutoCAD, enable CAD writes or saves, prove any real-machine or enterprise matrix, or freeze any candidate."
}

$suiteEvidencePath = Join-Path $EvidenceDirectory "all-gates.json"
$encoding = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($suiteEvidencePath, ($suiteEvidence | ConvertTo-Json -Depth 8), $encoding)

Complete-CodexBuildSafety -State $buildSafety -Stage "all-gates" | Out-Null

Write-Host "`n================ SUMMARY ================"
foreach ($result in $results) {
    $state = if ($result.Skipped) { "skipped" } elseif ($result.ExitCode -eq 0) { "pass" } else { "FAIL" }
    Write-Host ("{0,-30} {1,-8} exit={2,-4} pathUnchanged={3}" -f `
        $result.Name, $state, $result.ExitCode, $result.UserPathUnchanged)
}
Write-Host ("ALL_GATES_TOTAL=" + $summary.Total)
Write-Host ("ALL_GATES_PASSED=" + $summary.Passed)
Write-Host ("ALL_GATES_FAILED=" + $summary.Failed)
Write-Host ("ALL_GATES_RESIDUAL_PROCESSES=" + $residual)
Write-Host ("ALL_GATES_USER_PATH_UNCHANGED=" + $suiteEvidence.UserPathUnchanged)
Write-Host ("ALL_GATES_EVIDENCE=" + $suiteEvidencePath)

if (-not $summary.Success -or $residual -ne 0) {
    Write-Host "`n必过门禁套件未通过。" -ForegroundColor Yellow
    Write-Host "ALL_GATES=failed"
    exit 1
}

Write-Host "`n必过门禁套件全部通过；真实机器与企业矩阵仍未验证。" -ForegroundColor Green
Write-Host "ALL_GATES=passed"

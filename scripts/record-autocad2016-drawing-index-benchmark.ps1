[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^autocad2016-m2-drawing-index-v040-[a-f0-9]{8}-[a-f0-9]{8}-[a-f0-9]{8}$')]
    [string] $CandidateId,

    [Parameter(Mandatory = $true)]
    [ValidateSet(1000, 10000, 50000)]
    [int] $FixtureEntityCount,

    [Parameter(Mandatory = $true)]
    [ValidateSet('ready', 'partial', 'limited', 'failed', 'cancelled', 'stale')]
    [string] $Status,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 2000000)]
    [int] $EntityCount,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100000)]
    [int] $IndexedEntityCount,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100000)]
    [int] $UnsupportedEntityCount,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100000)]
    [int] $ReadFailedEntityCount,

    [Parameter(Mandatory = $true)]
    [bool] $Complete,

    [Parameter(Mandatory = $true)]
    [bool] $Limited,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 3600000)]
    [double] $MaximumIdleSliceMilliseconds,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 3600000)]
    [double] $TotalScanElapsedMilliseconds,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 2147483647)]
    [int] $IdleSliceCount,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 2147483647)]
    [long] $EstimatedManagedBytes,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 1099511627776)]
    [long] $AutoCadWorkingSetBeforeBytes,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 1099511627776)]
    [long] $PeakAutoCadWorkingSetBytes,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 2147483647)]
    [int] $QueryCount,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 3600000)]
    [double] $MaximumQueryMilliseconds,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 32767)]
    [int] $DbmodBefore,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 32767)]
    [int] $DbmodAfter,

    [Parameter(Mandatory = $true)]
    [bool] $AutoCadResponsive,

    [Parameter(Mandatory = $true)]
    [bool] $PaginationPassed,

    [Parameter(Mandatory = $true)]
    [bool] $DrawingIndexOnlyAskPassed,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureManifestPath = Join-Path $repoRoot 'handoff\autocad2016\benchmark-fixtures\DRAWING_INDEX_BENCHMARKS_V1.expected.json'
$fixtureManifest = Get-Content -LiteralPath $fixtureManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$fixture = @(
    $fixtureManifest.files |
        Where-Object { [int] $_.entityCount -eq $FixtureEntityCount }
)
if ($fixture.Count -ne 1) {
    throw 'benchmark_fixture_manifest_identity_invalid'
}
$fixture = $fixture[0]

if ($DbmodBefore -ne $DbmodAfter) {
    throw 'benchmark_dbmod_changed'
}
if ($IndexedEntityCount -gt $EntityCount) {
    throw 'benchmark_indexed_count_exceeds_entity_count'
}
if ($UnsupportedEntityCount -gt $IndexedEntityCount) {
    throw 'benchmark_unsupported_count_exceeds_indexed_count'
}
if ($ReadFailedEntityCount -gt $IndexedEntityCount) {
    throw 'benchmark_failed_count_exceeds_indexed_count'
}
if ($PeakAutoCadWorkingSetBytes -ne 0 -and
    $PeakAutoCadWorkingSetBytes -lt $AutoCadWorkingSetBeforeBytes) {
    throw 'benchmark_peak_working_set_before_baseline'
}
if ($Status -ceq 'ready') {
    if (-not $Complete -or $Limited) {
        throw 'benchmark_ready_flags_invalid'
    }
    if ($EntityCount -ne $FixtureEntityCount -or $IndexedEntityCount -ne $FixtureEntityCount) {
        throw 'benchmark_ready_fixture_count_mismatch'
    }
    if ($UnsupportedEntityCount -ne 0 -or $ReadFailedEntityCount -ne 0) {
        throw 'benchmark_ready_fixture_not_fully_parsed'
    }
}
if ($Limited -and $Status -cne 'limited') {
    throw 'benchmark_limited_status_mismatch'
}

$performanceObserved =
    $MaximumIdleSliceMilliseconds -gt 0 -and
    $TotalScanElapsedMilliseconds -gt 0 -and
    $IdleSliceCount -gt 0 -and
    $EstimatedManagedBytes -gt 0 -and
    $AutoCadWorkingSetBeforeBytes -gt 0 -and
    $PeakAutoCadWorkingSetBytes -ge $AutoCadWorkingSetBeforeBytes
$queriesObserved = $QueryCount -gt 0

$allAcceptanceChecksPassed =
    $Status -ceq 'ready' -and
    $Complete -and
    -not $Limited -and
    $EntityCount -eq $FixtureEntityCount -and
    $IndexedEntityCount -eq $FixtureEntityCount -and
    $UnsupportedEntityCount -eq 0 -and
    $ReadFailedEntityCount -eq 0 -and
    $DbmodBefore -eq $DbmodAfter -and
    $performanceObserved -and
    $queriesObserved -and
    $AutoCadResponsive -and
    $PaginationPassed -and
    $DrawingIndexOnlyAskPassed

$evidence = [ordered]@{
    schema = 'codex.autocad.drawing-index-runtime-benchmark-evidence/1'
    recordedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    candidateId = $CandidateId
    fixture = [ordered]@{
        fileName = [string] $fixture.fileName
        expectedEntityCount = [int] $fixture.entityCount
        expectedSha256 = [string] $fixture.sha256
        dxfVersion = [string] $fixtureManifest.dxfVersion
        units = [string] $fixtureManifest.units
        sanitized = [bool] $fixtureManifest.sanitized
    }
    result = [ordered]@{
        status = $Status
        entityCount = $EntityCount
        indexedEntityCount = $IndexedEntityCount
        unsupportedEntityCount = $UnsupportedEntityCount
        readFailedEntityCount = $ReadFailedEntityCount
        complete = $Complete
        limited = $Limited
        idleSliceCount = $IdleSliceCount
        maximumIdleSliceMilliseconds = $MaximumIdleSliceMilliseconds
        totalScanElapsedMilliseconds = $TotalScanElapsedMilliseconds
        estimatedManagedBytes = $EstimatedManagedBytes
        autoCadWorkingSetBeforeBytes = $AutoCadWorkingSetBeforeBytes
        peakAutoCadWorkingSetBytes = $PeakAutoCadWorkingSetBytes
        queryCount = $QueryCount
        maximumQueryMilliseconds = $MaximumQueryMilliseconds
        dbmodBefore = $DbmodBefore
        dbmodAfter = $DbmodAfter
        autoCadResponsive = $AutoCadResponsive
        paginationPassed = $PaginationPassed
        drawingIndexOnlyAskPassed = $DrawingIndexOnlyAskPassed
    }
    guardrails = [ordered]@{
        cooperativeIdleSliceTargetMilliseconds = 12
        scanTimeoutMilliseconds = 120000
        managedMemoryLimitBytes = 67108864
        maximumIndexedEntities = 100000
        maximumCadQueryPageSize = 200
        maximumIpcMessageBytes = 8388608
        note = 'Observed maximum idle time can exceed the cooperative target when one entity read or final publication is slower; retain the observed value.'
    }
    acceptance = [ordered]@{
        allChecksPassed = $allAcceptanceChecksPassed
        dbmodUnchanged = $DbmodBefore -eq $DbmodAfter
        expectedPopulationIndexed =
            $EntityCount -eq $FixtureEntityCount -and
            $IndexedEntityCount -eq $FixtureEntityCount
        fullyParsed = $UnsupportedEntityCount -eq 0 -and $ReadFailedEntityCount -eq 0
        completeAndNotLimited = $Complete -and -not $Limited
        performanceObserved = $performanceObserved
        queriesObserved = $queriesObserved
        autoCadResponsive = $AutoCadResponsive
        paginationPassed = $PaginationPassed
        drawingIndexOnlyAskPassed = $DrawingIndexOnlyAskPassed
    }
    privacy = [ordered]@{
        drawingNameCaptured = $false
        drawingPathCaptured = $false
        canonicalJsonCaptured = $false
        selectionHashCaptured = $false
        contextHashCaptured = $false
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "benchmark_evidence_output_exists: $resolvedOutput"
}
$parent = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($parent)) {
    throw 'benchmark_evidence_output_parent_invalid'
}
[IO.Directory]::CreateDirectory($parent) | Out-Null
$json = $evidence | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + "`r`n",
    (New-Object Text.UTF8Encoding($false)))

Write-Host "DrawingIndex runtime benchmark evidence recorded: $resolvedOutput"
Write-Host "ACCEPTANCE=$($allAcceptanceChecksPassed.ToString().ToLowerInvariant())"

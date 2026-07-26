[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$generatorPath = Join-Path $PSScriptRoot 'new-autocad2016-drawing-index-benchmarks.ps1'
$recorderPath = Join-Path $PSScriptRoot 'record-autocad2016-drawing-index-benchmark.ps1'
$expectedManifestPath = Join-Path $repoRoot 'handoff\autocad2016\benchmark-fixtures\DRAWING_INDEX_BENCHMARKS_V1.expected.json'
$allowedTypes = @('LINE', 'CIRCLE', 'ARC', 'TEXT', 'INSERT')
$expectedLayers = @(
    'BENCH_L00',
    'BENCH_L01',
    'BENCH_L02',
    'BENCH_L03',
    'BENCH_L04',
    'BENCH_L05',
    'BENCH_L06',
    'BENCH_L07'
)
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
$passed = 0

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal([object] $Expected, [object] $Actual, [string] $Message) {
    if ([string] $Expected -cne [string] $Actual) {
        throw "$Message Expected='$Expected' Actual='$Actual'."
    }
}

function New-CountMap([string[]] $Keys) {
    $result = @{}
    foreach ($key in $Keys) {
        $result[$key] = 0
    }
    return $result
}

function Add-CurrentEntity(
    [string] $EntityType,
    [string] $Layer,
    [bool] $PaperSpace,
    [Collections.IDictionary] $TypeCounts,
    [Collections.IDictionary] $LayerCounts,
    [ref] $EntityCount,
    [ref] $ModelSpaceCount,
    [ref] $PaperSpaceCount
) {
    if ([string]::IsNullOrEmpty($EntityType)) {
        return
    }
    if ($allowedTypes -cnotcontains $EntityType) {
        throw "benchmark_dxf_entity_type_unexpected: $EntityType"
    }
    if ($expectedLayers -cnotcontains $Layer) {
        throw "benchmark_dxf_layer_unexpected: $Layer"
    }
    $TypeCounts[$EntityType] = [int] $TypeCounts[$EntityType] + 1
    $LayerCounts[$Layer] = [int] $LayerCounts[$Layer] + 1
    $EntityCount.Value = [int] $EntityCount.Value + 1
    if ($PaperSpace) {
        $PaperSpaceCount.Value = [int] $PaperSpaceCount.Value + 1
    }
    else {
        $ModelSpaceCount.Value = [int] $ModelSpaceCount.Value + 1
    }
}

function Read-DxfEntityStats([string] $Path) {
    $typeCounts = New-CountMap $allowedTypes
    $layerCounts = New-CountMap $expectedLayers
    $entityCount = 0
    $modelSpaceCount = 0
    $paperSpaceCount = 0
    $section = ''
    $awaitingSectionName = $false
    $currentType = ''
    $currentLayer = ''
    $currentPaperSpace = $false
    $sawEntities = $false
    $sawEof = $false
    $pairCount = 0
    $reader = New-Object IO.StreamReader(
        $Path,
        [Text.Encoding]::ASCII,
        $false,
        65536)
    try {
        while ($true) {
            $codeLine = $reader.ReadLine()
            if ($null -eq $codeLine) {
                break
            }
            $valueLine = $reader.ReadLine()
            if ($null -eq $valueLine) {
                throw 'benchmark_dxf_odd_line_count'
            }
            $pairCount++
            $code = 0
            if (-not [int]::TryParse(
                    $codeLine.Trim(),
                    [Globalization.NumberStyles]::Integer,
                    $invariantCulture,
                    [ref] $code)) {
                throw "benchmark_dxf_group_code_invalid: $codeLine"
            }

            if ($code -eq 0) {
                if ($section -ceq 'ENTITIES' -and -not [string]::IsNullOrEmpty($currentType)) {
                    Add-CurrentEntity `
                        $currentType `
                        $currentLayer `
                        $currentPaperSpace `
                        $typeCounts `
                        $layerCounts `
                        ([ref] $entityCount) `
                        ([ref] $modelSpaceCount) `
                        ([ref] $paperSpaceCount)
                    $currentType = ''
                    $currentLayer = ''
                    $currentPaperSpace = $false
                }

                if ($valueLine -ceq 'SECTION') {
                    $awaitingSectionName = $true
                    continue
                }
                if ($valueLine -ceq 'ENDSEC') {
                    $section = ''
                    continue
                }
                if ($valueLine -ceq 'EOF') {
                    $sawEof = $true
                    continue
                }
                if ($section -ceq 'ENTITIES') {
                    $currentType = $valueLine
                }
                continue
            }

            if ($awaitingSectionName) {
                if ($code -ne 2 -or [string]::IsNullOrWhiteSpace($valueLine)) {
                    throw 'benchmark_dxf_section_name_invalid'
                }
                $section = $valueLine
                $awaitingSectionName = $false
                if ($section -ceq 'ENTITIES') {
                    $sawEntities = $true
                }
                continue
            }

            if ($section -ceq 'ENTITIES' -and -not [string]::IsNullOrEmpty($currentType)) {
                if ($code -eq 8) {
                    $currentLayer = $valueLine
                }
                elseif ($code -eq 67) {
                    $currentPaperSpace = $valueLine -ceq '1'
                }
            }
        }
    }
    finally {
        $reader.Dispose()
    }

    Assert-True $sawEntities 'benchmark_dxf_entities_section_missing'
    Assert-True $sawEof 'benchmark_dxf_eof_missing'
    Assert-True ($pairCount -gt 0) 'benchmark_dxf_empty'
    return [pscustomobject]@{
        EntityCount = $entityCount
        ModelSpaceCount = $modelSpaceCount
        PaperSpaceCount = $paperSpaceCount
        TypeCounts = $typeCounts
        LayerCounts = $layerCounts
    }
}

function Convert-CountListToMap([object[]] $Values) {
    $result = @{}
    foreach ($value in @($Values)) {
        $key = [string] $value.key
        if ([string]::IsNullOrWhiteSpace($key) -or $result.ContainsKey($key)) {
            throw 'benchmark_manifest_count_key_invalid'
        }
        $result[$key] = [int] $value.count
    }
    return $result
}

function Assert-CountMapsEqual(
    [Collections.IDictionary] $Expected,
    [Collections.IDictionary] $Actual,
    [string] $Label
) {
    $expectedKeys = @($Expected.Keys | Sort-Object)
    $actualKeys = @($Actual.Keys | Sort-Object)
    Assert-Equal ($expectedKeys -join ',') ($actualKeys -join ',') "$Label keys differ."
    foreach ($key in $expectedKeys) {
        Assert-Equal ([int] $Expected[$key]) ([int] $Actual[$key]) "$Label count differs for $key."
    }
}

function Assert-AsciiFile([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = New-Object byte[] 65536
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            for ($index = 0; $index -lt $read; $index++) {
                if ($buffer[$index] -gt 127) {
                    throw "benchmark_dxf_non_ascii_byte: $Path"
                }
            }
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ManifestAndFiles(
    [string] $OutputDirectory,
    [pscustomobject] $ExpectedManifest
) {
    $manifestPath = Join-Path $OutputDirectory 'drawing-index-benchmarks.manifest.json'
    Assert-True (Test-Path -LiteralPath $manifestPath -PathType Leaf) 'benchmark_manifest_missing'
    $actualManifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in @('schema', 'generatorVersion', 'dxfVersion', 'units', 'sanitized', 'deterministic')) {
        Assert-Equal $ExpectedManifest.$property $actualManifest.$property "Manifest $property differs."
    }
    Assert-Equal @($ExpectedManifest.files).Count @($actualManifest.files).Count 'Manifest file count differs.'

    $actualNames = @((Get-ChildItem -LiteralPath $OutputDirectory -File).Name | Sort-Object)
    $expectedNames = @('drawing-index-benchmarks.manifest.json') + @($ExpectedManifest.files.fileName)
    $expectedNames = @($expectedNames | Sort-Object)
    Assert-Equal ($expectedNames -join ',') ($actualNames -join ',') 'Generated output file set differs.'

    foreach ($expectedFile in @($ExpectedManifest.files)) {
        $actualFile = @($actualManifest.files | Where-Object { $_.fileName -ceq $expectedFile.fileName })
        Assert-Equal 1 $actualFile.Count "Manifest entry count differs for $($expectedFile.fileName)."
        $actualFile = $actualFile[0]
        foreach ($property in @('entityCount', 'modelSpaceCount', 'paperSpaceCount', 'bytes', 'sha256')) {
            Assert-Equal $expectedFile.$property $actualFile.$property "$($expectedFile.fileName) manifest $property differs."
        }

        $expectedTypeCounts = Convert-CountListToMap @($expectedFile.typeCounts)
        $actualTypeCounts = Convert-CountListToMap @($actualFile.typeCounts)
        $expectedLayerCounts = Convert-CountListToMap @($expectedFile.layerCounts)
        $actualLayerCounts = Convert-CountListToMap @($actualFile.layerCounts)
        Assert-CountMapsEqual $expectedTypeCounts $actualTypeCounts "$($expectedFile.fileName) manifest type"
        Assert-CountMapsEqual $expectedLayerCounts $actualLayerCounts "$($expectedFile.fileName) manifest layer"

        $dxfPath = Join-Path $OutputDirectory ([string] $expectedFile.fileName)
        Assert-True (Test-Path -LiteralPath $dxfPath -PathType Leaf) "benchmark_dxf_missing: $dxfPath"
        Assert-AsciiFile $dxfPath
        $item = Get-Item -LiteralPath $dxfPath
        $hash = (Get-FileHash -LiteralPath $dxfPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Equal ([long] $expectedFile.bytes) ([long] $item.Length) "$($expectedFile.fileName) byte count differs."
        Assert-Equal ([string] $expectedFile.sha256) $hash "$($expectedFile.fileName) hash differs."

        $stats = Read-DxfEntityStats $dxfPath
        Assert-Equal ([int] $expectedFile.entityCount) $stats.EntityCount "$($expectedFile.fileName) entity count differs."
        Assert-Equal ([int] $expectedFile.modelSpaceCount) $stats.ModelSpaceCount "$($expectedFile.fileName) model-space count differs."
        Assert-Equal ([int] $expectedFile.paperSpaceCount) $stats.PaperSpaceCount "$($expectedFile.fileName) paper-space count differs."
        Assert-CountMapsEqual $expectedTypeCounts $stats.TypeCounts "$($expectedFile.fileName) parsed type"
        Assert-CountMapsEqual $expectedLayerCounts $stats.LayerCounts "$($expectedFile.fileName) parsed layer"
    }
    return $actualManifest
}

Assert-True (Test-Path -LiteralPath $generatorPath -PathType Leaf) 'benchmark_generator_missing'
Assert-True (Test-Path -LiteralPath $recorderPath -PathType Leaf) 'benchmark_recorder_missing'
Assert-True (Test-Path -LiteralPath $expectedManifestPath -PathType Leaf) 'benchmark_expected_manifest_missing'
$generatorSource = Get-Content -LiteralPath $generatorPath -Raw -Encoding UTF8
foreach ($forbiddenPattern in @(
        '(?i)\bStart-Process\b',
        '(?i)\bInvoke-Expression\b',
        '(?i)\bacad\.exe\b',
        '(?i)\baccoreconsole(?:\.exe)?\b',
        '(?i)\bSendStringToExecute\b',
        '(?i)\bDwgOut\b',
        '(?i)\bSaveAs\b')) {
    Assert-True (-not [regex]::IsMatch($generatorSource, $forbiddenPattern)) `
        "benchmark_generator_forbidden_automation: $forbiddenPattern"
}
$recorderSource = Get-Content -LiteralPath $recorderPath -Raw -Encoding UTF8
foreach ($forbiddenPattern in @(
        '(?i)\bStart-Process\b',
        '(?i)\bInvoke-Expression\b',
        '(?i)\bacad\.exe\b',
        '(?i)\baccoreconsole(?:\.exe)?\b',
        '(?i)\bSendStringToExecute\b')) {
    Assert-True (-not [regex]::IsMatch($recorderSource, $forbiddenPattern)) `
        "benchmark_recorder_forbidden_automation: $forbiddenPattern"
}
$passed++

$expectedManifest = Get-Content -LiteralPath $expectedManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('codex-autocad-benchmark-' + [Guid]::NewGuid().ToString('N'))
$resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
$systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
Assert-True ($resolvedTempRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase)) `
    'benchmark_temp_path_outside_system_temp'

try {
    $firstRun = Join-Path $resolvedTempRoot 'first'
    $secondRun = Join-Path $resolvedTempRoot 'second'
    & $generatorPath -OutputDirectory $firstRun
    $firstManifest = Assert-ManifestAndFiles $firstRun $expectedManifest
    $passed++

    & $generatorPath -OutputDirectory $secondRun
    $secondManifest = Assert-ManifestAndFiles $secondRun $expectedManifest
    foreach ($firstFile in @($firstManifest.files)) {
        $secondFile = @($secondManifest.files | Where-Object { $_.fileName -ceq $firstFile.fileName })[0]
        Assert-Equal $firstFile.sha256 $secondFile.sha256 "$($firstFile.fileName) is not deterministic."
        Assert-Equal $firstFile.bytes $secondFile.bytes "$($firstFile.fileName) byte count is not deterministic."
    }
    $passed++

    $overwriteRejected = $false
    try {
        & $generatorPath -OutputDirectory $firstRun
    }
    catch {
        $overwriteRejected = $_.Exception.Message -like 'benchmark_output_directory_exists:*'
    }
    Assert-True $overwriteRejected 'benchmark_existing_output_was_not_rejected'
    $passed++

    $evidencePath = Join-Path $resolvedTempRoot 'runtime-evidence.json'
    & $recorderPath `
        -CandidateId 'autocad2016-m2-drawing-index-v040-11111111-22222222-33333333' `
        -FixtureEntityCount 1000 `
        -Status 'ready' `
        -EntityCount 1000 `
        -IndexedEntityCount 1000 `
        -UnsupportedEntityCount 0 `
        -ReadFailedEntityCount 0 `
        -Complete $true `
        -Limited $false `
        -MaximumIdleSliceMilliseconds 14.25 `
        -TotalScanElapsedMilliseconds 925 `
        -IdleSliceCount 12 `
        -EstimatedManagedBytes 262144 `
        -AutoCadWorkingSetBeforeBytes 400000000 `
        -PeakAutoCadWorkingSetBytes 450000000 `
        -QueryCount 3 `
        -MaximumQueryMilliseconds 1.75 `
        -DbmodBefore 7 `
        -DbmodAfter 7 `
        -AutoCadResponsive $true `
        -PaginationPassed $true `
        -DrawingIndexOnlyAskPassed $true `
        -OutputPath $evidencePath
    $runtimeEvidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'codex.autocad.drawing-index-runtime-benchmark-evidence/1' `
        $runtimeEvidence.schema `
        'Runtime evidence schema differs.'
    Assert-True ([bool] $runtimeEvidence.acceptance.allChecksPassed) `
        'Valid runtime evidence did not pass acceptance.'
    Assert-True ([bool] $runtimeEvidence.acceptance.performanceObserved) `
        'Valid runtime evidence did not retain observed performance evidence.'
    Assert-True ([bool] $runtimeEvidence.acceptance.queriesObserved) `
        'Valid runtime evidence did not retain observed query evidence.'
    Assert-Equal 400000000 `
        $runtimeEvidence.result.autoCadWorkingSetBeforeBytes `
        'Runtime evidence did not retain the AutoCAD working-set baseline.'
    Assert-Equal 450000000 `
        $runtimeEvidence.result.peakAutoCadWorkingSetBytes `
        'Runtime evidence did not retain the peak AutoCAD working set.'
    Assert-Equal 200 `
        $runtimeEvidence.guardrails.maximumCadQueryPageSize `
        'Runtime evidence did not retain the query page hard limit.'
    Assert-Equal 8388608 `
        $runtimeEvidence.guardrails.maximumIpcMessageBytes `
        'Runtime evidence did not retain the IPC message hard limit.'
    Assert-True (-not [bool] $runtimeEvidence.privacy.drawingPathCaptured) `
        'Runtime evidence unexpectedly captured a drawing path.'
    Assert-Equal 'd14e77f376c454fff2ac2dc0e618c649ca23f24cb1e0797ee711b69a2eeb34c6' `
        $runtimeEvidence.fixture.expectedSha256 `
        'Runtime evidence did not bind the frozen fixture hash.'

    $missingObservationPath = Join-Path $resolvedTempRoot 'missing-observation-evidence.json'
    & $recorderPath `
        -CandidateId 'autocad2016-m2-drawing-index-v040-11111111-22222222-33333333' `
        -FixtureEntityCount 1000 `
        -Status 'ready' `
        -EntityCount 1000 `
        -IndexedEntityCount 1000 `
        -UnsupportedEntityCount 0 `
        -ReadFailedEntityCount 0 `
        -Complete $true `
        -Limited $false `
        -MaximumIdleSliceMilliseconds 0 `
        -TotalScanElapsedMilliseconds 0 `
        -IdleSliceCount 0 `
        -EstimatedManagedBytes 0 `
        -AutoCadWorkingSetBeforeBytes 0 `
        -PeakAutoCadWorkingSetBytes 0 `
        -QueryCount 0 `
        -MaximumQueryMilliseconds 0 `
        -DbmodBefore 7 `
        -DbmodAfter 7 `
        -AutoCadResponsive $true `
        -PaginationPassed $true `
        -DrawingIndexOnlyAskPassed $true `
        -OutputPath $missingObservationPath
    $missingObservationEvidence =
        Get-Content -LiteralPath $missingObservationPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    Assert-True (-not [bool] $missingObservationEvidence.acceptance.allChecksPassed) `
        'Missing timing, memory and query observations were accepted.'
    Assert-True (-not [bool] $missingObservationEvidence.acceptance.performanceObserved) `
        'Missing performance observations were marked present.'
    Assert-True (-not [bool] $missingObservationEvidence.acceptance.queriesObserved) `
        'Missing query observations were marked present.'

    $workingSetOrderRejected = $false
    try {
        & $recorderPath `
            -CandidateId 'autocad2016-m2-drawing-index-v040-11111111-22222222-33333333' `
            -FixtureEntityCount 1000 `
            -Status 'ready' `
            -EntityCount 1000 `
            -IndexedEntityCount 1000 `
            -UnsupportedEntityCount 0 `
            -ReadFailedEntityCount 0 `
            -Complete $true `
            -Limited $false `
            -MaximumIdleSliceMilliseconds 10 `
            -TotalScanElapsedMilliseconds 500 `
            -IdleSliceCount 10 `
            -EstimatedManagedBytes 200000 `
            -AutoCadWorkingSetBeforeBytes 450000000 `
            -PeakAutoCadWorkingSetBytes 400000000 `
            -QueryCount 1 `
            -MaximumQueryMilliseconds 1 `
            -DbmodBefore 7 `
            -DbmodAfter 7 `
            -AutoCadResponsive $true `
            -PaginationPassed $true `
            -DrawingIndexOnlyAskPassed $true `
            -OutputPath (Join-Path $resolvedTempRoot 'invalid-working-set-evidence.json')
    }
    catch {
        $workingSetOrderRejected =
            $_.Exception.Message -ceq 'benchmark_peak_working_set_before_baseline'
    }
    Assert-True $workingSetOrderRejected `
        'Runtime evidence accepted a peak working set below its baseline.'
    $passed++

    $invalidEvidenceRejected = $false
    try {
        & $recorderPath `
            -CandidateId 'autocad2016-m2-drawing-index-v040-11111111-22222222-33333333' `
            -FixtureEntityCount 1000 `
            -Status 'ready' `
            -EntityCount 1000 `
            -IndexedEntityCount 1000 `
            -UnsupportedEntityCount 0 `
            -ReadFailedEntityCount 0 `
            -Complete $true `
            -Limited $false `
            -MaximumIdleSliceMilliseconds 10 `
            -TotalScanElapsedMilliseconds 500 `
            -IdleSliceCount 10 `
            -EstimatedManagedBytes 200000 `
            -AutoCadWorkingSetBeforeBytes 400000000 `
            -PeakAutoCadWorkingSetBytes 450000000 `
            -QueryCount 1 `
            -MaximumQueryMilliseconds 1 `
            -DbmodBefore 7 `
            -DbmodAfter 8 `
            -AutoCadResponsive $true `
            -PaginationPassed $true `
            -DrawingIndexOnlyAskPassed $true `
            -OutputPath (Join-Path $resolvedTempRoot 'invalid-evidence.json')
    }
    catch {
        $invalidEvidenceRejected = $_.Exception.Message -ceq 'benchmark_dbmod_changed'
    }
    Assert-True $invalidEvidenceRejected 'Runtime evidence accepted a DBMOD change.'

    $evidenceOverwriteRejected = $false
    try {
        & $recorderPath `
            -CandidateId 'autocad2016-m2-drawing-index-v040-11111111-22222222-33333333' `
            -FixtureEntityCount 1000 `
            -Status 'ready' `
            -EntityCount 1000 `
            -IndexedEntityCount 1000 `
            -UnsupportedEntityCount 0 `
            -ReadFailedEntityCount 0 `
            -Complete $true `
            -Limited $false `
            -MaximumIdleSliceMilliseconds 14.25 `
            -TotalScanElapsedMilliseconds 925 `
            -IdleSliceCount 12 `
            -EstimatedManagedBytes 262144 `
            -AutoCadWorkingSetBeforeBytes 400000000 `
            -PeakAutoCadWorkingSetBytes 450000000 `
            -QueryCount 3 `
            -MaximumQueryMilliseconds 1.75 `
            -DbmodBefore 7 `
            -DbmodAfter 7 `
            -AutoCadResponsive $true `
            -PaginationPassed $true `
            -DrawingIndexOnlyAskPassed $true `
            -OutputPath $evidencePath
    }
    catch {
        $evidenceOverwriteRejected = $_.Exception.Message -like 'benchmark_evidence_output_exists:*'
    }
    Assert-True $evidenceOverwriteRejected 'Runtime evidence overwrote an existing record.'
    $passed++
}
finally {
    if (Test-Path -LiteralPath $resolvedTempRoot -PathType Container) {
        Assert-True ($resolvedTempRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase)) `
            'benchmark_cleanup_path_outside_system_temp'
        [IO.Directory]::Delete($resolvedTempRoot, $true)
    }
}

Write-Host "AutoCAD 2016 DrawingIndex benchmark fixture checks passed: $passed/6"

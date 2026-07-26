[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$generatorPath = Join-Path $PSScriptRoot 'new-autocad2016-m3-core-read-fixture.ps1'
$expectedManifestPath = Join-Path $repoRoot 'handoff\autocad2016\m3-fixtures\M3_CORE_READ_FIXTURE_V1.expected.json'
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

function Assert-AsciiFile([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = New-Object byte[] 65536
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            for ($index = 0; $index -lt $read; $index++) {
                if ($buffer[$index] -gt 127) {
                    throw "m3_fixture_non_ascii_byte: $Path"
                }
            }
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Add-CompletedEntity([Collections.Generic.List[object]] $Entities, [object] $Entity) {
    if ($null -ne $Entity) {
        [void] $Entities.Add($Entity)
    }
}

function Read-DxfTopLevelEntities([string] $Path) {
    $reader = New-Object IO.StreamReader($Path, [Text.Encoding]::ASCII, $false, 65536)
    $entities = New-Object 'System.Collections.Generic.List[object]'
    $section = ''
    $awaitingSectionName = $false
    $current = $null
    $nestedSequence = $false
    $sawEof = $false
    $pairCount = 0
    try {
        while ($true) {
            $codeLine = $reader.ReadLine()
            if ($null -eq $codeLine) {
                break
            }
            $valueLine = $reader.ReadLine()
            if ($null -eq $valueLine) {
                throw 'm3_fixture_dxf_odd_line_count'
            }
            $pairCount++
            $code = 0
            if (-not [int]::TryParse(
                    $codeLine.Trim(),
                    [Globalization.NumberStyles]::Integer,
                    $invariantCulture,
                    [ref] $code)) {
                throw "m3_fixture_dxf_group_code_invalid: $codeLine"
            }

            if ($awaitingSectionName) {
                if ($code -ne 2 -or [string]::IsNullOrWhiteSpace($valueLine)) {
                    throw 'm3_fixture_dxf_section_name_invalid'
                }
                $section = $valueLine
                $awaitingSectionName = $false
                continue
            }

            if ($code -eq 0) {
                if ($valueLine -ceq 'SECTION') {
                    Add-CompletedEntity $entities $current
                    $current = $null
                    $nestedSequence = $false
                    $awaitingSectionName = $true
                    continue
                }
                if ($valueLine -ceq 'ENDSEC') {
                    Add-CompletedEntity $entities $current
                    $current = $null
                    $nestedSequence = $false
                    $section = ''
                    continue
                }
                if ($valueLine -ceq 'EOF') {
                    Add-CompletedEntity $entities $current
                    $current = $null
                    $nestedSequence = $false
                    $sawEof = $true
                    continue
                }
                if ($section -cne 'ENTITIES') {
                    continue
                }

                if ($null -ne $current -and $current.dxfType -ceq 'POLYLINE') {
                    if ($valueLine -ceq 'VERTEX') {
                        $nestedSequence = $true
                        continue
                    }
                    if ($valueLine -ceq 'SEQEND') {
                        Add-CompletedEntity $entities $current
                        $current = $null
                        $nestedSequence = $false
                        continue
                    }
                }
                if ($null -ne $current -and $current.dxfType -ceq 'INSERT') {
                    if ($valueLine -ceq 'ATTRIB') {
                        $nestedSequence = $true
                        continue
                    }
                    if ($valueLine -ceq 'SEQEND') {
                        Add-CompletedEntity $entities $current
                        $current = $null
                        $nestedSequence = $false
                        continue
                    }
                }

                Add-CompletedEntity $entities $current
                $current = [pscustomobject]@{
                    dxfType = $valueLine
                    layer = ''
                    polylineFlags = 0
                }
                $nestedSequence = $false
                continue
            }

            if ($section -ceq 'ENTITIES' -and $null -ne $current -and -not $nestedSequence) {
                if ($code -eq 8) {
                    $current.layer = $valueLine
                }
                elseif ($code -eq 70 -and $current.dxfType -ceq 'POLYLINE') {
                    $flags = 0
                    if (-not [int]::TryParse(
                            $valueLine.Trim(),
                            [Globalization.NumberStyles]::Integer,
                            $invariantCulture,
                            [ref] $flags)) {
                        throw "m3_fixture_polyline_flag_invalid: $valueLine"
                    }
                    $current.polylineFlags = $flags
                }
            }
        }
    }
    finally {
        $reader.Dispose()
    }

    Assert-True (-not $awaitingSectionName) 'm3_fixture_dxf_unfinished_section'
    Assert-True $sawEof 'm3_fixture_dxf_eof_missing'
    Assert-True ($pairCount -gt 0) 'm3_fixture_dxf_empty'
    return $entities.ToArray()
}

function Assert-EntityRecords(
    [object[]] $Expected,
    [object[]] $Actual,
    [string] $Label,
    [bool] $IncludeManifestFields
) {
    Assert-Equal $Expected.Count $Actual.Count "$Label entity count differs."
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        $expected = $Expected[$index]
        $actual = $Actual[$index]
        foreach ($property in @('dxfType', 'layer', 'polylineFlags')) {
            Assert-Equal $expected.$property $actual.$property "$Label entity $index $property differs."
        }
        if ($IncludeManifestFields) {
            foreach ($property in @('hostType', 'variant')) {
                Assert-Equal $expected.$property $actual.$property "$Label entity $index $property differs."
            }
        }
    }
}

function Assert-ManifestMatchesExpected([pscustomobject] $Expected, [pscustomobject] $Actual) {
    foreach ($property in @('schema', 'fixtureId', 'generatorVersion', 'dxfVersion')) {
        Assert-Equal $Expected.$property $Actual.$property "M3 fixture manifest $property differs."
    }
    Assert-Equal $Expected.expectedEntityRecordCount $Actual.entityRecordCount 'M3 fixture entity record count differs.'
    Assert-Equal $Expected.expectedDxfBytes $Actual.dxfFile.bytes 'M3 fixture byte count differs.'
    Assert-Equal $Expected.expectedDxfSha256 $Actual.dxfFile.sha256 'M3 fixture hash differs.'
    Assert-True ([bool] $Actual.sanitized) 'M3 fixture manifest did not retain sanitized=true.'
    Assert-True ([bool] $Actual.deterministic) 'M3 fixture manifest did not retain deterministic=true.'
    Assert-True (-not [bool] $Actual.startsOrControlsAutoCad) 'M3 fixture manifest unexpectedly controls AutoCAD.'
    Assert-EntityRecords @($Expected.entities) @($Actual.entityRecords) 'M3 fixture manifest' $true
}

Assert-True (Test-Path -LiteralPath $generatorPath -PathType Leaf) 'm3_fixture_generator_missing'
Assert-True (Test-Path -LiteralPath $expectedManifestPath -PathType Leaf) 'm3_fixture_expected_manifest_missing'
$generatorSource = Get-Content -LiteralPath $generatorPath -Raw -Encoding UTF8
foreach ($forbiddenPattern in @(
        '(?i)\bStart-Process\b',
        '(?i)\bInvoke-Expression\b',
        '(?i)\bacad\.exe\b',
        '(?i)\baccoreconsole(?:\.exe)?\b',
        '(?i)\bSendStringToExecute\b',
        '(?i)\bDwgOut\b',
        '(?i)\bSaveAs\b',
        '(?i)\bLISP\b')) {
    Assert-True (-not [regex]::IsMatch($generatorSource, $forbiddenPattern)) `
        "m3_fixture_generator_forbidden_automation: $forbiddenPattern"
}
$passed++

$expectedManifest = Get-Content -LiteralPath $expectedManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('codex-autocad-m3-core-fixture-' + [Guid]::NewGuid().ToString('N'))
$resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
$systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
Assert-True ($resolvedTempRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase)) `
    'm3_fixture_temp_path_outside_system_temp'

try {
    $firstRun = Join-Path $resolvedTempRoot 'first'
    $secondRun = Join-Path $resolvedTempRoot 'second'
    & $generatorPath -OutputDirectory $firstRun
    & $generatorPath -OutputDirectory $secondRun
    $firstManifestPath = Join-Path $firstRun 'm3-core-read-fixture-v1.manifest.json'
    $secondManifestPath = Join-Path $secondRun 'm3-core-read-fixture-v1.manifest.json'
    $firstManifest = Get-Content -LiteralPath $firstManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $secondManifest = Get-Content -LiteralPath $secondManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-ManifestMatchesExpected $expectedManifest $firstManifest
    Assert-ManifestMatchesExpected $expectedManifest $secondManifest
    $passed++

    Assert-Equal $firstManifest.dxfFile.sha256 $secondManifest.dxfFile.sha256 'M3 fixture is not deterministic.'
    Assert-Equal $firstManifest.dxfFile.bytes $secondManifest.dxfFile.bytes 'M3 fixture byte count is not deterministic.'
    $passed++

    $dxfPath = Join-Path $firstRun $firstManifest.dxfFile.fileName
    Assert-AsciiFile $dxfPath
    $parsedEntities = Read-DxfTopLevelEntities $dxfPath
    Assert-EntityRecords @($expectedManifest.entities) $parsedEntities 'M3 fixture DXF' $false
    $passed++

    $overwriteRejected = $false
    try {
        & $generatorPath -OutputDirectory $firstRun
    }
    catch {
        $overwriteRejected = $_.Exception.Message -like 'm3_fixture_output_directory_exists:*'
    }
    Assert-True $overwriteRejected 'm3_fixture_existing_output_was_not_rejected'
    $passed++

    $manifestNames = @((Get-ChildItem -LiteralPath $firstRun -File).Name | Sort-Object)
    Assert-Equal 'm3-core-read-fixture-v1.dxf,m3-core-read-fixture-v1.manifest.json' `
        ($manifestNames -join ',') `
        'M3 fixture output file set differs.'
    $passed++
}
finally {
    if (Test-Path -LiteralPath $resolvedTempRoot -PathType Container) {
        Assert-True ($resolvedTempRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase)) `
            'm3_fixture_cleanup_path_outside_system_temp'
        [IO.Directory]::Delete($resolvedTempRoot, $true)
    }
}

Write-Host "AutoCAD 2016 M3 core read fixture checks passed: $passed/6"

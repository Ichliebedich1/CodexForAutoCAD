[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
$fixtureCounts = @(1000, 10000, 50000)
$fixtureLayers = @(
    'BENCH_L00',
    'BENCH_L01',
    'BENCH_L02',
    'BENCH_L03',
    'BENCH_L04',
    'BENCH_L05',
    'BENCH_L06',
    'BENCH_L07'
)
$fixtureTypes = @('LINE', 'CIRCLE', 'ARC', 'TEXT', 'INSERT')

function ConvertTo-DxfValue([object] $Value) {
    if ($null -eq $Value) {
        return ''
    }
    if ($Value -is [double] -or $Value -is [single] -or $Value -is [decimal]) {
        return ([IFormattable] $Value).ToString('0.###############', $invariantCulture)
    }
    if ($Value -is [IFormattable]) {
        return ([IFormattable] $Value).ToString($null, $invariantCulture)
    }
    return [string] $Value
}

function Write-DxfPair(
    [IO.TextWriter] $Writer,
    [int] $Code,
    [object] $Value
) {
    $Writer.WriteLine($Code.ToString($invariantCulture))
    $Writer.WriteLine((ConvertTo-DxfValue $Value))
}

function Write-DxfHeader([IO.TextWriter] $Writer, [int] $EntityCount) {
    $rowCount = [Math]::Ceiling($EntityCount / 250.0)
    Write-DxfPair $Writer 999 'CodexForAutoCAD sanitized DrawingIndex benchmark fixture v1'
    Write-DxfPair $Writer 0 'SECTION'
    Write-DxfPair $Writer 2 'HEADER'
    Write-DxfPair $Writer 9 '$ACADVER'
    Write-DxfPair $Writer 1 'AC1009'
    Write-DxfPair $Writer 9 '$INSUNITS'
    Write-DxfPair $Writer 70 4
    Write-DxfPair $Writer 9 '$LUNITS'
    Write-DxfPair $Writer 70 2
    Write-DxfPair $Writer 9 '$LUPREC'
    Write-DxfPair $Writer 70 4
    Write-DxfPair $Writer 9 '$EXTMIN'
    Write-DxfPair $Writer 10 0.0
    Write-DxfPair $Writer 20 0.0
    Write-DxfPair $Writer 30 0.0
    Write-DxfPair $Writer 9 '$EXTMAX'
    Write-DxfPair $Writer 10 3000.0
    Write-DxfPair $Writer 20 ([double] ($rowCount * 12 + 12))
    Write-DxfPair $Writer 30 0.0
    Write-DxfPair $Writer 0 'ENDSEC'
}

function Write-DxfTables([IO.TextWriter] $Writer) {
    Write-DxfPair $Writer 0 'SECTION'
    Write-DxfPair $Writer 2 'TABLES'

    Write-DxfPair $Writer 0 'TABLE'
    Write-DxfPair $Writer 2 'LTYPE'
    Write-DxfPair $Writer 70 1
    Write-DxfPair $Writer 0 'LTYPE'
    Write-DxfPair $Writer 2 'CONTINUOUS'
    Write-DxfPair $Writer 70 0
    Write-DxfPair $Writer 3 'Solid line'
    Write-DxfPair $Writer 72 65
    Write-DxfPair $Writer 73 0
    Write-DxfPair $Writer 40 0.0
    Write-DxfPair $Writer 0 'ENDTAB'

    Write-DxfPair $Writer 0 'TABLE'
    Write-DxfPair $Writer 2 'LAYER'
    Write-DxfPair $Writer 70 ($fixtureLayers.Count + 1)
    Write-DxfPair $Writer 0 'LAYER'
    Write-DxfPair $Writer 2 '0'
    Write-DxfPair $Writer 70 0
    Write-DxfPair $Writer 62 7
    Write-DxfPair $Writer 6 'CONTINUOUS'
    for ($index = 0; $index -lt $fixtureLayers.Count; $index++) {
        Write-DxfPair $Writer 0 'LAYER'
        Write-DxfPair $Writer 2 $fixtureLayers[$index]
        Write-DxfPair $Writer 70 0
        Write-DxfPair $Writer 62 (($index % 7) + 1)
        Write-DxfPair $Writer 6 'CONTINUOUS'
    }
    Write-DxfPair $Writer 0 'ENDTAB'

    Write-DxfPair $Writer 0 'TABLE'
    Write-DxfPair $Writer 2 'STYLE'
    Write-DxfPair $Writer 70 1
    Write-DxfPair $Writer 0 'STYLE'
    Write-DxfPair $Writer 2 'STANDARD'
    Write-DxfPair $Writer 70 0
    Write-DxfPair $Writer 40 0.0
    Write-DxfPair $Writer 41 1.0
    Write-DxfPair $Writer 50 0.0
    Write-DxfPair $Writer 71 0
    Write-DxfPair $Writer 42 2.5
    Write-DxfPair $Writer 3 'txt'
    Write-DxfPair $Writer 4 ''
    Write-DxfPair $Writer 0 'ENDTAB'

    Write-DxfPair $Writer 0 'ENDSEC'
}

function Write-DxfBlocks([IO.TextWriter] $Writer) {
    Write-DxfPair $Writer 0 'SECTION'
    Write-DxfPair $Writer 2 'BLOCKS'
    Write-DxfPair $Writer 0 'BLOCK'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 2 'BENCH_MARKER'
    Write-DxfPair $Writer 70 0
    Write-DxfPair $Writer 10 0.0
    Write-DxfPair $Writer 20 0.0
    Write-DxfPair $Writer 30 0.0
    Write-DxfPair $Writer 3 'BENCH_MARKER'
    Write-DxfPair $Writer 1 ''
    Write-DxfPair $Writer 0 'LINE'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 10 -2.0
    Write-DxfPair $Writer 20 0.0
    Write-DxfPair $Writer 30 0.0
    Write-DxfPair $Writer 11 2.0
    Write-DxfPair $Writer 21 0.0
    Write-DxfPair $Writer 31 0.0
    Write-DxfPair $Writer 0 'LINE'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 10 0.0
    Write-DxfPair $Writer 20 -2.0
    Write-DxfPair $Writer 30 0.0
    Write-DxfPair $Writer 11 0.0
    Write-DxfPair $Writer 21 2.0
    Write-DxfPair $Writer 31 0.0
    Write-DxfPair $Writer 0 'ENDBLK'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 0 'ENDSEC'
}

function Write-DxfEntity(
    [IO.TextWriter] $Writer,
    [int] $Index,
    [string] $EntityType,
    [string] $Layer,
    [bool] $PaperSpace
) {
    $column = $Index % 250
    $row = [Math]::Floor($Index / 250.0)
    $x = [double] ($column * 12)
    $y = [double] ($row * 12)

    Write-DxfPair $Writer 0 $EntityType
    Write-DxfPair $Writer 8 $Layer
    if ($PaperSpace) {
        Write-DxfPair $Writer 67 1
    }

    switch ($EntityType) {
        'LINE' {
            Write-DxfPair $Writer 10 $x
            Write-DxfPair $Writer 20 $y
            Write-DxfPair $Writer 30 0.0
            Write-DxfPair $Writer 11 ($x + 8.0)
            Write-DxfPair $Writer 21 ($y + 3.0)
            Write-DxfPair $Writer 31 0.0
            break
        }
        'CIRCLE' {
            Write-DxfPair $Writer 10 ($x + 4.0)
            Write-DxfPair $Writer 20 ($y + 4.0)
            Write-DxfPair $Writer 30 0.0
            Write-DxfPair $Writer 40 3.0
            break
        }
        'ARC' {
            Write-DxfPair $Writer 10 ($x + 4.0)
            Write-DxfPair $Writer 20 ($y + 4.0)
            Write-DxfPair $Writer 30 0.0
            Write-DxfPair $Writer 40 3.0
            Write-DxfPair $Writer 50 15.0
            Write-DxfPair $Writer 51 225.0
            break
        }
        'TEXT' {
            Write-DxfPair $Writer 10 $x
            Write-DxfPair $Writer 20 $y
            Write-DxfPair $Writer 30 0.0
            Write-DxfPair $Writer 40 2.5
            Write-DxfPair $Writer 1 ('BENCH-{0:D6}' -f ($Index + 1))
            Write-DxfPair $Writer 7 'STANDARD'
            Write-DxfPair $Writer 50 0.0
            break
        }
        'INSERT' {
            Write-DxfPair $Writer 2 'BENCH_MARKER'
            Write-DxfPair $Writer 10 ($x + 4.0)
            Write-DxfPair $Writer 20 ($y + 4.0)
            Write-DxfPair $Writer 30 0.0
            Write-DxfPair $Writer 41 1.0
            Write-DxfPair $Writer 42 1.0
            Write-DxfPair $Writer 43 1.0
            Write-DxfPair $Writer 50 (($Index * 17) % 360)
            break
        }
        default {
            throw "benchmark_entity_type_unsupported: $EntityType"
        }
    }
}

function New-CountMap([string[]] $Keys) {
    $result = [ordered]@{}
    foreach ($key in $Keys) {
        $result[$key] = 0
    }
    return $result
}

function Convert-CountMapToManifest([Collections.IDictionary] $Map) {
    return @(
        foreach ($key in $Map.Keys) {
            [ordered]@{
                key = [string] $key
                count = [int] $Map[$key]
            }
        }
    )
}

function New-BenchmarkDxf([string] $Path, [int] $EntityCount) {
    $typeCounts = New-CountMap $fixtureTypes
    $layerCounts = New-CountMap $fixtureLayers
    $modelSpaceCount = 0
    $paperSpaceCount = 0
    $encoding = New-Object Text.ASCIIEncoding
    $stream = New-Object IO.FileStream(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None,
        65536,
        [IO.FileOptions]::SequentialScan)
    $writer = New-Object IO.StreamWriter($stream, $encoding, 65536)
    $writer.NewLine = "`r`n"
    try {
        Write-DxfHeader $writer $EntityCount
        Write-DxfTables $writer
        Write-DxfBlocks $writer
        Write-DxfPair $writer 0 'SECTION'
        Write-DxfPair $writer 2 'ENTITIES'
        for ($index = 0; $index -lt $EntityCount; $index++) {
            $positionInGroup = $index % 10
            $groupIndex = [Math]::Floor($index / 10.0)
            $typeIndex = [int] (($groupIndex + $positionInGroup) % $fixtureTypes.Count)
            $layerIndex = [int] (($index + $groupIndex) % $fixtureLayers.Count)
            $entityType = $fixtureTypes[$typeIndex]
            $layer = $fixtureLayers[$layerIndex]
            # Performance fixtures stay entirely in model space so AutoCAD-created paper-space
            # viewports cannot change the named 1k/10k/50k population after DXF import.
            $paperSpace = $false
            Write-DxfEntity $writer $index $entityType $layer $paperSpace
            $typeCounts[$entityType]++
            $layerCounts[$layer]++
            if ($paperSpace) {
                $paperSpaceCount++
            }
            else {
                $modelSpaceCount++
            }
        }
        Write-DxfPair $writer 0 'ENDSEC'
        Write-DxfPair $writer 0 'EOF'
    }
    finally {
        $writer.Dispose()
    }

    $item = Get-Item -LiteralPath $Path
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    return [ordered]@{
        fileName = $item.Name
        entityCount = $EntityCount
        modelSpaceCount = $modelSpaceCount
        paperSpaceCount = $paperSpaceCount
        typeCounts = Convert-CountMapToManifest $typeCounts
        layerCounts = Convert-CountMapToManifest $layerCounts
        bytes = [long] $item.Length
        sha256 = $hash
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($resolvedOutput)) {
    throw 'benchmark_output_path_invalid'
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "benchmark_output_directory_exists: $resolvedOutput"
}

$parent = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($parent)) {
    throw 'benchmark_output_parent_invalid'
}
[IO.Directory]::CreateDirectory($parent) | Out-Null
$staging = $resolvedOutput + '.staging-' + [Guid]::NewGuid().ToString('N')
[IO.Directory]::CreateDirectory($staging) | Out-Null

try {
    $files = @(
        foreach ($count in $fixtureCounts) {
            $fileName = 'drawing-index-{0:D6}.dxf' -f $count
            New-BenchmarkDxf (Join-Path $staging $fileName) $count
        }
    )
    $manifest = [ordered]@{
        schema = 'codex.autocad.drawing-index-benchmark/1'
        generatorVersion = '1.0.0'
        dxfVersion = 'AC1009'
        units = 'millimeters'
        sanitized = $true
        deterministic = $true
        files = $files
    }
    $manifestPath = Join-Path $staging 'drawing-index-benchmarks.manifest.json'
    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText(
        $manifestPath,
        $manifestJson + "`r`n",
        (New-Object Text.UTF8Encoding($false)))
    [IO.Directory]::Move($staging, $resolvedOutput)
}
catch {
    if (Test-Path -LiteralPath $staging -PathType Container) {
        [IO.Directory]::Delete($staging, $true)
    }
    throw
}

Write-Host "DrawingIndex benchmark fixtures generated: $resolvedOutput"
foreach ($file in $files) {
    Write-Host ("{0}: entities={1}, bytes={2}, sha256={3}" -f
        $file.fileName,
        $file.entityCount,
        $file.bytes,
        $file.sha256)
}

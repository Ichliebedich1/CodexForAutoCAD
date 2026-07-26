[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
$fixtureId = 'autocad2016-m3-core-read-fixture-v1'
$dxfFileName = 'm3-core-read-fixture-v1.dxf'
$manifestFileName = 'm3-core-read-fixture-v1.manifest.json'
$coreLayer = 'M3_CORE'
$legacyLayer = 'M3_LEGACY'
$blockLayer = 'M3_BLOCKS'
$script:entityRecords = New-Object 'System.Collections.Generic.List[object]'

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

function Write-DxfPoint(
    [IO.TextWriter] $Writer,
    [int] $BaseCode,
    [double] $X,
    [double] $Y,
    [double] $Z
) {
    Write-DxfPair $Writer $BaseCode $X
    Write-DxfPair $Writer ($BaseCode + 10) $Y
    Write-DxfPair $Writer ($BaseCode + 20) $Z
}

function Write-EntityHeader(
    [IO.TextWriter] $Writer,
    [string] $DxfType,
    [string] $Layer,
    [string] $Subclass
) {
    Write-DxfPair $Writer 0 $DxfType
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 $Layer
    Write-DxfPair $Writer 100 $Subclass
}

function Add-EntityRecord(
    [string] $DxfType,
    [string] $Layer,
    [string] $HostType,
    [string] $Variant,
    [int] $PolylineFlags
) {
    [void] $script:entityRecords.Add([pscustomobject] ([ordered]@{
                dxfType = $DxfType
                layer = $Layer
                hostType = $HostType
                variant = $Variant
                polylineFlags = $PolylineFlags
            }))
}

function Write-DxfHeader([IO.TextWriter] $Writer) {
    Write-DxfPair $Writer 999 'CodexForAutoCAD sanitized M3 core read fixture v1'
    Write-DxfPair $Writer 0 'SECTION'
    Write-DxfPair $Writer 2 'HEADER'
    Write-DxfPair $Writer 9 '$ACADVER'
    Write-DxfPair $Writer 1 'AC1015'
    Write-DxfPair $Writer 9 '$DWGCODEPAGE'
    Write-DxfPair $Writer 3 'ANSI_1252'
    Write-DxfPair $Writer 9 '$INSUNITS'
    Write-DxfPair $Writer 70 4
    Write-DxfPair $Writer 9 '$LUNITS'
    Write-DxfPair $Writer 70 2
    Write-DxfPair $Writer 9 '$LUPREC'
    Write-DxfPair $Writer 70 4
    Write-DxfPair $Writer 9 '$EXTMIN'
    Write-DxfPoint $Writer 10 0.0 0.0 0.0
    Write-DxfPair $Writer 9 '$EXTMAX'
    Write-DxfPoint $Writer 10 260.0 160.0 0.0
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

    $layers = @('0', $coreLayer, $legacyLayer, $blockLayer)
    Write-DxfPair $Writer 0 'TABLE'
    Write-DxfPair $Writer 2 'LAYER'
    Write-DxfPair $Writer 70 $layers.Count
    for ($index = 0; $index -lt $layers.Count; $index++) {
        Write-DxfPair $Writer 0 'LAYER'
        Write-DxfPair $Writer 2 $layers[$index]
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
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 100 'AcDbBlockBegin'
    Write-DxfPair $Writer 2 'M3_NESTED'
    Write-DxfPair $Writer 70 0
    Write-DxfPoint $Writer 10 0.0 0.0 0.0
    Write-DxfPair $Writer 3 'M3_NESTED'
    Write-DxfPair $Writer 1 ''
    Write-EntityHeader $Writer 'CIRCLE' $blockLayer 'AcDbCircle'
    Write-DxfPoint $Writer 10 0.0 0.0 0.0
    Write-DxfPair $Writer 40 1.5
    Write-DxfPair $Writer 0 'ENDBLK'
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 100 'AcDbBlockEnd'

    Write-DxfPair $Writer 0 'BLOCK'
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 100 'AcDbBlockBegin'
    Write-DxfPair $Writer 2 'M3_ATTR_BLOCK'
    Write-DxfPair $Writer 70 2
    Write-DxfPoint $Writer 10 0.0 0.0 0.0
    Write-DxfPair $Writer 3 'M3_ATTR_BLOCK'
    Write-DxfPair $Writer 1 ''
    Write-EntityHeader $Writer 'ATTDEF' $blockLayer 'AcDbText'
    Write-DxfPoint $Writer 10 0.0 4.0 0.0
    Write-DxfPair $Writer 40 2.5
    Write-DxfPair $Writer 1 'M3_DEFAULT'
    Write-DxfPair $Writer 7 'STANDARD'
    Write-DxfPair $Writer 50 0.0
    Write-DxfPair $Writer 100 'AcDbAttributeDefinition'
    Write-DxfPair $Writer 3 'M3 fixture attribute'
    Write-DxfPair $Writer 2 'M3_TAG'
    Write-DxfPair $Writer 70 0
    Write-EntityHeader $Writer 'LINE' $blockLayer 'AcDbLine'
    Write-DxfPoint $Writer 10 -3.0 -2.0 0.0
    Write-DxfPoint $Writer 11 3.0 -2.0 0.0
    Write-EntityHeader $Writer 'INSERT' $blockLayer 'AcDbBlockReference'
    Write-DxfPair $Writer 2 'M3_NESTED'
    Write-DxfPoint $Writer 10 0.0 0.0 0.0
    Write-DxfPair $Writer 41 1.0
    Write-DxfPair $Writer 42 1.0
    Write-DxfPair $Writer 43 1.0
    Write-DxfPair $Writer 50 0.0
    Write-DxfPair $Writer 0 'ENDBLK'
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 '0'
    Write-DxfPair $Writer 100 'AcDbBlockEnd'

    Write-DxfPair $Writer 0 'ENDSEC'
}

function Write-CoreEntities([IO.TextWriter] $Writer) {
    Write-DxfPair $Writer 0 'SECTION'
    Write-DxfPair $Writer 2 'ENTITIES'

    Write-EntityHeader $Writer 'LINE' $coreLayer 'AcDbLine'
    Write-DxfPoint $Writer 10 10.0 10.0 0.0
    Write-DxfPoint $Writer 11 40.0 25.0 0.0
    Add-EntityRecord 'LINE' $coreLayer 'Line' 'line' 0

    Write-EntityHeader $Writer 'CIRCLE' $coreLayer 'AcDbCircle'
    Write-DxfPoint $Writer 10 65.0 20.0 0.0
    Write-DxfPair $Writer 40 10.0
    Add-EntityRecord 'CIRCLE' $coreLayer 'Circle' 'circle' 0

    Write-EntityHeader $Writer 'ARC' $coreLayer 'AcDbArc'
    Write-DxfPoint $Writer 10 105.0 20.0 0.0
    Write-DxfPair $Writer 40 10.0
    Write-DxfPair $Writer 50 15.0
    Write-DxfPair $Writer 51 225.0
    Add-EntityRecord 'ARC' $coreLayer 'Arc' 'arc' 0

    Write-EntityHeader $Writer 'ELLIPSE' $coreLayer 'AcDbEllipse'
    Write-DxfPoint $Writer 10 150.0 20.0 0.0
    Write-DxfPoint $Writer 11 20.0 0.0 0.0
    Write-DxfPair $Writer 40 0.5
    Write-DxfPair $Writer 41 0.0
    Write-DxfPair $Writer 42 6.283185307179586
    Add-EntityRecord 'ELLIPSE' $coreLayer 'Ellipse' 'ellipse' 0

    Write-EntityHeader $Writer 'SPLINE' $coreLayer 'AcDbSpline'
    Write-DxfPair $Writer 70 8
    Write-DxfPair $Writer 71 3
    Write-DxfPair $Writer 72 8
    Write-DxfPair $Writer 73 4
    Write-DxfPair $Writer 74 0
    Write-DxfPair $Writer 42 0.0000001
    Write-DxfPair $Writer 43 0.0000001
    Write-DxfPair $Writer 44 0.0000001
    foreach ($knot in @(0.0, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 1.0)) {
        Write-DxfPair $Writer 40 $knot
    }
    Write-DxfPoint $Writer 10 10.0 55.0 0.0
    Write-DxfPoint $Writer 10 30.0 70.0 0.0
    Write-DxfPoint $Writer 10 55.0 40.0 0.0
    Write-DxfPoint $Writer 10 80.0 55.0 0.0
    Add-EntityRecord 'SPLINE' $coreLayer 'Spline' 'spline' 0

    Write-EntityHeader $Writer 'POINT' $coreLayer 'AcDbPoint'
    Write-DxfPoint $Writer 10 105.0 55.0 0.0
    Add-EntityRecord 'POINT' $coreLayer 'DBPoint' 'point' 0

    Write-EntityHeader $Writer 'RAY' $coreLayer 'AcDbRay'
    Write-DxfPoint $Writer 10 130.0 55.0 0.0
    Write-DxfPoint $Writer 11 1.0 0.25 0.0
    Add-EntityRecord 'RAY' $coreLayer 'Ray' 'ray' 0

    Write-EntityHeader $Writer 'XLINE' $coreLayer 'AcDbXline'
    Write-DxfPoint $Writer 10 165.0 55.0 0.0
    Write-DxfPoint $Writer 11 1.0 -0.5 0.0
    Add-EntityRecord 'XLINE' $coreLayer 'Xline' 'xline' 0

    Write-EntityHeader $Writer 'LWPOLYLINE' $coreLayer 'AcDbPolyline'
    Write-DxfPair $Writer 90 4
    Write-DxfPair $Writer 70 1
    Write-DxfPair $Writer 38 0.0
    Write-DxfPair $Writer 43 0.0
    Write-DxfPair $Writer 10 195.0
    Write-DxfPair $Writer 20 10.0
    Write-DxfPair $Writer 10 220.0
    Write-DxfPair $Writer 20 10.0
    Write-DxfPair $Writer 42 0.5
    Write-DxfPair $Writer 10 220.0
    Write-DxfPair $Writer 20 35.0
    Write-DxfPair $Writer 10 195.0
    Write-DxfPair $Writer 20 35.0
    Write-DxfPair $Writer 210 0.0
    Write-DxfPair $Writer 220 0.0
    Write-DxfPair $Writer 230 1.0
    Add-EntityRecord 'LWPOLYLINE' $coreLayer 'Polyline' 'lightweight-polyline' 0

    Write-EntityHeader $Writer 'TEXT' $coreLayer 'AcDbText'
    Write-DxfPoint $Writer 10 10.0 100.0 0.0
    Write-DxfPair $Writer 40 3.0
    Write-DxfPair $Writer 1 'M3_DBText'
    Write-DxfPair $Writer 7 'STANDARD'
    Write-DxfPair $Writer 50 0.0
    Add-EntityRecord 'TEXT' $coreLayer 'DBText' 'dbtext' 0

    Write-EntityHeader $Writer 'MTEXT' $coreLayer 'AcDbMText'
    Write-DxfPoint $Writer 10 60.0 100.0 0.0
    Write-DxfPair $Writer 40 3.0
    Write-DxfPair $Writer 41 70.0
    Write-DxfPair $Writer 1 'M3_MText core fixture'
    Write-DxfPair $Writer 7 'STANDARD'
    Write-DxfPair $Writer 71 1
    Write-DxfPair $Writer 72 5
    Write-DxfPair $Writer 50 0.0
    Write-DxfPair $Writer 210 0.0
    Write-DxfPair $Writer 220 0.0
    Write-DxfPair $Writer 230 1.0
    Add-EntityRecord 'MTEXT' $coreLayer 'MText' 'mtext' 0

    Write-EntityHeader $Writer 'INSERT' $coreLayer 'AcDbBlockReference'
    Write-DxfPair $Writer 2 'M3_ATTR_BLOCK'
    Write-DxfPoint $Writer 10 155.0 105.0 0.0
    Write-DxfPair $Writer 41 1.5
    Write-DxfPair $Writer 42 1.5
    Write-DxfPair $Writer 43 1.0
    Write-DxfPair $Writer 50 15.0
    Write-DxfPair $Writer 66 1
    Write-EntityHeader $Writer 'ATTRIB' $coreLayer 'AcDbText'
    Write-DxfPoint $Writer 10 155.0 111.0 0.0
    Write-DxfPair $Writer 40 2.5
    Write-DxfPair $Writer 1 'M3_VALUE'
    Write-DxfPair $Writer 7 'STANDARD'
    Write-DxfPair $Writer 50 15.0
    Write-DxfPair $Writer 100 'AcDbAttribute'
    Write-DxfPair $Writer 2 'M3_TAG'
    Write-DxfPair $Writer 70 0
    Write-DxfPair $Writer 0 'SEQEND'
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 $coreLayer
    Add-EntityRecord 'INSERT' $coreLayer 'BlockReference' 'attributed-nested-block' 0

    Write-EntityHeader $Writer 'POLYLINE' $legacyLayer 'AcDb2dPolyline'
    Write-DxfPair $Writer 66 1
    Write-DxfPoint $Writer 10 10.0 130.0 2.0
    Write-DxfPair $Writer 70 1
    foreach ($vertex in @(
            @(10.0, 130.0, 2.0, 0.0),
            @(35.0, 130.0, 2.0, 0.25),
            @(35.0, 150.0, 2.0, 0.0))) {
        Write-EntityHeader $Writer 'VERTEX' $legacyLayer 'AcDbVertex'
        Write-DxfPair $Writer 100 'AcDb2dVertex'
        Write-DxfPoint $Writer 10 ([double] $vertex[0]) ([double] $vertex[1]) ([double] $vertex[2])
        Write-DxfPair $Writer 42 ([double] $vertex[3])
        Write-DxfPair $Writer 70 0
    }
    Write-DxfPair $Writer 0 'SEQEND'
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 $legacyLayer
    Add-EntityRecord 'POLYLINE' $legacyLayer 'Polyline2d' 'legacy-2d-polyline' 1

    Write-EntityHeader $Writer 'POLYLINE' $legacyLayer 'AcDb3dPolyline'
    Write-DxfPair $Writer 66 1
    Write-DxfPoint $Writer 10 80.0 130.0 0.0
    Write-DxfPair $Writer 70 8
    foreach ($vertex in @(
            @(80.0, 130.0, 0.0),
            @(100.0, 140.0, 12.0),
            @(120.0, 130.0, 4.0))) {
        Write-EntityHeader $Writer 'VERTEX' $legacyLayer 'AcDbVertex'
        Write-DxfPair $Writer 100 'AcDb3dPolylineVertex'
        Write-DxfPoint $Writer 10 ([double] $vertex[0]) ([double] $vertex[1]) ([double] $vertex[2])
        Write-DxfPair $Writer 70 32
    }
    Write-DxfPair $Writer 0 'SEQEND'
    Write-DxfPair $Writer 100 'AcDbEntity'
    Write-DxfPair $Writer 8 $legacyLayer
    Add-EntityRecord 'POLYLINE' $legacyLayer 'Polyline3d' 'legacy-3d-polyline' 8

    Write-DxfPair $Writer 0 'ENDSEC'
    Write-DxfPair $Writer 0 'EOF'
}

function Convert-EntityRecordsToDxfTypeCounts([object[]] $Records) {
    $counts = [ordered]@{}
    foreach ($record in $Records) {
        $key = [string] $record.dxfType
        if (-not $counts.Contains($key)) {
            $counts[$key] = 0
        }
        $counts[$key] = [int] $counts[$key] + 1
    }
    return @(
        foreach ($key in $counts.Keys) {
            [ordered]@{
                dxfType = $key
                count = [int] $counts[$key]
            }
        }
    )
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($resolvedOutput)) {
    throw 'm3_fixture_output_path_invalid'
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "m3_fixture_output_directory_exists: $resolvedOutput"
}

$parent = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($parent)) {
    throw 'm3_fixture_output_parent_invalid'
}
[IO.Directory]::CreateDirectory($parent) | Out-Null
$staging = $resolvedOutput + '.staging-' + [Guid]::NewGuid().ToString('N')
[IO.Directory]::CreateDirectory($staging) | Out-Null

try {
    $dxfPath = Join-Path $staging $dxfFileName
    $stream = New-Object IO.FileStream(
        $dxfPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None,
        65536,
        [IO.FileOptions]::SequentialScan)
    $writer = New-Object IO.StreamWriter($stream, (New-Object Text.ASCIIEncoding), 65536)
    $writer.NewLine = "`r`n"
    try {
        Write-DxfHeader $writer
        Write-DxfTables $writer
        Write-DxfBlocks $writer
        Write-CoreEntities $writer
    }
    finally {
        $writer.Dispose()
    }

    $dxfItem = Get-Item -LiteralPath $dxfPath
    $records = @($script:entityRecords.ToArray())
    $dxfTypeCounts = @(Convert-EntityRecordsToDxfTypeCounts -Records $records)
    $manifest = [ordered]@{
        schema = 'codex.autocad.m3-core-read-fixture/1'
        fixtureId = $fixtureId
        generatorVersion = '1.0.0'
        dxfVersion = 'AC1015'
        units = 'millimeters'
        sanitized = $true
        deterministic = $true
        startsOrControlsAutoCad = $false
        entityRecordCount = $script:entityRecords.Count
        entityRecords = $records
        dxfTypeCounts = $dxfTypeCounts
        dxfFile = [ordered]@{
            fileName = $dxfFileName
            bytes = [long] $dxfItem.Length
            sha256 = (Get-FileHash -LiteralPath $dxfPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        limitations = @(
            'This fixture covers 14 core and legacy entity variants only.',
            'Dimension, Hatch, Leader, MLeader, and Table require separate manual AutoCAD fixtures.',
            'Loading and field verification in AutoCAD are manual test steps and are not implied by generation.'
        )
    }
    [IO.File]::WriteAllText(
        (Join-Path $staging $manifestFileName),
        ($manifest | ConvertTo-Json -Depth 12) + "`r`n",
        (New-Object Text.UTF8Encoding($false)))

    [IO.Directory]::Move($staging, $resolvedOutput)
}
catch {
    if (Test-Path -LiteralPath $staging -PathType Container) {
        [IO.Directory]::Delete($staging, $true)
    }
    throw
}

Write-Host "M3 core read fixture generated: $resolvedOutput"
Write-Host "DXF=$dxfFileName"
Write-Host "ENTITY_RECORDS=$($script:entityRecords.Count)"

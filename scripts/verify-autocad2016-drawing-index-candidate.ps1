[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',

    [string] $AutoCad2016Dir = 'D:\AutoCAD 2016',

    [ValidateSet('M2', 'M3')]
    [string] $CandidateStage = 'M2'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$git = (Get-Command git -ErrorAction Stop).Source
$powerShell = (Get-Process -Id $PID).Path
$runId = [Guid]::NewGuid().ToString('N')
$candidateProfile = if ($CandidateStage -ceq 'M2') {
    [pscustomobject]@{
        Title = 'M2 DrawingIndex'
        ArtifactPrefix = 'autocad2016-m2-drawing-index'
        CandidatePrefix = 'autocad2016-m2-drawing-index-v040'
        ExpectedHostVersion = '0.4.0.0'
        EvidenceScope = 'autocad2016-m2-drawing-index-candidate-build'
        EvidencePrefix = 'm2-drawing-index-candidate-'
        UsesM3ApiProbe = $false
        UsesM3CoreFixture = $false
    }
}
else {
    [pscustomobject]@{
        Title = 'M3 CAD 读取语义'
        ArtifactPrefix = 'autocad2016-m3-read-semantics'
        CandidatePrefix = 'autocad2016-m3-read-semantics-v042'
        ExpectedHostVersion = '0.4.2.0'
        EvidenceScope = 'autocad2016-m3-read-semantics-candidate-build'
        EvidencePrefix = 'm3-read-semantics-candidate-'
        UsesM3ApiProbe = $true
        UsesM3CoreFixture = $true
    }
}
$stageRoot = Join-Path $repoRoot ('artifacts\' + $candidateProfile.ArtifactPrefix + '-' + $runId)
$packageRoot = Join-Path $stageRoot 'packages'
$publishRoot = Join-Path $stageRoot 'agenthost-publish'
$net45ReferencePath = Join-Path $packageRoot 'microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5'
$phase2Script = Join-Path $repoRoot 'scripts\verify-phase2.ps1'
$benchmarkScript = Join-Path $repoRoot 'scripts\verify-autocad2016-drawing-index-benchmarks.ps1'
$m3ApiProbeStageScript = Join-Path $repoRoot 'scripts\verify-autocad2016-v2-api-surface-stage.ps1'
$m3CoreFixtureScript = Join-Path $repoRoot 'scripts\verify-autocad2016-m3-core-read-fixture.ps1'
$benchmarkManifestPath = Join-Path $repoRoot 'handoff\autocad2016\benchmark-fixtures\DRAWING_INDEX_BENCHMARKS_V1.expected.json'
$m3CoreFixtureManifestPath = Join-Path $repoRoot 'handoff\autocad2016\m3-fixtures\M3_CORE_READ_FIXTURE_V1.expected.json'
$hostProject = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj'
$nugetConfig = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\NuGet.Config'
$assemblyInfoPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Properties\AssemblyInfo.cs'
$evidenceDirectory = Join-Path $repoRoot 'handoff\autocad2016\evidence'

$expectedLockHashes = [ordered]@{
    'src\Codex.AutoCAD.Bridge.Client\packages.lock.json' = '0714AABB5B4165653D0B05FDC92C1B45AA727152'
    'src\Codex.AutoCAD.Host.2016\packages.lock.json' = '2C611BA38245398D856FA511F36FE1425DE8F66B'
}

function Get-Sha256([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少文件：$Path"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-GitBlobHash([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少文件：$Path"
    }
    $value = (& $git hash-object -- $Path 2>&1 | Select-Object -Last 1).ToString().Trim()
    if ($LASTEXITCODE -ne 0 -or $value -notmatch '^[0-9a-fA-F]{40}$') {
        throw "无法计算 Git blob：$Path"
    }
    return $value.ToUpperInvariant()
}

function Invoke-CapturedOutput(
    [string] $FilePath,
    [string[]] $Arguments,
    [string] $Description) {
    Write-Host "==> $Description" -ForegroundColor Cyan
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string] $_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "$Description 失败，退出码：$exitCode"
    }
    return $lines
}

function Invoke-Captured(
    [string] $FilePath,
    [string[]] $Arguments,
    [string] $Description) {
    [void](Invoke-CapturedOutput $FilePath $Arguments $Description)
}

function Get-FilesSnapshot([string] $Root) {
    $snapshot = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Root.Length + 1).Replace('\', '/')
        $snapshot[$relative] = [ordered]@{
            Length = $file.Length
            Sha256 = Get-Sha256 $file.FullName
        }
    }
    return $snapshot
}

function Assert-Same($Left, $Right, [string] $Label) {
    $leftJson = $Left | ConvertTo-Json -Depth 30 -Compress
    $rightJson = $Right | ConvertTo-Json -Depth 30 -Compress
    if ($leftJson -cne $rightJson) {
        throw "$Label 不一致。"
    }
}

function Test-X64Pe([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) { return $false }
            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            if ($peOffset -lt 0 -or $peOffset -gt ($stream.Length - 6)) { return $false }
            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) { return $false }
            return $reader.ReadUInt16() -eq 0x8664
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-HostCompileSources([string] $ProjectPath) {
    [xml] $project = Get-Content -LiteralPath $ProjectPath -Raw -Encoding UTF8
    $namespace = New-Object Xml.XmlNamespaceManager($project.NameTable)
    $namespace.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $projectDirectory = Split-Path -Parent $ProjectPath
    $sources = @(
        foreach ($node in @($project.SelectNodes('//m:Compile[@Include]', $namespace))) {
            $path = [IO.Path]::GetFullPath((Join-Path $projectDirectory $node.Include))
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Host.2016 Compile 项不存在：$path"
            }
            $path
        }
    ) | Sort-Object -Unique
    if ($sources.Count -eq 0) {
        throw 'Host.2016 Compile 闭包为空。'
    }
    return $sources
}

function Assert-M2ReadOnlySource([string[]] $SourceFiles) {
    $forbidden = [ordered]@{
        'CAD ForWrite' = '(?i)\bOpenMode\s*\.\s*ForWrite\b'
        'CAD mutation' = '(?i)\.\s*(?:UpgradeOpen|DowngradeOpen|AppendEntity|AddNewlyCreatedDBObject|Erase|WblockCloneObjects|DeepCloneObjects|TransformBy)\s*\('
        'CAD command/save' = '(?i)\.\s*(?:SetSystemVariable|SetImpliedSelection|SendStringToExecute|Save|SaveAs|DwgOut|DxfOut|CloseAndSave|Command|CommandAsync|ExecuteInCommandContextAsync)\s*\('
    }
    $findings = @(
        foreach ($rule in $forbidden.GetEnumerator()) {
            foreach ($match in @(Select-String -LiteralPath $SourceFiles -Pattern ([string] $rule.Value))) {
                [pscustomobject]@{
                    Rule = [string] $rule.Key
                    Path = $match.Path
                    Line = $match.LineNumber
                }
            }
        }
    )
    if ($findings.Count -ne 0) {
        throw "M2 Host.2016 只读源码扫描失败：$($findings | ConvertTo-Json -Compress)"
    }

    $lockMatches = @(Select-String -LiteralPath $SourceFiles -Pattern '(?i)\.\s*LockDocument\s*\(')
    if ($lockMatches.Count -ne 2) {
        throw "M2 必须且只能包含两处 DocumentLock，实际：$($lockMatches.Count)。"
    }
    foreach ($match in $lockMatches) {
        if ([IO.Path]::GetFileName($match.Path) -cne 'DrawingIndexRuntime.cs') {
            throw "DocumentLock 只能位于 DrawingIndexRuntime.cs：$($match.Path)"
        }
    }

    $drawingRuntime = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\DrawingIndexRuntime.cs'
    $drawingText = Get-Content -LiteralPath $drawingRuntime -Raw -Encoding UTF8
    foreach ($required in @(
        'MaximumIdsPerPreparationSlice = 4096',
        'MaximumEntitiesPerIdleSlice = 128',
        'MaximumMillisecondsPerIdleSlice = 12',
        'MaximumScanDuration = TimeSpan.FromMinutes(2)',
        'StartOpenCloseTransaction',
        'OpenMode.ForRead',
        'AutoCadApplication.Idle',
        'ObjectAppended',
        'ObjectModified',
        'ObjectErased',
        'DrawingIndexAgentSnapshot.CreateFromOwnedFrozenEntities')) {
        if ($drawingText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndexRuntime 缺少受审只读/分片要素：$required"
        }
    }
    if ($drawingText -match '\bBlockTableRecordEnumerator\b') {
        throw 'DrawingIndexRuntime 禁止跨 Idle 或 Transaction 保存 BlockTableRecordEnumerator。'
    }
    if ($drawingText -match 'CreateUniqueObjectToken|ReadObjectToken\s*\(\s*objectId\s*\)') {
        throw 'DrawingIndex 查询实体令牌禁止由 AutoCAD Handle/ObjectId 派生。'
    }
    foreach ($required in @(
        'CadQueryEntityTokens.Create(preparation.Items.Count + 1)',
        'using (var enumerator = record.GetEnumerator())',
        'CurrentObjectIds',
        'DrawingIndexSpaceSnapshot')) {
        if ($drawingText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndexRuntime 缺少枚举生命周期或 opaque token 门禁要素：$required"
        }
    }

    $drawingCorePath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\DrawingIndexCore.cs'
    $drawingCoreText = Get-Content -LiteralPath $drawingCorePath -Raw -Encoding UTF8
    foreach ($required in @(
        'DrawingIndexCursorRegistry',
        'TimeSpan.FromMinutes(5)',
        'RandomNumberGenerator.Create()',
        'entry.DocumentRevision != documentRevision',
        'entry.QueryFingerprint')) {
        if ($drawingCoreText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndexCore 缺少服务端 opaque cursor 门禁要素：$required"
        }
    }
    if ($drawingCoreText -match '\b(?:EncodeCursor|DecodeCursor)\s*\(') {
        throw 'DrawingIndex cursor 禁止恢复为客户端可解码的偏移载荷。'
    }

    $contractsText = Get-Content -LiteralPath (
        Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\DrawingIndexContracts.cs'
    ) -Raw -Encoding UTF8
    foreach ($required in @(
        'EntityTokenPrefix = "obj-"',
        'EntityTokenCharacters = 12',
        'CadQueryEntityTokens.IsValid')) {
        if ($contractsText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndex Contracts 缺少 opaque entity token 门禁要素：$required"
        }
    }

    $agentToolsText = Get-Content -LiteralPath (
        Join-Path $repoRoot 'src\Codex.AutoCAD.AgentRuntime\CadDynamicTools.cs'
    ) -Raw -Encoding UTF8
    foreach ($required in @(
        '"pattern": "^obj-[0-9]{8}$"',
        '"cursor": { "type": "string", "maxLength": 512 }')) {
        if ($agentToolsText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "AgentRuntime drawing-query schema 缺少受审限制：$required"
        }
    }

    $performancePath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\DrawingIndexPerformanceMetrics.cs'
    if ($SourceFiles -notcontains $performancePath) {
        throw 'Host.2016 Compile 闭包缺少 DrawingIndexPerformanceMetrics.cs。'
    }
    $performanceText = Get-Content -LiteralPath $performancePath -Raw -Encoding UTF8
    if ($performanceText -match '(?i)\bAutodesk\s*\.') {
        throw 'DrawingIndex 性能遥测禁止引用 Autodesk API。'
    }
    foreach ($required in @(
        'RecordIdleSlice',
        'CompleteScan',
        'RecordQuery',
        'MaximumIdleSliceDuration',
        'TotalScanDuration')) {
        if ($performanceText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndex 性能遥测缺少受审要素：$required"
        }
    }
    foreach ($required in @(
        'Maximum idle slice ms:',
        'Total scan elapsed ms:',
        'Managed memory budget bytes:',
        'Query page entity limit:',
        'IPC message hard limit bytes:',
        'Maximum query ms:')) {
        if ($drawingText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndex INFO 缺少性能证据字段：$required"
        }
    }

    $snapshotPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\DrawingIndexAgentSnapshot.cs'
    if ($SourceFiles -notcontains $snapshotPath) {
        throw 'Host.2016 Compile 闭包缺少 DrawingIndexAgentSnapshot.cs。'
    }
    $snapshotText = Get-Content -LiteralPath $snapshotPath -Raw -Encoding UTF8
    if ($snapshotText -match '(?i)\bAutodesk\s*\.') {
        throw 'DrawingIndexAgentSnapshot 禁止引用 Autodesk API。'
    }
    foreach ($required in @(
        'DrawingIndexSnapshotValidity',
        'Volatile.Read',
        'DrawingIndexQueryEngine.Execute',
        'CreateFromOwnedFrozenEntities',
        'performanceMetrics.RecordQuery(timer.Elapsed)',
        'cancellationToken.ThrowIfCancellationRequested')) {
        if ($snapshotText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndexAgentSnapshot 缺少纯托管快照要素：$required"
        }
    }

    $agentClientText = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\MvpAgentClient.cs') -Raw -Encoding UTF8
    foreach ($required in @(
        'drawingQueryHandler: HandleDrawingQueryAsync',
        'AgentDrawingQueryRequest',
        'ResultIdentityMismatch',
        'DrawingQueryUnavailable',
        'requestTurn.TryBindProviderTurn(request.TurnId)',
        'SnapshotGeneration')) {
        if ($agentClientText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "MvpAgentClient 缺少整图反向查询绑定要素：$required"
        }
    }

    $bridgeClientText = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\AgentBridgeClient.cs') -Raw -Encoding UTF8
    foreach ($required in @(
        '_pendingTurnStarts',
        'RegisterPendingTurnStart',
        'pending.TryBindProviderTurn(request.TurnId)',
        'identity.Matches(request.RequestId, request.ThreadId)')) {
        if ($bridgeClientText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "Bridge Client 缺少启动响应前反向查询身份门禁：$required"
        }
    }

    $commandsText = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\CodexCad2016Commands.cs') -Raw -Encoding UTF8
    $extensionText = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\CodexAutoCad2016Extension.cs') -Raw -Encoding UTF8
    foreach ($staleDeclaration in @(
        'Codex drawing-query tool: not connected in this host slice',
        'Codex 动态查询工具尚未接入')) {
        if ($commandsText.IndexOf($staleDeclaration, [StringComparison]::Ordinal) -ge 0 -or
            $extensionText.IndexOf($staleDeclaration, [StringComparison]::Ordinal) -ge 0) {
            throw "M2-B 已接入，但 Host 仍包含过期诊断声明：$staleDeclaration"
        }
    }
    foreach ($required in @(
        'Codex drawing-query tool: authenticated AgentHost Bridge; manual Agent start',
        'DrawingIndex v1/CadQuery v1 通过认证 AgentHost Bridge')) {
        if ($commandsText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "Host 诊断缺少 M2-B 已接入声明：$required"
        }
    }
    if ($extensionText.IndexOf('手动启动的 Codex 通过认证 Bridge 按需分页查询', [StringComparison]::Ordinal) -lt 0) {
        throw 'Host 加载横幅缺少 M2-B 已接入声明。'
    }
    foreach ($command in @('CODEX16INDEX','CODEX16INDEXINFO','CODEX16INDEXCANCEL','CODEX16QUERY','CODEX16QUERYNEXT')) {
        $count = [regex]::Matches($commandsText, 'CommandMethod\("' + [regex]::Escape($command) + '"').Count
        if ($count -ne 1) {
            throw "M2 命令必须精确声明一次：$command，实际：$count。"
        }
    }
}

function Assert-M3ReadSemantics([string[]] $SourceFiles) {
    $typeStatisticsPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\CadReadTypeStatistics.cs'
    $blockCorePath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\DrawingIndexBlockReadCore.cs'
    foreach ($requiredSource in @($typeStatisticsPath, $blockCorePath)) {
        if ($SourceFiles -notcontains $requiredSource) {
            throw "M3 Host.2016 Compile 闭包缺少：$requiredSource"
        }
    }

    $typeStatisticsText = Get-Content -LiteralPath $typeStatisticsPath -Raw -Encoding UTF8
    foreach ($required in @(
        'MaximumCountBuckets',
        'FromSelection',
        'FormatActualTypeCounts',
        'BuildSupportedTypeCatalog',
        '当前 19 类强类型读取对象：',
        '整图索引受限类别：')) {
        if ($typeStatisticsText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "CadReadTypeStatistics 缺少 M3 受审要素：$required"
        }
    }
    $catalogCount = [regex]::Matches($typeStatisticsText, 'AppendCatalogEntry\(builder,').Count
    if ($catalogCount -ne 19) {
        throw "M3 中文对象目录必须精确包含 19 项，实际：$catalogCount。"
    }

    $readerPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\DrawingIndexEntityReader.cs'
    $readerText = Get-Content -LiteralPath $readerPath -Raw -Encoding UTF8
    foreach ($required in @(
        'ReadBlockDetails',
        'ReadAttributeDetails',
        'ReadDynamicPropertyDetails',
        'ReadNestedDefinitionIds',
        'DrawingIndexBlockTraversal.Traverse',
        'DrawingIndexEntityTypes.IsHighValueLimited',
        'IsFromExternalReference',
        'IsFromOverlayReference',
        'HasAttributeDefinitions',
        'DynamicBlockReferencePropertyCollection')) {
        if ($readerText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndexEntityReader 缺少 M3 读取语义要素：$required"
        }
    }
    if ($readerText -match '(?i)\b(?:PathName|XrefPath|ExternalReferencePath)\b') {
        throw 'M3 读取语义禁止引用外部 Xref 路径字段。'
    }

    $blockCoreText = Get-Content -LiteralPath $blockCorePath -Raw -Encoding UTF8
    foreach ($required in @(
        'DrawingIndexBlockDefinitionSummaryCache',
        'StoreIfReusable',
        'BudgetExpired',
        'DrawingIndexBlockTraversal',
        'current.Path.Contains')) {
        if ($blockCoreText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "DrawingIndexBlockReadCore 缺少有界缓存/循环保护要素：$required"
        }
    }

    $commandsText = Get-Content -LiteralPath (
        Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\CodexCad2016Commands.cs'
    ) -Raw -Encoding UTF8
    if ([regex]::Matches($commandsText, 'CommandMethod\("CODEX16TYPEINFO"').Count -ne 1 -or
        $commandsText.IndexOf('CadReadTypeStatistics.BuildSupportedTypeCatalog()', [StringComparison]::Ordinal) -lt 0) {
        throw 'M3 CODEX16TYPEINFO 必须精确声明一次并连接中文对象目录。'
    }

    $contractsText = Get-Content -LiteralPath (
        Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\DrawingIndexContracts.cs'
    ) -Raw -Encoding UTF8
    foreach ($required in @(
        'CadQueryBlockDetails',
        'CadQueryBlockDetailsCloner',
        'MaximumBlockAttributes',
        'DrawingIndexEntityTypes',
        'IsHighValueLimited')) {
        if ($contractsText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "M3 Contracts 缺少块详情或受限类别边界：$required"
        }
    }

    $bridgeCodecText = Get-Content -LiteralPath (
        Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\BridgeClientJsonCodec.cs'
    ) -Raw -Encoding UTF8
    foreach ($required in @('BlockDetails = ToWire', 'DataMember(Name = "blockDetails"')) {
        if ($bridgeCodecText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "M3 Bridge Codec 缺少 blockDetails 传输边界：$required"
        }
    }

    $agentToolText = Get-Content -LiteralPath (
        Join-Path $repoRoot 'src\Codex.AutoCAD.AgentRuntime\IAgentCadDrawingQueryBroker.cs'
    ) -Raw -Encoding UTF8
    foreach ($required in @('CadDrawingQueryToolBlockDetailsWire', 'JsonPropertyName("blockDetails")')) {
        if ($agentToolText.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
            throw "M3 Agent 工具缺少 blockDetails 传输边界：$required"
        }
    }
}

$requiredPaths = @(
    $AutoCad2016Dir,
    (Join-Path $AutoCad2016Dir 'acad.exe'),
    (Join-Path $AutoCad2016Dir 'accoremgd.dll'),
    (Join-Path $AutoCad2016Dir 'acdbmgd.dll'),
    (Join-Path $AutoCad2016Dir 'acmgd.dll'),
    $phase2Script,
    $hostProject,
    $nugetConfig,
    $assemblyInfoPath)
if ($candidateProfile.UsesM3ApiProbe) {
    $requiredPaths += $m3ApiProbeStageScript
}
if ($candidateProfile.UsesM3CoreFixture) {
    $requiredPaths += $m3CoreFixtureScript
    $requiredPaths += $m3CoreFixtureManifestPath
}
foreach ($path in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "缺少必要路径：$path"
    }
}
if (-not (Test-X64Pe (Join-Path $AutoCad2016Dir 'acad.exe'))) {
    throw '目标 acad.exe 不是 x64 PE。'
}
foreach ($assemblyName in @('accoremgd.dll','acdbmgd.dll','acmgd.dll')) {
    $identity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $AutoCad2016Dir $assemblyName))
    if ($identity.Version.ToString() -cne '20.1.0.0') {
        throw "$assemblyName 版本不是 20.1.0.0：$($identity.Version)"
    }
}

foreach ($entry in $expectedLockHashes.GetEnumerator()) {
    $path = Join-Path $repoRoot $entry.Key
    if ((Get-GitBlobHash $path) -cne $entry.Value) {
        throw "锁文件哈希漂移：$($entry.Key)"
    }
}

$sourceCommit = (
    & $git rev-parse --verify HEAD 2>&1 |
        Select-Object -Last 1
).ToString().Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '\A[0-9a-f]{40,64}\z') {
    throw '无法解析候选源码提交。'
}
$sourceStatus = @(
    & $git -c core.quotepath=false status --porcelain=v1 --untracked-files=all 2>&1 |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) }
)
if ($LASTEXITCODE -ne 0) {
    throw '无法检查候选源码工作树状态。'
}
if ($sourceStatus.Count -ne 0) {
    throw '候选必须从干净工作树构建；请先提交或清理全部非忽略变更。'
}

$assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw -Encoding UTF8
$versionMatch = [regex]::Match($assemblyInfo, 'AssemblyVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
$fileVersionMatch = [regex]::Match($assemblyInfo, 'AssemblyFileVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
if (-not $versionMatch.Success -or -not $fileVersionMatch.Success -or
    $versionMatch.Groups['Version'].Value -cne $fileVersionMatch.Groups['Version'].Value) {
    throw 'Host.2016 AssemblyVersion 与 AssemblyFileVersion 必须存在且完全一致。'
}
$hostVersion = $versionMatch.Groups['Version'].Value
if ($hostVersion -cne $candidateProfile.ExpectedHostVersion) {
    throw "$($candidateProfile.Title) 候选必须为 $($candidateProfile.ExpectedHostVersion)，实际：$hostVersion"
}

$sourceFiles = Get-HostCompileSources $hostProject
Assert-M2ReadOnlySource $sourceFiles
if ($CandidateStage -ceq 'M3') {
    Assert-M3ReadSemantics $sourceFiles
}
$documentLockCount = @(
    Select-String -LiteralPath $sourceFiles -Pattern '(?i)\.\s*LockDocument\s*\('
).Count

$lockSnapshots = [ordered]@{}
foreach ($lockFile in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter 'packages.lock.json')) {
    $lockSnapshots[$lockFile.FullName] = [IO.File]::ReadAllBytes($lockFile.FullName)
}

New-Item -ItemType Directory -Path $stageRoot,$packageRoot,$publishRoot -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $stageRoot 'dotnet-cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:NUGET_PACKAGES = $packageRoot
$env:NUGET_HTTP_CACHE_PATH = Join-Path $stageRoot 'nuget-http-cache'
$m3ApiProbeSummary = $null
$m3CoreFixtureSummary = $null

try {
    $phase2Output = Invoke-CapturedOutput $powerShell @(
        '-NoProfile',
        '-File', $phase2Script,
        '-Configuration', $Configuration
    ) ($candidateProfile.Title + ' Phase 2 动态门禁')

    $phase2Summary = @(
        foreach ($line in $phase2Output) {
            $match = [regex]::Match($line, '规格动态计数汇总：(?<Passed>\d+)/(?<Total>\d+)')
            if ($match.Success -and $match.Groups['Passed'].Value -ceq $match.Groups['Total'].Value) {
                $match.Groups['Passed'].Value + '/' + $match.Groups['Total'].Value
            }
        }
    ) | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($phase2Summary)) {
        throw 'Phase 2 输出缺少全部通过的动态规格摘要。'
    }
    $phase2ProjectSummaries = [ordered]@{}
    foreach ($line in $phase2Output) {
        $match = [regex]::Match(
            $line,
            '^\s*(?<Project>Codex\.AutoCAD\.[A-Za-z0-9.]+\.Specs):\s*(?<Passed>\d+)/(?<Total>\d+)\s*$')
        if ($match.Success -and $match.Groups['Passed'].Value -ceq $match.Groups['Total'].Value) {
            $phase2ProjectSummaries[$match.Groups['Project'].Value] =
                $match.Groups['Passed'].Value + '/' + $match.Groups['Total'].Value
        }
    }
    $contractsProject = 'Codex.AutoCAD.Contracts.Specs'
    if (-not $phase2ProjectSummaries.Contains($contractsProject)) {
        throw 'Phase 2 输出缺少 Contracts 动态规格摘要。'
    }

    $benchmarkOutput = Invoke-CapturedOutput $powerShell @(
        '-NoProfile',
        '-File', $benchmarkScript
    ) ($candidateProfile.Title + ' 1k/10k/50k benchmark fixture 门禁')
    $benchmarkSummary = @(
        foreach ($line in $benchmarkOutput) {
            $match = [regex]::Match($line, 'benchmark fixture checks passed: (?<Passed>\d+)/(?<Total>\d+)')
            if ($match.Success -and $match.Groups['Passed'].Value -ceq $match.Groups['Total'].Value) {
                $match.Groups['Passed'].Value + '/' + $match.Groups['Total'].Value
            }
        }
    ) | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($benchmarkSummary)) {
        throw 'Benchmark fixture 输出缺少全部通过的动态门禁摘要。'
    }
    $benchmarkManifest = Get-Content -LiteralPath $benchmarkManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

    if ($candidateProfile.UsesM3ApiProbe) {
        $m3ApiProbeEvidenceDirectory = Join-Path $stageRoot 'm3-api-probe-evidence'
        Invoke-Captured $powerShell @(
            '-NoProfile',
            '-File', $m3ApiProbeStageScript,
            '-AutoCad2016Dir', $AutoCad2016Dir,
            '-Configuration', $Configuration,
            '-EvidenceDirectory', $m3ApiProbeEvidenceDirectory
        ) ($candidateProfile.Title + ' R20.1 API 双 Shell Probe')

        $m3ApiProbeEvidenceFiles = @(
            Get-ChildItem -LiteralPath $m3ApiProbeEvidenceDirectory -File `
                -Filter 'v2-api-surface-probe-m3-cross-shell-*.json'
        )
        if ($m3ApiProbeEvidenceFiles.Count -ne 1) {
            throw "M3 API Probe 必须精确产生一份聚合 evidence，实际：$($m3ApiProbeEvidenceFiles.Count)。"
        }
        $m3ApiProbeEvidencePath = $m3ApiProbeEvidenceFiles[0].FullName
        $m3ApiProbeEvidence =
            Get-Content -LiteralPath $m3ApiProbeEvidencePath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        if ([string] $m3ApiProbeEvidence.status -cne 'dual-shell-gate-passed' -or
            [int] $m3ApiProbeEvidence.runtimeChecksPassed -ne 29 -or
            [int] $m3ApiProbeEvidence.runtimeChecksFailed -ne 8 -or
            -not [bool] $m3ApiProbeEvidence.passedMembersMatchExpected -or
            -not [bool] $m3ApiProbeEvidence.failedMembersMatchExpected -or
            -not [bool] $m3ApiProbeEvidence.crossShellNormalizedIdentical -or
            -not [bool] $m3ApiProbeEvidence.crossShellDllSha256Identical -or
            [int] $m3ApiProbeEvidence.buildWarnings -ne 0 -or
            [int] $m3ApiProbeEvidence.buildErrors -ne 0 -or
            [int] $m3ApiProbeEvidence.autodeskDllsInOutput -ne 0 -or
            [bool] $m3ApiProbeEvidence.autoCadStartedOrRestarted -or
            [bool] $m3ApiProbeEvidence.cadCommandsSent) {
            throw 'M3 API Probe evidence 未满足冻结门禁。'
        }
        $m3ApiProbeSummary = [ordered]@{
            runtimeChecksPassed = [int] $m3ApiProbeEvidence.runtimeChecksPassed
            runtimeChecksExpectedFailed = [int] $m3ApiProbeEvidence.runtimeChecksFailed
            crossShellNormalizedIdentical = [bool] $m3ApiProbeEvidence.crossShellNormalizedIdentical
            crossShellDllSha256Identical = [bool] $m3ApiProbeEvidence.crossShellDllSha256Identical
            dllSha256 = [string] $m3ApiProbeEvidence.dllSha256
            aggregateEvidenceSha256 = Get-Sha256 $m3ApiProbeEvidencePath
        }
    }

    if ($candidateProfile.UsesM3CoreFixture) {
        $m3CoreFixtureOutput = Invoke-CapturedOutput $powerShell @(
            '-NoProfile',
            '-File', $m3CoreFixtureScript
        ) ($candidateProfile.Title + ' 核心读取 DXF fixture 门禁')
        $m3CoreFixtureCheckSummary = @(
            foreach ($line in $m3CoreFixtureOutput) {
                $match = [regex]::Match(
                    $line,
                    'M3 core read fixture checks passed: (?<Passed>\d+)/(?<Total>\d+)')
                if ($match.Success -and
                    $match.Groups['Passed'].Value -ceq $match.Groups['Total'].Value) {
                    $match.Groups['Passed'].Value + '/' + $match.Groups['Total'].Value
                }
            }
        ) | Select-Object -Last 1
        if ([string]::IsNullOrWhiteSpace($m3CoreFixtureCheckSummary)) {
            throw 'M3 核心读取 DXF fixture 输出缺少全部通过的动态门禁摘要。'
        }
        $m3CoreFixtureManifest =
            Get-Content -LiteralPath $m3CoreFixtureManifestPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        if ([int] $m3CoreFixtureManifest.expectedEntityRecordCount -ne 14 -or
            [string] $m3CoreFixtureManifest.expectedDxfSha256 -notmatch '^[0-9a-f]{64}$') {
            throw 'M3 核心读取 DXF fixture manifest 未满足冻结边界。'
        }
        $m3CoreFixtureSummary = [ordered]@{
            checks = $m3CoreFixtureCheckSummary
            fixtureId = [string] $m3CoreFixtureManifest.fixtureId
            entityRecordCount = [int] $m3CoreFixtureManifest.expectedEntityRecordCount
            dxfSha256 = [string] $m3CoreFixtureManifest.expectedDxfSha256
            expectedManifestSha256 = Get-Sha256 $m3CoreFixtureManifestPath
        }
    }

    foreach ($snapshot in $lockSnapshots.GetEnumerator()) {
        [IO.File]::WriteAllBytes([string] $snapshot.Key, [byte[]] $snapshot.Value)
    }

    $dependencyProjects = @(
        (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj'),
        (Join-Path $repoRoot 'src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj'),
        (Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj'),
        (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj')
    )

    foreach ($project in @($dependencyProjects + $hostProject)) {
        $restoreArguments = @(
            'restore', $project,
            '--configfile', $nugetConfig,
            '--packages', $packageRoot,
            '--force', '--no-cache', '--disable-parallel',
            '--disable-build-servers',
            '-p:EnableAutoCad2016=true'
        )
        if ($project -eq $hostProject) {
            $restoreArguments += '-p:RestoreLockedMode=false'
            $restoreArguments += '--force-evaluate'
        }
        else {
            $restoreArguments += '-p:RestoreLockedMode=true'
        }
        Invoke-Captured $dotnet $restoreArguments ('离线锁定恢复 ' + (Split-Path -Leaf $project))
    }
    foreach ($snapshot in $lockSnapshots.GetEnumerator()) {
        [IO.File]::WriteAllBytes([string] $snapshot.Key, [byte[]] $snapshot.Value)
    }

    foreach ($dependency in $dependencyProjects) {
        Invoke-Captured $dotnet @(
            'build', $dependency,
            '--configuration', $Configuration,
            '--framework', 'net45',
            '--no-restore', '--nologo', '--disable-build-servers', '-m:1',
            '-p:BuildProjectReferences=false',
            '-p:UseSharedCompilation=false',
            '-p:EnableAutoCad2016=true',
            ('-p:FrameworkPathOverride=' + $net45ReferencePath)
        ) ('net45 依赖编译 ' + (Split-Path -Leaf $dependency))
    }

    $hostBuilds = @()
    foreach ($label in @('A','B')) {
        $output = Join-Path $stageRoot ('host-' + $label)
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        foreach ($dependencyName in @('Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.AgentLauncher.dll')) {
            $dependency = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter $dependencyName |
                Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]net45[\\/]' } |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
            if ($null -eq $dependency) {
                throw "缺少 Host.2016 net45 依赖：$dependencyName"
            }
            Copy-Item -LiteralPath $dependency.FullName -Destination (Join-Path $output $dependencyName) -Force
        }
        Invoke-Captured $dotnet @(
            'msbuild', $hostProject,
            '/t:Rebuild',
            "/p:Configuration=$Configuration",
            '/p:Platform=x64',
            ('/p:AutoCad2016Dir=' + $AutoCad2016Dir),
            '/p:EnableAutoCad2016=true',
            '/p:AutomaticallyUseReferenceAssemblyPackages=true',
            ('/p:FrameworkPathOverride=' + $net45ReferencePath),
            '/p:BuildProjectReferences=false',
            ('/p:OutputPath=' + $output + '\'),
            '/p:DebugSymbols=false',
            '/p:DebugType=None',
            '/p:ContinuousIntegrationBuild=true',
            '/m:1', '/nr:false', '/nologo'
        ) ('Host.2016 R20.1 ' + $candidateProfile.Title + ' 编译 ' + $label)

        $hostDll = Join-Path $output 'Codex.AutoCAD.Host.2016.dll'
        if (-not (Test-X64Pe $hostDll)) {
            throw "Host.2016 $label 不是 x64 PE。"
        }
        foreach ($autodeskName in @('accoremgd.dll','acdbmgd.dll','acmgd.dll')) {
            if (Test-Path -LiteralPath (Join-Path $output $autodeskName)) {
                throw "Host.2016 输出禁止复制 Autodesk 程序集：$autodeskName"
            }
        }
        $identity = [Reflection.AssemblyName]::GetAssemblyName($hostDll)
        if ($identity.Name -cne 'Codex.AutoCAD.Host.2016' -or
            $identity.Version.ToString() -cne $hostVersion) {
            throw "Host.2016 $label 程序集身份不匹配：$($identity.FullName)"
        }
        $hostBuilds += [pscustomobject]@{
            Label = $label
            Root = $output
            Dll = $hostDll
            Sha256 = Get-Sha256 $hostDll
        }
    }
    Assert-Same `
        (Get-FilesSnapshot $hostBuilds[0].Root) `
        (Get-FilesSnapshot $hostBuilds[1].Root) `
        ('Host.2016 ' + $candidateProfile.Title + ' A/B 输出')

    Invoke-Captured $dotnet @(
        'publish', (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj'),
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'false',
        '--no-restore',
        '--output', $publishRoot,
        '--nologo', '--disable-build-servers', '-m:1',
        '-p:BuildInParallel=false',
        '-p:UseSharedCompilation=false'
    ) 'AgentHost framework-dependent 发布'

    $hostSha = $hostBuilds[0].Sha256
    $agentExe = Join-Path $publishRoot 'Codex.AutoCAD.AgentHost.exe'
    $agentSha = Get-Sha256 $agentExe
    $candidateId = $candidateProfile.CandidatePrefix + '-' +
        $hostSha.Substring(0,8).ToLowerInvariant() + '-' +
        $agentSha.Substring(0,8).ToLowerInvariant() + '-' +
        $runId.Substring(0,8)
    $candidateRoot = Join-Path $repoRoot ('artifacts\' + $candidateId)
    if (Test-Path -LiteralPath $candidateRoot) {
        throw "候选目录已存在，拒绝覆盖：$candidateRoot"
    }
    New-Item -ItemType Directory -Path $candidateRoot -Force | Out-Null

    Copy-Item -LiteralPath $hostBuilds[0].Dll -Destination (Join-Path $candidateRoot 'Codex.AutoCAD.Host.2016.dll')
    foreach ($name in @('Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.AgentLauncher.dll')) {
        $source = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter $name |
            Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]net45[\\/]' } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $source) {
            throw "找不到 net45 依赖产物：$name"
        }
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $candidateRoot $name)
    }

    $agentTarget = Join-Path $candidateRoot 'AgentHost'
    New-Item -ItemType Directory -Path $agentTarget -Force | Out-Null
    foreach ($file in @(Get-ChildItem -LiteralPath $publishRoot -File)) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $agentTarget $file.Name) -Force
    }
    [IO.File]::WriteAllText(
        (Join-Path $agentTarget 'Codex.AutoCAD.AgentHost.exe.sha256'),
        $agentSha + "  Codex.AutoCAD.AgentHost.exe`n",
        (New-Object Text.UTF8Encoding($false)))

    $files = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $candidateRoot -Recurse -File | Sort-Object FullName)) {
        if ($file.Name -ieq 'manifest.json') { continue }
        $relative = $file.FullName.Substring($candidateRoot.Length + 1).Replace('\', '/')
        $files[$relative] = [ordered]@{
            Length = $file.Length
            Sha256 = Get-Sha256 $file.FullName
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        candidateId = $candidateId
        candidateStage = $CandidateStage
        sourceCommit = $sourceCommit
        hostVersion = $hostVersion
        cadContextSchema = 'codex.autocad.cad-context/2'
        drawingIndexSchema = 'codex.autocad.drawing-index/1'
        cadQuerySchema = 'codex.autocad.cad-query/1'
        targetApi = 'AutoCAD R20.1 / managed 20.1.0.0 / net45 / x64'
        boundaries = [ordered]@{
            cadWrite = $false
            pluginInitiatedSave = $false
            codexDynamicDrawingQueryTool = $true
            maximumIndexedEntities = 100000
            maximumReportedEntities = 2000000
            maximumEstimatedManagedBytes = 67108864
            maximumScanSeconds = 120
            cooperativeIdleSliceTargetMilliseconds = 12
            maximumCadQueryPageSize = 200
            maximumIpcMessageBytes = 8388608
        }
        benchmarkFixtures = @(
            foreach ($fixture in @($benchmarkManifest.files)) {
                [ordered]@{
                    fileName = [string] $fixture.fileName
                    entityCount = [int] $fixture.entityCount
                    sha256 = [string] $fixture.sha256
                }
            }
        )
        files = $files
    }
    if ($candidateProfile.UsesM3ApiProbe) {
        $manifest['readSemantics'] = [ordered]@{
            issueTypeStatistics = $true
            supportedTypeCatalogCount = 19
            blockDetails = $true
            highValueLimitedCategoryCount = 8
            externalXrefPathsExcluded = $true
            apiProbe = $m3ApiProbeSummary
            coreReadFixture = $m3CoreFixtureSummary
        }
    }
    $manifestPath = Join-Path $candidateRoot 'manifest.json'
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 40) + "`n",
        (New-Object Text.UTF8Encoding($false)))

    $gates = [ordered]@{
        phase2Specs = $phase2Summary
        phase2Projects = $phase2ProjectSummaries
        contractsNet45Net8 = $phase2ProjectSummaries[$contractsProject]
        r20_1ReleaseX64Build = $true
        hostABBitForBitEqual = $true
        hostReadOnlySourceScan = $true
        earlyReverseDrawingQueryRaceCovered = $true
        frozenEntityArrayOwnershipTransfer = $true
        opaqueEntityTokens = $true
        serverSideOpaqueCursorWithExpiry = $true
        blockTableRecordEnumeratorEscapesTransaction = $false
        autoCadPreparationSliceBudgetVerified = $false
        benchmarkFixtures = $benchmarkSummary
        hostLocalPerformanceTelemetry = $true
        hostCompileSourceCount = $sourceFiles.Count
        documentLockCount = $documentLockCount
        sourceTreeCleanAtStart = $true
        lockFileHashesPreserved = $true
        autoCadStartedOrRestarted = $false
        commandsSent = $false
    }
    if ($candidateProfile.UsesM3ApiProbe) {
        $gates['m3ReadSemantics'] = $true
        $gates['m3DualShellApiProbe'] = $true
        $gates['m3SupportedTypeCatalogCount'] = 19
        $gates['m3HighValueLimitedCategoryCount'] = 8
        $gates['m3ExternalXrefPathsExcluded'] = $true
        $gates['m3CoreReadFixture'] = $m3CoreFixtureSummary.checks
    }

    $limitations = @(
        '本证据未启动、重启或操作 AutoCAD；人工 NETLOAD 和图纸级运行时行为仍需实机验证。',
        'CadContextJson v2 的 64 实体选择上限保持不变；M2 通过独立 DrawingIndex/CadQuery 处理大图。',
        'Codex 动态 drawing-query 已接入认证反向 Bridge；仍需 AutoCAD 实机验证整图索引、无选择集提问和失效行为。',
        '空间 ObjectId 在单个只读 Transaction 内形成有界托管快照；50k preparation 最大 Idle slice 仍必须由精确候选实机遥测证明。',
        'CAD 写入、插件保存、Provider 抽象、Direct API 和自研 Agent Loop 均不在本候选范围。'
    )
    if ($candidateProfile.UsesM3ApiProbe) {
        $limitations +=
            'M3 的 19 类字段、复杂对象、块/Xref 边界和高价值受限对象仍需按精确候选完成 AutoCAD 2016 实机矩阵。'
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        scope = $candidateProfile.EvidenceScope
        candidateFrozenAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        candidateId = $candidateId
        candidateStage = $CandidateStage
        sourceCommit = $sourceCommit
        hostVersion = $hostVersion
        autoCadLiveEvidence = $false
        netLoadVerified = $false
        gates = $gates
        build = [ordered]@{
            hostDllSha256 = $hostSha
            agentHostExeSha256 = $agentSha
            manifestSha256 = Get-Sha256 $manifestPath
        }
        candidate = [ordered]@{
            root = 'artifacts/' + $candidateId
            files = $files
        }
        limitations = $limitations
    }
    if ($candidateProfile.UsesM3ApiProbe) {
        $evidence['m3ApiProbe'] = $m3ApiProbeSummary
        $evidence['m3CoreReadFixture'] = $m3CoreFixtureSummary
    }
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $evidencePath = Join-Path $evidenceDirectory (
        $candidateProfile.EvidencePrefix + $candidateId + '.json')
    [IO.File]::WriteAllText(
        $evidencePath,
        ($evidence | ConvertTo-Json -Depth 50) + "`n",
        (New-Object Text.UTF8Encoding($false)))

    Write-Host ('AutoCAD 2016 ' + $candidateProfile.Title + ' 候选自动化冻结通过。') -ForegroundColor Green
    Write-Host "CANDIDATE_ROOT=$candidateRoot"
    Write-Host "CANDIDATE_ID=$candidateId"
    Write-Host "CANDIDATE_STAGE=$CandidateStage"
    Write-Host "SOURCE_COMMIT=$sourceCommit"
    Write-Host "HOST_VERSION=$hostVersion"
    Write-Host "PHASE2_SPECS=$phase2Summary"
    Write-Host "BENCHMARK_FIXTURES=$benchmarkSummary"
    Write-Host "HOST_SHA256=$hostSha"
    Write-Host "AGENTHOST_SHA256=$agentSha"
    Write-Host "MANIFEST_SHA256=$(Get-Sha256 $manifestPath)"
    Write-Host "EVIDENCE=$evidencePath"
}
finally {
    try {
        & $dotnet 'build-server' 'shutdown' 2>&1 | Out-Null
    }
    catch {
        # Lock snapshots still must be restored if build-server shutdown is unavailable.
    }
    foreach ($snapshot in $lockSnapshots.GetEnumerator()) {
        [IO.File]::WriteAllBytes([string] $snapshot.Key, [byte[]] $snapshot.Value)
    }
}

foreach ($entry in $expectedLockHashes.GetEnumerator()) {
    $path = Join-Path $repoRoot $entry.Key
    if ((Get-GitBlobHash $path) -cne $entry.Value) {
        throw "验证结束后锁文件哈希漂移：$($entry.Key)"
    }
}
$finalCommit = (
    & $git rev-parse --verify HEAD 2>&1 |
        Select-Object -Last 1
).ToString().Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $finalCommit -cne $sourceCommit) {
    throw '候选构建期间源码提交发生变化。'
}

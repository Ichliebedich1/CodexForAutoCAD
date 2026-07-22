[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',

    [string] $AutoCad2016Dir = 'D:\AutoCAD 2016'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$git = (Get-Command git -ErrorAction Stop).Source
$powerShell = (Get-Process -Id $PID).Path
$runId = [Guid]::NewGuid().ToString('N')
$stageRoot = Join-Path $repoRoot ('artifacts\autocad2016-m2-drawing-index-' + $runId)
$packageRoot = Join-Path $stageRoot 'packages'
$publishRoot = Join-Path $stageRoot 'agenthost-publish'
$net45ReferencePath = Join-Path $packageRoot 'microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5'
$phase2Script = Join-Path $repoRoot 'scripts\verify-phase2.ps1'
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

foreach ($path in @(
    $AutoCad2016Dir,
    (Join-Path $AutoCad2016Dir 'acad.exe'),
    (Join-Path $AutoCad2016Dir 'accoremgd.dll'),
    (Join-Path $AutoCad2016Dir 'acdbmgd.dll'),
    (Join-Path $AutoCad2016Dir 'acmgd.dll'),
    $phase2Script,
    $hostProject,
    $nugetConfig,
    $assemblyInfoPath)) {
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

$assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw -Encoding UTF8
$versionMatch = [regex]::Match($assemblyInfo, 'AssemblyVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
$fileVersionMatch = [regex]::Match($assemblyInfo, 'AssemblyFileVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
if (-not $versionMatch.Success -or -not $fileVersionMatch.Success -or
    $versionMatch.Groups['Version'].Value -cne $fileVersionMatch.Groups['Version'].Value) {
    throw 'Host.2016 AssemblyVersion 与 AssemblyFileVersion 必须存在且完全一致。'
}
$hostVersion = $versionMatch.Groups['Version'].Value
if ($hostVersion -cne '0.4.0.0') {
    throw "M2 DrawingIndex 候选必须为 0.4.0.0，实际：$hostVersion"
}

$sourceFiles = Get-HostCompileSources $hostProject
Assert-M2ReadOnlySource $sourceFiles

$lockSnapshots = [ordered]@{}
foreach ($lockFile in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter 'packages.lock.json')) {
    $lockSnapshots[$lockFile.FullName] = [IO.File]::ReadAllBytes($lockFile.FullName)
}

New-Item -ItemType Directory -Path $stageRoot,$packageRoot,$publishRoot -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $stageRoot 'dotnet-cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = $packageRoot
$env:NUGET_HTTP_CACHE_PATH = Join-Path $stageRoot 'nuget-http-cache'

try {
    $phase2Output = Invoke-CapturedOutput $powerShell @(
        '-NoProfile',
        '-File', $phase2Script,
        '-Configuration', $Configuration
    ) 'M2 Phase 2 动态门禁'

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
            '--no-restore', '--nologo', '-m:1',
            '-p:BuildProjectReferences=false',
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
            '/m:1', '/nologo'
        ) ('Host.2016 R20.1 M2 编译 ' + $label)

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
    Assert-Same (Get-FilesSnapshot $hostBuilds[0].Root) (Get-FilesSnapshot $hostBuilds[1].Root) 'Host.2016 M2 A/B 输出'

    Invoke-Captured $dotnet @(
        'publish', (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj'),
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'false',
        '--no-restore',
        '--output', $publishRoot,
        '--nologo', '-m:1',
        '-p:BuildInParallel=false',
        '-p:UseSharedCompilation=false'
    ) 'AgentHost framework-dependent 发布'

    $hostSha = $hostBuilds[0].Sha256
    $agentExe = Join-Path $publishRoot 'Codex.AutoCAD.AgentHost.exe'
    $agentSha = Get-Sha256 $agentExe
    $candidateId = 'autocad2016-m2-drawing-index-v040-' +
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
        }
        files = $files
    }
    $manifestPath = Join-Path $candidateRoot 'manifest.json'
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 40) + "`n",
        (New-Object Text.UTF8Encoding($false)))

    $evidence = [ordered]@{
        schemaVersion = 1
        scope = 'autocad2016-m2-drawing-index-candidate-build'
        candidateFrozenAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        candidateId = $candidateId
        hostVersion = $hostVersion
        autoCadLiveEvidence = $false
        netLoadVerified = $false
        gates = [ordered]@{
            phase2Specs = $phase2Summary
            contractsNet45Net8 = '84/84'
            r20_1ReleaseX64Build = $true
            hostABBitForBitEqual = $true
            hostReadOnlySourceScan = $true
            earlyReverseDrawingQueryRaceCovered = $true
            frozenEntityArrayOwnershipTransfer = $true
            hostCompileSourceCount = $sourceFiles.Count
            documentLockCount = 2
            lockFileHashesPreserved = $true
            autoCadStartedOrRestarted = $false
            commandsSent = $false
        }
        build = [ordered]@{
            hostDllSha256 = $hostSha
            agentHostExeSha256 = $agentSha
            manifestSha256 = Get-Sha256 $manifestPath
        }
        candidate = [ordered]@{
            root = 'artifacts/' + $candidateId
            files = $files
        }
        limitations = @(
            '本证据未启动、重启或操作 AutoCAD；人工 NETLOAD 和图纸级运行时行为仍需实机验证。',
            'CadContextJson v2 的 64 实体选择上限保持不变；M2 通过独立 DrawingIndex/CadQuery 处理大图。',
            'Codex 动态 drawing-query 已接入认证反向 Bridge；仍需 AutoCAD 实机验证整图索引、无选择集提问和失效行为。',
            'CAD 写入、插件保存、Provider 抽象、Direct API 和自研 Agent Loop 均不在本候选范围。'
        )
    }
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $evidencePath = Join-Path $evidenceDirectory ('m2-drawing-index-candidate-' + $candidateId + '.json')
    [IO.File]::WriteAllText(
        $evidencePath,
        ($evidence | ConvertTo-Json -Depth 50) + "`n",
        (New-Object Text.UTF8Encoding($false)))

    Write-Host 'AutoCAD 2016 M2 DrawingIndex 候选自动化冻结通过。' -ForegroundColor Green
    Write-Host "CANDIDATE_ROOT=$candidateRoot"
    Write-Host "CANDIDATE_ID=$candidateId"
    Write-Host "HOST_VERSION=$hostVersion"
    Write-Host "PHASE2_SPECS=$phase2Summary"
    Write-Host "HOST_SHA256=$hostSha"
    Write-Host "AGENTHOST_SHA256=$agentSha"
    Write-Host "MANIFEST_SHA256=$(Get-Sha256 $manifestPath)"
    Write-Host "EVIDENCE=$evidencePath"
}
finally {
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

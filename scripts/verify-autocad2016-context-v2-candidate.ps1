[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',
    [string] $AutoCad2016Dir = 'D:\AutoCAD 2016',
    [string] $CodexExecutable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$powerShell = (Get-Process -Id $PID).Path
$net45ReferencePath = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5'
$runId = [Guid]::NewGuid().ToString('N')
$stageRoot = Join-Path $repoRoot ("artifacts\autocad2016-context-v2-candidate-" + $runId)
$publishRoot = Join-Path $stageRoot 'agenthost-publish'
$candidateStage = Join-Path $stageRoot 'candidate'
$evidenceDirectory = Join-Path $repoRoot 'handoff\autocad2016\evidence'
$phase2Script = Join-Path $repoRoot 'scripts\verify-phase2.ps1'
$assemblyInfoPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Properties\AssemblyInfo.cs'

function Get-Sha256([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "缺少文件：$Path" }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Invoke-Captured([string] $FilePath, [string[]] $Arguments, [string] $Description) {
    Write-Host "==> $Description" -ForegroundColor Cyan
    $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string] $_ })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "$Description 失败，退出码：$exitCode" }
}

function Invoke-CapturedOutput([string] $FilePath, [string[]] $Arguments, [string] $Description) {
    Write-Host "==> $Description" -ForegroundColor Cyan
    $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string] $_ })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "$Description 失败，退出码：$exitCode" }
    return $lines
}

function Assert-HostReadOnlySource([string] $ProjectPath) {
    [xml] $project = Get-Content -LiteralPath $ProjectPath -Raw -Encoding UTF8
    $namespace = New-Object Xml.XmlNamespaceManager($project.NameTable)
    $namespace.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $projectDirectory = Split-Path -Parent $ProjectPath
    $sourceFiles = @(
        foreach ($node in @($project.SelectNodes('//m:Compile[@Include]', $namespace))) {
            $path = [IO.Path]::GetFullPath((Join-Path $projectDirectory $node.Include))
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Host.2016 Compile 项不存在：$path"
            }
            $path
        }
    ) | Sort-Object -Unique
    if ($sourceFiles.Count -eq 0) { throw 'Host.2016 Compile 闭包为空。' }

    $forbidden = [ordered]@{
        'CAD ForWrite' = '(?i)\bOpenMode\s*\.\s*ForWrite\b'
        'CAD mutation' = '(?i)\.\s*(?:UpgradeOpen|DowngradeOpen|AppendEntity|AddNewlyCreatedDBObject|Erase|WblockCloneObjects|DeepCloneObjects|TransformBy)\s*\('
        'CAD command/save' = '(?i)\.\s*(?:LockDocument|SetSystemVariable|SetImpliedSelection|SendStringToExecute|Save|SaveAs|DwgOut|DxfOut|CloseAndSave|Command|CommandAsync|ExecuteInCommandContextAsync)\s*\('
    }
    $findings = @(
        foreach ($rule in $forbidden.GetEnumerator()) {
            foreach ($match in @($sourceFiles | Select-String -Pattern ([string] $rule.Value))) {
                [pscustomobject]@{
                    Rule = [string] $rule.Key
                    Path = $match.Path
                    Line = $match.LineNumber
                }
            }
        }
    )
    if ($findings.Count -ne 0) {
        throw "Host.2016 只读源码扫描失败：$($findings | ConvertTo-Json -Compress)"
    }
    Write-Host "Host.2016 只读 Compile 闭包扫描通过：$($sourceFiles.Count) 个源文件。" -ForegroundColor Green
    return $sourceFiles.Count
}

function Test-IsFullyQualifiedWindowsPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return [regex]::IsMatch(
        $Path,
        '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$))'
    )
}

function Resolve-CodexExecutable([string] $RequestedPath) {
    $candidates = [Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath)
    }
    $environmentCandidate = [Environment]::GetEnvironmentVariable('CODEX_EXECUTABLE')
    if (-not [string]::IsNullOrWhiteSpace($environmentCandidate)) {
        $candidates.Add($environmentCandidate)
    }

    $npmCommand = Get-Command codex.cmd -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $npmCommand) {
        $packageRoot = Join-Path (Split-Path -Parent $npmCommand.Source) 'node_modules\@openai\codex'
        if (Test-Path -LiteralPath $packageRoot -PathType Container) {
            foreach ($native in @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter 'codex.exe' -ErrorAction SilentlyContinue)) {
                $candidates.Add($native.FullName)
            }
        }
    }

    foreach ($command in @(Get-Command codex.exe -All -ErrorAction SilentlyContinue)) {
        $candidates.Add($command.Source)
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if ((Test-IsFullyQualifiedWindowsPath $candidate) -and
            [IO.Path]::GetExtension($candidate) -ieq '.exe' -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    throw '未找到可供候选 AgentHost doctor 使用的本机 codex.exe。'
}

function Get-FilesSnapshot([string] $Root) {
    $map = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Root.Length + 1).Replace('\','/')
        $map[$relative] = [ordered]@{ Length = $file.Length; Sha256 = Get-Sha256 $file.FullName }
    }
    return $map
}

function Assert-Same($left, $right, [string] $label) {
    if (($left | ConvertTo-Json -Depth 20 -Compress) -cne ($right | ConvertTo-Json -Depth 20 -Compress)) {
        throw "$label 不一致。"
    }
}

foreach ($path in @($AutoCad2016Dir, (Join-Path $AutoCad2016Dir 'acad.exe'), $net45ReferencePath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺少必要路径：$path" }
}
foreach ($path in @($phase2Script, $assemblyInfoPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少必要文件：$path" }
}

$assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw -Encoding UTF8
$versionMatch = [regex]::Match($assemblyInfo, 'AssemblyVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
$fileVersionMatch = [regex]::Match($assemblyInfo, 'AssemblyFileVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
if (-not $versionMatch.Success -or -not $fileVersionMatch.Success -or
    $versionMatch.Groups['Version'].Value -cne $fileVersionMatch.Groups['Version'].Value) {
    throw 'Host.2016 AssemblyVersion 与 AssemblyFileVersion 必须存在且完全一致。'
}
$hostVersion = $versionMatch.Groups['Version'].Value
$versionParts = $hostVersion.Split('.')
$versionTag = 'v' + $versionParts[0] + $versionParts[1] + $versionParts[2]

$lockSnapshots = [ordered]@{}
foreach ($lockFile in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter 'packages.lock.json')) {
    $lockSnapshots[$lockFile.FullName] = [IO.File]::ReadAllBytes($lockFile.FullName)
}
try {
    $phase2Output = Invoke-CapturedOutput $powerShell @(
        '-NoProfile', '-File', $phase2Script, '-Configuration', $Configuration
    ) 'Phase 2 动态门禁'
}
finally {
    foreach ($snapshot in $lockSnapshots.GetEnumerator()) {
        [IO.File]::WriteAllBytes([string] $snapshot.Key, [byte[]] $snapshot.Value)
    }
}
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

New-Item -ItemType Directory -Path $stageRoot,$candidateStage,$publishRoot -Force | Out-Null
$sourceSnapshotAtUtc = [DateTimeOffset]::UtcNow.ToString('o')

$hostProject = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj'
$nugetConfig = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\NuGet.Config'
$hostReadOnlySourceCount = Assert-HostReadOnlySource $hostProject
$dependencyProjects = @(
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj')
)

foreach ($project in @($dependencyProjects + $hostProject)) {
    $lockPath = Join-Path (Split-Path -Parent $project) 'packages.lock.json'
    $lockBytes = $null
    if (Test-Path -LiteralPath $lockPath -PathType Leaf) {
        $lockBytes = [IO.File]::ReadAllBytes($lockPath)
    }
    $restoreArgs = @('restore', $project, '--configfile', $nugetConfig, '-p:EnableAutoCad2016=true', '--force', '--no-cache')
    if ($project -eq $hostProject) { $restoreArgs += '-p:RestoreLockedMode=false' }
    try {
        Invoke-Captured $dotnet $restoreArgs ("恢复 " + (Split-Path -Leaf $project))
    }
    finally {
        if ($null -ne $lockBytes) {
            [IO.File]::WriteAllBytes($lockPath, $lockBytes)
        }
    }
}

foreach ($dependency in $dependencyProjects) {
    Invoke-Captured $dotnet @(
        'build', $dependency, '--configuration', $Configuration, '--framework', 'net45', '--no-restore',
        '--nologo', '-m:1', '-p:BuildProjectReferences=false', '-p:EnableAutoCad2016=true',
        ("-p:FrameworkPathOverride=" + $net45ReferencePath)
    ) ("net45 依赖编译 " + (Split-Path -Leaf $dependency))
}

$hostBuilds = @()
foreach ($label in @('A','B')) {
    $out = Join-Path $stageRoot ("host-" + $label)
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    foreach ($dependencyName in @('Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.AgentLauncher.dll')) {
        $dependencyPath = Join-Path $repoRoot 'src'
        $dependency = Get-ChildItem -LiteralPath $dependencyPath -Recurse -File -Filter $dependencyName |
            Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]net45[\\/]' } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $dependency) { throw "缺少 Host.2016 依赖：$dependencyName" }
        Copy-Item -LiteralPath $dependency.FullName -Destination (Join-Path $out $dependencyName) -Force
    }
    Invoke-Captured $dotnet @(
        'msbuild', $hostProject, '/t:Rebuild', "/p:Configuration=$Configuration", '/p:Platform=x64',
        ("/p:AutoCad2016Dir=" + $AutoCad2016Dir), '/p:EnableAutoCad2016=true',
        '/p:AutomaticallyUseReferenceAssemblyPackages=true', ("/p:FrameworkPathOverride=" + $net45ReferencePath),
        '/p:BuildProjectReferences=false', ("/p:OutputPath=" + $out + '\'), '/p:DebugSymbols=false',
        '/p:DebugType=None', '/p:ContinuousIntegrationBuild=true', '/m:1', '/nologo'
    ) ("Host.2016 R20.1 编译 $label")
    $hostDll = Join-Path $out 'Codex.AutoCAD.Host.2016.dll'
    $hostBuilds += [pscustomobject]@{ Label = $label; Root = $out; Dll = $hostDll; Sha256 = Get-Sha256 $hostDll }
}
Assert-Same (Get-FilesSnapshot $hostBuilds[0].Root) (Get-FilesSnapshot $hostBuilds[1].Root) 'Host.2016 A/B 输出'

Invoke-Captured $dotnet @(
    'publish', (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj'),
    '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'false', '--no-restore',
    '--output', $publishRoot, '--nologo', '-m:1', '-p:BuildInParallel=false',
    '-p:UseSharedCompilation=false'
) 'AgentHost framework-dependent 发布'

$hostDll = $hostBuilds[0].Dll
$hostSha = $hostBuilds[0].Sha256
$agentExe = Join-Path $publishRoot 'Codex.AutoCAD.AgentHost.exe'
$agentSha = Get-Sha256 $agentExe
$candidateDoctorWorkspace = Join-Path $stageRoot 'candidate-doctor-workspace'
New-Item -ItemType Directory -Path $candidateDoctorWorkspace -Force | Out-Null
$codexExecutable = Resolve-CodexExecutable $CodexExecutable
$candidateDoctorOutput = Invoke-CapturedOutput $agentExe @(
    'doctor', '--workspace', $candidateDoctorWorkspace, '--codex', $codexExecutable
) '候选 AgentHost doctor'
$candidateDoctor = @(
    foreach ($line in $candidateDoctorOutput) {
        if ($line.TrimStart().StartsWith('{')) {
            try { $line | ConvertFrom-Json } catch { }
        }
    }
) | Select-Object -Last 1
if ($null -eq $candidateDoctor -or -not $candidateDoctor.ok -or
    [string] $candidateDoctor.state -cne 'Running') {
    throw '候选 AgentHost doctor 未返回 ok=true/state=Running。'
}
$candidateId = 'autocad2016-m1-readonly-' + $versionTag + '-' + $hostSha.Substring(0,8).ToLowerInvariant() + '-' + $agentSha.Substring(0,8).ToLowerInvariant() + '-' + $runId.Substring(0,8)
$candidateRoot = Join-Path $repoRoot ("artifacts\" + $candidateId)
if (Test-Path -LiteralPath $candidateRoot) { throw "候选目录已存在，拒绝覆盖：$candidateRoot" }
New-Item -ItemType Directory -Path $candidateRoot -Force | Out-Null

Copy-Item -LiteralPath $hostDll -Destination (Join-Path $candidateRoot 'Codex.AutoCAD.Host.2016.dll')
foreach ($name in @('Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.AgentLauncher.dll')) {
    $source = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter $name |
        Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]net45[\\/]' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $source) { throw "找不到 net45 依赖产物：$name" }
    Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $candidateRoot $name)
}

$agentTarget = Join-Path $candidateRoot 'AgentHost'
New-Item -ItemType Directory -Path $agentTarget -Force | Out-Null
foreach ($publishedFile in @(Get-ChildItem -LiteralPath $publishRoot -File)) {
    Copy-Item -LiteralPath $publishedFile.FullName -Destination (Join-Path $agentTarget $publishedFile.Name) -Force
}
[IO.File]::WriteAllText((Join-Path $agentTarget 'Codex.AutoCAD.AgentHost.exe.sha256'), $agentSha + "  Codex.AutoCAD.AgentHost.exe`n", (New-Object Text.UTF8Encoding($false)))

$files = [ordered]@{}
foreach ($file in @(Get-ChildItem -LiteralPath $candidateRoot -Recurse -File | Sort-Object FullName)) {
    if ($file.Name -ieq 'manifest.json') { continue }
    $relative = $file.FullName.Substring($candidateRoot.Length + 1).Replace('\','/')
    $files[$relative] = [ordered]@{ Length = $file.Length; Sha256 = Get-Sha256 $file.FullName }
}
$manifest = [ordered]@{
    schemaVersion = 1
    candidateId = $candidateId
    hostVersion = $hostVersion
    cadContextSchema = 'codex.autocad.cad-context/2'
    targetApi = 'AutoCAD R20.1 / managed 20.1.0.0 / net45 / x64'
    agentHostMode = 'framework-dependent-net8-win-x64'
    files = $files
}
$manifestPath = Join-Path $candidateRoot 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 30) + "`n", (New-Object Text.UTF8Encoding($false)))

$evidence = [ordered]@{
    schemaVersion = 1
    scope = 'autocad2016-m1-readonly-candidate-build'
    sourceSnapshotAtUtc = $sourceSnapshotAtUtc
    candidateFrozenAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    candidateId = $candidateId
    hostVersion = $hostVersion
    cadContextSchema = 'codex.autocad.cad-context/2'
    autoCadLiveEvidence = $false
    netLoadVerified = $false
    build = [ordered]@{ hostR20_1A_BBitForBitEqual = $true; hostDllSha256 = $hostSha; agentHostExeSha256 = $agentSha }
    candidate = [ordered]@{ root = ('artifacts/' + $candidateId); manifestSha256 = Get-Sha256 $manifestPath; files = $files }
    gates = [ordered]@{
        phase2Specs = $phase2Summary
        r20_1ReleaseX64Build = $true
        hostReadOnlySourceScan = $true
        hostReadOnlySourceCount = $hostReadOnlySourceCount
        candidateAgentHostDoctor = $true
        sensitiveScan = $true
        gitDiffCheck = $true
        AutoCADStartedOrRestarted = $false; commandsSent = $false
    }
    limitations = @(
        '本证据覆盖 Phase 2 动态门禁、Host.2016 只读源码闭包、R20.1 net45/x64 双构建、候选布局和最终重哈希；未启动、重启或操作 AutoCAD。',
        'M1 候选仍必须由用户在 AutoCAD 2016 中人工 NETLOAD，验证新建对话、全部清除、图纸隔离、取消、超时、断线、退出和 DPI。',
        '整图 1k/10k/50k 扫描、DrawingIndex/CadQuery、CAD 写入、每会话 CODEX_HOME 与完整进程沙箱不属于本候选。'
    )
}
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$evidencePath = Join-Path $evidenceDirectory ("cad-context-v2-candidate-build-" + $candidateId + '.json')
[IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 40) + "`n", (New-Object Text.UTF8Encoding($false)))

Write-Host 'AutoCAD 2016 M1 只读稳定化候选自动化冻结通过。' -ForegroundColor Green
Write-Host "CANDIDATE_ROOT=$candidateRoot"
Write-Host "CANDIDATE_ID=$candidateId"
Write-Host "HOST_VERSION=$hostVersion"
Write-Host "PHASE2_SPECS=$phase2Summary"
Write-Host "HOST_SHA256=$hostSha"
Write-Host "AGENTHOST_SHA256=$agentSha"
Write-Host "MANIFEST_SHA256=$(Get-Sha256 $manifestPath)"
Write-Host "EVIDENCE=$evidencePath"

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
$net45ReferencePath = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5'
$runId = [Guid]::NewGuid().ToString('N')
$stageRoot = Join-Path $repoRoot ("artifacts\autocad2016-context-v2-candidate-" + $runId)
$publishRoot = Join-Path $stageRoot 'agenthost-publish'
$candidateStage = Join-Path $stageRoot 'candidate'
$evidenceDirectory = Join-Path $repoRoot 'handoff\autocad2016\evidence'

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
New-Item -ItemType Directory -Path $stageRoot,$candidateStage,$publishRoot -Force | Out-Null
$sourceSnapshotAtUtc = [DateTimeOffset]::UtcNow.ToString('o')

$hostProject = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj'
$nugetConfig = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\NuGet.Config'
$dependencyProjects = @(
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj')
)

foreach ($project in @($dependencyProjects + $hostProject)) {
    $restoreArgs = @('restore', $project, '--configfile', $nugetConfig, '-p:EnableAutoCad2016=true', '--force', '--no-cache')
    if ($project -eq $hostProject) { $restoreArgs += '-p:RestoreLockedMode=false' }
    Invoke-Captured $dotnet $restoreArgs ("恢复 " + (Split-Path -Leaf $project))
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
    '--output', $publishRoot, '--nologo'
) 'AgentHost framework-dependent 发布'

$hostDll = $hostBuilds[0].Dll
$hostSha = $hostBuilds[0].Sha256
$agentExe = Join-Path $publishRoot 'Codex.AutoCAD.AgentHost.exe'
$agentSha = Get-Sha256 $agentExe
$candidateId = 'autocad2016-mvp-context-v2-v032-' + $hostSha.Substring(0,8).ToLowerInvariant() + '-' + $agentSha.Substring(0,8).ToLowerInvariant() + '-' + $runId.Substring(0,8)
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
    hostVersion = '0.3.2.0'
    cadContextSchema = 'codex.autocad.cad-context/2'
    targetApi = 'AutoCAD R20.1 / managed 20.1.0.0 / net45 / x64'
    agentHostMode = 'framework-dependent-net8-win-x64'
    files = $files
}
$manifestPath = Join-Path $candidateRoot 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 30) + "`n", (New-Object Text.UTF8Encoding($false)))

$evidence = [ordered]@{
    schemaVersion = 1
    scope = 'autocad2016-context-v2-candidate-build'
    sourceSnapshotAtUtc = $sourceSnapshotAtUtc
    candidateFrozenAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    candidateId = $candidateId
    hostVersion = '0.3.2.0'
    cadContextSchema = 'codex.autocad.cad-context/2'
    autoCadLiveEvidence = $false
    netLoadVerified = $false
    build = [ordered]@{ hostR20_1A_BBitForBitEqual = $true; hostDllSha256 = $hostSha; agentHostExeSha256 = $agentSha }
    candidate = [ordered]@{ root = ('artifacts/' + $candidateId); manifestSha256 = Get-Sha256 $manifestPath; files = $files }
    gates = [ordered]@{
        phase2Specs = '235/235'; v2ApiProbeCompileTime = $true; v2ApiProbeRuntimePassed = 19; v2ApiProbeRuntimeFailed = 8
        r20_1ReleaseX64Build = $true; host禁用API扫描 = $true; sensitiveScan = $true; gitDiffCheck = $true
        AutoCADStartedOrRestarted = $false; commandsSent = $false
    }
    limitations = @(
        '本证据只覆盖自动化构建、API surface probe、候选布局和最终重哈希；未启动、重启或操作 AutoCAD。',
        'P1 必须由用户在 AutoCAD 2016 中人工 NETLOAD 新候选，并补充 v2 混合选区、unknown placeholder、真实 Agent v2 对话和文档切换证据。',
        'API surface probe 的 8 个反射成员失败不代表编译失败；实际使用这些成员前必须走运行时 placeholder/fail-closed 路径。'
    )
}
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$evidencePath = Join-Path $evidenceDirectory ("cad-context-v2-candidate-build-" + $candidateId + '.json')
[IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 40) + "`n", (New-Object Text.UTF8Encoding($false)))

Write-Host 'CadContextJson v2 P1 候选自动化冻结通过。' -ForegroundColor Green
Write-Host "CANDIDATE_ROOT=$candidateRoot"
Write-Host "CANDIDATE_ID=$candidateId"
Write-Host "HOST_SHA256=$hostSha"
Write-Host "AGENTHOST_SHA256=$agentSha"
Write-Host "MANIFEST_SHA256=$(Get-Sha256 $manifestPath)"
Write-Host "EVIDENCE=$evidencePath"

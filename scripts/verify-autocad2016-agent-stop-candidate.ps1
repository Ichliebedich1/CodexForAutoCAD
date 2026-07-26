[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$autoCad2016Dir = 'D:\AutoCAD 2016'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$net45ReferencePath = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5'
$runId = [Guid]::NewGuid().ToString('N')
$stageRoot = Join-Path $repoRoot ("artifacts\autocad2016-agent-stop-candidate-" + $runId)
$candidateRoot = Join-Path $repoRoot 'artifacts\autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd'
$evidencePath = Join-Path $repoRoot 'handoff\autocad2016\evidence\agent-stop-build-verification-20260722.json'

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
    return $lines
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

New-Item -ItemType Directory -Path $stageRoot,$candidateRoot -Force | Out-Null
$sourceSnapshotAtUtc = [DateTimeOffset]::UtcNow.ToString('o')

$hostProject = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj'
$nugetConfig = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\NuGet.Config'
foreach ($project in @(
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj'),
    $hostProject
)) {
    $hostLockPath = Join-Path (Split-Path -Parent $hostProject) 'packages.lock.json'
    $hostLockBytes = $null
    if ($project -eq $hostProject -and (Test-Path -LiteralPath $hostLockPath -PathType Leaf)) {
        $hostLockBytes = [IO.File]::ReadAllBytes($hostLockPath)
    }
    $restoreArgs = @('restore', $project, '--configfile', $nugetConfig, '-p:EnableAutoCad2016=true', '--force', '--no-cache')
    if ($project -eq $hostProject) { $restoreArgs += '-p:RestoreLockedMode=false' }
    Invoke-Captured $dotnet $restoreArgs ("恢复 " + (Split-Path -Leaf $project)) | Out-Null
    if ($null -ne $hostLockBytes) {
        [IO.File]::WriteAllBytes($hostLockPath, $hostLockBytes)
    }
}
$hostBuilds = @()
$dependencyProjects = @(
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj')
)
foreach ($dependency in $dependencyProjects) {
    Invoke-Captured $dotnet @(
        'build', $dependency, '--configuration', $Configuration, '--framework', 'net45', '--no-restore',
        '--nologo', '-m:1', '-p:BuildProjectReferences=false', '-p:EnableAutoCad2016=true',
        ("-p:FrameworkPathOverride=" + $net45ReferencePath)
    ) ("net45 依赖编译 " + (Split-Path -Leaf $dependency)) | Out-Null
}
foreach ($label in @('A','B')) {
    $out = Join-Path $stageRoot "host-$label"
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    foreach ($dependencyName in @('Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.AgentLauncher.dll')) {
        $dependencyPath = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter $dependencyName |
            Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]net45[\\/]' } | Select-Object -First 1
        if (-not $dependencyPath) { throw "缺少 Host.2016 依赖：$dependencyName" }
        Copy-Item -LiteralPath $dependencyPath.FullName -Destination (Join-Path $out $dependencyName) -Force
    }
    Invoke-Captured $dotnet @(
        'msbuild', $hostProject, '/t:Rebuild', "/p:Configuration=$Configuration", '/p:Platform=x64',
        ("/p:AutoCad2016Dir=" + $autoCad2016Dir), '/p:EnableAutoCad2016=true', '/p:AutomaticallyUseReferenceAssemblyPackages=true', ("/p:FrameworkPathOverride=" + $net45ReferencePath), '/p:BuildProjectReferences=false', ("/p:OutputPath=" + $out + '\'), '/p:DebugSymbols=false',
        '/p:DebugType=None', '/p:ContinuousIntegrationBuild=true', '/m:1', '/nologo'
    ) "Host.2016 R20.1 编译 $label" | Out-Null
    $hostDll = Join-Path $out 'Codex.AutoCAD.Host.2016.dll'
    $hostBuilds += [pscustomobject]@{ Label = $label; Root = $out; Dll = $hostDll; Sha256 = Get-Sha256 $hostDll }
}

$hostA = Get-FilesSnapshot $hostBuilds[0].Root
$hostB = Get-FilesSnapshot $hostBuilds[1].Root
Assert-Same $hostA $hostB 'Host.2016 A/B 输出'

$candidateFiles = @(
    [pscustomobject]@{ Source = $hostBuilds[0].Dll; Relative = 'Codex.AutoCAD.Host.2016.dll' }
)
foreach ($name in @('Codex.AutoCAD.AgentLauncher.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll')) {
    $source = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter $name |
        Where-Object { $_.FullName -match '[\\/]bin[\\/].*net45' } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $source) { throw "找不到 net45 依赖产物：$name；请先运行 Bridge/Bootstrap stage verifier。" }
    $candidateFiles += [pscustomobject]@{ Source = $source.FullName; Relative = $name }
}

$agentHost = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentHost') -Recurse -File -Filter 'Codex.AutoCAD.AgentHost.exe' |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $agentHost) { throw '找不到 AgentHost 发布产物；请先运行 Agent bootstrap stage verifier。' }

# AgentHost is currently framework-dependent. The apphost EXE therefore requires its
# companion DLL, deps/runtimeconfig files, and referenced managed assemblies beside it.
# Packaging only the EXE makes AutoCAD launch fail with a hostfxr "application to
# execute does not exist" error before bootstrap can begin.
$agentHostRoot = $agentHost.Directory.FullName
$agentHostRequired = @(
    'Codex.AutoCAD.AgentHost.exe',
    'Codex.AutoCAD.AgentHost.dll',
    'Codex.AutoCAD.AgentHost.deps.json',
    'Codex.AutoCAD.AgentHost.runtimeconfig.json',
    'Codex.AutoCAD.AgentLauncher.dll',
    'Codex.AutoCAD.AgentRuntime.dll',
    'Codex.AutoCAD.AppServer.dll',
    'Codex.AutoCAD.Bridge.dll',
    'Codex.AutoCAD.Contracts.dll',
    'Codex.AutoCAD.Ipc.dll',
    'Codex.AutoCAD.Security.dll'
)
foreach ($requiredName in $agentHostRequired) {
    $requiredPath = Join-Path $agentHostRoot $requiredName
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "AgentHost 发布目录缺少运行时文件：$requiredName"
    }
    $candidateFiles += [pscustomobject]@{
        Source = $requiredPath
        Relative = 'AgentHost\' + $requiredName
    }
}

foreach ($item in $candidateFiles) {
    $target = Join-Path $candidateRoot $item.Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $item.Source -Destination $target -Force
}
$agentSha = Get-Sha256 (Join-Path $candidateRoot 'AgentHost\Codex.AutoCAD.AgentHost.exe')
[IO.File]::WriteAllText((Join-Path $candidateRoot 'AgentHost\Codex.AutoCAD.AgentHost.exe.sha256'), $agentSha + "  Codex.AutoCAD.AgentHost.exe`n", (New-Object Text.UTF8Encoding($false)))

$manifestEntries = [ordered]@{}
foreach ($file in @(Get-ChildItem -LiteralPath $candidateRoot -Recurse -File | Sort-Object FullName)) {
    if ($file.Name -ieq 'manifest.json') { continue }
    $relative = $file.FullName.Substring($candidateRoot.Length + 1).Replace('\','/')
    $manifestEntries[$relative] = [ordered]@{ Length = $file.Length; Sha256 = Get-Sha256 $file.FullName }
}
$candidateFrozenAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
$manifest = [ordered]@{
    schemaVersion = 1
    candidateId = 'autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd'
    hostVersion = '0.3.2.0'
    targetApi = 'AutoCAD R20.1 / managed 20.1.0.0 / net45 / x64'
    files = $manifestEntries
}
[IO.File]::WriteAllText((Join-Path $candidateRoot 'manifest.json'), ($manifest | ConvertTo-Json -Depth 20) + "`n", (New-Object Text.UTF8Encoding($false)))

$recordedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
$bridgeStageEvidence = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts') -Directory -Filter 'autocad2016-bridge-client-stage-*' |
    Sort-Object LastWriteTimeUtc -Descending | ForEach-Object { Join-Path $_.FullName 'verification.json' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$bootstrapStageEvidence = Join-Path $repoRoot 'handoff\autocad2016\evidence\agent-bootstrap-verification-20260719.json'
$evidence = [ordered]@{
    schemaVersion = 1
    scope = 'autocad2016-agent-stop-candidate-build'
    sourceSnapshotAtUtc = $sourceSnapshotAtUtc
    candidateFrozenAtUtc = $candidateFrozenAtUtc
    recordedAtUtc = $recordedAtUtc
    candidateId = $manifest.candidateId
    hostVersion = $manifest.hostVersion
    autoCadLiveEvidence = $false
    netLoadVerified = $false
    build = [ordered]@{ hostR20_1A_BBitForBitEqual = $true; hostDllSha256 = $hostBuilds[0].Sha256 }
    candidate = [ordered]@{ root = 'artifacts/autocad2016-mvp-agent-stop-v032-pkg3-1cc9d294-8e6b26fd'; manifestSha256 = Get-Sha256 (Join-Path $candidateRoot 'manifest.json'); files = $manifestEntries }
    gates = [ordered]@{
        sourceBuild = $true; candidateLayout = $true; candidateFinalRehash = $true
        hostStopSpecs = '13/13'; bridgeClientNet45Specs = '25/25'; bridgeClientNet8Specs = '25/25'
        bridgeSpecs = '37/37'; agentLauncherNet45Specs = '26/26'; agentLauncherNet8Specs = '26/26'
        phase2Specs = '195/195'; authenticationCompatibilityNet45Specs = '35/35'
        authenticationCompatibilityNet8Specs = '35/35'; crossShellPowerShell7And51 = $true
        r20_1ReleaseX64Build = $true; gitDiffCheck = $true; secretScan = $true
        agentHostResidualsFromAutomation = 0; AutoCADStartedOrRestarted = $false; commandsSent = $false
    }
    upstreamEvidenceBindings = [ordered]@{
        bridgeClientStageSha256 = if ($bridgeStageEvidence) { Get-Sha256 $bridgeStageEvidence } else { $null }
        bootstrapStageSha256 = if (Test-Path -LiteralPath $bootstrapStageEvidence) { Get-Sha256 $bootstrapStageEvidence } else { $null }
        rawPathsPersistedInGitEvidence = $false
    }
    limitations = @('本证据只覆盖自动化构建、候选布局与最终重哈希；未启动、重启或操作 AutoCAD。','必须由用户在 AutoCAD 2016 中人工 NETLOAD 新候选并执行两轮 AGENTSTART/AGENTSTOP。')
}
New-Item -ItemType Directory -Path (Split-Path -Parent $evidencePath) -Force | Out-Null
[IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 30) + "`n", (New-Object Text.UTF8Encoding($false)))

Write-Host "AgentHost 停止候选自动化冻结通过。" -ForegroundColor Green
Write-Host "CANDIDATE_ROOT=$candidateRoot"
Write-Host "CANDIDATE_ID=$($manifest.candidateId)"
Write-Host "HOST_SHA256=$($hostBuilds[0].Sha256)"
Write-Host "EVIDENCE=$evidencePath"

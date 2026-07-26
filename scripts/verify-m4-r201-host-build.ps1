[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $AutoCad2016Dir,

    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [string] $EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactsRoot = $buildSafety.ArtifactRoot
$hostProject = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj"
$launcherProject = Join-Path $repoRoot "src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj"
$bridgeClientProject = Join-Path $repoRoot "src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj"
$nugetConfig = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\NuGet.Config"
$bridgeLockPath = Join-Path $repoRoot "src\Codex.AutoCAD.Bridge.Client\packages.lock.json"
$hostLockPath = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\packages.lock.json"
$globalJsonPath = Join-Path $repoRoot "global.json"
$stageRoot = Join-Path $artifactsRoot ("m4-r201-host-" + [Guid]::NewGuid().ToString("N"))
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$autoCadRoot = [IO.Path]::GetFullPath($AutoCad2016Dir)
$apiNames = @("accoremgd.dll", "acdbmgd.dll", "acmgd.dll")
$dependencyNames = @(
    "Codex.AutoCAD.AgentLauncher.dll",
    "Codex.AutoCAD.Bridge.Client.dll",
    "Codex.AutoCAD.Contracts.dll",
    "Codex.AutoCAD.Ipc.dll"
)

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Invoke-Captured {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $Description
    )

    Write-Host ("`n==> " + $Description) -ForegroundColor Cyan
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $raw = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    foreach ($line in $raw) {
        Write-Host ([string] $line)
    }
    if ($exitCode -ne 0) {
        throw "$Description 失败，退出码：$exitCode"
    }
    return @($raw | ForEach-Object { [string] $_ })
}

function Assert-RequiredFile {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少 M4 R20.1 构建输入。"
    }
}

function Get-OutputSnapshot {
    param([Parameter(Mandatory = $true)][string] $Root)
    return @(
        Get-ChildItem -LiteralPath $Root -File | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name
                Length = [long] $_.Length
                Sha256 = Get-Sha256 -Path $_.FullName
            }
        }
    )
}

function Assert-SnapshotsEqual {
    param(
        [Parameter(Mandatory = $true)][object[]] $Left,
        [Parameter(Mandatory = $true)][object[]] $Right
    )
    $leftJson = $Left | ConvertTo-Json -Depth 4 -Compress
    $rightJson = $Right | ConvertTo-Json -Depth 4 -Compress
    if ($leftJson -cne $rightJson) {
        throw "R20.1 Host 两次隔离输出不一致。"
    }
}

function Get-RelevantProcessCount {
    return @(
        Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -ieq "Codex.AutoCAD.AgentHost" -or
            $_.ProcessName -ieq "Codex.AutoCAD.AgentLauncher.FakeAgentHost" -or
            $_.ProcessName -like "CodexLauncherFake-*" -or
            $_.ProcessName -ieq "Codex.AutoCAD.Bridge.Client.TestServer"
        }
    ).Count
}

foreach ($required in @(
    $hostProject,
    $launcherProject,
    $bridgeClientProject,
    $nugetConfig,
    $bridgeLockPath,
    $hostLockPath,
    $globalJsonPath,
    (Join-Path $autoCadRoot "acad.exe")
) + @($apiNames | ForEach-Object { Join-Path $autoCadRoot $_ })) {
    Assert-RequiredFile -Path $required
}

$apiEvidence = [ordered]@{}
foreach ($apiName in $apiNames) {
    $apiPath = Join-Path $autoCadRoot $apiName
    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($apiPath)
    if ($assemblyName.Version.ToString() -cne "20.1.0.0") {
        throw "AutoCAD 托管 API 版本不是目标 R20.1。"
    }
    $apiEvidence[$apiName] = [ordered]@{
        AssemblyVersion = $assemblyName.Version.ToString()
        Sha256 = Get-Sha256 -Path $apiPath
    }
}

$expectedSdk = [string] ((Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json).sdk.version)
$actualSdk = (& $dotnetCommand --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
    throw "需要固定 .NET SDK $expectedSdk。"
}

$processBefore = Get-RelevantProcessCount
if ($processBefore -ne 0) {
    throw "R20.1 构建前存在相关 Agent 测试或宿主进程。"
}
$cadBefore = @(Get-Process -Name acad -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
$bridgeLockBytes = [IO.File]::ReadAllBytes($bridgeLockPath)
$hostLockBytes = [IO.File]::ReadAllBytes($hostLockPath)
$bridgeLockSha256 = Get-Sha256 -Path $bridgeLockPath
$hostLockSha256 = Get-Sha256 -Path $hostLockPath
$previousCliHome = $env:DOTNET_CLI_HOME
$previousAddGlobalTools = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
$previousSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$builds = [System.Collections.Generic.List[object]]::new()

try {
    foreach ($label in @("a", "b")) {
        $buildRoot = Join-Path $stageRoot ("build-" + $label)
        $outputRoot = Join-Path $buildRoot "out"
        $packageRoot = Join-Path $buildRoot "packages"
        $hostOutput = Join-Path $buildRoot "host"
        $env:DOTNET_CLI_HOME = Join-Path $buildRoot "dotnet-home"
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
        $net45ReferencePath = Join-Path $packageRoot "microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5"
        New-Item -ItemType Directory -Path $hostOutput -Force | Out-Null

        try {
            Invoke-Captured -FilePath $dotnetCommand -Arguments @(
                "restore", $launcherProject,
                "--configfile", $nugetConfig,
                "--packages", $packageRoot,
                "--force", "--no-cache",
                "-p:EnableAutoCad2016=true"
            ) -Description ("隔离恢复 AgentLauncher " + $label) | Out-Null
            Invoke-Captured -FilePath $dotnetCommand -Arguments @(
                "restore", $bridgeClientProject,
                "--configfile", $nugetConfig,
                "--packages", $packageRoot,
                "--force", "--no-cache",
                "-p:EnableAutoCad2016=true"
            ) -Description ("隔离恢复 Bridge.Client " + $label) | Out-Null
            Invoke-Captured -FilePath $dotnetCommand -Arguments @(
                "restore", $hostProject,
                "--configfile", $nugetConfig,
                "--packages", $packageRoot,
                "--force-evaluate", "--force", "--no-cache",
                "-p:RestoreLockedMode=false",
                "-p:EnableAutoCad2016=true"
            ) -Description ("隔离恢复 Host.2016 " + $label) | Out-Null
            # Legacy Host restore evaluates SDK project references without reliably
            # forwarding the conditional net45 target. Refresh the two dependency
            # roots afterwards so their project.assets.json files remain dual-target.
            foreach ($dependencyRoot in @($launcherProject, $bridgeClientProject)) {
                Invoke-Captured -FilePath $dotnetCommand -Arguments @(
                    "restore", $dependencyRoot,
                    "--configfile", $nugetConfig,
                    "--packages", $packageRoot,
                    "--force", "--no-cache",
                    "-p:EnableAutoCad2016=true"
                ) -Description ("刷新 net45 依赖资产 " + (Split-Path -Leaf $dependencyRoot) + " " + $label) | Out-Null
            }
        }
        finally {
            [IO.File]::WriteAllBytes($bridgeLockPath, $bridgeLockBytes)
            [IO.File]::WriteAllBytes($hostLockPath, $hostLockBytes)
        }

        foreach ($project in @($launcherProject, $bridgeClientProject)) {
            Invoke-Captured -FilePath $dotnetCommand -Arguments @(
                "build", $project,
                "--configuration", $Configuration,
                "--framework", "net45",
                "--no-restore", "--nologo", "--disable-build-servers", "-m:1",
                "-p:EnableAutoCad2016=true",
                "-p:UseSharedCompilation=false",
                ("-p:FrameworkPathOverride=" + $net45ReferencePath),
                "-p:ContinuousIntegrationBuild=true",
                "-warnaserror"
            ) -Description ("构建 net45 依赖 " + (Split-Path -Leaf $project) + " " + $label) | Out-Null
        }

        foreach ($dependencyName in $dependencyNames) {
            $candidate = @(
                Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") -Recurse -File -Filter $dependencyName |
                    Where-Object { $_.FullName -match "[\\/]bin[\\/]Release[\\/]net45[\\/]" } |
                    Sort-Object FullName
            ) | Select-Object -First 1
            if ($null -eq $candidate) {
                throw "缺少 R20.1 Host net45 依赖输出。"
            }
            Copy-Item -LiteralPath $candidate.FullName -Destination (Join-Path $hostOutput $dependencyName) -Force
        }

        Invoke-Captured -FilePath $dotnetCommand -Arguments @(
            "msbuild", $hostProject,
            "/t:Rebuild",
            ("/p:Configuration=" + $Configuration),
            "/p:Platform=x64",
            ("/p:AutoCad2016Dir=" + $autoCadRoot),
            "/p:EnableAutoCad2016=true",
            "/p:AutomaticallyUseReferenceAssemblyPackages=true",
            ("/p:FrameworkPathOverride=" + $net45ReferencePath),
            "/p:BuildProjectReferences=false",
            ("/p:OutputPath=" + $hostOutput + "\"),
            "/p:DebugSymbols=false",
            "/p:DebugType=None",
            "/p:ContinuousIntegrationBuild=true",
            "/m:1", "/nr:false", "/nologo", "/warnaserror"
        ) -Description ("R20.1 Host.2016 Release 构建 " + $label) | Out-Null

        foreach ($apiName in $apiNames) {
            if (Test-Path -LiteralPath (Join-Path $hostOutput $apiName)) {
                throw "R20.1 Host 输出包含禁止复制的 Autodesk 程序集。"
            }
        }
        $hostDll = Join-Path $hostOutput "Codex.AutoCAD.Host.2016.dll"
        Assert-RequiredFile -Path $hostDll
        $hostIdentity = [Reflection.AssemblyName]::GetAssemblyName($hostDll)
        if ($hostIdentity.Name -cne "Codex.AutoCAD.Host.2016") {
            throw "R20.1 Host 程序集身份不匹配。"
        }
        $builds.Add([pscustomobject]@{
            HostDll = $hostDll
            HostSha256 = Get-Sha256 -Path $hostDll
            HostSize = [long] (Get-Item -LiteralPath $hostDll).Length
            HostVersion = $hostIdentity.Version.ToString()
            Snapshot = @(Get-OutputSnapshot -Root $hostOutput)
        })
    }

    Assert-SnapshotsEqual -Left $builds[0].Snapshot -Right $builds[1].Snapshot
    if ($builds[0].HostSha256 -cne $builds[1].HostSha256) {
        throw "R20.1 Host 两次隔离构建哈希不一致。"
    }
    if ((Get-Sha256 -Path $bridgeLockPath) -cne $bridgeLockSha256 -or
        (Get-Sha256 -Path $hostLockPath) -cne $hostLockSha256) {
        throw "R20.1 构建修改了跟踪的锁文件。"
    }
    $processAfter = Get-RelevantProcessCount
    if ($processAfter -ne 0) {
        throw "R20.1 构建后存在相关 Agent 测试或宿主残留进程。"
    }
    $cadAfter = @(Get-Process -Name acad -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
    if (($cadBefore -join ",") -cne ($cadAfter -join ",")) {
        throw "R20.1 构建期间 AutoCAD 进程集合发生变化。"
    }

    if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
        $EvidencePath = Join-Path $stageRoot "verification.json"
    }
    $resolvedEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) {
        [IO.Path]::GetFullPath($EvidencePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidencePath))
    }
    $evidenceParent = Split-Path -Parent $resolvedEvidencePath
    New-Item -ItemType Directory -Path $evidenceParent -Force | Out-Null
    $evidence = [ordered]@{
        SchemaVersion = 1
        RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
        Scope = "m4-r201-host-build-gate"
        Status = "automated-r201-build-passed"
        PowerShellEdition = [string] $PSVersionTable.PSEdition
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        DotNetSdk = $actualSdk
        Configuration = $Configuration
        TargetFramework = ".NETFramework,Version=v4.5"
        Architecture = "x64"
        AutoCadManagedApiVersion = "20.1.0.0"
        AutoCadManagedApis = $apiEvidence
        HostAssemblyVersion = $builds[0].HostVersion
        HostCandidateSha256 = $builds[0].HostSha256
        HostCandidateSize = $builds[0].HostSize
        IsolatedBuildCount = 2
        BitForBitMatch = $true
        OutputFileCount = $builds[0].Snapshot.Count
        AutodeskDllCopiedCount = 0
        BridgeClientLockSha256 = $bridgeLockSha256
        HostLockSha256 = $hostLockSha256
        LockFilesRestored = $true
        ReleaseWarnings = 0
        ReleaseErrors = 0
        RelevantProcessBaselineCount = $processBefore
        RelevantProcessFinalCount = $processAfter
        ResidualAgentProcesses = $false
        AutoCadProcessSetChanged = $false
        AutoCadStartedOrCommanded = $false
        CadWriteEnabled = $false
        PluginInitiatedSaveEnabled = $false
        NetLoadVerified = $false
        RuntimeVerified = $false
        EnterpriseMatrixVerified = $false
        EvidenceBoundary = "This evidence proves two isolated, bit-for-bit identical Release/x64 builds of the current Host.2016 source against the installed R20.1 managed API identities, with warnings treated as errors, no copied Autodesk assemblies, restored lock files, and no relevant residual Agent processes. It does not start or command AutoCAD, verify NETLOAD or runtime behavior, enable CAD writes or saves, verify enterprise policy behavior, or freeze M4.16."
    }
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($resolvedEvidencePath, ($evidence | ConvertTo-Json -Depth 10), $encoding)
    Write-Host "`nM4 R20.1 Host 自动化构建门禁通过。" -ForegroundColor Green
    Write-Host ("M4_R201_HOST_EVIDENCE=" + $resolvedEvidencePath)
}
finally {
    Complete-CodexBuildSafety -State $buildSafety -Stage "m4-r201-host-build" | Out-Null
    [IO.File]::WriteAllBytes($bridgeLockPath, $bridgeLockBytes)
    [IO.File]::WriteAllBytes($hostLockPath, $hostLockBytes)
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $previousAddGlobalTools
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTime
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
}

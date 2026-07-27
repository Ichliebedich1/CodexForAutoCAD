[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [string] $EvidencePath,

    [ValidateRange(0, 40)]
    [double] $MinimumFreeGiB = 40
)

# This file is intentionally ASCII so Windows PowerShell 5.1 can parse it without
# relying on the machine's legacy code page.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot `
    -MinimumFreeGiB $MinimumFreeGiB
$artifactRoot = $buildSafety.ArtifactRoot
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$globalJsonPath = Join-Path $repoRoot "global.json"
$nugetConfigPath = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\NuGet.Config"
$offlinePackagePath = Join-Path $repoRoot `
    "third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg"
$expectedOfflinePackageSha256 = `
    "23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3"
$stageRoot = Join-Path $artifactRoot ("m9-net45-x64-" + [Guid]::NewGuid().ToString("N"))
$packagesRoot = Join-Path $stageRoot "packages"
$httpCacheRoot = Join-Path $stageRoot "nuget-http-cache"
$cliHome = Join-Path $stageRoot "dotnet-home"
$artifactsPath = Join-Path $stageRoot "artifacts"
$lockRoot = Join-Path $stageRoot "locks"
$lockFileTemplate = Join-Path $lockRoot '$(MSBuildProjectName).packages.lock.json'

$projects = [ordered]@{
    "Codex.AutoCAD.AgentLauncher" =
        "src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj"
    "Codex.AutoCAD.Bridge.Client" =
        "src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj"
}
$expectedAssemblies = @(
    "Codex.AutoCAD.AgentLauncher",
    "Codex.AutoCAD.Bridge.Client",
    "Codex.AutoCAD.Contracts",
    "Codex.AutoCAD.Ipc"
)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $Description
    )

    Write-Host ("`n==> " + $Description) -ForegroundColor Cyan
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    foreach ($line in $output) {
        Write-Host ([string] $line)
    }
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-SourceLockSnapshot {
    $snapshot = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") `
            -Recurse -File -Filter "packages.lock.json" | Sort-Object FullName)) {
        $relativePath = $file.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")
        $snapshot[$relativePath] = Get-Sha256 -Path $file.FullName
    }
    return $snapshot
}

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [IO.File]::Open(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        if ($stream.Length -lt 256) {
            throw "Managed output is too small to contain a valid PE header."
        }
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) {
                throw "Managed output does not have an MZ header."
            }
            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            if ($peOffset -lt 0x40 -or ($peOffset + 6) -gt $stream.Length) {
                throw "Managed output has an invalid PE header offset."
            }
            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "Managed output does not have a PE signature."
            }
            return $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Resolve-EvidencePath {
    param([string] $RequestedPath)

    if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        return Join-Path $stageRoot "verification.json"
    }
    $resolved = if ([IO.Path]::IsPathRooted($RequestedPath)) {
        [IO.Path]::GetFullPath($RequestedPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $artifactRoot $RequestedPath))
    }
    $rootWithSeparator = $artifactRoot.TrimEnd([char[]]@('\', '/')) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith(
            $rootWithSeparator,
            [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetExtension($resolved) -cne ".json") {
        throw "Evidence must be a JSON file inside the build-safety artifact root."
    }
    return $resolved
}

$requiredInputs = @(
    $globalJsonPath,
    $nugetConfigPath,
    $offlinePackagePath
) + @($projects.Values | ForEach-Object { Join-Path $repoRoot $_ })
foreach ($requiredInput in $requiredInputs) {
    if (-not (Test-Path -LiteralPath $requiredInput -PathType Leaf)) {
        throw "A required net45/x64 CI input is missing."
    }
}

$expectedSdk = [string] (
    (Get-Content -LiteralPath $globalJsonPath -Raw -Encoding UTF8 |
        ConvertFrom-Json -ErrorAction Stop).sdk.version)
$actualSdk = (& $dotnetCommand --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
    throw "The resolved .NET SDK does not match global.json."
}
if ((Get-Sha256 -Path $offlinePackagePath) -cne $expectedOfflinePackageSha256) {
    throw "The offline net45 reference package does not match its reviewed SHA-256."
}

$nugetConfigText = Get-Content -LiteralPath $nugetConfigPath -Raw -Encoding UTF8
if ($nugetConfigText -notmatch "<clear\s*/>" -or
    $nugetConfigText -notmatch "third_party[\\/]nuget" -or
    $nugetConfigText -notmatch "signatureValidationMode") {
    throw "The explicit NuGet configuration is not the reviewed offline configuration."
}
$sourceLocksBefore = Get-SourceLockSnapshot

$previousCliHome = $env:DOTNET_CLI_HOME
$previousAddGlobalTools = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
$previousSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$previousNoLogo = $env:DOTNET_NOLOGO
$previousPackages = $env:NUGET_PACKAGES
$previousHttpCache = $env:NUGET_HTTP_CACHE_PATH
$cadBefore = @(Get-Process -Name acad -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Id | Sort-Object)
$outputs = [ordered]@{}

try {
    $env:DOTNET_CLI_HOME = $cliHome
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_NOLOGO = "1"
    $env:NUGET_PACKAGES = $packagesRoot
    $env:NUGET_HTTP_CACHE_PATH = $httpCacheRoot
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $lockRoot -Force | Out-Null

    foreach ($entry in $projects.GetEnumerator()) {
        $projectPath = Join-Path $repoRoot ([string] $entry.Value)
        Invoke-CheckedCommand -FilePath $dotnetCommand -Arguments @(
            "restore", $projectPath,
            "--configfile", $nugetConfigPath,
            "--packages", $packagesRoot,
            "--force", "--no-cache",
            "-p:EnableAutoCad2016=true",
            "-p:TargetFramework=net45",
            "-p:RestorePackagesWithLockFile=true",
            ("-p:NuGetLockFilePath=" + $lockFileTemplate),
            "-p:UseArtifactsOutput=true",
            ("-p:ArtifactsPath=" + $artifactsPath)
        ) -Description ("Restore " + [string] $entry.Key + " net45 from the offline feed")

        Invoke-CheckedCommand -FilePath $dotnetCommand -Arguments @(
            "build", $projectPath,
            "--framework", "net45",
            "--configuration", $Configuration,
            "--no-restore", "--nologo", "--disable-build-servers", "-m:1",
            "-p:EnableAutoCad2016=true",
            "-p:PlatformTarget=x64",
            "-p:ContinuousIntegrationBuild=true",
            "-p:UseArtifactsOutput=true",
            ("-p:ArtifactsPath=" + $artifactsPath),
            "-warnaserror"
        ) -Description ("Build " + [string] $entry.Key + " as net45/x64")
    }

    foreach ($assemblyName in $expectedAssemblies) {
        $assemblyPath = Join-Path $artifactsPath (
            "bin\" + $assemblyName + "\release_net45\" + $assemblyName + ".dll")
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "An expected net45/x64 managed output is missing."
        }
        $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
        if ($identity.Name -cne $assemblyName) {
            throw "A net45/x64 managed output has an unexpected assembly identity."
        }
        $machine = Get-PeMachine -Path $assemblyPath
        if ($machine -ne 0x8664) {
            throw "A net45 output is not an AMD64 PE image."
        }
        $outputs[$assemblyName] = [ordered]@{
            Sha256 = Get-Sha256 -Path $assemblyPath
            Bytes = (Get-Item -LiteralPath $assemblyPath).Length
            PeMachine = "0x8664"
        }
    }
    $sourceLocksAfter = Get-SourceLockSnapshot
    if (($sourceLocksBefore | ConvertTo-Json -Depth 4 -Compress) -cne
        ($sourceLocksAfter | ConvertTo-Json -Depth 4 -Compress)) {
        throw "Source-tree NuGet lock files changed during the isolated net45/x64 gate."
    }

    Invoke-CheckedCommand -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $repoRoot.Replace("\", "/")),
        "-C", $repoRoot, "diff", "--check"
    ) -Description "Check unstaged diff formatting"
    Invoke-CheckedCommand -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $repoRoot.Replace("\", "/")),
        "-C", $repoRoot, "diff", "--cached", "--check"
    ) -Description "Check staged diff formatting"

    $cadAfter = @(Get-Process -Name acad -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id | Sort-Object)
    if (($cadBefore -join ",") -cne ($cadAfter -join ",")) {
        throw "The AutoCAD process set changed during the net45/x64 CI gate."
    }

    $resolvedEvidencePath = Resolve-EvidencePath -RequestedPath $EvidencePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) -Force |
        Out-Null
    $evidence = [ordered]@{
        Schema = "codex.autocad.m9-net45-x64/1"
        Status = "net45_x64_build_verified"
        RunCorrelationId = Get-CodexGateRunCorrelationId
        DotNetSdk = $actualSdk
        Configuration = $Configuration
        TargetFramework = "net45"
        Architecture = "x64"
        ExplicitOfflineNuGetConfig = $true
        UserNuGetConfigRead = $false
        LockFilesIsolatedToArtifactRoot = $true
        OfflinePackageSha256 = $expectedOfflinePackageSha256
        Outputs = $outputs
        AutoCadStartedOrCommanded = $false
        CadWriteEnabled = $false
        RemoteWorkflowRunVerified = $false
        EvidenceBoundary = "This evidence proves isolated net45/x64 builds of the AutoCAD-compatible managed launcher, bridge client, contracts, and IPC assemblies using the reviewed offline NuGet configuration. It does not build the Autodesk-dependent Host.2016 assembly, start AutoCAD, run NETLOAD, verify a GitHub-hosted workflow run, or enable CAD writes."
    }
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText(
        $resolvedEvidencePath,
        ($evidence | ConvertTo-Json -Depth 8),
        $utf8)
    Write-Host ("M9_NET45_X64_EVIDENCE=" + $resolvedEvidencePath)
    Write-Host "M9 net45/x64 CI gate passed." -ForegroundColor Green
}
finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $previousAddGlobalTools
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTime
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    $env:DOTNET_NOLOGO = $previousNoLogo
    $env:NUGET_PACKAGES = $previousPackages
    $env:NUGET_HTTP_CACHE_PATH = $previousHttpCache
    Complete-CodexBuildSafety -State $buildSafety -Stage "m9-net45-x64" | Out-Null
}

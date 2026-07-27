[CmdletBinding()]
param(
    [string] $AutoCad2016Dir,
    [string] $EvidencePath,
    [switch] $SkipR201BinaryProbe,
    [switch] $ValidationOnly,
    [switch] $SelfTestOnly,
    [ValidateRange(0, 40)]
    [double] $MinimumFreeGiB = 40
)

# This file is intentionally ASCII so Windows PowerShell 5.1 can parse it
# without relying on the machine legacy code page.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot `
    -MinimumFreeGiB $MinimumFreeGiB
$artifactRoot = $buildSafety.ArtifactRoot
$lockPath = Join-Path $repoRoot "eng\toolchain-lock.json"
$globalJsonPath = Join-Path $repoRoot "global.json"
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$stageRoot = Join-Path $artifactRoot (
    "m9-toolchain-" + [Guid]::NewGuid().ToString("N"))
$strictUtf8 = New-Object Text.UTF8Encoding($false, $true)

$expectedNuGetInputs = @(
    "src/Codex.AutoCAD.Bridge.Client/packages.lock.json",
    "src/Codex.AutoCAD.Host.2016.Palette/NuGet.Config",
    "src/Codex.AutoCAD.Host.2016.Palette/packages.lock.json",
    "src/Codex.AutoCAD.Host.2016.ReadOnlyContext/NuGet.Config",
    "src/Codex.AutoCAD.Host.2016.ReadOnlyContext/packages.lock.json",
    "src/Codex.AutoCAD.Host.2016/NuGet.Config",
    "src/Codex.AutoCAD.Host.2016/packages.lock.json",
    "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/NuGet.Config",
    "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/packages.lock.json"
)
$expectedProbeSources = @(
    "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/Codex.AutoCAD.Host.2016.V2ApiProbe.csproj",
    "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/Properties/AssemblyInfo.cs",
    "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/V2ApiSurfaceProbe.cs"
)
$expectedR201Binaries = [ordered]@{
    "acad.exe" = ""
    "accoremgd.dll" =
        "accoremgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null"
    "acdbmgd.dll" =
        "Acdbmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null"
    "acmgd.dll" =
        "Acmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null"
}

function Read-StrictUtf8Text {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][int] $MinimumBytes,
        [Parameter(Mandatory = $true)][int] $MaximumBytes
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "A required toolchain lock input is missing."
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Toolchain lock inputs must be ordinary files."
    }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt $MinimumBytes -or $bytes.Length -gt $MaximumBytes) {
        throw "A toolchain lock input has an invalid size."
    }
    try {
        $text = $strictUtf8.GetString($bytes)
    }
    catch {
        throw "A toolchain lock input is not strict UTF-8."
    }
    if ($text.Length -gt 0 -and $text[0] -eq [char] 0xFEFF) {
        $text = $text.Substring(1)
    }
    return $text
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory = $true)] $Value,
        [Parameter(Mandatory = $true)][string[]] $Expected,
        [Parameter(Mandatory = $true)][string] $Context
    )

    if ($null -eq $Value -or $Value -is [string] -or
        $Value -is [System.Collections.IEnumerable]) {
        throw "$Context must be a JSON object."
    }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if (($actual -join "`n") -cne ($wanted -join "`n")) {
        throw "$Context contains missing or unknown properties."
    }
}

function Assert-UpperSha256 {
    param(
        [Parameter(Mandatory = $true)][string] $Value,
        [Parameter(Mandatory = $true)][string] $Context
    )

    if ($Value -cnotmatch "^[0-9A-F]{64}$" -or
        @($Value.ToCharArray() | Select-Object -Unique).Count -lt 2) {
        throw "$Context is not a reviewed SHA-256."
    }
}

function Assert-PositiveJsonInteger {
    param(
        [Parameter(Mandatory = $true)] $Value,
        [Parameter(Mandatory = $true)][string] $Context
    )

    if (-not ($Value -is [int] -or $Value -is [long]) -or
        [long] $Value -le 0) {
        throw "$Context must be a positive JSON integer."
    }
}

function Assert-RelativeLockPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Context
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path -cnotmatch "^[A-Za-z0-9._/-]+$" -or
        $Path.Contains("\") -or
        [IO.Path]::IsPathRooted($Path) -or
        @($Path.Split("/") | Where-Object {
                $_ -eq "." -or $_ -eq ".." -or $_.Length -eq 0
            }).Count -ne 0) {
        throw "$Context is not a canonical repository-relative path."
    }
}

function Assert-FileEntry {
    param(
        [Parameter(Mandatory = $true)] $Entry,
        [Parameter(Mandatory = $true)][string] $Context
    )

    Assert-ExactProperties $Entry @("Path", "Bytes", "Sha256") $Context
    Assert-RelativeLockPath ([string] $Entry.Path) "$Context.Path"
    Assert-PositiveJsonInteger $Entry.Bytes "$Context.Bytes"
    Assert-UpperSha256 ([string] $Entry.Sha256) "$Context.Sha256"
}

function Assert-OrderedPaths {
    param(
        [Parameter(Mandatory = $true)] $Entries,
        [Parameter(Mandatory = $true)][string[]] $Expected,
        [Parameter(Mandatory = $true)][string] $Context
    )

    $items = @($Entries)
    if ($items.Count -ne $Expected.Count) {
        throw "$Context count does not match the reviewed lock."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert-FileEntry $items[$index] "$Context[$index]"
        if ([string] $items[$index].Path -cne $Expected[$index]) {
            throw "$Context path order or membership changed."
        }
    }
}

function Assert-ToolchainLockDocument {
    param([Parameter(Mandatory = $true)] $Document)

    Assert-ExactProperties $Document `
        @("Schema", "DotNet", "OfflinePackage", "NuGet", "R201Probe") `
        "ToolchainLock"
    if ([string] $Document.Schema -cne "codex.autocad.toolchain-lock/1") {
        throw "Toolchain lock schema is not supported."
    }

    Assert-ExactProperties $Document.DotNet `
        @("SdkVersion", "RollForward", "AllowPrerelease",
            "NuGetVersion", "MsBuildVersion") `
        "ToolchainLock.DotNet"
    if ([string] $Document.DotNet.SdkVersion -cne "8.0.319" -or
        [string] $Document.DotNet.RollForward -cne "disable" -or
        -not ($Document.DotNet.AllowPrerelease -is [bool]) -or
        [bool] $Document.DotNet.AllowPrerelease -or
        [string] $Document.DotNet.NuGetVersion -cne "6.10.2.8" -or
        [string] $Document.DotNet.MsBuildVersion -cne "17.10.46.46604") {
        throw "The reviewed .NET SDK, NuGet, or MSBuild identity changed."
    }

    Assert-ExactProperties $Document.OfflinePackage `
        @("Path", "Id", "Version", "Bytes", "Sha256",
            "AuthorCertificateSha256", "RepositoryCertificateSha256") `
        "ToolchainLock.OfflinePackage"
    Assert-RelativeLockPath ([string] $Document.OfflinePackage.Path) `
        "ToolchainLock.OfflinePackage.Path"
    if ([string] $Document.OfflinePackage.Path -cne
        "third_party/nuget/Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg" -or
        [string] $Document.OfflinePackage.Id -cne
        "Microsoft.NETFramework.ReferenceAssemblies.net45" -or
        [string] $Document.OfflinePackage.Version -cne "1.0.3") {
        throw "The offline net45 package identity changed."
    }
    Assert-PositiveJsonInteger $Document.OfflinePackage.Bytes `
        "ToolchainLock.OfflinePackage.Bytes"
    Assert-UpperSha256 ([string] $Document.OfflinePackage.Sha256) `
        "ToolchainLock.OfflinePackage.Sha256"
    Assert-UpperSha256 `
        ([string] $Document.OfflinePackage.AuthorCertificateSha256) `
        "ToolchainLock.OfflinePackage.AuthorCertificateSha256"
    Assert-UpperSha256 `
        ([string] $Document.OfflinePackage.RepositoryCertificateSha256) `
        "ToolchainLock.OfflinePackage.RepositoryCertificateSha256"

    Assert-ExactProperties $Document.NuGet @("Inputs") "ToolchainLock.NuGet"
    Assert-OrderedPaths $Document.NuGet.Inputs $expectedNuGetInputs `
        "ToolchainLock.NuGet.Inputs"

    Assert-ExactProperties $Document.R201Probe `
        @("Target", "SourceInputs", "BinaryInputs") `
        "ToolchainLock.R201Probe"
    if ([string] $Document.R201Probe.Target -cne
        "AutoCAD 2016 R20.1 / managed 20.1.0.0 / net45 / x64") {
        throw "The R20.1 probe target changed."
    }
    Assert-OrderedPaths $Document.R201Probe.SourceInputs $expectedProbeSources `
        "ToolchainLock.R201Probe.SourceInputs"

    $binaryInputs = @($Document.R201Probe.BinaryInputs)
    $expectedBinaryNames = @($expectedR201Binaries.Keys)
    if ($binaryInputs.Count -ne $expectedBinaryNames.Count) {
        throw "The R20.1 binary input count changed."
    }
    for ($index = 0; $index -lt $expectedBinaryNames.Count; $index++) {
        $entry = $binaryInputs[$index]
        $name = $expectedBinaryNames[$index]
        Assert-ExactProperties $entry `
            @("Name", "Bytes", "Sha256", "AssemblyFullName",
                "AuthenticodeStatus", "SignerThumbprint") `
            "ToolchainLock.R201Probe.BinaryInputs[$index]"
        if ([string] $entry.Name -cne $name -or
            [string] $entry.AssemblyFullName -cne
                [string] $expectedR201Binaries[$name] -or
            [string] $entry.AuthenticodeStatus -cne "Valid") {
            throw "An R20.1 binary identity changed."
        }
        Assert-PositiveJsonInteger $entry.Bytes `
            "ToolchainLock.R201Probe.BinaryInputs[$index].Bytes"
        Assert-UpperSha256 ([string] $entry.Sha256) `
            "ToolchainLock.R201Probe.BinaryInputs[$index].Sha256"
        if ([string] $entry.SignerThumbprint -cnotmatch "^[0-9A-F]{40}$") {
            throw "An R20.1 signer thumbprint is invalid."
        }
    }
}

function Copy-JsonDocument {
    param([Parameter(Mandatory = $true)] $Document)
    return ($Document | ConvertTo-Json -Depth 20 |
        ConvertFrom-Json -ErrorAction Stop)
}

function Assert-MutationRejected {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)] $Document,
        [Parameter(Mandatory = $true)][scriptblock] $Mutation
    )

    $copy = Copy-JsonDocument $Document
    & $Mutation $copy
    try {
        Assert-ToolchainLockDocument $copy
    }
    catch {
        return
    }
    throw "Toolchain lock self-test mutation was accepted: $Name."
}

function Invoke-ToolchainLockSelfTest {
    param([Parameter(Mandatory = $true)] $Document)

    $cases = @(
        @{ Name = "schema"; Mutate = { param($d) $d.Schema = "other" } },
        @{ Name = "top-extra"; Mutate = {
                param($d)
                $d | Add-Member -NotePropertyName Extra -NotePropertyValue 1
            } },
        @{ Name = "sdk"; Mutate = {
                param($d) $d.DotNet.SdkVersion = "8.0.318"
            } },
        @{ Name = "roll-forward"; Mutate = {
                param($d) $d.DotNet.RollForward = "latestPatch"
            } },
        @{ Name = "allow-prerelease-type"; Mutate = {
                param($d) $d.DotNet.AllowPrerelease = "false"
            } },
        @{ Name = "nuget-version"; Mutate = {
                param($d) $d.DotNet.NuGetVersion = "6.10.2.7"
            } },
        @{ Name = "msbuild-version"; Mutate = {
                param($d) $d.DotNet.MsBuildVersion = "17.10.46"
            } },
        @{ Name = "offline-path"; Mutate = {
                param($d) $d.OfflinePackage.Path = "../package.nupkg"
            } },
        @{ Name = "offline-bytes-type"; Mutate = {
                param($d) $d.OfflinePackage.Bytes = "19820444"
            } },
        @{ Name = "placeholder-hash"; Mutate = {
                param($d) $d.OfflinePackage.Sha256 = ("A" * 64)
            } },
        @{ Name = "missing-nuget-input"; Mutate = {
                param($d)
                $d.NuGet.Inputs = @($d.NuGet.Inputs | Select-Object -Skip 1)
            } },
        @{ Name = "duplicate-nuget-input"; Mutate = {
                param($d)
                $d.NuGet.Inputs[1].Path = $d.NuGet.Inputs[0].Path
            } },
        @{ Name = "nuget-input-extra"; Mutate = {
                param($d)
                $d.NuGet.Inputs[0] |
                    Add-Member -NotePropertyName Extra -NotePropertyValue 1
            } },
        @{ Name = "probe-source"; Mutate = {
                param($d)
                $d.R201Probe.SourceInputs[0].Path = "tests/other.csproj"
            } },
        @{ Name = "probe-source-hash"; Mutate = {
                param($d)
                $d.R201Probe.SourceInputs[0].Sha256 = ("0" * 64)
            } },
        @{ Name = "binary-name"; Mutate = {
                param($d) $d.R201Probe.BinaryInputs[0].Name = "other.exe"
            } },
        @{ Name = "binary-assembly"; Mutate = {
                param($d)
                $d.R201Probe.BinaryInputs[1].AssemblyFullName = "other"
            } },
        @{ Name = "binary-signature"; Mutate = {
                param($d)
                $d.R201Probe.BinaryInputs[1].AuthenticodeStatus = "UnknownError"
            } }
    )
    foreach ($case in $cases) {
        Assert-MutationRejected ([string] $case.Name) $Document `
            ([scriptblock] $case.Mutate)
    }
    return $cases.Count
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).
        Hash.ToUpperInvariant()
}

function Resolve-RepositoryFile {
    param([Parameter(Mandatory = $true)][string] $RelativePath)

    Assert-RelativeLockPath $RelativePath "Repository input path"
    $path = [IO.Path]::GetFullPath(
        (Join-Path $repoRoot $RelativePath.Replace("/", "\")))
    $rootPrefix = $repoRoot.TrimEnd("\", "/") +
        [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith(
            $rootPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "A toolchain input escaped the repository root."
    }
    return $path
}

function Assert-FileMatchesLock {
    param(
        [Parameter(Mandatory = $true)] $Entry,
        [Parameter(Mandatory = $true)][string] $Context
    )

    $path = Resolve-RepositoryFile ([string] $Entry.Path)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "$Context is missing."
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        [long] $item.Length -ne [long] $Entry.Bytes -or
        (Get-Sha256 $path) -cne [string] $Entry.Sha256) {
        throw "$Context does not match the reviewed toolchain lock."
    }
    return $path
}

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $Description
    )

    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $FilePath @Arguments 2>&1 |
            ForEach-Object { [string] $_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
        Text = ($output -join "`n")
    }
}

function Get-ExactVersionLine {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Context
    )

    $matches = [regex]::Matches(
        $Text,
        "(?m)^\s*(?<Version>\d+\.\d+\.\d+(?:\.\d+)?)\s*$")
    if ($matches.Count -ne 1) {
        throw "$Context did not report one exact version."
    }
    return [string] $matches[0].Groups["Version"].Value
}

function Get-SourceLockSnapshot {
    param([Parameter(Mandatory = $true)] $Entries)

    $snapshot = [ordered]@{}
    foreach ($entry in @($Entries)) {
        if ([string] $entry.Path -like "*/packages.lock.json") {
            $path = Resolve-RepositoryFile ([string] $entry.Path)
            $snapshot[[string] $entry.Path] = Get-Sha256 $path
        }
    }
    return ($snapshot | ConvertTo-Json -Depth 4 -Compress)
}

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [IO.File]::Open(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) {
                throw "The probe output is not a PE file."
            }
            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "The probe output has no PE signature."
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

function Invoke-WithIsolatedDotNetEnvironment {
    param(
        [Parameter(Mandatory = $true)][string] $Stage,
        [Parameter(Mandatory = $true)][scriptblock] $Action
    )

    $values = [ordered]@{
        DOTNET_CLI_HOME = (Join-Path $Stage "dotnet-home")
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
        DOTNET_CLI_TELEMETRY_OPTOUT = "1"
        DOTNET_NOLOGO = "1"
        DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
        NUGET_PACKAGES = (Join-Path $Stage "packages")
        NUGET_HTTP_CACHE_PATH = (Join-Path $Stage "http-cache")
        NUGET_CERT_REVOCATION_MODE = "offline"
    }
    $original = @{}
    try {
        foreach ($entry in $values.GetEnumerator()) {
            $original[$entry.Key] =
                [Environment]::GetEnvironmentVariable(
                    $entry.Key,
                    [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable(
                $entry.Key,
                $entry.Value,
                [EnvironmentVariableTarget]::Process)
        }
        & $Action
    }
    finally {
        foreach ($entry in $values.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                $entry.Key,
                $original[$entry.Key],
                [EnvironmentVariableTarget]::Process)
        }
    }
}

function Invoke-R201ProbeBuild {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $AutoCadDirectory
    )

    $stage = Join-Path $stageRoot ("probe-" + $Label)
    $packages = Join-Path $stage "packages"
    $objBase = Join-Path $stage "obj-base"
    $objExtensions = Join-Path $stage "obj-extensions"
    $obj = Join-Path $stage "obj"
    $bin = Join-Path $stage "bin"
    foreach ($directory in @(
            $stage, $packages, $objBase, $objExtensions, $obj, $bin)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $project = Resolve-RepositoryFile `
        "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/Codex.AutoCAD.Host.2016.V2ApiProbe.csproj"
    $config = Resolve-RepositoryFile `
        "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/NuGet.Config"
    $packageLock = Resolve-RepositoryFile `
        "tests/Codex.AutoCAD.Host.2016.V2ApiProbe/packages.lock.json"

    Invoke-WithIsolatedDotNetEnvironment $stage {
        Invoke-CapturedCommand $dotnetCommand @(
            "msbuild", $project,
            "/t:Restore",
            "/nologo",
            "/verbosity:minimal",
            ("/p:AutoCad2016Dir=" + $AutoCadDirectory),
            ("/p:RestoreConfigFile=" + $config),
            ("/p:RestorePackagesPath=" + $packages),
            "/p:RestoreLockedMode=true",
            "/p:RestoreNoCache=true",
            ("/p:NuGetLockFilePath=" + $packageLock),
            ("/p:BaseIntermediateOutputPath=" + $objBase + "\"),
            ("/p:MSBuildProjectExtensionsPath=" + $objExtensions + "\")
        ) ("Restore locked R20.1 probe pass " + $Label) | Out-Null

        Invoke-CapturedCommand $dotnetCommand @(
            "msbuild", $project,
            "/t:Build",
            "/nologo",
            "/verbosity:minimal",
            "/p:Configuration=Release",
            "/p:Platform=x64",
            ("/p:AutoCad2016Dir=" + $AutoCadDirectory),
            ("/p:RestoreConfigFile=" + $config),
            ("/p:RestorePackagesPath=" + $packages),
            "/p:RestoreLockedMode=true",
            ("/p:NuGetLockFilePath=" + $packageLock),
            ("/p:OutputPath=" + $bin + "\"),
            ("/p:BaseIntermediateOutputPath=" + $objBase + "\"),
            ("/p:IntermediateOutputPath=" + $obj + "\"),
            ("/p:MSBuildProjectExtensionsPath=" + $objExtensions + "\"),
            "/p:DebugSymbols=false",
            "/p:DebugType=None",
            "/p:Deterministic=true",
            "/p:ContinuousIntegrationBuild=true",
            "/warnaserror"
        ) ("Build locked R20.1 probe pass " + $Label) | Out-Null
    }

    $output = Join-Path $bin "Codex.AutoCAD.Host.2016.V2ApiProbe.dll"
    if (-not (Test-Path -LiteralPath $output -PathType Leaf) -or
        (Get-PeMachine $output) -ne 0x8664) {
        throw "The locked R20.1 probe output is missing or not AMD64."
    }
    foreach ($name in @("accoremgd.dll", "acdbmgd.dll", "acmgd.dll")) {
        if (Test-Path -LiteralPath (Join-Path $bin $name)) {
            throw "An Autodesk assembly was copied into the probe output."
        }
    }
    return [pscustomobject]@{
        Sha256 = Get-Sha256 $output
        Bytes = (Get-Item -LiteralPath $output).Length
        PeMachine = "0x8664"
    }
}

function Resolve-EvidencePath {
    param([string] $Requested)

    if ([string]::IsNullOrWhiteSpace($Requested)) {
        return Join-Path $stageRoot "verification.json"
    }
    $resolved = if ([IO.Path]::IsPathRooted($Requested)) {
        [IO.Path]::GetFullPath($Requested)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $artifactRoot $Requested))
    }
    $rootPrefix = $artifactRoot.TrimEnd("\", "/") +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith(
            $rootPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetExtension($resolved) -cne ".json") {
        throw "Evidence must be a JSON file under the artifact root."
    }
    return $resolved
}

try {
    if ($SelfTestOnly -and (
            $SkipR201BinaryProbe -or $ValidationOnly -or
            -not [string]::IsNullOrWhiteSpace($AutoCad2016Dir) -or
            -not [string]::IsNullOrWhiteSpace($EvidencePath))) {
        throw "SelfTestOnly cannot be combined with runtime options."
    }
    if ($SkipR201BinaryProbe -and
        -not [string]::IsNullOrWhiteSpace($AutoCad2016Dir)) {
        throw "SkipR201BinaryProbe cannot accept an AutoCAD directory."
    }
    if (-not $SkipR201BinaryProbe -and
        [string]::IsNullOrWhiteSpace($AutoCad2016Dir) -and
        -not $SelfTestOnly) {
        throw "AutoCad2016Dir is required unless R20.1 binary checks are skipped."
    }

    $lockText = Read-StrictUtf8Text $lockPath 1024 131072
    $lock = $lockText | ConvertFrom-Json -ErrorAction Stop
    Assert-ToolchainLockDocument $lock

    if ($SelfTestOnly) {
        $caseCount = Invoke-ToolchainLockSelfTest $lock
        Complete-CodexBuildSafety -State $buildSafety `
            -Stage "m9-toolchain-self-test" | Out-Null
        Write-Host "M9_TOOLCHAIN_SELF_TEST=passed"
        Write-Host ("M9_TOOLCHAIN_SELF_TEST_CASES=" + $caseCount)
        return
    }

    $globalText = Read-StrictUtf8Text $globalJsonPath 32 4096
    $global = $globalText | ConvertFrom-Json -ErrorAction Stop
    Assert-ExactProperties $global @("sdk") "global.json"
    Assert-ExactProperties $global.sdk `
        @("version", "rollForward", "allowPrerelease") "global.json.sdk"
    if ([string] $global.sdk.version -cne [string] $lock.DotNet.SdkVersion -or
        [string] $global.sdk.rollForward -cne
            [string] $lock.DotNet.RollForward -or
        -not ($global.sdk.allowPrerelease -is [bool]) -or
        [bool] $global.sdk.allowPrerelease -ne
            [bool] $lock.DotNet.AllowPrerelease) {
        throw "global.json does not match the toolchain lock."
    }

    foreach ($entry in @($lock.NuGet.Inputs)) {
        Assert-FileMatchesLock $entry "A tracked NuGet input" | Out-Null
    }
    foreach ($entry in @($lock.R201Probe.SourceInputs)) {
        Assert-FileMatchesLock $entry "An R20.1 probe source input" | Out-Null
    }
    $offlinePackagePath = Assert-FileMatchesLock `
        $lock.OfflinePackage "The offline net45 package"

    $discoveredNuGetInputs = @(
        Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
            Where-Object {
                $_.Name -in @("NuGet.Config", "packages.lock.json") -and
                $_.FullName -notmatch "[\\/](?:bin|obj|artifacts)[\\/]"
            } |
            ForEach-Object {
                $_.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")
            } |
            Sort-Object
    )
    if (($discoveredNuGetInputs -join "`n") -cne
        ($expectedNuGetInputs -join "`n")) {
        throw "The repository NuGet input set changed without a lock update."
    }

    $sdkResult = Invoke-CapturedCommand $dotnetCommand @("--version") `
        "Resolve the pinned .NET SDK"
    $sdkVersion = $sdkResult.Text.Trim()
    if ($sdkVersion -cne [string] $lock.DotNet.SdkVersion) {
        throw "The resolved .NET SDK does not match the toolchain lock."
    }
    $nugetResult = Invoke-CapturedCommand $dotnetCommand @("nuget", "--version") `
        "Resolve the pinned NuGet CLI"
    $nugetVersion = Get-ExactVersionLine $nugetResult.Text "NuGet CLI"
    if ($nugetVersion -cne [string] $lock.DotNet.NuGetVersion) {
        throw "The resolved NuGet CLI does not match the toolchain lock."
    }
    $msbuildResult = Invoke-CapturedCommand $dotnetCommand `
        @("msbuild", "-version", "-nologo") "Resolve the pinned MSBuild"
    $msbuildVersion = Get-ExactVersionLine $msbuildResult.Text "MSBuild"
    if ($msbuildVersion -cne [string] $lock.DotNet.MsBuildVersion) {
        throw "The resolved MSBuild does not match the toolchain lock."
    }

    $signatureResult = Invoke-WithIsolatedDotNetEnvironment $stageRoot {
        Invoke-CapturedCommand $dotnetCommand `
            @("nuget", "verify", $offlinePackagePath, "--all") `
            "Verify the offline net45 package signature"
    }
    foreach ($fingerprint in @(
            [string] $lock.OfflinePackage.AuthorCertificateSha256,
            [string] $lock.OfflinePackage.RepositoryCertificateSha256)) {
        if ($signatureResult.Text -cnotmatch
            [regex]::Escape($fingerprint)) {
            throw "The offline package signer fingerprint changed."
        }
    }

    $cadBefore = @(Get-Process -Name acad -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id | Sort-Object)
    $sourceLocksBefore = Get-SourceLockSnapshot $lock.NuGet.Inputs
    $r201BinaryVerified = $false
    $cleanCacheVerified = $false
    $probeOutput = $null

    if (-not $SkipR201BinaryProbe) {
        $autoCadDirectory = [IO.Path]::GetFullPath($AutoCad2016Dir)
        $binaryInputs = @($lock.R201Probe.BinaryInputs)
        foreach ($entry in $binaryInputs) {
            $path = Join-Path $autoCadDirectory ([string] $entry.Name)
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "A locked R20.1 binary input is missing."
            }
            $item = Get-Item -LiteralPath $path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                [long] $item.Length -ne [long] $entry.Bytes -or
                (Get-Sha256 $path) -cne [string] $entry.Sha256) {
                throw "An installed R20.1 binary does not match the lock."
            }
            if (-not [string]::IsNullOrWhiteSpace(
                    [string] $entry.AssemblyFullName)) {
                $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($path)
                if ($assemblyName.FullName -cne
                    [string] $entry.AssemblyFullName) {
                    throw "An R20.1 managed assembly identity changed."
                }
            }
            $signature = Get-AuthenticodeSignature -LiteralPath $path
            if ([string] $signature.Status -cne
                    [string] $entry.AuthenticodeStatus -or
                $null -eq $signature.SignerCertificate -or
                $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -cne
                    [string] $entry.SignerThumbprint) {
                throw "An R20.1 Authenticode identity changed."
            }
        }
        $r201BinaryVerified = $true

        if (-not $ValidationOnly) {
            $passA = Invoke-R201ProbeBuild "a" $autoCadDirectory
            $passB = Invoke-R201ProbeBuild "b" $autoCadDirectory
            if ($passA.Sha256 -cne $passB.Sha256 -or
                [long] $passA.Bytes -ne [long] $passB.Bytes -or
                $passA.PeMachine -cne $passB.PeMachine) {
                throw "Clean-cache R20.1 probe outputs are not reproducible."
            }
            $cleanCacheVerified = $true
            $probeOutput = $passA
        }
    }

    $sourceLocksAfter = Get-SourceLockSnapshot $lock.NuGet.Inputs
    if ($sourceLocksBefore -cne $sourceLocksAfter) {
        throw "Source-tree NuGet lock files changed during verification."
    }
    $cadAfter = @(Get-Process -Name acad -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id | Sort-Object)
    if (($cadBefore -join ",") -cne ($cadAfter -join ",")) {
        throw "The AutoCAD process set changed during toolchain verification."
    }

    if (-not $ValidationOnly) {
        $resolvedEvidence = Resolve-EvidencePath $EvidencePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidence) `
            -Force | Out-Null
        $report = [ordered]@{
            Schema = "codex.autocad.m9-toolchain-verification/1"
            Status = "toolchain_lock_verified"
            DotNetSdk = $sdkVersion
            NuGetVersion = $nugetVersion
            MsBuildVersion = $msbuildVersion
            GlobalJsonExact = $true
            TrackedNuGetInputCount = @($lock.NuGet.Inputs).Count
            ExplicitOfflineNuGetOnly = $true
            UserNuGetConfigRead = $false
            OfflinePackageSha256 = [string] $lock.OfflinePackage.Sha256
            OfflinePackageSignaturesVerified = $true
            R201ProbeSourceInputCount = @(
                $lock.R201Probe.SourceInputs).Count
            R201BinaryInputCount = @(
                $lock.R201Probe.BinaryInputs).Count
            R201BinaryInputsVerified = $r201BinaryVerified
            CleanCachePassCount = if ($cleanCacheVerified) { 2 } else { 0 }
            CleanCacheReproducible = $cleanCacheVerified
            ProbeOutputSha256 = if ($null -ne $probeOutput) {
                [string] $probeOutput.Sha256
            } else {
                $null
            }
            SourceLocksUnchanged = $true
            AutoCadStartedOrCommanded = $false
            CadWriteEnabled = $false
            RemoteWorkflowRunVerified = $false
            EvidenceBoundary = if ($SkipR201BinaryProbe) {
                "This evidence verifies the pinned SDK, NuGet, MSBuild, offline package signatures, NuGet inputs, and R20.1 probe source lock. A GitHub runner has no Autodesk binaries, so installed R20.1 binary hashes and clean-cache probe A/B builds remain local gates. It does not start AutoCAD or enable CAD writes."
            } else {
                "This evidence verifies the pinned SDK, NuGet, MSBuild, offline package signatures, all reviewed NuGet inputs, exact installed R20.1 binary hashes and signatures, and two isolated clean-cache R20.1 probe builds with identical AMD64 output. It does not start AutoCAD, run NETLOAD, or enable CAD writes."
            }
        }
        [IO.File]::WriteAllText(
            $resolvedEvidence,
            ($report | ConvertTo-Json -Depth 8),
            (New-Object Text.UTF8Encoding($false)))
        Write-Host ("M9_TOOLCHAIN_EVIDENCE=" + $resolvedEvidence)
    }

    Complete-CodexBuildSafety -State $buildSafety `
        -Stage "m9-toolchain-lock" | Out-Null
    Write-Host "M9_TOOLCHAIN_LOCK=passed"
    Write-Host ("M9_TOOLCHAIN_R201_BINARIES=" + $r201BinaryVerified)
    Write-Host ("M9_TOOLCHAIN_CLEAN_CACHE_REPRO=" + $cleanCacheVerified)
}
catch {
    try {
        Complete-CodexBuildSafety -State $buildSafety `
            -Stage "m9-toolchain-lock-failed" | Out-Null
    }
    catch {
    }
    throw
}

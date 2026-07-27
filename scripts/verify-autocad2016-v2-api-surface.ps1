[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$MsBuildPath,

    [string]$EvidencePath = "",

    [string]$ArtifactRoot = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'build-safety.ps1')
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactsRoot = $buildSafety.ArtifactRoot
$projectPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.V2ApiProbe\Codex.AutoCAD.Host.2016.V2ApiProbe.csproj'
$nuGetConfigPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.V2ApiProbe\NuGet.Config'
$packageLockPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.V2ApiProbe\packages.lock.json'
$vendoredPackagePath = Join-Path $repoRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
$toolchainVerifierPath = Join-Path $repoRoot 'scripts\verify-m9-toolchain-lock.ps1'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
$effectiveArtifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    Join-Path $artifactsRoot ("v2api-probe-verify-{0}" -f [Guid]::NewGuid().ToString('N'))
} else {
    [IO.Path]::GetFullPath($ArtifactRoot)
}
$verificationRoot = $effectiveArtifactRoot
$outputDirectory = Join-Path $verificationRoot 'bin'
$baseIntermediateDirectory = Join-Path $verificationRoot 'obj-base'
$intermediateDirectory = Join-Path $verificationRoot 'obj-compile'
$projectExtensionsDirectory = Join-Path $verificationRoot 'obj-project-extensions'
$packageCache = Join-Path $verificationRoot 'packages'
$dotnetCliHome = Join-Path $verificationRoot 'dotnet-state\cli-home'
$dotnetNuGetPackages = Join-Path $verificationRoot 'dotnet-state\packages'
$dotnetHttpCache = Join-Path $verificationRoot 'dotnet-state\http-cache'

foreach ($directory in @($outputDirectory, $baseIntermediateDirectory, $intermediateDirectory, $projectExtensionsDirectory, $packageCache, $dotnetCliHome, $dotnetNuGetPackages, $dotnetHttpCache)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$strictUtf8 = New-Object Text.UTF8Encoding($false, $true)

function Read-Utf8File {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [IO.File]::ReadAllText($Path, $script:strictUtf8)
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory
    )

    $previousLocation = $null
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $previousLocation = Get-Location
        Set-Location -LiteralPath $WorkingDirectory
    }
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        if ($null -ne $previousLocation) {
            Set-Location -LiteralPath $previousLocation.Path
        }
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
        Text = $output -join "`n"
    }
}

function Invoke-DotNetIsolated {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $isolatedEnvironment = [ordered]@{
        DOTNET_CLI_HOME = $script:dotnetCliHome
        # 与 DOTNET_CLI_HOME 同作用域禁止 .NET CLI 把临时工具目录写入用户 PATH。
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
        NUGET_PACKAGES = $script:packageCache
        NUGET_HTTP_CACHE_PATH = $script:dotnetHttpCache
        NUGET_CERT_REVOCATION_MODE = 'offline'
    }
    $originalEnvironment = @{}
    try {
        foreach ($entry in $isolatedEnvironment.GetEnumerator()) {
            $originalEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, [EnvironmentVariableTarget]::Process)
        }
        return Invoke-NativeCapture -FilePath $FilePath -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    }
    finally {
        foreach ($entry in $isolatedEnvironment.GetEnumerator()) {
            if ($null -eq $originalEnvironment[$entry.Key]) {
                [Environment]::SetEnvironmentVariable($entry.Key, $null, [EnvironmentVariableTarget]::Process)
            }
            else {
                [Environment]::SetEnvironmentVariable($entry.Key, $originalEnvironment[$entry.Key], [EnvironmentVariableTarget]::Process)
            }
        }
    }
}

# --- Preconditions ---
Write-Host "=== V2 API Surface Probe Verification ==="
Write-Host "PowerShell version: $($PSVersionTable.PSVersion)"
Write-Host "AutoCAD 2016 dir: $AutoCad2016Dir"
Write-Host "Configuration: $Configuration"
Write-Host ""

if (-not (Test-Path $projectPath)) {
    throw "Probe project not found: $projectPath"
}

if (-not (Test-Path (Join-Path $AutoCad2016Dir 'acad.exe'))) {
    throw "acad.exe not found in $AutoCad2016Dir"
}

foreach ($dll in @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
    $dllPath = Join-Path $AutoCad2016Dir $dll
    if (-not (Test-Path $dllPath)) {
        throw "$dll not found in $AutoCad2016Dir"
    }
    $fileInfo = Get-Item $dllPath
    Write-Host "  $dll : $($fileInfo.Length) bytes, LastWrite: $($fileInfo.LastWriteTimeUtc.ToString('o'))"
}

if (-not (Test-Path $vendoredPackagePath)) {
    throw "Vendored NuGet package not found: $vendoredPackagePath"
}

if (-not (Test-Path $nuGetConfigPath)) {
    throw "NuGet.Config not found: $nuGetConfigPath"
}

if (-not (Test-Path $packageLockPath)) {
    throw "packages.lock.json not found: $packageLockPath"
}

# Bind the legacy R20.1 API probe to the reviewed M9.2 source, package, SDK,
# NuGet, MSBuild, Autodesk binary hash, and Authenticode lock before restore.
& $toolchainVerifierPath `
    -AutoCad2016Dir $AutoCad2016Dir `
    -ValidationOnly

# --- Resolve the locked MSBuild entry point ---
$dotnetPath = (Get-Command dotnet -ErrorAction Stop |
    Select-Object -First 1).Source
if (-not [string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $requestedMsBuildPath = [IO.Path]::GetFullPath($MsBuildPath)
    if ($requestedMsBuildPath -cne [IO.Path]::GetFullPath($dotnetPath)) {
        throw "V2ApiProbe only accepts the global.json-pinned dotnet msbuild entry point."
    }
}
$MsBuildPath = $dotnetPath
$useDotNetMsbuild = $true
Write-Host "MSBuild: pinned dotnet msbuild ($MsBuildPath)"
Write-Host ""

# --- Restore ---
Write-Host "--- Restoring V2ApiProbe ---"

$restoreArgs = @(
    $projectPath
    "/p:AutoCad2016Dir=$AutoCad2016Dir"
    "/p:RestoreConfigFile=$nuGetConfigPath"
    "/p:RestorePackagesPath=$packageCache"
    "/p:NuGetLockFilePath=$packageLockPath"
    "/p:BaseIntermediateOutputPath=$baseIntermediateDirectory\"
    "/p:MSBuildProjectExtensionsPath=$projectExtensionsDirectory\"
    "/p:RestoreLockedMode=true"
    "/p:RestoreNoCache=true"
    "/nologo"
    "/verbosity:minimal"
)

if ($useDotNetMsbuild) {
    $restoreExe = $MsBuildPath
    $restoreArgs = @('msbuild', $projectPath, "/t:Restore") + $restoreArgs[1..($restoreArgs.Count-1)]
} else {
    $restoreExe = $MsBuildPath
    $restoreArgs = @($projectPath, "/t:Restore") + $restoreArgs[1..($restoreArgs.Count-1)]
}

$restoreResult = Invoke-DotNetIsolated -FilePath $restoreExe -Arguments $restoreArgs -WorkingDirectory $verificationRoot
if ($restoreResult.ExitCode -ne 0) {
    Write-Host "--- Restore output ---"
    foreach ($line in $restoreResult.Output) { Write-Host "  $line" }
    throw "V2ApiProbe restore failed."
}
Write-Host "Restore succeeded"
Write-Host ""

# --- Build ---
Write-Host "--- Building V2ApiProbe ($Configuration) ---"

if ($useDotNetMsbuild) {
    $buildArgs = @(
        'msbuild'
        $projectPath
        "/p:Configuration=$Configuration"
        "/p:Platform=x64"
        "/p:AutoCad2016Dir=$AutoCad2016Dir"
        "/p:RestoreConfigFile=$nuGetConfigPath"
        "/p:RestorePackagesPath=$packageCache"
        "/p:NuGetLockFilePath=$packageLockPath"
        "/p:RestoreLockedMode=true"
        "/p:OutputPath=$outputDirectory\"
        "/p:BaseIntermediateOutputPath=$baseIntermediateDirectory\"
        "/p:IntermediateOutputPath=$intermediateDirectory\"
        "/p:MSBuildProjectExtensionsPath=$projectExtensionsDirectory\"
        "/t:Build"
        "/nologo"
        "/verbosity:minimal"
    )
} else {
    $buildArgs = @(
        $projectPath
        "/p:Configuration=$Configuration"
        "/p:Platform=x64"
        "/p:AutoCad2016Dir=$AutoCad2016Dir"
        "/p:RestoreConfigFile=$nuGetConfigPath"
        "/p:RestorePackagesPath=$packageCache"
        "/p:NuGetLockFilePath=$packageLockPath"
        "/p:RestoreLockedMode=true"
        "/p:OutputPath=$outputDirectory\"
        "/p:BaseIntermediateOutputPath=$baseIntermediateDirectory\"
        "/p:IntermediateOutputPath=$intermediateDirectory\"
        "/p:MSBuildProjectExtensionsPath=$projectExtensionsDirectory\"
        "/t:Build"
        "/nologo"
        "/verbosity:minimal"
    )
}

$buildResult = Invoke-DotNetIsolated -FilePath $MsBuildPath -Arguments $buildArgs -WorkingDirectory $verificationRoot

Write-Host "Build exit code: $($buildResult.ExitCode)"
if ($buildResult.ExitCode -ne 0) {
    Write-Host "--- Build output ---"
    foreach ($line in $buildResult.Output) {
        Write-Host "  $line"
    }
    throw "V2ApiProbe build failed with exit code $($buildResult.ExitCode). Missing API members in R20.1."
}

# Check for warnings
$warningLines = @($buildResult.Output | Where-Object { $_ -match ': warning ' })
if ($warningLines.Count -gt 0) {
    Write-Host "--- Build warnings ---"
    foreach ($line in $warningLines) {
        Write-Host "  $line"
    }
    throw "V2ApiProbe build produced $($warningLines.Count) warning(s). TreatWarningsAsErrors is enabled, so this should have failed. Investigate."
}

Write-Host "Build: 0 warnings, 0 errors"
Write-Host ""

# --- Verify output ---
$dllPath = Join-Path $outputDirectory 'Codex.AutoCAD.Host.2016.V2ApiProbe.dll'
if (-not (Test-Path $dllPath)) {
    throw "Probe DLL not found after build: $dllPath"
}

$dllInfo = Get-Item $dllPath
Write-Host "Probe DLL: $($dllInfo.Length) bytes"

# Verify no Autodesk DLLs in output
foreach ($autodeskDll in @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
    $autodeskPath = Join-Path $outputDirectory $autodeskDll
    if (Test-Path $autodeskPath) {
        throw "Autodesk DLL found in probe output: $autodeskPath. Private=false must be set."
    }
}

# Check for any DLLs that shouldn't be there
$outputDlls = Get-ChildItem -Path $outputDirectory -Filter '*.dll' -File
Write-Host "Output DLLs:"
foreach ($dll in $outputDlls) {
    Write-Host "  $($dll.Name) ($($dll.Length) bytes)"
}

Write-Host "Output directory clean: no Autodesk DLLs copied"
Write-Host ""

# --- Run runtime probe ---
Write-Host "--- Running runtime API surface probe ---"

$probeOutputPath = Join-Path $verificationRoot 'probe-result.json'

try {
    # Register assembly resolver so the probe can find Autodesk DLLs at their
    # original location (Private=false means they are NOT in the output dir).
    $autoCad2016DirResolved = $AutoCad2016Dir
    $resolveHandler = [System.ResolveEventHandler]{
        param($sender, $e)
        $assemblyName = New-Object System.Reflection.AssemblyName($e.Name)
        $candidate = Join-Path $autoCad2016DirResolved ($assemblyName.Name + '.dll')
        if (Test-Path $candidate) {
            return [System.Reflection.Assembly]::LoadFrom($candidate)
        }
        return $null
    }
    [AppDomain]::CurrentDomain.add_AssemblyResolve($resolveHandler)

    # Load the probe assembly and call Run()
    $assembly = [System.Reflection.Assembly]::LoadFrom($dllPath)
    $probeType = $assembly.GetType('V2ApiSurfaceProbe')
    if ($null -eq $probeType) {
        throw "V2ApiSurfaceProbe type not found in assembly"
    }

    $runMethod = $probeType.GetMethod('Run', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
    if ($null -eq $runMethod) {
        throw "V2ApiSurfaceProbe.Run() method not found"
    }

    # Capture stdout
    $stringWriter = New-Object System.IO.StringWriter
    $originalOut = [Console]::Out
    [Console]::SetOut($stringWriter)

    try {
        $runMethod.Invoke($null, $null)
    }
    finally {
        [Console]::SetOut($originalOut)
    }

    $probeJson = $stringWriter.ToString()
    [IO.File]::WriteAllText($probeOutputPath, $probeJson, $strictUtf8)

    Write-Host "Probe JSON written to: $probeOutputPath"
    Write-Host ""

    # Parse and display results
    $result = $probeJson | ConvertFrom-Json

    Write-Host "--- Probe Results ---"
    Write-Host "Target assembly: $($result.targetAssembly)"
    Write-Host "Framework: $($result.framework)"
    Write-Host "Platform: $($result.platform)"
    Write-Host ""
    Write-Host "Compile-time checks: $($result.compileTimeNote)"
    Write-Host ""
    Write-Host "Runtime method/property checks:"
    Write-Host "  Total: $($result.summary.totalRuntimeChecks)"
    Write-Host "  Passed: $($result.summary.passed)"
    Write-Host "  Failed: $($result.summary.failed)"
    Write-Host ""

    if ($result.runtimeMethodChecks.passed.Count -gt 0) {
        Write-Host "  Passed members:"
        foreach ($member in $result.runtimeMethodChecks.passed) {
            Write-Host "    [PASS] $member"
        }
    }

    if ($result.runtimeMethodChecks.failed.Count -gt 0) {
        Write-Host ""
        Write-Host "  Failed members:"
        foreach ($member in $result.runtimeMethodChecks.failed) {
            Write-Host "    [FAIL] $member"
        }
    }

    Write-Host ""
    Write-Host "Disclaimer: $($result.disclaimer)"

}
catch {
    $ex = $_.Exception
    if ($ex.InnerException) {
        $ex = $ex.InnerException
    }
    Write-Host "Runtime probe failed: $($ex.Message)"
    throw
}

# --- Summary ---
Write-Host ""
Write-Host "=== Verification Summary ==="
Write-Host "Build: 0 warnings, 0 error"
Write-Host "Compile-time type/property checks: ALL PASSED (enforced by C# compiler)"
Write-Host "Runtime method/property checks: $($result.summary.passed) passed, $($result.summary.failed) failed"
Write-Host "Autodesk DLLs in output: 0 (Private=false enforced)"
Write-Host "PowerShell version: $($PSVersionTable.PSVersion)"
Write-Host ""
Write-Host "IMPORTANT: This probe verifies API surface existence only."
Write-Host "It does NOT start or operate AutoCAD and is NOT equivalent to runtime verification."

# Write evidence JSON
$resolvedEvidencePath = if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    Join-Path $repoRoot 'handoff\autocad2016\evidence\v2-api-surface-probe-verification.json'
} else {
    [IO.Path]::GetFullPath($EvidencePath)
}
$evidenceDir = Split-Path -Parent $resolvedEvidencePath
if (-not (Test-Path $evidenceDir)) {
    New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
}

$dllSha256 = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToUpperInvariant()

$evidence = [ordered]@{
    probeVersion = "1.0.0"
    verificationTimeUtc = [DateTime]::UtcNow.ToString("o")
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    targetAssembly = $result.targetAssembly
    framework = $result.framework
    platform = $result.platform
    buildWarnings = 0
    buildErrors = 0
    compileTimeTypeChecks = "all-passed"
    compileTimePropertyChecks = "all-passed"
    runtimeChecksPassed = $result.summary.passed
    runtimeChecksFailed = $result.summary.failed
    runtimePassedMembers = $result.runtimeMethodChecks.passed
    runtimeFailedMembers = $result.runtimeMethodChecks.failed
    dllSha256 = $dllSha256
    autodeskDllsInOutput = 0
    toolchainLockVerified = $true
    autoCadStartedOrRestarted = $false
    cadCommandsSent = $false
    netLoadVerified = $false
    autoCadLiveEvidence = $false
    disclaimer = "Compile-time checks verify types/properties exist in R20.1 assemblies. Runtime checks verify additional methods/properties via reflection. This probe does NOT start or operate AutoCAD and is NOT equivalent to AutoCAD runtime verification."
}

$evidenceJson = $evidence | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($resolvedEvidencePath, $evidenceJson, $strictUtf8)
Write-Host "Evidence written to: $resolvedEvidencePath"
Complete-CodexBuildSafety -State $buildSafety -Stage 'v2-api-surface' | Out-Null

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$MsBuildPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.V2ApiProbe\Codex.AutoCAD.Host.2016.V2ApiProbe.csproj'
$nuGetConfigPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.V2ApiProbe\NuGet.Config'
$packageLockPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.V2ApiProbe\packages.lock.json'
$vendoredPackagePath = Join-Path $repoRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
$verificationRoot = Join-Path $repoRoot ("artifacts\v2api-probe-verify-{0}" -f [Guid]::NewGuid().ToString('N'))
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
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
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

# --- Locate MSBuild ---
$useDotNetMsbuild = $false
if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    # Try VS MSBuild first
    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vsWhere) {
        $installPath = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($installPath) {
            $candidate = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path $candidate) {
                $MsBuildPath = $candidate
            }
            else {
                $candidate15 = Join-Path $installPath 'MSBuild\15.0\Bin\MSBuild.exe'
                if (Test-Path $candidate15) {
                    $MsBuildPath = $candidate15
                }
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
        $fallback = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
        if (Test-Path $fallback) {
            $MsBuildPath = $fallback
        }
    }
    # Fall back to dotnet msbuild
    if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or -not (Test-Path $MsBuildPath)) {
        $dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1).Source
        if ($dotnetPath) {
            $MsBuildPath = $dotnetPath
            $useDotNetMsbuild = $true
        }
    }
}

if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or -not (Test-Path $MsBuildPath)) {
    throw "MSBuild not found. Pass -MsBuildPath or install Visual Studio Build Tools or .NET SDK."
}

if ($useDotNetMsbuild) {
    Write-Host "MSBuild: dotnet msbuild ($MsBuildPath)"
} else {
    Write-Host "MSBuild: $MsBuildPath"
    $msBuildInfo = Get-Item $MsBuildPath
    Write-Host "  Version: $($msBuildInfo.VersionInfo.FileVersion)"
}
Write-Host ""

# --- Restore ---
Write-Host "--- Restoring V2ApiProbe ---"

$restoreArgs = @(
    $projectPath
    "/p:AutoCad2016Dir=$AutoCad2016Dir"
    "/p:BaseIntermediateOutputPath=$baseIntermediateDirectory\"
    "/p:MSBuildProjectExtensionsPath=$projectExtensionsDirectory\"
    "/p:RestoreLockedMode=false"
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
$evidencePath = Join-Path $repoRoot 'handoff\autocad2016\evidence\v2-api-surface-probe-verification.json'
$evidenceDir = Split-Path -Parent $evidencePath
if (-not (Test-Path $evidenceDir)) {
    New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
}

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
    autodeskDllsInOutput = 0
    autoCadStartedOrRestarted = $false
    cadCommandsSent = $false
    netLoadVerified = $false
    autoCadLiveEvidence = $false
    disclaimer = "Compile-time checks verify types/properties exist in R20.1 assemblies. Runtime checks verify additional methods/properties via reflection. This probe does NOT start or operate AutoCAD and is NOT equivalent to AutoCAD runtime verification."
}

$evidenceJson = $evidence | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($evidencePath, $evidenceJson, $strictUtf8)
Write-Host "Evidence written to: $evidencePath"

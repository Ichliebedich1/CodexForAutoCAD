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
$projectPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.R201SignatureProbe\Codex.AutoCAD.Host.2016.R201SignatureProbe.csproj'
$nuGetConfigPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.R201SignatureProbe\NuGet.Config'
$packageLockPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.R201SignatureProbe\packages.lock.json'
$vendoredPackagePath = Join-Path $repoRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
$verificationRoot = Join-Path $repoRoot ("artifacts\r201-sig-probe-verify-{0}" -f [Guid]::NewGuid().ToString('N'))
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
        if ($null -ne $previousLocation) { Set-Location -LiteralPath $previousLocation.Path }
    }
    [pscustomobject]@{ ExitCode = $exitCode; Output = $output; Text = $output -join "`n" }
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
            } else {
                [Environment]::SetEnvironmentVariable($entry.Key, $originalEnvironment[$entry.Key], [EnvironmentVariableTarget]::Process)
            }
        }
    }
}

# --- Preconditions ---
Write-Host "=== R20.1 Exact API Signature Probe Verification ==="
Write-Host "PowerShell version: $($PSVersionTable.PSVersion)"
Write-Host "AutoCAD 2016 dir: $AutoCad2016Dir"
Write-Host "Configuration: $Configuration"
Write-Host ""

if (-not (Test-Path $projectPath)) { throw "Probe project not found: $projectPath" }
if (-not (Test-Path (Join-Path $AutoCad2016Dir 'acad.exe'))) { throw "acad.exe not found in $AutoCad2016Dir" }

foreach ($dll in @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
    $dllPath = Join-Path $AutoCad2016Dir $dll
    if (-not (Test-Path $dllPath)) { throw "$dll not found in $AutoCad2016Dir" }
    $fileInfo = Get-Item $dllPath
    Write-Host "  $dll : $($fileInfo.Length) bytes, LastWrite: $($fileInfo.LastWriteTimeUtc.ToString('o'))"
}

if (-not (Test-Path $vendoredPackagePath)) { throw "Vendored NuGet package not found: $vendoredPackagePath" }
if (-not (Test-Path $nuGetConfigPath)) { throw "NuGet.Config not found: $nuGetConfigPath" }
if (-not (Test-Path $packageLockPath)) { throw "packages.lock.json not found: $packageLockPath" }

# --- Locate MSBuild ---
$useDotNetMsbuild = $false
if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vsWhere) {
        $installPath = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($installPath) {
            $candidate = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path $candidate) { $MsBuildPath = $candidate }
            else {
                $candidate15 = Join-Path $installPath 'MSBuild\15.0\Bin\MSBuild.exe'
                if (Test-Path $candidate15) { $MsBuildPath = $candidate15 }
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
        $fallback = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
        if (Test-Path $fallback) { $MsBuildPath = $fallback }
    }
    if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or -not (Test-Path $MsBuildPath)) {
        $dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1).Source
        if ($dotnetPath) { $MsBuildPath = $dotnetPath; $useDotNetMsbuild = $true }
    }
}
if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or -not (Test-Path $MsBuildPath)) {
    throw "MSBuild not found."
}

if ($useDotNetMsbuild) { Write-Host "MSBuild: dotnet msbuild ($MsBuildPath)" }
else { Write-Host "MSBuild: $MsBuildPath"; $msBuildInfo = Get-Item $MsBuildPath; Write-Host "  Version: $($msBuildInfo.VersionInfo.FileVersion)" }
Write-Host ""

# --- Restore ---
Write-Host "--- Restoring R201SignatureProbe ---"
$restoreArgs = @(
    $projectPath
    "/p:AutoCad2016Dir=$AutoCad2016Dir"
    "/p:BaseIntermediateOutputPath=$baseIntermediateDirectory\"
    "/p:MSBuildProjectExtensionsPath=$projectExtensionsDirectory\"
    "/p:RestoreLockedMode=false"
    "/nologo"
    "/verbosity:minimal"
)
if ($useDotNetMsbuild) { $restoreExe = $MsBuildPath; $restoreArgs = @('msbuild', $projectPath, "/t:Restore") + $restoreArgs[1..($restoreArgs.Count-1)] }
else { $restoreExe = $MsBuildPath; $restoreArgs = @($projectPath, "/t:Restore") + $restoreArgs[1..($restoreArgs.Count-1)] }

$restoreResult = Invoke-DotNetIsolated -FilePath $restoreExe -Arguments $restoreArgs -WorkingDirectory $verificationRoot
if ($restoreResult.ExitCode -ne 0) {
    foreach ($line in $restoreResult.Output) { Write-Host "  $line" }
    throw "R201SignatureProbe restore failed."
}
Write-Host "Restore succeeded"
Write-Host ""

# --- Build ---
Write-Host "--- Building R201SignatureProbe ($Configuration) ---"
if ($useDotNetMsbuild) {
    $buildArgs = @('msbuild', $projectPath,
        "/p:Configuration=$Configuration", "/p:Platform=x64", "/p:AutoCad2016Dir=$AutoCad2016Dir",
        "/p:OutputPath=$outputDirectory\", "/p:BaseIntermediateOutputPath=$baseIntermediateDirectory\",
        "/p:IntermediateOutputPath=$intermediateDirectory\", "/p:MSBuildProjectExtensionsPath=$projectExtensionsDirectory\",
        "/t:Build", "/nologo", "/verbosity:minimal")
} else {
    $buildArgs = @($projectPath,
        "/p:Configuration=$Configuration", "/p:Platform=x64", "/p:AutoCad2016Dir=$AutoCad2016Dir",
        "/p:OutputPath=$outputDirectory\", "/p:BaseIntermediateOutputPath=$baseIntermediateDirectory\",
        "/p:IntermediateOutputPath=$intermediateDirectory\", "/p:MSBuildProjectExtensionsPath=$projectExtensionsDirectory\",
        "/t:Build", "/nologo", "/verbosity:minimal")
}
$buildResult = Invoke-DotNetIsolated -FilePath $MsBuildPath -Arguments $buildArgs -WorkingDirectory $verificationRoot

Write-Host "Build exit code: $($buildResult.ExitCode)"
if ($buildResult.ExitCode -ne 0) {
    foreach ($line in $buildResult.Output) { Write-Host "  $line" }
    throw "R201SignatureProbe build failed."
}
$warningLines = @($buildResult.Output | Where-Object { $_ -match ': warning ' })
if ($warningLines.Count -gt 0) {
    foreach ($line in $warningLines) { Write-Host "  $line" }
    throw "R201SignatureProbe build produced $($warningLines.Count) warning(s)."
}
Write-Host "Build: 0 warnings, 0 errors"
Write-Host ""

# --- Verify output ---
$dllPath = Join-Path $outputDirectory 'Codex.AutoCAD.Host.2016.R201SignatureProbe.dll'
if (-not (Test-Path $dllPath)) { throw "Probe DLL not found: $dllPath" }

$dllInfo = Get-Item $dllPath
Write-Host "Probe DLL: $($dllInfo.Length) bytes"
$dllSha256 = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
Write-Host "Probe DLL SHA-256: $dllSha256"

foreach ($autodeskDll in @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
    if (Test-Path (Join-Path $outputDirectory $autodeskDll)) {
        throw "Autodesk DLL found in probe output: $autodeskDll. Private=false must be set."
    }
}
$outputDlls = Get-ChildItem -Path $outputDirectory -Filter '*.dll' -File
Write-Host "Output DLLs:"
foreach ($dll in $outputDlls) { Write-Host "  $($dll.Name) ($($dll.Length) bytes)" }
Write-Host "Output directory clean: no Autodesk DLLs copied"
Write-Host ""

# --- Run runtime probe ---
Write-Host "--- Running runtime R20.1 API signature probe ---"
$probeOutputPath = Join-Path $verificationRoot 'probe-result.json'

try {
    $autoCad2016DirResolved = $AutoCad2016Dir
    $resolveHandler = [System.ResolveEventHandler]{
        param($sender, $e)
        $assemblyName = New-Object System.Reflection.AssemblyName($e.Name)
        $candidate = Join-Path $autoCad2016DirResolved ($assemblyName.Name + '.dll')
        if (Test-Path $candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate) }
        return $null
    }
    [AppDomain]::CurrentDomain.add_AssemblyResolve($resolveHandler)

    $assembly = [System.Reflection.Assembly]::LoadFrom($dllPath)
    $probeType = $assembly.GetType('R201SignatureProbe')
    if ($null -eq $probeType) { throw "R201SignatureProbe type not found" }

    $runMethod = $probeType.GetMethod('Run', [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static,
        $null, @([string]), $null)
    if ($null -eq $runMethod) { throw "R201SignatureProbe.Run(string) not found" }

    $stringWriter = New-Object System.IO.StringWriter
    $originalOut = [Console]::Out
    [Console]::SetOut($stringWriter)
    try { $runMethod.Invoke($null, @($AutoCad2016Dir)) }
    finally { [Console]::SetOut($originalOut) }

    $probeJson = $stringWriter.ToString()
    [IO.File]::WriteAllText($probeOutputPath, $probeJson, $strictUtf8)

    $result = $probeJson | ConvertFrom-Json

    Write-Host ""
    Write-Host "--- Positive Method Signatures ---"
    foreach ($m in $result.positiveMethodSignatureChecks) {
        $status = if ($m.passed) { "[PASS]" } else { "[FAIL]" }
        $ret = if ($m.PSObject.Properties['returnType']) { $m.returnType } else { $m.reason }
        Write-Host "  $status $($m.type).$($m.member) -> $ret"
    }
    Write-Host ""
    Write-Host "--- Positive Property Signatures ---"
    foreach ($p in $result.positivePropertySignatureChecks) {
        $status = if ($p.passed) { "[PASS]" } else { "[FAIL]" }
        $pt = if ($p.PSObject.Properties['propertyType']) { $p.propertyType } else { $p.reason }
        Write-Host "  $status $($p.type).$($p.member) : $pt"
    }
    Write-Host ""
    Write-Host "--- Expected Absence ---"
    foreach ($a in $result.expectedAbsenceChecks) {
        $status = if ($a.correctlyAbsent) { "[PASS]" } else { "[FAIL]" }
        Write-Host "  $status $($a.type).$($a.member) ($($a.expectedKind)) absent"
    }
    Write-Host ""
    Write-Host "--- Enum Freeze ---"
    foreach ($prop in $result.enumSignatureChecks.PSObject.Properties) {
        $e = $prop.Value
        $status = if ($e.passed) { "[PASS]" } else { "[FAIL]" }
        $cnt = if ($e.PSObject.Properties['count']) { $e.count } else { "N/A" }
        $frozen = if ($e.PSObject.Properties['matchesFrozenExpected']) { $e.matchesFrozenExpected } else { "N/A" }
        Write-Host "  $status $($e.fullName) ($cnt values, frozen=$frozen)"
    }
    Write-Host ""
    Write-Host "--- Assembly Identity ---"
    foreach ($prop in $result.assemblySignatureChecks.PSObject.Properties) {
        $a = $prop.Value
        $status = if ($a.passed) { "[PASS]" } else { "[FAIL]" }
        $asmName = if ($a.PSObject.Properties['assemblyName']) { $a.assemblyName } else { $prop.Name }
        $asmVer = if ($a.PSObject.Properties['assemblyVersion']) { $a.assemblyVersion } else { "N/A" }
        Write-Host "  $status $asmName $asmVer"
        if ($a.PSObject.Properties['sha256'] -and $a.sha256) { Write-Host "       SHA-256: $($a.sha256)" }
        if ($a.PSObject.Properties['authenticodeValid'] -and $a.authenticodeValid) { Write-Host "       Authenticode: Valid" }
        if ($a.PSObject.Properties['authenticodeValid'] -and -not $a.authenticodeValid) { Write-Host "       Authenticode: INVALID" }
        if ($a.PSObject.Properties['reason']) { Write-Host "       Reason: $($a.reason)" }
    }
    Write-Host ""
    Write-Host "--- Summary ---"
    $s = $result.summary
    Write-Host "Positive signatures: $($s.positiveSignature.methods.passed)/$($s.positiveSignature.methods.total) methods, $($s.positiveSignature.properties.passed)/$($s.positiveSignature.properties.total) properties"
    Write-Host "Expected absence: $($s.expectedAbsence.correctlyAbsent)/$($s.expectedAbsence.total) correctly absent"
    Write-Host "Enum freeze: $($s.enumFreeze.passed)/$($s.enumFreeze.total) passed (DimensionType absent=$($s.enumFreeze.dimensionTypeAbsent))"
    Write-Host "Assembly identity: $($s.assemblyIdentity.passed)/$($s.assemblyIdentity.total) passed"
    Write-Host "Overall: $(if ($s.overallPassed) { 'PASSED' } else { 'FAILED' })"
    Write-Host ""
    Write-Host "Disclaimer: $($result.disclaimer)"
}
catch {
    $ex = $_.Exception
    if ($ex.InnerException) { $ex = $ex.InnerException }
    Write-Host "Runtime probe failed: $($ex.Message)"
    throw
}

# --- Compute artifact hashes ---
$probeDllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
$probeJsonHash = (Get-FileHash -LiteralPath $probeOutputPath -Algorithm SHA256).Hash

# --- Return structured result ---
$output = [ordered]@{
    probeVersion = "1.0.0"
    verificationTimeUtc = [DateTime]::UtcNow.ToString("o")
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    shellIdentity = if ($PSVersionTable.PSEdition -eq 'Core') { "pwsh-$($PSVersionTable.PSVersion.Major).$($PSVersionTable.PSVersion.Minor)" } else { "powershell-$($PSVersionTable.PSVersion.Major).$($PSVersionTable.PSVersion.Minor)" }
    autoCad2016Dir = "REDACTED"
    configuration = $Configuration
    buildWarnings = 0
    buildErrors = 0
    probeDllSha256 = $probeDllHash
    probeJsonSha256 = $probeJsonHash
    probeOutput = $result
    autoCadStartedOrRestarted = $false
    cadCommandsSent = $false
    netLoadVerified = $false
    autoCadLiveEvidence = $false
    autodeskDllsInOutput = 0
}

return $output

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
. (Join-Path $PSScriptRoot 'build-safety.ps1')
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactsRoot = $buildSafety.ArtifactRoot
$projectPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj'
$solutionPath = Join-Path $repoRoot 'Codex.AutoCAD.2016.sln'
$mainSolutionPath = Join-Path $repoRoot 'Codex.AutoCAD.sln'
$nuGetConfigPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\NuGet.Config'
$packageLockPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\packages.lock.json'
$vendoredPackagePath = Join-Path $repoRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
$verificationRoot = Join-Path $artifactsRoot ("autocad2016-host-verify-{0}" -f [Guid]::NewGuid().ToString('N'))
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
        NUGET_PACKAGES = $script:dotnetNuGetPackages
        NUGET_HTTP_CACHE_PATH = $script:dotnetHttpCache
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
                Remove-Item -LiteralPath ("Env:{0}" -f $entry.Key) -ErrorAction SilentlyContinue
            }
            else {
                [Environment]::SetEnvironmentVariable($entry.Key, $originalEnvironment[$entry.Key], [EnvironmentVariableTarget]::Process)
            }
        }
    }
}

function Get-TrustedMicrosoftTool {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$DescriptionPattern,
        [int]$MinimumMajorVersion = 0,
        [int]$MaximumMajorVersion = 2147483647
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found at the resolved path: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.VersionInfo.CompanyName -cne 'Microsoft Corporation') {
        throw "$Label must have exact Microsoft Corporation company metadata: $($item.FullName)"
    }
    if ($item.VersionInfo.FileDescription -notmatch $DescriptionPattern) {
        throw "$Label has an unexpected file description '$($item.VersionInfo.FileDescription)': $($item.FullName)"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $item.FullName
    if ($signature.Status.ToString() -cne 'Valid' -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?i)(?:^|,\s*)O="?Microsoft Corporation"?(?:,|$)') {
        throw "$Label must carry a valid Microsoft Authenticode signature; status was $($signature.Status): $($item.FullName)"
    }

    $versionMatch = [regex]::Match([string]$item.VersionInfo.FileVersion, '\d+(?:[\.,]\d+){1,3}')
    if (-not $versionMatch.Success) {
        throw "$Label file version could not be parsed: $($item.VersionInfo.FileVersion)"
    }
    $versionText = $versionMatch.Value.Replace(',', '.')
    $versionParts = @($versionText -split '\.')
    while ($versionParts.Count -lt 4) { $versionParts += '0' }
    $version = New-Object Version (($versionParts | Select-Object -First 4) -join '.')
    if ($version.Major -lt $MinimumMajorVersion -or $version.Major -gt $MaximumMajorVersion) {
        throw "$Label major version must be in [$MinimumMajorVersion, $MaximumMajorVersion], got $version at $($item.FullName)"
    }

    [pscustomobject]@{
        Name = $Label
        Path = $item.FullName
        FileVersion = $item.VersionInfo.FileVersion
        ProductVersion = $item.VersionInfo.ProductVersion
        Version = $version.ToString()
        CompanyName = $item.VersionInfo.CompanyName
        FileDescription = $item.VersionInfo.FileDescription
        Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        SignatureStatus = $signature.Status.ToString()
        SignerSubject = $signature.SignerCertificate.Subject
        SignerThumbprint = $signature.SignerCertificate.Thumbprint
    }
}

function Get-DirectoryManifestHash {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return '<absent>'
    }

    $root = (Get-Item -LiteralPath $Path).FullName.TrimEnd('\')
    $lines = @(
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)) {
            $relativePath = $file.FullName.Substring($root.Length).TrimStart('\')
            '{0}|{1}|{2}' -f $relativePath, $file.Length, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    )
    $bytes = [Text.Encoding]::UTF8.GetBytes($lines -join "`n")
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-IlMemberReferenceMap {
    param([Parameter(Mandatory = $true)][string]$IlText)

    # Ildasm byte-array and explanatory comments are not metadata. Removing them prevents comment/string spoofing.
    $codeOnly = [regex]::Replace($IlText, '//[^\r\n]*(?=\r|\n|$)', '')
    $flat = [regex]::Replace($codeOnly, '\s+', ' ')
    $pattern = '(?<owner>(?:\[[^\]]+\])?[A-Za-z0-9_.$+`''/<>]+(?:/\*[0-9A-Fa-f]{8}\*/)?)(?:/\*[0-9A-Fa-f]{8}\*/)?::(?<method>[A-Za-z0-9_.$+`''<>]+)\s*\((?<args>[^)]{0,1000})\)\s*/\*\s*(?<token>0A[0-9A-Fa-f]{6})\s*\*/'
    $map = @{}
    foreach ($match in [regex]::Matches($flat, $pattern)) {
        $owner = [regex]::Replace($match.Groups['owner'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $owner = [regex]::Replace($owner, '\[([^/\]]+)(?:/\*[0-9A-Fa-f]{8}\*/)?\]', '[$1]')
        $arguments = [regex]::Replace($match.Groups['args'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $arguments = [regex]::Replace($arguments, '\[([^/\]]+)(?:/\*[0-9A-Fa-f]{8}\*/)?\]', '[$1]')
        $arguments = [regex]::Replace($arguments, '\s+', ' ').Trim()
        $token = $match.Groups['token'].Value.ToUpperInvariant()
        $canonical = '{0}::{1}({2})' -f $owner, $match.Groups['method'].Value, $arguments
        if ($map.ContainsKey($token) -and $map[$token] -cne $canonical) {
            throw "Ildasm emitted conflicting identities for MemberRef ${token}: '$($map[$token])' and '$canonical'."
        }
        $map[$token] = $canonical
    }
    $hiddenDebuggablePattern = '(?m)^\s*//\s+\.custom\s+/\*0C000003:0A000003\*/\s+instance\s+void\s+\[mscorlib/\*23000001\*/\]System\.Diagnostics\.DebuggableAttribute/\*01000003\*/::\.ctor\(valuetype\s+\[mscorlib/\*23000001\*/\]System\.Diagnostics\.DebuggableAttribute/\*01000003\*//DebuggingModes/\*01000004\*/\)\s+/\*\s*0A000003\s*\*/\s*$'
    $hiddenDebuggableMatches = @([regex]::Matches($IlText, $hiddenDebuggablePattern))
    if ($hiddenDebuggableMatches.Count -eq 1) {
        $map['0A000003'] = '[mscorlib]System.Diagnostics.DebuggableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggableAttribute/DebuggingModes)'
    }
    elseif ($hiddenDebuggableMatches.Count -gt 1) {
        throw 'Ildasm emitted duplicate hidden DebuggableAttribute MemberRefs.'
    }
    return $map
}

function Get-IlMethodDefinitions {
    param([Parameter(Mandatory = $true)][string]$IlText)

    $definitions = @()
    $pattern = '(?s)\.method\s+/\*(?<token>060[0-9A-Fa-f]{5})\*/(?<body>.*?)}\s*//\s*end of method\s+(?<name>[^\r\n]+)'
    foreach ($match in [regex]::Matches($IlText, $pattern)) {
        $header = ($match.Groups['body'].Value -split '\{', 2)[0]
        $header = [regex]::Replace($header, '/\*[0-9A-Fa-f]{8}\*/', '')
        $header = [regex]::Replace($header, '\s+', ' ').Trim()
        $bodyCanonical = [regex]::Replace($match.Groups['body'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $bodyCanonical = [regex]::Replace($bodyCanonical, '\s+', ' ').Trim()
        $definitions += [pscustomobject]@{
            Token = $match.Groups['token'].Value.ToUpperInvariant()
            Name = $match.Groups['name'].Value.Trim()
            Header = $header
            BodyCanonical = $bodyCanonical
        }
    }
    return $definitions
}

function Assert-NoHighRiskMemberReferences {
    param([Parameter(Mandatory = $true)][string[]]$Signatures)

    $violations = @($Signatures | Where-Object {
        $_ -match '(?i)Autodesk\.AutoCAD\.[^:]*::(?:Save|SaveAs|DxfOut|Quit|Invoke|CloseAndSave|SetSystemVariable|SendStringToExecute|ExecuteInCommandContextAsync|Command|CommandAsync)\s*\('
    })
    if ($violations.Count -ne 0) {
        throw "High-risk CAD MemberRef rejected: $($violations -join ', ')"
    }
}

function Invoke-VerificationRuleSelfTests {
    $safe = '[accoremgd]Autodesk.AutoCAD.EditorInput.Editor::WriteMessage(string)'
    Assert-NoHighRiskMemberReferences -Signatures @($safe)

    $negativeSamples = @(
        '[Acdbmgd]Autodesk.AutoCAD.DatabaseServices.Database::Save()',
        '[Acdbmgd]Autodesk.AutoCAD.DatabaseServices.Database::DxfOut(string)',
        '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Core.Application::Quit()',
        '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Core.Application::Invoke(class ResultBuffer)'
    )
    foreach ($sample in $negativeSamples) {
        $wasRejected = $false
        try {
            Assert-NoHighRiskMemberReferences -Signatures @($sample)
        }
        catch {
            $wasRejected = $true
        }
        if (-not $wasRejected) {
            throw "Verifier self-test failed to reject: $sample"
        }
    }

    $commentOnly = "// IL_0000: call instance void [Acdbmgd]Autodesk.AutoCAD.DatabaseServices.Database::Save() /* 0A000020 */"
    if ((Get-IlMemberReferenceMap -IlText $commentOnly).Count -ne 0) {
        throw 'Verifier self-test failed: an IL comment was accepted as a real MemberRef.'
    }
    $activeDangerous = "IL_0000: call instance void [Acdbmgd/*23000002*/]Autodesk.AutoCAD.DatabaseServices.Database/*01000020*/::Save() /* 0A000020 */"
    $activeMap = Get-IlMemberReferenceMap -IlText $activeDangerous
    $wasRejected = $false
    try {
        Assert-NoHighRiskMemberReferences -Signatures @($activeMap.Values)
    }
    catch {
        $wasRejected = $true
    }
    if (-not $wasRejected) {
        throw 'Verifier self-test failed: an active Save MemberRef was not rejected.'
    }

    [pscustomobject]@{
        Passed = $true
        NegativeSamplesRejected = $negativeSamples
        CommentSpoofRejected = $true
    }
}

$ruleSelfTestEvidence = Invoke-VerificationRuleSelfTests

if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $msbuildCommand = Get-Command msbuild.exe -ErrorAction Stop | Select-Object -First 1
    $MsBuildPath = $msbuildCommand.Source
}
$MsBuildPath = [IO.Path]::GetFullPath($MsBuildPath)
$msbuildEvidence = Get-TrustedMicrosoftTool -Path $MsBuildPath -Label 'MSBuild' -DescriptionPattern '^MSBuild\.exe$' -MinimumMajorVersion 17 -MaximumMajorVersion 17

$programFiles64 = [Environment]::GetEnvironmentVariable('ProgramW6432')
if ([string]::IsNullOrWhiteSpace($programFiles64)) {
    $programFiles64 = [Environment]::GetEnvironmentVariable('ProgramFiles')
}
if ([string]::IsNullOrWhiteSpace($programFiles64)) {
    throw 'The 64-bit Program Files directory is unavailable; trusted dotnet discovery cannot continue.'
}
$dotnetPath = Join-Path $programFiles64 'dotnet\dotnet.exe'
$dotnetEvidence = Get-TrustedMicrosoftTool -Path $dotnetPath -Label 'dotnet' -DescriptionPattern '^\.NET Host$' -MinimumMajorVersion 8
$dotnetVersionResult = Invoke-DotNetIsolated -FilePath $dotnetPath -Arguments @('--version') -WorkingDirectory $repoRoot
$resolvedDotnetSdk = $dotnetVersionResult.Text.Trim()
if ($dotnetVersionResult.ExitCode -ne 0 -or $resolvedDotnetSdk -cne '8.0.319') {
    throw "Repository must resolve exactly .NET SDK 8.0.319 from repoRoot; got '$resolvedDotnetSdk'."
}

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) {
                throw "Not a PE file: $Path"
            }
            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "Invalid PE signature: $Path"
            }
            $machine = $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    switch ($machine) {
        0x8664 { 'x64' }
        0x014C { 'x86' }
        0xAA64 { 'arm64' }
        default { '0x{0:X4}' -f $machine }
    }
}

function Get-TrustedAutodeskFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][bool]$RequireAssemblyVersion
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required AutoCAD 2016 file is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.VersionInfo.FileVersion -notmatch '^R?20\.1\.') {
        throw "Expected AutoCAD R20.1 file version, got '$($item.VersionInfo.FileVersion)' for $Path"
    }
    if ($item.VersionInfo.CompanyName -ine 'Autodesk, Inc.') {
        throw "Expected exact Autodesk, Inc. company metadata for $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?i)(?:^|,\s*)O=(?:"Autodesk, Inc"|Autodesk, Inc\.?)(?:,|$)') {
        throw "Expected a valid Autodesk Authenticode signature for $Path; status was $($signature.Status)."
    }

    $assemblyVersion = $null
    if ($RequireAssemblyVersion) {
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($item.FullName).Version.ToString()
        if ($assemblyVersion -ne '20.1.0.0') {
            throw "Expected AutoCAD 2016 assembly version 20.1.0.0, got '$assemblyVersion' for $Path"
        }
    }

    [pscustomobject]@{
        Name = $item.Name
        FileVersion = $item.VersionInfo.FileVersion
        AssemblyVersion = $assemblyVersion
        Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        SignatureStatus = $signature.Status.ToString()
        SignerSubject = $signature.SignerCertificate.Subject
        SignerThumbprint = $signature.SignerCertificate.Thumbprint
    }
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Host.2016 project not found: $projectPath"
}
$expectedProjectSha256 = '93C091263A089C84AE76C91C1E57CCC02D858EEF897762F655594234A1F0F7CE'
$projectSha256 = (Get-FileHash -LiteralPath $projectPath -Algorithm SHA256).Hash
if ($projectSha256 -cne $expectedProjectSha256) {
    throw 'Host.2016 project file changed; implicit build-property/target injection is rejected until the project gate is reviewed.'
}
foreach ($requiredSolution in @($solutionPath, $mainSolutionPath)) {
    if (-not (Test-Path -LiteralPath $requiredSolution -PathType Leaf)) {
        throw "Required solution not found: $requiredSolution"
    }
}

$projectGuid = '{C4AB73B7-44D5-4BA4-9C9F-584338F0DA16}'
$mainSolutionText = Read-Utf8File -Path $mainSolutionPath
if ($mainSolutionText -match '(?i)Codex\.AutoCAD\.Host\.2016' -or $mainSolutionText -match [regex]::Escape($projectGuid)) {
    throw 'Host.2016 must remain absent from the main Codex.AutoCAD.sln build graph.'
}

$solutionText = Read-Utf8File -Path $solutionPath
$solutionProjectMatches = @([regex]::Matches($solutionText, '(?m)^Project\("[^"]+"\)\s*=\s*"([^"]+)",\s*"([^"]+\.csproj)",\s*"(\{[A-Fa-f0-9-]+\})"'))
if ($solutionProjectMatches.Count -ne 1 -or
    $solutionProjectMatches[0].Groups[1].Value -ne 'Codex.AutoCAD.Host.2016' -or
    $solutionProjectMatches[0].Groups[2].Value.Replace('/', '\') -ine 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj' -or
    $solutionProjectMatches[0].Groups[3].Value -ine $projectGuid) {
    throw 'Codex.AutoCAD.2016.sln must contain exactly the reviewed Host.2016 project and no other build project.'
}
$expectedSolutionMappings = @(
    "$projectGuid.Debug|Any CPU.ActiveCfg = Debug|x64",
    "$projectGuid.Debug|Any CPU.Build.0 = Debug|x64",
    "$projectGuid.Release|Any CPU.ActiveCfg = Release|x64",
    "$projectGuid.Release|Any CPU.Build.0 = Release|x64"
)
$actualSolutionMappings = @($solutionText -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "$projectGuid.*" })
if (@(Compare-Object -ReferenceObject $expectedSolutionMappings -DifferenceObject $actualSolutionMappings).Count -ne 0) {
    throw "Codex.AutoCAD.2016.sln must map Debug/Release Any CPU exclusively to Host.2016 x64.`n$($actualSolutionMappings -join [Environment]::NewLine)"
}
if (-not (Test-Path -LiteralPath $MsBuildPath -PathType Leaf)) {
    throw "MSBuild not found: $MsBuildPath"
}

$acadEvidence = Get-TrustedAutodeskFile -Path (Join-Path $AutoCad2016Dir 'acad.exe') -RequireAssemblyVersion $false
if ((Get-PeMachine -Path (Join-Path $AutoCad2016Dir 'acad.exe')) -ne 'x64') {
    throw 'The target AutoCAD 2016 process must be x64 for this Host.2016 build.'
}

$managedApiNames = @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')
$managedApiEvidence = @(
    foreach ($managedApiName in $managedApiNames) {
        Get-TrustedAutodeskFile -Path (Join-Path $AutoCad2016Dir $managedApiName) -RequireAssemblyVersion $true
    }
)

$projectText = Read-Utf8File -Path $projectPath
[xml]$project = $projectText
$namespaceManager = New-Object Xml.XmlNamespaceManager($project.NameTable)
$namespaceManager.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')

$targetFramework = $project.SelectSingleNode('//msb:TargetFrameworkVersion', $namespaceManager)
if ($null -eq $targetFramework -or $targetFramework.InnerText -ne 'v4.5') {
    throw 'Host.2016 must target exactly .NET Framework 4.5.'
}
$platformTarget = $project.SelectSingleNode('//msb:PlatformTarget', $namespaceManager)
if ($null -eq $platformTarget -or $platformTarget.InnerText -ne 'x64') {
    throw 'Host.2016 must target x64.'
}

$commonPropsImportText = '<Import Project="$(MSBuildToolsPath)\Microsoft.Common.props"'
$commonPropsImportIndex = $projectText.IndexOf($commonPropsImportText, [StringComparison]::Ordinal)
if ($commonPropsImportIndex -lt 0) {
    throw 'Host.2016 is missing the reviewed Microsoft.Common.props import.'
}
foreach ($isolationProperty in @('ImportDirectoryBuildProps', 'ImportDirectoryBuildTargets')) {
    $nodes = @($project.SelectNodes("//msb:$isolationProperty", $namespaceManager))
    $rawPropertyText = "<$isolationProperty>false</$isolationProperty>"
    $rawPropertyIndex = $projectText.IndexOf($rawPropertyText, [StringComparison]::Ordinal)
    if ($nodes.Count -ne 1 -or $nodes[0].InnerText -cne 'false' -or
        $rawPropertyIndex -lt 0 -or $rawPropertyIndex -gt $commonPropsImportIndex) {
        throw "$isolationProperty=false must appear exactly once before Microsoft.Common.props."
    }
}
foreach ($requiredBuildProperty in @{
    Deterministic = 'true'
    ContinuousIntegrationBuild = 'true'
    TreatWarningsAsErrors = 'true'
    RestoreLockedMode = 'true'
}.GetEnumerator()) {
    $nodes = @($project.SelectNodes("//msb:$($requiredBuildProperty.Key)", $namespaceManager))
    if ($nodes.Count -ne 1 -or $nodes[0].InnerText -cne $requiredBuildProperty.Value) {
        throw "Host.2016 build property '$($requiredBuildProperty.Key)' must equal '$($requiredBuildProperty.Value)' exactly once."
    }
}
$releaseGroups = @($project.SelectNodes('//msb:PropertyGroup', $namespaceManager) | Where-Object { $null -ne $_.Attributes['Condition'] -and $_.GetAttribute('Condition') -match 'Release\|x64' })
if ($releaseGroups.Count -ne 1 -or
    $null -eq $releaseGroups[0].DebugSymbols -or $releaseGroups[0].DebugSymbols -cne 'false' -or
    $null -eq $releaseGroups[0].DebugType -or $releaseGroups[0].DebugType -cne 'none' -or
    $null -eq $releaseGroups[0].Optimize -or $releaseGroups[0].Optimize -cne 'true') {
    throw 'Host.2016 Release|x64 must be optimized with DebugSymbols=false and DebugType=none.'
}
if ($project.SelectNodes('//msb:DirectoryBuildPropsPath | //msb:DirectoryBuildTargetsPath', $namespaceManager).Count -ne 0) {
    throw 'Host.2016 may not redirect Directory.Build props or targets imports.'
}

$allowedReferences = @('System', 'System.Core', 'accoremgd', 'acdbmgd', 'acmgd')
$referenceNodes = @($project.SelectNodes('//msb:Reference', $namespaceManager))
$referenceNames = @(
    foreach ($referenceNode in $referenceNodes) {
        ($referenceNode.Include -split ',', 2)[0].Trim()
    }
)
$unexpectedReferences = @($referenceNames | Where-Object { $_ -notin $allowedReferences })
$missingReferences = @($allowedReferences | Where-Object { $_ -notin $referenceNames })
if ($unexpectedReferences.Count -ne 0 -or $missingReferences.Count -ne 0) {
    throw "Host.2016 project assembly references must be the exact diagnostic allowlist. Unexpected: $($unexpectedReferences -join ', '); missing: $($missingReferences -join ', ')."
}
if (@($referenceNames | Sort-Object -Unique).Count -ne $referenceNames.Count) {
    throw 'Host.2016 project contains duplicate assembly references.'
}

foreach ($referenceName in @('accoremgd', 'acdbmgd', 'acmgd')) {
    $reference = $project.SelectSingleNode("//msb:Reference[starts-with(translate(@Include, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '$referenceName')]", $namespaceManager)
    $hintPathNode = $reference.SelectSingleNode('msb:HintPath', $namespaceManager)
    $specificVersionNode = $reference.SelectSingleNode('msb:SpecificVersion', $namespaceManager)
    $privateNode = $reference.SelectSingleNode('msb:Private', $namespaceManager)
    $expectedHintPath = '$(AutoCad2016Dir)\{0}.dll' -f $referenceName
    if ($null -eq $reference -or
        $null -eq $hintPathNode -or $hintPathNode.InnerText -ine $expectedHintPath -or
        $null -eq $specificVersionNode -or $specificVersionNode.InnerText -ine 'true' -or
        $null -eq $privateNode -or $privateNode.InnerText -ine 'false') {
        throw "Autodesk reference '$referenceName' must use the target-machine HintPath, SpecificVersion=true, and Private=false."
    }
}

$packageReferences = @($project.SelectNodes('//msb:PackageReference', $namespaceManager))
$packagePrivateAssets = if ($packageReferences.Count -eq 1) {
    $packageReferences[0].SelectSingleNode('msb:PrivateAssets', $namespaceManager)
}
$packageIncludeAssets = if ($packageReferences.Count -eq 1) {
    $packageReferences[0].SelectSingleNode('msb:IncludeAssets', $namespaceManager)
}
if ($packageReferences.Count -ne 1 -or
    $packageReferences[0].Include -ine 'Microsoft.NETFramework.ReferenceAssemblies.net45' -or
    $packageReferences[0].Version -ne '[1.0.3]' -or
    $null -eq $packagePrivateAssets -or $packagePrivateAssets.InnerText -ine 'all' -or
    $null -eq $packageIncludeAssets -or $packageIncludeAssets.InnerText -ine 'runtime;build;native;contentfiles;analyzers') {
    throw 'Host.2016 may use only the exact locked Microsoft.NETFramework.ReferenceAssemblies.net45 [1.0.3] compile-time package.'
}
foreach ($requiredRestoreProperty in @{
    RestoreProjectStyle = 'PackageReference'
    RestorePackagesWithLockFile = 'true'
    NuGetLockFilePath = '$(MSBuildThisFileDirectory)packages.lock.json'
}.GetEnumerator()) {
    $propertyNode = $project.SelectSingleNode("//msb:$($requiredRestoreProperty.Key)", $namespaceManager)
    if ($null -eq $propertyNode -or $propertyNode.InnerText -ine $requiredRestoreProperty.Value) {
        throw "Host.2016 restore property '$($requiredRestoreProperty.Key)' must equal '$($requiredRestoreProperty.Value)'."
    }
}

foreach ($forbiddenItemType in @('ProjectReference', 'COMReference', 'NativeReference', 'Analyzer', 'UsingTask')) {
    if ($project.SelectNodes("//msb:$forbiddenItemType", $namespaceManager).Count -ne 0) {
        throw "Diagnostic Host.2016 must not contain $forbiddenItemType items."
    }
}
if ($project.SelectNodes('//msb:Exec | //msb:CodeTaskFactory', $namespaceManager).Count -ne 0) {
    throw 'Diagnostic Host.2016 project must not execute external programs or inline build tasks.'
}
$targetNodes = @($project.SelectNodes('//msb:Target', $namespaceManager))
$expectedTargetNames = @('ValidateAutoCad2016References', 'RejectAutodeskCopyLocal')
if (@(Compare-Object -ReferenceObject $expectedTargetNames -DifferenceObject @($targetNodes.Name)).Count -ne 0 -or
    @($targetNodes.Name | Sort-Object -Unique).Count -ne $expectedTargetNames.Count) {
    throw 'Diagnostic Host.2016 may contain only the two reviewed validation targets.'
}
foreach ($targetNode in $targetNodes) {
    $taskElements = @($targetNode.ChildNodes | Where-Object { $_.NodeType -eq [Xml.XmlNodeType]::Element })
    if ($taskElements.Count -eq 0 -or @($taskElements | Where-Object { $_.LocalName -cne 'Error' }).Count -ne 0) {
        throw "Host.2016 target '$($targetNode.Name)' may contain only fail-closed Error tasks."
    }
}

$allowedImports = @(
    '$(MSBuildToolsPath)\Microsoft.Common.props',
    '$(MSBuildToolsPath)\Microsoft.CSharp.targets'
)
$projectImports = @($project.SelectNodes('//msb:Import', $namespaceManager) | ForEach-Object { $_.Project })
$unexpectedImports = @($projectImports | Where-Object { $_ -notin $allowedImports })
if ($unexpectedImports.Count -ne 0 -or @($projectImports | Sort-Object -Unique).Count -ne $allowedImports.Count) {
    throw "Diagnostic Host.2016 contains unexpected or missing MSBuild imports: $($projectImports -join ', ')."
}

$expectedCompileItems = @(
    'CodexAutoCad2016Extension.cs',
    'CodexCad2016Commands.cs',
    'Properties\AssemblyInfo.cs'
)
$compileItems = @(
    $project.SelectNodes('//msb:Compile', $namespaceManager) |
        ForEach-Object { $_.Include.Replace('/', '\') }
)
$compileDifferences = @(Compare-Object -ReferenceObject $expectedCompileItems -DifferenceObject $compileItems)
if ($compileDifferences.Count -ne 0 -or @($compileItems | Sort-Object -Unique).Count -ne $expectedCompileItems.Count) {
    throw "Diagnostic Host.2016 compile items must match the reviewed allowlist:`n$($compileDifferences | Out-String)"
}

$hostProjectDirectory = Split-Path -Parent $projectPath
$hostSources = @(
    foreach ($compileItem in $compileItems) {
        $sourcePath = Join-Path $hostProjectDirectory $compileItem
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Host.2016 compile item is missing: $sourcePath"
        }
        Get-Item -LiteralPath $sourcePath
    }
)

$expectedSourceHashes = [ordered]@{
    'CodexAutoCad2016Extension.cs' = '33253D67711EF23C189A0C32E9A309E38C83656295AD7C3DA0C3BA0111C2E5E6'
    'CodexCad2016Commands.cs' = '2FBB9544935FDB25D9A0893EF2A31109008E90137F8F6861AC9D52C0694A52C3'
    'Properties\AssemblyInfo.cs' = 'AA20B302F8BB95D9710FA9E8639FD875CF4BDF69DC818E98F7C80054CAED8D60'
}
$sourceHashEvidence = @(
    foreach ($compileItem in $expectedCompileItems) {
        $sourcePath = Join-Path $hostProjectDirectory $compileItem
        $actualHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        if ($actualHash -cne $expectedSourceHashes[$compileItem]) {
            throw "Reviewed Host.2016 source hash changed for '$compileItem'; source/comment spoofing is rejected."
        }
        [pscustomobject]@{
            Path = $sourcePath
            Sha256 = $actualHash
        }
    }
)

$forbiddenSourceRules = @(
    [pscustomobject]@{ Category = 'CAD transaction/write'; Pattern = '(?i)(?:\bDocumentLock\b|\bLockDocument\s*\(|\bStart(?:OpenClose)?Transaction\s*\(|\bTransactionManager\b|\bOpenMode\s*\.\s*ForWrite\b|\bUpgradeOpen\s*\(|\bDowngradeOpen\s*\(|\bAppendEntity\s*\(|\bAddNewlyCreatedDBObject\s*\(|\bErase\s*\(|\bWblockCloneObjects\s*\(|\bDeepCloneObjects\s*\(|\bTransformBy\b)' }
    [pscustomobject]@{ Category = 'save or command injection'; Pattern = '(?i)(?:\bSave\s*\(|\bSaveAs\s*\(|\bDxfOut\s*\(|\bQuit\s*\(|\bInvoke\s*\(|\bCloseAndSave\s*\(|\bSetSystemVariable\s*\(|\bSendStringToExecute\s*\(|\bExecuteInCommandContextAsync\s*\(|\.\s*Command(?:Async)?\s*\(|["''](?:_+|\.)?(?:QSAVE|SAVEAS|SAVE|WBLOCK|ERASE)["''])' }
    [pscustomobject]@{ Category = 'process or shell'; Pattern = '(?i)(?:\bSystem\s*\.\s*Diagnostics\b|\bProcessStartInfo\b|\bProcess\s*\.\s*Start\s*\(|\.\s*Start\s*\(|\bShellExecute\b|\bCreateProcess\b|\bcmd(?:\.exe)?\b|\bpowershell(?:\.exe)?\b)' }
    [pscustomobject]@{ Category = 'IPC or network'; Pattern = '(?i)(?:\bSystem\s*\.\s*IO\s*\.\s*Pipes\b|\bNamedPipe\w*\b|\bAnonymousPipe\w*\b|\bPipeStream\b|\bMemoryMappedFile\b|\bSystem\s*\.\s*Net\b|\bHttpClient\b|\bWebRequest\b|\bWebClient\b|\bHttpListener\b|\bSocket\b|\bTcpClient\b|\bUdpClient\b|\\\\\.\\pipe\\)' }
    [pscustomobject]@{ Category = 'file system or registry'; Pattern = '(?i)(?:\bSystem\s*\.\s*IO\s*\.\s*(?:File|Directory|FileInfo|DirectoryInfo|FileStream|StreamReader|StreamWriter)\b|\bFile\s*\.\s*(?:Open|Create|Write|Append|Delete|Move|Copy)\w*\s*\(|\bDirectory\s*\.\s*(?:Create|Delete|Move|Enumerate|GetFiles)\w*\s*\(|\bMicrosoft\s*\.\s*Win32\b|\bRegistry(?:Key)?\b)' }
    [pscustomobject]@{ Category = 'authentication or Agent coupling'; Pattern = '(?i)(?:\bSystem\s*\.\s*Security\s*\.\s*Cryptography\b|\bHMAC\w*\b|\bRandomNumberGenerator\b|\bProtectedData\b|\bCadApprovalGate\b|\bIAgentBridgeClient\b|\bCodex\s*\.\s*AutoCAD\s*\.\s*(?:Bridge|AgentRuntime|Ipc|Security)\b)' }
    [pscustomobject]@{ Category = 'reflection, native, or dynamic execution'; Pattern = '(?i)(?:\bAssembly\s*\.\s*Load(?:From|File)?\s*\(|\bType\s*\.\s*GetType\s*\(|\bActivator\s*\.\s*CreateInstance\s*\(|\bGetMethod\s*\(|\bMethodInfo\s*\.\s*Invoke\s*\(|\bDllImport\b|\bMarshal\s*\.|\bLoadLibrary\b|\bGetProcAddress\b|\bunsafe\b|\bdynamic\b)' }
    [pscustomobject]@{ Category = 'background execution'; Pattern = '(?i)(?:\bSystem\s*\.\s*Threading\b|\bTask\s*\.\s*Run\s*\(|\bThreadPool\b|\bBackgroundWorker\b|\bnew\s+(?:Thread|Timer)\s*\(|\basync\b|\bawait\b)' }
    [pscustomobject]@{ Category = 'diagnostic-stage UI'; Pattern = '(?i)(?:\bPaletteSet\b|\bSystem\s*\.\s*Windows\s*\.\s*Forms\b|\bSystem\s*\.\s*Windows\s*\.\s*Controls\b)' }
)

$sourceViolations = @(
    foreach ($source in $hostSources) {
        foreach ($rule in $forbiddenSourceRules) {
            foreach ($match in @(Select-String -LiteralPath $source.FullName -Pattern $rule.Pattern -AllMatches)) {
                [pscustomobject]@{
                    Category = $rule.Category
                    File = $source.FullName
                    Line = $match.LineNumber
                    Text = $match.Line.Trim()
                }
            }
        }
    }
)
if ($sourceViolations.Count -ne 0) {
    throw "Diagnostic Host.2016 contains forbidden source APIs:`n$($sourceViolations | Format-Table -AutoSize | Out-String)"
}

$systemVariableCalls = @(
    foreach ($match in @($hostSources | Select-String -Pattern 'WriteSystemVariable\s*\(\s*editor\s*,\s*"([^"]+)"\s*\)' -AllMatches)) {
        foreach ($regexMatch in $match.Matches) {
            $regexMatch.Groups[1].Value
        }
    }
)
$expectedSystemVariables = @('ACADVER', 'VERNUM', 'SECURELOAD', 'APPAUTOLOAD', 'DBMOD')
if (@(Compare-Object -ReferenceObject $expectedSystemVariables -DifferenceObject $systemVariableCalls).Count -ne 0 -or
    @($systemVariableCalls | Sort-Object -Unique).Count -ne $expectedSystemVariables.Count) {
    throw "Diagnostic Host.2016 may query only the explicit safe system-variable allowlist: $($expectedSystemVariables -join ', ')."
}
$getSystemVariableMatches = @($hostSources | Select-String -Pattern '\bGetSystemVariable\s*\(' -AllMatches)
$getSystemVariableCount = ($getSystemVariableMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
$safeGetSystemVariableMatches = @($hostSources | Select-String -Pattern 'AutoCadApplication\s*\.\s*GetSystemVariable\s*\(\s*variableName\s*\)' -AllMatches)
$safeGetSystemVariableCount = ($safeGetSystemVariableMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
if ($getSystemVariableCount -ne 1 -or $safeGetSystemVariableCount -ne 1) {
    throw 'GetSystemVariable must occur exactly once through the reviewed safe-variable helper; direct or additional queries are forbidden.'
}
$trustedPathsMentions = @($hostSources | Select-String -Pattern 'TRUSTEDPATHS' -AllMatches)
$trustedPathsMentionCount = ($trustedPathsMentions | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
if ($trustedPathsMentionCount -ne 1 -or $trustedPathsMentions[0].Line -notmatch '(?i)intentionally omitted') {
    throw 'TRUSTEDPATHS may appear exactly once only in the fixed omission notice and must never be queried.'
}

$declaredCommands = @(
    foreach ($match in @($hostSources | Select-String -Pattern '\[CommandMethod\s*\(\s*"([^"]+)"' -AllMatches)) {
        foreach ($regexMatch in $match.Matches) {
            $regexMatch.Groups[1].Value
        }
    }
)
$expectedCommands = @('CODEXCADDOCTOR', 'CODEXCAD')
$commandDifferences = @(Compare-Object -ReferenceObject $expectedCommands -DifferenceObject $declaredCommands)
if ($commandDifferences.Count -ne 0 -or @($declaredCommands | Sort-Object -Unique).Count -ne $expectedCommands.Count) {
    throw "Diagnostic Host.2016 command surface must be exactly CODEXCADDOCTOR and CODEXCAD:`n$($commandDifferences | Out-String)"
}

foreach ($requiredDependencyFile in @($nuGetConfigPath, $packageLockPath, $vendoredPackagePath)) {
    if (-not (Test-Path -LiteralPath $requiredDependencyFile -PathType Leaf)) {
        throw "Required reproducible-build dependency file is missing: $requiredDependencyFile"
    }
}

[xml]$nuGetConfig = Read-Utf8File -Path $nuGetConfigPath
$packageSourceNodes = @($nuGetConfig.configuration.packageSources.add)
$packageSourceClearNodes = @($nuGetConfig.configuration.packageSources.clear)
$expectedFeedRelativePath = '..\..\third_party\nuget'
if ($packageSourceClearNodes.Count -ne 1 -or $packageSourceNodes.Count -ne 1 -or
    $packageSourceNodes[0].value -ine $expectedFeedRelativePath) {
    throw 'The Host.2016 NuGet.Config must clear inherited sources and expose only the repository-local third_party\nuget feed.'
}
$signatureModeNode = @($nuGetConfig.configuration.config.add | Where-Object { $_.key -eq 'signatureValidationMode' })
$trustedCertificateNodes = @($nuGetConfig.configuration.trustedSigners.author.certificate)
$expectedAuthorFingerprint = 'AA12DA22A49BCE7D5C1AE64CC1F3D892F150DA76140F210ABD2CBFFCA2C18A27'
if ($signatureModeNode.Count -ne 1 -or $signatureModeNode[0].value -ine 'require' -or
    $trustedCertificateNodes.Count -ne 1 -or $trustedCertificateNodes[0].fingerprint -ine $expectedAuthorFingerprint -or
    $trustedCertificateNodes[0].hashAlgorithm -ine 'SHA256' -or $trustedCertificateNodes[0].allowUntrustedRoot -ine 'false') {
    throw 'The Host.2016 NuGet.Config must require the reviewed Microsoft author signature and may not trust an unbounded signer set.'
}

$vendoredPackages = @(Get-ChildItem -LiteralPath (Split-Path -Parent $vendoredPackagePath) -Filter '*.nupkg' -File)
if ($vendoredPackages.Count -ne 1 -or $vendoredPackages[0].FullName -ine (Get-Item -LiteralPath $vendoredPackagePath).FullName) {
    throw 'The offline NuGet feed must contain exactly the reviewed net45 reference package.'
}
$expectedPackageSha256 = '23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3'
$expectedPackageSha512 = 'zPJ5Pqc6+cBg4ir33AWryA8CUxJJj68Cs1Cfo8plZt1HH3Q0B/EqVon6LRXw9b8dfQyLYMqTJJk2maXgLhGJIw=='
$packageSha256 = (Get-FileHash -LiteralPath $vendoredPackagePath -Algorithm SHA256).Hash
$sha512Algorithm = [Security.Cryptography.SHA512]::Create()
try {
    $packageSha512 = [Convert]::ToBase64String($sha512Algorithm.ComputeHash([IO.File]::ReadAllBytes($vendoredPackagePath)))
}
finally {
    $sha512Algorithm.Dispose()
}
if ($packageSha256 -ine $expectedPackageSha256 -or $packageSha512 -cne $expectedPackageSha512) {
    throw 'The vendored net45 reference package does not match the reviewed SHA-256/SHA-512 identity.'
}

$packageLock = Read-Utf8File -Path $packageLockPath | ConvertFrom-Json
$expectedFrameworkLocks = @(
    '.NETFramework,Version=v4.5',
    '.NETFramework,Version=v4.5/win',
    '.NETFramework,Version=v4.5/win-arm64',
    '.NETFramework,Version=v4.5/win-x64',
    '.NETFramework,Version=v4.5/win-x86'
)
$actualFrameworkLocks = @($packageLock.dependencies.PSObject.Properties.Name)
$lockDifferences = @(Compare-Object -ReferenceObject $expectedFrameworkLocks -DifferenceObject $actualFrameworkLocks)
$net45Lock = $packageLock.dependencies.'.NETFramework,Version=v4.5'.'Microsoft.NETFramework.ReferenceAssemblies.net45'
$expectedContentHash = 'dcSLNuUX2rfZejsyta2EWZ1W5U6ucbFt697lRg1qiTlTM5ZlYv4uAvuxE6ROy6xLWWhLhOaReCDxkhxcajRYtQ=='
if ($packageLock.version -ne 1 -or $lockDifferences.Count -ne 0 -or $null -eq $net45Lock -or
    $net45Lock.type -cne 'Direct' -or $net45Lock.requested -cne '[1.0.3, 1.0.3]' -or
    $net45Lock.resolved -cne '1.0.3' -or $net45Lock.contentHash -cne $expectedContentHash) {
    throw 'packages.lock.json does not match the exact reviewed net45 dependency and content hash.'
}
foreach ($emptyFrameworkLock in $expectedFrameworkLocks | Where-Object { $_ -ne '.NETFramework,Version=v4.5' }) {
    if (@($packageLock.dependencies.$emptyFrameworkLock.PSObject.Properties).Count -ne 0) {
        throw "Unexpected locked dependency under $emptyFrameworkLock."
    }
}

$packageSignatureResult = Invoke-DotNetIsolated -FilePath $dotnetPath -Arguments @(
    'nuget', 'verify', $vendoredPackagePath, '--all', '--configfile', $nuGetConfigPath
) -WorkingDirectory $repoRoot
$packageSignatureOutput = $packageSignatureResult.Output
$expectedRepositoryFingerprint = '5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4'
$packageSignatureText = $packageSignatureResult.Text
if ($packageSignatureResult.ExitCode -ne 0 -or
    $packageSignatureText.IndexOf($expectedAuthorFingerprint, [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
    $packageSignatureText.IndexOf($expectedRepositoryFingerprint, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw 'NuGet author/repository signature verification failed for the vendored net45 reference package.'
}

$defaultIntermediatePath = Join-Path (Split-Path -Parent $projectPath) 'obj'
$defaultIntermediateManifestBefore = Get-DirectoryManifestHash -Path $defaultIntermediatePath
$outputDirectoryProperty = $outputDirectory.TrimEnd('\') + '/'
$baseIntermediateProperty = $baseIntermediateDirectory.TrimEnd('\') + '/'
$intermediateProperty = $intermediateDirectory.TrimEnd('\') + '/'
$projectExtensionsProperty = $projectExtensionsDirectory.TrimEnd('\') + '/'
$packageCacheProperty = $packageCache.TrimEnd('\') + '/'

$globalBuildProperties = @(
    "/p:Configuration=$Configuration",
    '/p:Platform=Any CPU',
    "/p:AutoCad2016Dir=$AutoCad2016Dir",
    "/p:RestoreConfigFile=$nuGetConfigPath",
    "/p:RestorePackagesPath=$packageCacheProperty",
    '/p:RestoreLockedMode=true',
    '/p:RestorePackagesWithLockFile=true',
    '/p:ImportDirectoryBuildProps=false',
    '/p:ImportDirectoryBuildTargets=false',
    "/p:OutDir=$outputDirectoryProperty",
    "/p:BaseIntermediateOutputPath=$baseIntermediateProperty",
    "/p:IntermediateOutputPath=$intermediateProperty",
    "/p:MSBuildProjectExtensionsPath=$projectExtensionsProperty"
)
$buildArguments = @(
    $solutionPath,
    '/restore',
    '/t:Rebuild',
    '/m:1'
) + $globalBuildProperties + @('/v:minimal')
$buildResult = Invoke-NativeCapture -FilePath $MsBuildPath -Arguments $buildArguments -WorkingDirectory $repoRoot
if ($buildResult.ExitCode -ne 0) {
    throw "Host.2016 build failed with exit code $($buildResult.ExitCode):`n$($buildResult.Text)"
}

$defaultIntermediateManifestAfter = Get-DirectoryManifestHash -Path $defaultIntermediatePath
if ($defaultIntermediateManifestAfter -cne $defaultIntermediateManifestBefore) {
    throw 'The isolated verification build modified the project-local obj directory; MSBuild project-extension isolation failed.'
}

$evaluationArguments = @(
    $projectPath,
    '-nologo',
    '-getProperty:TargetFrameworkVersion,PlatformTarget,Deterministic,ContinuousIntegrationBuild,TreatWarningsAsErrors,RestoreLockedMode,ImportDirectoryBuildProps,ImportDirectoryBuildTargets,DebugSymbols,DebugType,MSBuildProjectExtensionsPath,BaseIntermediateOutputPath,IntermediateOutputPath',
    '-getItem:Compile,ProjectReference,PackageReference,Reference,Content,None,EmbeddedResource,Resource,COMReference,NativeReference,Analyzer',
    "/p:Configuration=$Configuration",
    '/p:Platform=x64',
    "/p:AutoCad2016Dir=$AutoCad2016Dir",
    "/p:RestoreConfigFile=$nuGetConfigPath",
    "/p:RestorePackagesPath=$packageCacheProperty",
    '/p:RestoreLockedMode=true',
    '/p:ImportDirectoryBuildProps=false',
    '/p:ImportDirectoryBuildTargets=false',
    "/p:BaseIntermediateOutputPath=$baseIntermediateProperty",
    "/p:IntermediateOutputPath=$intermediateProperty",
    "/p:MSBuildProjectExtensionsPath=$projectExtensionsProperty"
)
$evaluationResult = Invoke-NativeCapture -FilePath $MsBuildPath -Arguments $evaluationArguments -WorkingDirectory $repoRoot
if ($evaluationResult.ExitCode -ne 0) {
    throw "MSBuild evaluated-graph inspection failed:`n$($evaluationResult.Text)"
}
$jsonStart = $evaluationResult.Text.IndexOf('{')
$jsonEnd = $evaluationResult.Text.LastIndexOf('}')
if ($jsonStart -lt 0 -or $jsonEnd -le $jsonStart) {
    throw "MSBuild evaluated-graph output was not JSON:`n$($evaluationResult.Text)"
}
$evaluatedGraph = $evaluationResult.Text.Substring($jsonStart, $jsonEnd - $jsonStart + 1) | ConvertFrom-Json
$expectedEvaluatedProperties = [ordered]@{
    TargetFrameworkVersion = 'v4.5'
    PlatformTarget = 'x64'
    Deterministic = 'true'
    ContinuousIntegrationBuild = 'true'
    TreatWarningsAsErrors = 'true'
    RestoreLockedMode = 'true'
    ImportDirectoryBuildProps = 'false'
    ImportDirectoryBuildTargets = 'false'
    DebugSymbols = 'false'
    DebugType = 'none'
}
foreach ($property in $expectedEvaluatedProperties.GetEnumerator()) {
    if ([string]$evaluatedGraph.Properties.($property.Key) -cne $property.Value) {
        throw "Evaluated Host.2016 property '$($property.Key)' was '$($evaluatedGraph.Properties.($property.Key))', expected '$($property.Value)'."
    }
}
foreach ($isolatedPathProperty in @{
    MSBuildProjectExtensionsPath = $projectExtensionsDirectory
    BaseIntermediateOutputPath = $baseIntermediateDirectory
    IntermediateOutputPath = $intermediateDirectory
}.GetEnumerator()) {
    $actualPath = [IO.Path]::GetFullPath([string]$evaluatedGraph.Properties.($isolatedPathProperty.Key)).TrimEnd('\')
    $expectedPath = [IO.Path]::GetFullPath([string]$isolatedPathProperty.Value).TrimEnd('\')
    if ($actualPath -cne $expectedPath) {
        throw "Evaluated $($isolatedPathProperty.Key) escaped isolation: '$actualPath' != '$expectedPath'."
    }
}

$evaluatedCompileItems = @($evaluatedGraph.Items.Compile)
$evaluatedCompileIdentities = @($evaluatedCompileItems | ForEach-Object { ([string]$_.Identity).Replace('/', '\') })
if (@(Compare-Object -ReferenceObject $expectedCompileItems -DifferenceObject $evaluatedCompileIdentities).Count -ne 0 -or
    @($evaluatedCompileIdentities | Sort-Object -Unique).Count -ne $expectedCompileItems.Count) {
    throw 'The evaluated Host.2016 compile graph contains injected or missing source files.'
}
foreach ($compileItem in $evaluatedCompileItems) {
    if ([IO.Path]::GetFullPath([string]$compileItem.DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($projectPath)) {
        throw "Compile item '$($compileItem.Identity)' was injected by '$($compileItem.DefiningProjectFullPath)'."
    }
}
foreach ($forbiddenEvaluatedItemType in @('ProjectReference', 'Content', 'None', 'EmbeddedResource', 'Resource', 'COMReference', 'NativeReference', 'Analyzer')) {
    if (@($evaluatedGraph.Items.($forbiddenEvaluatedItemType)).Count -ne 0) {
        throw "The evaluated Host.2016 graph contains forbidden $forbiddenEvaluatedItemType items."
    }
}
$expectedEvaluatedReferences = @(
    'System',
    'System.Core',
    'accoremgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'acdbmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'acmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'mscorlib'
)
$evaluatedReferences = @($evaluatedGraph.Items.Reference)
if (@(Compare-Object -ReferenceObject $expectedEvaluatedReferences -DifferenceObject @($evaluatedReferences.Identity)).Count -ne 0) {
    throw 'The evaluated Host.2016 reference graph differs from the exact reviewed allowlist.'
}
foreach ($reference in $evaluatedReferences) {
    if ($reference.Identity -ceq 'mscorlib') {
        $expectedMscorlibTarget = Join-Path $packageCache 'microsoft.netframework.referenceassemblies.net45\1.0.3\build\Microsoft.NETFramework.ReferenceAssemblies.net45.targets'
        if ([IO.Path]::GetFullPath([string]$reference.DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($expectedMscorlibTarget)) {
            throw "The pinned net45 package did not define mscorlib from the isolated signed package cache: '$($reference.DefiningProjectFullPath)'."
        }
    }
    elseif ([IO.Path]::GetFullPath([string]$reference.DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($projectPath)) {
        throw "Reference '$($reference.Identity)' was injected by '$($reference.DefiningProjectFullPath)'."
    }
}
if ((Get-DirectoryManifestHash -Path $defaultIntermediatePath) -cne $defaultIntermediateManifestBefore) {
    throw 'MSBuild evaluated-graph inspection modified the project-local obj directory.'
}
$evaluatedPackages = @($evaluatedGraph.Items.PackageReference)
if ($evaluatedPackages.Count -ne 1 -or
    $evaluatedPackages[0].Identity -cne 'Microsoft.NETFramework.ReferenceAssemblies.net45' -or
    $evaluatedPackages[0].Version -cne '[1.0.3]' -or
    $evaluatedPackages[0].PrivateAssets -cne 'all' -or
    $evaluatedPackages[0].IncludeAssets -cne 'runtime;build;native;contentfiles;analyzers' -or
    [IO.Path]::GetFullPath([string]$evaluatedPackages[0].DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($projectPath)) {
    throw 'The evaluated Host.2016 PackageReference graph differs from the exact reviewed allowlist.'
}
$hostDll = Join-Path $outputDirectory 'Codex.AutoCAD.Host.2016.dll'
if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    throw "Build output is missing: $hostDll"
}
if ((Get-PeMachine -Path $hostDll) -ne 'x64') {
    throw 'Host.2016 output is not an x64 PE image.'
}

$hostAssemblyIdentity = [Reflection.AssemblyName]::GetAssemblyName($hostDll)
if ($hostAssemblyIdentity.Name -ne 'Codex.AutoCAD.Host.2016') {
    throw "Unexpected Host.2016 assembly identity: $($hostAssemblyIdentity.FullName)"
}

try {
    $loadedHostAssembly = [Reflection.Assembly]::LoadFile((Get-Item -LiteralPath $hostDll).FullName)
    $outputReferences = @($loadedHostAssembly.GetReferencedAssemblies())
}
catch {
    throw "Could not inspect Host.2016 output assembly references: $($_.Exception.Message)"
}

$expectedOutputReferences = @(
    'mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089',
    'Acdbmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'accoremgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null'
)
$actualOutputReferences = @($outputReferences | ForEach-Object { $_.FullName })
if (@(Compare-Object -ReferenceObject $expectedOutputReferences -DifferenceObject $actualOutputReferences -CaseSensitive).Count -ne 0 -or
    @($actualOutputReferences | Sort-Object -Unique).Count -ne $expectedOutputReferences.Count) {
    throw "Host.2016 output assembly-reference table differs from the exact allowlist:`n$($actualOutputReferences -join [Environment]::NewLine)"
}

$hostBytes = [IO.File]::ReadAllBytes($hostDll)
$binaryText = [Text.Encoding]::ASCII.GetString($hostBytes)
$binaryUtf8Text = [Text.Encoding]::UTF8.GetString($hostBytes)
$binaryUnicodeText = [Text.Encoding]::Unicode.GetString($hostBytes)
foreach ($forbiddenBuildPath in @($verificationRoot, $repoRoot, $hostProjectDirectory)) {
    if ($binaryUtf8Text.IndexOf($forbiddenBuildPath, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $binaryUnicodeText.IndexOf($forbiddenBuildPath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Host.2016 output leaks a build path (usually a random PDB path): $forbiddenBuildPath"
    }
}
if ($binaryUtf8Text.IndexOf('.pdb', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
    $binaryUnicodeText.IndexOf('.pdb', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw 'Host.2016 Release output contains a PDB path marker.'
}
if ($binaryText -notmatch [regex]::Escape('.NETFramework,Version=v4.5')) {
    throw 'Host.2016 output does not contain the .NET Framework 4.5 target framework marker.'
}

$programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
    throw 'ProgramFiles(x86) is unavailable; trusted ildasm discovery cannot continue.'
}
$ildasmSearchRoots = @(
    (Join-Path $programFilesX86 'Microsoft SDKs\Windows\v10.0A\bin'),
    (Join-Path $programFilesX86 'Windows Kits\10\bin')
)
$ildasmCandidatePaths = @(
    foreach ($searchRoot in $ildasmSearchRoots) {
        if (Test-Path -LiteralPath $searchRoot -PathType Container) {
            Get-ChildItem -LiteralPath $searchRoot -Recurse -Filter 'ildasm.exe' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '(?i)\\NETFX\s+[^\\]+\s+Tools(?:\\x64)?\\ildasm\.exe$' } |
                ForEach-Object { $_.FullName }
        }
    }
) | Sort-Object -Unique
$trustedIldasmCandidates = @(
    foreach ($candidatePath in $ildasmCandidatePaths) {
        try {
            Get-TrustedMicrosoftTool -Path $candidatePath -Label 'ildasm' -DescriptionPattern '^Microsoft \.NET Framework IL disassembler$' -MinimumMajorVersion 4 -MaximumMajorVersion 4
        }
        catch {
            # An untrusted lookalike is never selected. Other official SDK installations may still be considered.
        }
    }
)
if ($trustedIldasmCandidates.Count -eq 0) {
    throw 'No Microsoft-signed .NET Framework ildasm.exe was found in a known Windows SDK directory.'
}
$ildasmEvidence = @($trustedIldasmCandidates | Sort-Object @{ Expression = { [Version]$_.Version }; Descending = $true }, Path | Select-Object -First 1)[0]
$ildasmPath = $ildasmEvidence.Path
$ilOutputPath = Join-Path $verificationRoot 'Codex.AutoCAD.Host.2016.il'
$ildasmResult = Invoke-NativeCapture -FilePath $ildasmPath -Arguments @(
    '/text', '/nobar', '/tokens', '/utf8', '/caverbal', "/out=$ilOutputPath", $hostDll
) -WorkingDirectory $repoRoot
if ($ildasmResult.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ilOutputPath -PathType Leaf)) {
    throw "ildasm failed with exit code $($ildasmResult.ExitCode); refusing to continue the exact metadata gate.`n$($ildasmResult.Text)"
}
$ilDisassembly = Read-Utf8File -Path $ilOutputPath
if ([string]::IsNullOrWhiteSpace($ilDisassembly)) {
    throw 'ildasm produced an empty IL file.'
}

$expectedMethodDefinitions = [ordered]@{
    '06000001' = 'CodexAutoCad2016Extension::Initialize|public hidebysig newslot virtual final instance void Initialize() cil managed'
    '06000002' = 'CodexAutoCad2016Extension::Terminate|public hidebysig newslot virtual final instance void Terminate() cil managed'
    '06000003' = 'CodexAutoCad2016Extension::.ctor|public hidebysig specialname rtspecialname instance void .ctor() cil managed'
    '06000004' = 'CodexCad2016Commands::RunDoctor|public hidebysig instance void RunDoctor() cil managed'
    '06000005' = 'CodexCad2016Commands::ShowCandidateStatus|public hidebysig instance void ShowCandidateStatus() cil managed'
    '06000006' = 'CodexCad2016Commands::WriteSystemVariable|private hidebysig static void WriteSystemVariable(class [accoremgd]Autodesk.AutoCAD.EditorInput.Editor editor, string variableName) cil managed'
    '06000007' = 'CodexCad2016Commands::.ctor|public hidebysig specialname rtspecialname instance void .ctor() cil managed'
}
$methodDefinitions = @(Get-IlMethodDefinitions -IlText $ilDisassembly)
if ($methodDefinitions.Count -ne $expectedMethodDefinitions.Count -or
    @($methodDefinitions.Token | Sort-Object -Unique).Count -ne $expectedMethodDefinitions.Count) {
    throw "Host.2016 MethodDef count/token set changed; expected exactly $($expectedMethodDefinitions.Count), got $($methodDefinitions.Count)."
}
foreach ($expectedMethod in $expectedMethodDefinitions.GetEnumerator()) {
    $actualMethods = @($methodDefinitions | Where-Object { $_.Token -ceq $expectedMethod.Key })
    $actualIdentity = if ($actualMethods.Count -eq 1) { '{0}|{1}' -f $actualMethods[0].Name, $actualMethods[0].Header } else { '<missing-or-duplicate>' }
    if ($actualIdentity -cne $expectedMethod.Value) {
        throw "MethodDef $($expectedMethod.Key) changed: '$actualIdentity' != '$($expectedMethod.Value)'."
    }
}

$expectedMemberReferences = [ordered]@{
    '0A000001' = '[mscorlib]System.Runtime.CompilerServices.CompilationRelaxationsAttribute::.ctor(int32)'
    '0A000002' = '[mscorlib]System.Runtime.CompilerServices.RuntimeCompatibilityAttribute::.ctor()'
    '0A000003' = '[mscorlib]System.Diagnostics.DebuggableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggableAttribute/DebuggingModes)'
    '0A000004' = '[mscorlib]System.Reflection.AssemblyTitleAttribute::.ctor(string)'
    '0A000005' = '[mscorlib]System.Reflection.AssemblyDescriptionAttribute::.ctor(string)'
    '0A000006' = '[mscorlib]System.Reflection.AssemblyCompanyAttribute::.ctor(string)'
    '0A000007' = '[mscorlib]System.Reflection.AssemblyProductAttribute::.ctor(string)'
    '0A000008' = '[mscorlib]System.Reflection.AssemblyCopyrightAttribute::.ctor(string)'
    '0A000009' = '[mscorlib]System.Runtime.InteropServices.ComVisibleAttribute::.ctor(bool)'
    '0A00000A' = '[mscorlib]System.Runtime.InteropServices.GuidAttribute::.ctor(string)'
    '0A00000B' = '[mscorlib]System.Reflection.AssemblyFileVersionAttribute::.ctor(string)'
    '0A00000C' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.ExtensionApplicationAttribute::.ctor(class [mscorlib]System.Type)'
    '0A00000D' = '[accoremgd]Autodesk.AutoCAD.Runtime.CommandClassAttribute::.ctor(class [mscorlib]System.Type)'
    '0A00000E' = '[mscorlib]System.Runtime.Versioning.TargetFrameworkAttribute::.ctor(string)'
    '0A00000F' = '[accoremgd]Autodesk.AutoCAD.Runtime.CommandMethodAttribute::.ctor(string, valuetype [accoremgd]Autodesk.AutoCAD.Runtime.CommandFlags)'
    '0A000010' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Core.Application::get_DocumentManager()'
    '0A000011' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollection::get_MdiActiveDocument()'
    '0A000012' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper::op_Inequality(class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper, class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper)'
    '0A000013' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Document::get_Editor()'
    '0A000014' = '[accoremgd]Autodesk.AutoCAD.EditorInput.Editor::WriteMessage(string)'
    '0A000015' = '[mscorlib]System.Object::.ctor()'
    '0A000016' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper::op_Equality(class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper, class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper)'
    '0A000017' = '[mscorlib]System.Environment::get_Is64BitProcess()'
    '0A000018' = '[accoremgd]Autodesk.AutoCAD.EditorInput.Editor::WriteMessage(string, object[])'
    '0A000019' = '[mscorlib]System.Environment::get_Version()'
    '0A00001A' = '[mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)'
    '0A00001B' = '[mscorlib]System.Type::get_Assembly()'
    '0A00001C' = '[mscorlib]System.Reflection.Assembly::GetName()'
    '0A00001D' = '[mscorlib]System.Reflection.AssemblyName::get_Version()'
    '0A00001E' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Core.Application::GetSystemVariable(string)'
    '0A00001F' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.Exception::get_ErrorStatus()'
}
$memberReferences = Get-IlMemberReferenceMap -IlText $ilDisassembly
if ($memberReferences.Count -ne $expectedMemberReferences.Count) {
    throw "Host.2016 MemberRef count changed; expected exactly $($expectedMemberReferences.Count), got $($memberReferences.Count)."
}
foreach ($expectedMember in $expectedMemberReferences.GetEnumerator()) {
    if (-not $memberReferences.ContainsKey($expectedMember.Key) -or $memberReferences[$expectedMember.Key] -cne $expectedMember.Value) {
        $actualMember = if ($memberReferences.ContainsKey($expectedMember.Key)) { $memberReferences[$expectedMember.Key] } else { '<missing>' }
        throw "MemberRef $($expectedMember.Key) changed: '$actualMember' != '$($expectedMember.Value)'."
    }
}
Assert-NoHighRiskMemberReferences -Signatures @($memberReferences.Values)

$semanticIl = [regex]::Replace($ilDisassembly, '//[^\r\n]*(?=\r|\n|$)', '')
$semanticIl = [regex]::Replace($semanticIl, '/\*[^*]*\*/', '')
$semanticIl = [regex]::Replace($semanticIl, '\s+', ' ').Trim()
$extensionAttributeNeedle = 'Autodesk.AutoCAD.Runtime.ExtensionApplicationAttribute::.ctor(class [mscorlib]System.Type) = {type(Codex.AutoCAD.Host2016.CodexAutoCad2016Extension)}'
$commandClassNeedle = 'Autodesk.AutoCAD.Runtime.CommandClassAttribute::.ctor(class [mscorlib]System.Type) = {type(Codex.AutoCAD.Host2016.CodexCad2016Commands)}'
if ([regex]::Matches($semanticIl, 'Autodesk\.AutoCAD\.Runtime\.ExtensionApplicationAttribute::\.ctor\(').Count -ne 1 -or
    $semanticIl.IndexOf($extensionAttributeNeedle, [StringComparison]::Ordinal) -lt 0) {
    throw 'The real assembly ExtensionApplication attribute/type differs from the exact reviewed value.'
}
if ([regex]::Matches($semanticIl, 'Autodesk\.AutoCAD\.Runtime\.CommandClassAttribute::\.ctor\(').Count -ne 1 -or
    $semanticIl.IndexOf($commandClassNeedle, [StringComparison]::Ordinal) -lt 0) {
    throw 'The real assembly CommandClass attribute/type differs from the exact reviewed value.'
}
$extensionInterfaceNeedle = '.class public auto ansi sealed beforefieldinit Codex.AutoCAD.Host2016.CodexAutoCad2016Extension extends [mscorlib]System.Object implements [Acdbmgd]Autodesk.AutoCAD.Runtime.IExtensionApplication'
if ($semanticIl.IndexOf($extensionInterfaceNeedle, [StringComparison]::Ordinal) -lt 0) {
    throw 'CodexAutoCad2016Extension does not implement the exact AutoCAD IExtensionApplication interface.'
}
if ([regex]::Matches($semanticIl, 'Autodesk\.AutoCAD\.Runtime\.CommandMethodAttribute::\.ctor\(').Count -ne 2) {
    throw 'The real assembly must contain exactly two CommandMethod attributes.'
}
$expectedCommandAttributes = [ordered]@{
    '06000004' = "Autodesk.AutoCAD.Runtime.CommandMethodAttribute::.ctor(string, valuetype [accoremgd]Autodesk.AutoCAD.Runtime.CommandFlags) = {string('CODEXCADDOCTOR') int32(0)}"
    '06000005' = "Autodesk.AutoCAD.Runtime.CommandMethodAttribute::.ctor(string, valuetype [accoremgd]Autodesk.AutoCAD.Runtime.CommandFlags) = {string('CODEXCAD') int32(0)}"
}
foreach ($commandAttribute in $expectedCommandAttributes.GetEnumerator()) {
    $method = @($methodDefinitions | Where-Object { $_.Token -ceq $commandAttribute.Key })[0]
    $methodSemantic = [regex]::Replace($method.BodyCanonical, '/\*[^*]*\*/', '')
    $methodSemantic = [regex]::Replace($methodSemantic, '\s+', ' ').Trim()
    if ($methodSemantic.IndexOf($commandAttribute.Value, [StringComparison]::Ordinal) -lt 0) {
        throw "CommandMethod metadata/arguments changed on MethodDef $($commandAttribute.Key)."
    }
}
foreach ($method in @($methodDefinitions | Where-Object { $_.Token -notin @('06000004', '06000005') })) {
    if ($method.BodyCanonical.IndexOf('CommandMethodAttribute', [StringComparison]::Ordinal) -ge 0) {
        throw "Unexpected CommandMethod attribute on $($method.Token) $($method.Name)."
    }
}
$customAttributeEvidence = [pscustomobject]@{
    ExtensionApplicationType = 'Codex.AutoCAD.Host2016.CodexAutoCad2016Extension'
    CommandClassType = 'Codex.AutoCAD.Host2016.CodexCad2016Commands'
    Commands = @(
        [pscustomobject]@{ MethodDef = '06000004'; Method = 'RunDoctor'; GlobalName = 'CODEXCADDOCTOR'; Flags = 0 },
        [pscustomobject]@{ MethodDef = '06000005'; Method = 'ShowCandidateStatus'; GlobalName = 'CODEXCAD'; Flags = 0 }
    )
}
$forbiddenBinaryTokens = @(
    'ProcessStartInfo',
    'System.IO.Pipes', 'NamedPipe', 'AnonymousPipe', 'PipeStream', 'MemoryMappedFile',
    'System.Net', 'HttpClient', 'WebRequest', 'WebClient', 'HttpListener', 'TcpClient', 'UdpClient',
    'System.IO.File', 'System.IO.Directory', 'FileStream', 'StreamReader', 'StreamWriter',
    'Microsoft.Win32', 'RegistryKey',
    'DocumentLock', 'StartTransaction', 'StartOpenCloseTransaction', 'ForWrite', 'UpgradeOpen',
    'AppendEntity', 'AddNewlyCreatedDBObject', 'WblockCloneObjects', 'DeepCloneObjects',
    'SaveAs', 'CloseAndSave', 'SetSystemVariable', 'SendStringToExecute', 'ExecuteInCommandContextAsync',
    'System.Security.Cryptography', 'HMAC', 'CadApprovalGate', 'IAgentBridgeClient',
    'Codex.AutoCAD.Bridge', 'Codex.AutoCAD.AgentRuntime', 'Codex.AutoCAD.Ipc', 'Codex.AutoCAD.Security',
    'DllImportAttribute', 'LoadLibrary', 'GetProcAddress',
    'System.Threading', 'BackgroundWorker', 'PaletteSet'
)
$binaryTokenHits = @(
    foreach ($token in $forbiddenBinaryTokens) {
        if ($binaryText.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $token
        }
    }
)
if ($binaryTokenHits.Count -ne 0) {
    throw "Host.2016 output contains forbidden API metadata tokens: $($binaryTokenHits -join ', ')."
}

$outputFiles = @(Get-ChildItem -LiteralPath $outputDirectory -Recurse -File)
if ($outputFiles.Count -ne 1 -or $outputFiles[0].FullName -cne (Get-Item -LiteralPath $hostDll).FullName) {
    throw "The isolated Host.2016 output must contain exactly the reviewed DLL and no PDB/config/script/native payload:`n$($outputFiles.FullName -join [Environment]::NewLine)"
}

$copiedAutodeskFiles = @(
    foreach ($managedApiName in $managedApiNames) {
        $copiedPath = Join-Path $outputDirectory $managedApiName
        if (Test-Path -LiteralPath $copiedPath) { $copiedPath }
    }
)
if ($copiedAutodeskFiles.Count -ne 0) {
    throw "Autodesk managed assemblies were copied to the plugin output:`n$($copiedAutodeskFiles -join [Environment]::NewLine)"
}

$rebuildRoot = Join-Path $artifactsRoot ("autocad2016-host-rebuild-{0}" -f [Guid]::NewGuid().ToString('N'))
$rebuildOutputDirectory = Join-Path $rebuildRoot 'bin'
$rebuildBaseIntermediateDirectory = Join-Path $rebuildRoot 'obj-base'
$rebuildIntermediateDirectory = Join-Path $rebuildRoot 'obj-compile'
$rebuildProjectExtensionsDirectory = Join-Path $rebuildRoot 'obj-project-extensions'
$rebuildPackageCache = Join-Path $rebuildRoot 'packages'
foreach ($directory in @($rebuildOutputDirectory, $rebuildBaseIntermediateDirectory, $rebuildIntermediateDirectory, $rebuildProjectExtensionsDirectory, $rebuildPackageCache)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$rebuildProperties = @(
    "/p:Configuration=$Configuration",
    '/p:Platform=Any CPU',
    "/p:AutoCad2016Dir=$AutoCad2016Dir",
    "/p:RestoreConfigFile=$nuGetConfigPath",
    "/p:RestorePackagesPath=$($rebuildPackageCache.TrimEnd('\'))/",
    '/p:RestoreLockedMode=true',
    '/p:RestorePackagesWithLockFile=true',
    '/p:ImportDirectoryBuildProps=false',
    '/p:ImportDirectoryBuildTargets=false',
    "/p:OutDir=$($rebuildOutputDirectory.TrimEnd('\'))/",
    "/p:BaseIntermediateOutputPath=$($rebuildBaseIntermediateDirectory.TrimEnd('\'))/",
    "/p:IntermediateOutputPath=$($rebuildIntermediateDirectory.TrimEnd('\'))/",
    "/p:MSBuildProjectExtensionsPath=$($rebuildProjectExtensionsDirectory.TrimEnd('\'))/"
)
$rebuildArguments = @($solutionPath, '/restore', '/t:Rebuild', '/m:1') + $rebuildProperties + @('/v:minimal')
$rebuildResult = Invoke-NativeCapture -FilePath $MsBuildPath -Arguments $rebuildArguments -WorkingDirectory $repoRoot
if ($rebuildResult.ExitCode -ne 0) {
    throw "The independent deterministic rebuild failed with exit code $($rebuildResult.ExitCode):`n$($rebuildResult.Text)"
}
if ((Get-DirectoryManifestHash -Path $defaultIntermediatePath) -cne $defaultIntermediateManifestBefore) {
    throw 'The independent deterministic rebuild modified the project-local obj directory.'
}
$rebuildHostDll = Join-Path $rebuildOutputDirectory 'Codex.AutoCAD.Host.2016.dll'
$rebuildOutputFiles = @(Get-ChildItem -LiteralPath $rebuildOutputDirectory -Recurse -File)
if ($rebuildOutputFiles.Count -ne 1 -or -not (Test-Path -LiteralPath $rebuildHostDll -PathType Leaf)) {
    throw 'The deterministic rebuild output must contain exactly one Host.2016 DLL.'
}
$hostSha256 = (Get-FileHash -LiteralPath $hostDll -Algorithm SHA256).Hash
$rebuildHostSha256 = (Get-FileHash -LiteralPath $rebuildHostDll -Algorithm SHA256).Hash
if ($hostSha256 -cne $rebuildHostSha256) {
    throw "Release Host.2016 is not bit-for-bit reproducible across independent output/intermediate/package paths: $hostSha256 != $rebuildHostSha256"
}
$deterministicRebuildEvidence = [pscustomobject]@{
    FirstPath = $hostDll
    FirstSha256 = $hostSha256
    SecondPath = $rebuildHostDll
    SecondSha256 = $rebuildHostSha256
    BitForBitMatch = $true
    PdbProduced = $false
}
$memberReferenceEvidence = @(
    $memberReferences.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{ Token = $_.Name; Signature = $_.Value }
    }
)
$methodDefinitionEvidence = @(
    $methodDefinitions | Sort-Object Token | ForEach-Object {
        [pscustomobject]@{ Token = $_.Token; Name = $_.Name; Header = $_.Header }
    }
)

[pscustomobject]@{
    Ok = $true
    Status = 'compiled-candidate-not-runtime-verified-by-this-script'
    AutoCad = $acadEvidence
    ManagedApis = $managedApiEvidence
    Toolchain = [pscustomobject]@{
        MSBuild = $msbuildEvidence
        DotNetHost = $dotnetEvidence
        ResolvedSdkFromRepoRoot = $resolvedDotnetSdk
        Ildasm = $ildasmEvidence
    }
    Host = [pscustomobject]@{
        Path = $hostDll
        TargetFramework = '.NETFramework,Version=v4.5'
        Architecture = 'x64'
        Sha256 = $hostSha256
        ReferencedAssemblies = $actualOutputReferences
        OutputFiles = @($outputFiles.FullName)
    }
    DeterministicRebuild = $deterministicRebuildEvidence
    BuildIsolation = [pscustomobject]@{
        VerificationRoot = $verificationRoot
        BaseIntermediateOutputPath = $baseIntermediateDirectory
        IntermediateOutputPath = $intermediateDirectory
        MSBuildProjectExtensionsPath = $projectExtensionsDirectory
        RestorePackagesPath = $packageCache
        DotNetCliHome = $dotnetCliHome
        DotNetNuGetPackages = $dotnetNuGetPackages
        DotNetHttpCache = $dotnetHttpCache
        DotNetSkipFirstTimeExperience = $true
        DotNetTelemetryOptOut = $true
        ProjectLocalObjManifestBefore = $defaultIntermediateManifestBefore
        ProjectLocalObjManifestAfter = (Get-DirectoryManifestHash -Path $defaultIntermediatePath)
    }
    Project = [pscustomobject]@{
        Path = $projectPath
        Sha256 = $projectSha256
        Sources = $sourceHashEvidence
    }
    IlMetadata = [pscustomobject]@{
        MethodDefinitions = $methodDefinitionEvidence
        MemberReferences = $memberReferenceEvidence
        RegistrationAttributes = $customAttributeEvidence
    }
    VerifierSelfTests = $ruleSelfTestEvidence
    AutodeskAssembliesCopiedToOutput = $false
    SolutionIsolationVerified = $true
    DedicatedSolutionBuilt = $true
    DependencyRestoreMode = 'host-project-local-signed-package-locked'
    NuGetConfig = $nuGetConfigPath
    PackageLock = $packageLockPath
    VendoredPackage = [pscustomobject]@{
        Path = $vendoredPackagePath
        Sha256 = $packageSha256
        Sha512 = $packageSha512
        ContentHash = $expectedContentHash
        AuthorCertificateSha256 = $expectedAuthorFingerprint
        RepositoryCertificateSha256 = $expectedRepositoryFingerprint
        SignatureVerificationOutput = $packageSignatureOutput
    }
    DeterministicBuildSettingsVerified = $true
    NoPdbOrBuildPathLeakVerified = $true
    EvaluatedProjectGraphAllowlistVerified = $true
    ProjectReferenceAllowlistVerified = $true
    SourceHashAllowlistVerified = $true
    OutputReferenceAllowlistVerified = $true
    OutputPayloadAllowlistVerified = $true
    IlMemberReferenceAllowlistVerified = $true
    IlMethodDefinitionAllowlistVerified = $true
    AutoCadRegistrationAttributesVerified = $true
    HighRiskMemberNegativeSamplesVerified = $true
    SafeSystemVariableAllowlistVerified = $true
    NetLoadVerified = $false
} | ConvertTo-Json -Depth 8
Complete-CodexBuildSafety -State $buildSafety -Stage 'host-verifier' | Out-Null

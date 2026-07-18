[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$MsBuildPath,

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
$verificationRoot = Join-Path $repoRoot ("artifacts\autocad2016-host-verify-{0}" -f [Guid]::NewGuid().ToString('N'))
$outputDirectory = Join-Path $verificationRoot 'bin'
$intermediateDirectory = Join-Path $verificationRoot 'obj'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $intermediateDirectory -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $msbuildCommand = Get-Command msbuild -ErrorAction Stop
    $MsBuildPath = $msbuildCommand.Source
}
$MsBuildPath = [IO.Path]::GetFullPath($MsBuildPath)

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
    if ($item.VersionInfo.CompanyName -notmatch '(?i)Autodesk') {
        throw "Expected Autodesk company metadata for $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?i)Autodesk') {
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
    }
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Host.2016 project not found: $projectPath"
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

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
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

foreach ($referenceName in @('accoremgd', 'acdbmgd', 'acmgd')) {
    $reference = $project.SelectSingleNode("//msb:Reference[starts-with(translate(@Include, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '$referenceName')]", $namespaceManager)
    if ($null -eq $reference -or $null -eq $reference.Private -or $reference.Private -ne 'false') {
        throw "Autodesk reference '$referenceName' must set Private=false."
    }
}

$hostSources = Get-ChildItem -LiteralPath (Split-Path -Parent $projectPath) -Filter '*.cs' -File -Recurse
$forbidden = $hostSources | Select-String -Pattern 'DocumentLock|LockDocument|StartTransaction|SaveAs|QSAVE|SendStringToExecute|Editor\.Command|Process\.Start|NamedPipe|HMAC'
if ($forbidden) {
    throw "The diagnostic Host.2016 contains forbidden write/process/IPC APIs:`n$($forbidden | Out-String)"
}

$arguments = @(
    $projectPath,
    '/t:Rebuild',
    '/m:1',
    "/p:Configuration=$Configuration",
    '/p:Platform=x64',
    "/p:AutoCad2016Dir=$AutoCad2016Dir",
    "/p:RestorePackagesPath=$repoRoot\packages",
    "/p:OutDir=$outputDirectory\",
    "/p:IntermediateOutputPath=$intermediateDirectory\",
    '/v:minimal'
)
if (-not $NoRestore) {
    $arguments = @('/restore') + $arguments
}

& $MsBuildPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Host.2016 build failed with exit code $LASTEXITCODE."
}

$hostDll = Join-Path $outputDirectory 'Codex.AutoCAD.Host.2016.dll'
if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    throw "Build output is missing: $hostDll"
}
if ((Get-PeMachine -Path $hostDll) -ne 'x64') {
    throw 'Host.2016 output is not an x64 PE image.'
}

$binaryText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($hostDll))
if ($binaryText -notmatch [regex]::Escape('.NETFramework,Version=v4.5')) {
    throw 'Host.2016 output does not contain the .NET Framework 4.5 target framework marker.'
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

[pscustomobject]@{
    Ok = $true
    Status = 'compiled-candidate-not-netload-verified'
    AutoCad = $acadEvidence
    ManagedApis = $managedApiEvidence
    Host = [pscustomobject]@{
        Path = $hostDll
        TargetFramework = '.NETFramework,Version=v4.5'
        Architecture = 'x64'
        Sha256 = (Get-FileHash -LiteralPath $hostDll -Algorithm SHA256).Hash
    }
    AutodeskAssembliesCopiedToOutput = $false
    NetLoadVerified = $false
} | ConvertTo-Json -Depth 6

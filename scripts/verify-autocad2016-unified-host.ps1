[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [string]$MsBuildPath,
    [string]$EvidencePath,
    [switch]$RuleSelfTestOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016'
$projectPath = Join-Path $projectRoot 'Codex.AutoCAD.Host.2016.csproj'
$solutionPath = Join-Path $repoRoot 'Codex.AutoCAD.2016.sln'
$mainSolutionPath = Join-Path $repoRoot 'Codex.AutoCAD.sln'
$specProjectPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Contracts.Specs\Codex.AutoCAD.Contracts.Specs.csproj'
$nuGetConfigPath = Join-Path $projectRoot 'NuGet.Config'
$offlinePackagePath = Join-Path $repoRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
$phase2Script = Join-Path $repoRoot 'scripts\verify-phase2.ps1'
$readOnlyContextScript = Join-Path $repoRoot 'scripts\verify-autocad2016-readonly-context.ps1'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)
$stageRoot = Join-Path $repoRoot ('artifacts\u16-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
$lf = [string][char]10
$cr = [string][char]13

$expectedSdk = '8.0.319'
$expectedSpecCount = 32
$expectedPhase2Count = 162
$expectedReadOnlyCount = 25
$expectedPublicBytes = 2225
$expectedPublicSha256 = 'c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423'
$expectedMappingBytes = 2198
$expectedMappingSha256 = 'e57ebb86e98216a501e8de0c702fe64e65a3db9e391be4a7cc7a6cfdcac71e18'
$expectedCandidateSha256 = 'F5D8007526467ED77A240596633892258ADC5CDC6F4B57A47B5578818AD172E0'
$expectedCandidateSize = 87552
$expectedNormalizedIlSha256 = '1DFA07D870AB99EC53D303185A970D9F5939CFA5379BA7908C506CA9B1F89455'
$expectedMethodCount = 509
$expectedMemberRefCount = 326
$expectedTypeCount = 64
$expectedFieldCount = 280
$expectedManifestSha256 = '1D826300DBF447C0AC65AC058AC484FFDA1A122AF4713C32255CE7161A4F3F1A'
$expectedPackageSha256 = '23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3'
$projectGuid = '{C4AB73B7-44D5-4BA4-9C9F-584338F0DA16}'

$expectedCompileItems = @(
    'CodexAutoCad2016Extension.cs',
    'CodexCad2016Commands.cs',
    'EmbeddedContractGlobalUsings.cs',
    'EmbeddedCadValidationFailure.cs',
    'CadContextJsonMapper.cs',
    'DocumentContextRegistry.cs',
    'UnifiedReadOnlyContextRuntime.cs',
    'UnifiedPaletteRuntime.cs',
    'UnifiedPaletteController.cs',
    'UnifiedPalettePanel.cs',
    '..\Codex.AutoCAD.Contracts\ProtocolConstants.cs',
    '..\Codex.AutoCAD.Contracts\Geometry.cs',
    '..\Codex.AutoCAD.Contracts\CadContextJsonV1Contracts.cs',
    '..\Codex.AutoCAD.Contracts\CadContextJsonV1Codec.cs',
    '..\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlySelectionCapture.cs',
    '..\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlyContextSnapshot.cs',
    '..\Codex.AutoCAD.Host.2016.ReadOnlyContext\CanonicalSelectionHash.cs',
    'Properties\AssemblyInfo.cs'
)
$expectedReferences = @(
    'System',
    'System.Core',
    'System.Drawing',
    'System.Xaml',
    'WindowsBase',
    'PresentationCore',
    'PresentationFramework',
    'accoremgd',
    'acdbmgd',
    'acmgd'
)
$expectedAssemblyRefs = @(
    'mscorlib',
    'Acdbmgd',
    'accoremgd',
    'System.Core',
    'System',
    'Acmgd',
    'System.Drawing',
    'WindowsBase',
    'PresentationFramework',
    'PresentationCore'
)
$expectedCommands = [ordered]@{
    'CODEXCADDOCTOR' = 0
    'CODEXCAD' = 0
    'CODEX16PAL' = 0
    'CODEX16PALINFO' = 0
    'CODEX16PALRESET' = 0
    'CODEX16CTX' = 2
    'CODEX16CTXINFO' = 0
    'CODEX16CTXCLEAR' = 0
}
$reviewedFiles = @(
    'Codex.AutoCAD.2016.sln',
    'global.json',
    'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj',
    'src\Codex.AutoCAD.Host.2016\NuGet.Config',
    'src\Codex.AutoCAD.Host.2016\packages.lock.json',
    'src\Codex.AutoCAD.Host.2016\CodexAutoCad2016Extension.cs',
    'src\Codex.AutoCAD.Host.2016\CodexCad2016Commands.cs',
    'src\Codex.AutoCAD.Host.2016\EmbeddedContractGlobalUsings.cs',
    'src\Codex.AutoCAD.Host.2016\EmbeddedCadValidationFailure.cs',
    'src\Codex.AutoCAD.Host.2016\CadContextJsonMapper.cs',
    'src\Codex.AutoCAD.Host.2016\DocumentContextRegistry.cs',
    'src\Codex.AutoCAD.Host.2016\UnifiedReadOnlyContextRuntime.cs',
    'src\Codex.AutoCAD.Host.2016\UnifiedPaletteRuntime.cs',
    'src\Codex.AutoCAD.Host.2016\UnifiedPaletteController.cs',
    'src\Codex.AutoCAD.Host.2016\UnifiedPalettePanel.cs',
    'src\Codex.AutoCAD.Host.2016\Properties\AssemblyInfo.cs',
    'src\Codex.AutoCAD.Contracts\ProtocolConstants.cs',
    'src\Codex.AutoCAD.Contracts\Geometry.cs',
    'src\Codex.AutoCAD.Contracts\CadContextJsonV1Contracts.cs',
    'src\Codex.AutoCAD.Contracts\CadContextJsonV1Codec.cs',
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlySelectionCapture.cs',
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlyContextSnapshot.cs',
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\CanonicalSelectionHash.cs',
    'tests\Codex.AutoCAD.Contracts.Specs\Codex.AutoCAD.Contracts.Specs.csproj',
    'tests\Codex.AutoCAD.Contracts.Specs\Program.cs',
    'scripts\verify-phase2.ps1',
    'scripts\verify-autocad2016-readonly-context.ps1'
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Read-Utf8 {
    param([string]$Path)
    $encoding = New-Object Text.UTF8Encoding($false, $true)
    return [IO.File]::ReadAllText($Path, $encoding)
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256 {
    param([string]$Value)
    $encoding = New-Object Text.UTF8Encoding($false, $true)
    $bytes = $encoding.GetBytes($Value)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Assert-Sequence {
    param([object[]]$Actual, [object[]]$Expected, [string]$Message)
    $difference = @(Compare-Object -ReferenceObject $Expected -DifferenceObject $Actual -SyncWindow 0)
    if ($difference.Count -ne 0) {
        throw ($Message + [Environment]::NewLine + ($difference | Out-String))
    }
}

function Invoke-Captured {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$Description,
        [string]$WorkingDirectory
    )
    $previous = $null
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $previous = Get-Location
        Set-Location -LiteralPath $WorkingDirectory
    }
    $preference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $preference
        if ($null -ne $previous) {
            Set-Location -LiteralPath $previous.Path
        }
    }
    if ($exitCode -ne 0) {
        throw ($Description + ' failed with exit code ' + $exitCode + '.' +
            [Environment]::NewLine + ($output -join [Environment]::NewLine))
    }
    return $output
}

function Assert-Signed {
    param([string]$Path, [string]$PublisherPattern, [string]$Label)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "$Label was not found: $Path"
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    Assert-True ($signature.Status -eq 'Valid') "$Label signature is invalid."
    Assert-True ($null -ne $signature.SignerCertificate) "$Label signer is missing."
    Assert-True ($signature.SignerCertificate.Subject -match $PublisherPattern) "$Label publisher is unexpected."
    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        Name = $Label
        FileVersion = $item.VersionInfo.FileVersion
        ProductVersion = $item.VersionInfo.ProductVersion
        Sha256 = Get-Sha256 -Path $Path
        SignatureStatus = $signature.Status.ToString()
        SignerThumbprint = $signature.SignerCertificate.Thumbprint
    }
}

function Get-PeMachine {
    param([string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        try {
            Assert-True ($reader.ReadUInt16() -eq 0x5A4D) "Not a PE file: $Path"
            $stream.Position = 0x3C
            $offset = $reader.ReadInt32()
            $stream.Position = $offset
            Assert-True ($reader.ReadUInt32() -eq 0x00004550) "Invalid PE signature: $Path"
            $machine = $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
    if ($machine -eq 0x8664) { return 'x64' }
    if ($machine -eq 0x014C) { return 'x86' }
    if ($machine -eq 0xAA64) { return 'arm64' }
    return ('0x{0:X4}' -f $machine)
}

function Resolve-MsBuild {
    if (-not [string]::IsNullOrWhiteSpace($MsBuildPath)) {
        $resolved = [IO.Path]::GetFullPath($MsBuildPath)
    }
    else {
        $resolved = (Get-Command 'MSBuild.exe' -ErrorAction Stop | Select-Object -First 1).Source
    }
    Assert-True (Test-Path -LiteralPath $resolved -PathType Leaf) "MSBuild not found: $resolved"
    $match = [regex]::Match([string](Get-Item -LiteralPath $resolved).VersionInfo.FileVersion, '\d+(?:[\.,]\d+){1,3}')
    Assert-True $match.Success 'MSBuild version cannot be parsed.'
    $parts = @($match.Value.Replace(',', '.') -split '\.')
    while ($parts.Count -lt 4) { $parts += '0' }
    $version = New-Object Version (($parts | Select-Object -First 4) -join '.')
    Assert-True ($version.Major -eq 17 -and $version.Minor -eq 14) "MSBuild 17.14 is required; actual=$version"
    return $resolved
}

function Resolve-Ildasm {
    foreach ($path in @(
        'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\ildasm.exe',
        'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe'
    )) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }
    throw 'Microsoft ildasm.exe was not found.'
}

function Get-ReviewedManifest {
    $lines = @(
        foreach ($relative in $reviewedFiles) {
            $path = Join-Path $repoRoot $relative
            Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Reviewed input is missing: $relative"
            $item = Get-Item -LiteralPath $path
            $relative.Replace('\', '/') + '|' + $item.Length + '|' + (Get-Sha256 -Path $path)
        }
    )
    return Get-TextSha256 -Value ($lines -join $lf)
}

function Get-DirectoryManifest {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return '<absent>'
    }
    $root = (Get-Item -LiteralPath $Path).FullName.TrimEnd('\')
    $lines = @(
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($root.Length).TrimStart('\')
            $relative + '|' + $file.Length + '|' + (Get-Sha256 -Path $file.FullName)
        }
    )
    return Get-TextSha256 -Value ($lines -join $lf)
}

function Get-AcadProcessState {
    $identities = @(
        foreach ($process in @(Get-Process -Name acad -ErrorAction SilentlyContinue | Sort-Object Id)) {
            try {
                $started = $process.StartTime.ToUniversalTime().Ticks
            }
            catch {
                $started = 'unavailable'
            }
            [string]$process.Id + '|' + [string]$started
        }
    )
    return [pscustomobject]@{
        Count = $identities.Count
        Hash = Get-TextSha256 -Value ($identities -join $lf)
    }
}

function Get-CompileClosure {
    [xml]$project = Read-Utf8 -Path $projectPath
    $ns = New-Object Xml.XmlNamespaceManager($project.NameTable)
    $ns.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $items = @($project.SelectNodes('//m:Compile', $ns) | ForEach-Object { [string]$_.Include })
    return [pscustomobject]@{
        Xml = $project
        Ns = $ns
        Items = $items
        Paths = @($items | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) })
    }
}

function Assert-ProjectGraph {
    $closure = Get-CompileClosure
    Assert-Sequence -Actual $closure.Items -Expected $expectedCompileItems -Message 'Compile closure changed.'
    foreach ($path in $closure.Paths) {
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Compile input is missing: $path"
        Assert-True ($path.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) "Compile input escaped the repository: $path"
    }
    $xml = $closure.Xml
    $ns = $closure.Ns
    $properties = [ordered]@{
        TargetFrameworkVersion = 'v4.5'
        PlatformTarget = 'x64'
        LangVersion = '12.0'
        Nullable = 'annotations'
        Deterministic = 'true'
        ContinuousIntegrationBuild = 'true'
        TreatWarningsAsErrors = 'true'
        RestoreLockedMode = 'true'
        ImportDirectoryBuildProps = 'false'
        ImportDirectoryBuildTargets = 'false'
    }
    foreach ($entry in $properties.GetEnumerator()) {
        $nodes = @($xml.SelectNodes("//m:$($entry.Key)", $ns))
        Assert-True ($nodes.Count -eq 1 -and [string]$nodes[0].InnerText -ceq [string]$entry.Value) "Project property changed: $($entry.Key)"
    }
    $release = @($xml.SelectNodes('//m:PropertyGroup', $ns) | Where-Object {
        $null -ne $_.Attributes['Condition'] -and $_.GetAttribute('Condition') -match 'Release\|x64'
    })
    Assert-True ($release.Count -eq 1) 'Release|x64 property group changed.'
    Assert-True ([string]$release[0].DebugSymbols -ceq 'false') 'Release DebugSymbols must be false.'
    Assert-True ([string]$release[0].DebugType -ceq 'none') 'Release DebugType must be none.'
    Assert-True ([string]$release[0].Optimize -ceq 'true') 'Release Optimize must be true.'
    $references = @($xml.SelectNodes('//m:Reference', $ns) | ForEach-Object { ([string]$_.Include).Split(',')[0] })
    Assert-Sequence -Actual $references -Expected $expectedReferences -Message 'Reference allowlist changed.'
    foreach ($name in @('accoremgd', 'acdbmgd', 'acmgd')) {
        $node = $xml.SelectSingleNode("//m:Reference[starts-with(translate(@Include,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'$name')]", $ns)
        Assert-True ($null -ne $node) "Autodesk reference is missing: $name"
        Assert-True ([string]$node.Private -ieq 'false') "Autodesk Private=false changed: $name"
        Assert-True ([string]$node.SpecificVersion -ieq 'true') "Autodesk SpecificVersion=true changed: $name"
    }
    $packages = @($xml.SelectNodes('//m:PackageReference', $ns))
    Assert-True ($packages.Count -eq 1) 'Only one compile-time package is allowed.'
    Assert-True ([string]$packages[0].Include -ceq 'Microsoft.NETFramework.ReferenceAssemblies.net45') 'PackageReference changed.'
    Assert-True ([string]$packages[0].Version -ceq '[1.0.3]') 'PackageReference version changed.'
    foreach ($forbidden in @('ProjectReference', 'COMReference', 'NativeReference', 'Analyzer', 'UsingTask', 'Exec', 'CodeTaskFactory')) {
        Assert-True ($xml.SelectNodes("//m:$forbidden", $ns).Count -eq 0) "Forbidden project item exists: $forbidden"
    }
    $imports = @($xml.SelectNodes('//m:Import', $ns) | ForEach-Object { [string]$_.Project })
    Assert-Sequence -Actual $imports -Expected @('$(MSBuildToolsPath)\Microsoft.Common.props', '$(MSBuildToolsPath)\Microsoft.CSharp.targets') -Message 'Import allowlist changed.'
    $targets = @($xml.SelectNodes('//m:Target', $ns))
    Assert-Sequence -Actual @($targets | ForEach-Object { [string]$_.Name }) -Expected @('ValidateAutoCad2016References', 'RejectAutodeskCopyLocal') -Message 'Target allowlist changed.'
    foreach ($target in $targets) {
        $tasks = @($target.ChildNodes | Where-Object { $_.NodeType -eq [Xml.XmlNodeType]::Element })
        Assert-True ($tasks.Count -gt 0 -and @($tasks | Where-Object { $_.LocalName -ne 'Error' }).Count -eq 0) "Target may contain only Error tasks: $($target.Name)"
    }
    $solution = Read-Utf8 -Path $solutionPath
    $projects = @([regex]::Matches($solution, '(?m)^Project\("[^"]+"\)\s*=\s*"([^"]+)",\s*"([^"]+\.csproj)",\s*"(\{[A-Fa-f0-9-]+\})"'))
    Assert-True ($projects.Count -eq 1) 'Dedicated solution must contain exactly one build project.'
    Assert-True ($projects[0].Groups[1].Value -ceq 'Codex.AutoCAD.Host.2016') 'Dedicated solution project name changed.'
    Assert-True ($projects[0].Groups[3].Value -ieq $projectGuid) 'Dedicated solution project GUID changed.'
    Assert-True ((Read-Utf8 -Path $mainSolutionPath) -notmatch '(?i)Codex\.AutoCAD\.Host\.2016') 'Host.2016 must remain outside the modern main solution.'
}

function Get-SourceFindings {
    param([string]$Text)
    $patterns = @(
        'OpenMode\s*\.\s*ForWrite|\.\s*UpgradeOpen\s*\(|\.\s*DowngradeOpen\s*\(|\.\s*AppendEntity\s*\(|\.\s*AddNewlyCreatedDBObject\s*\(|\.\s*Commit\s*\(|\.\s*Abort\s*\(|\.\s*Erase\s*\(|\.\s*WblockCloneObjects\s*\(|\.\s*DeepCloneObjects\s*\(|\.\s*TransformBy\s*\(',
        'DocumentLock|LockDocument|SetSystemVariable|SetImpliedSelection|SendStringToExecute|SaveAs|DwgOut|DxfOut|CloseAndSave|ExecuteInCommandContext|\.\s*Command(?:Async)?\s*\(',
        '(?i:\bdocument\s*\.\s*Name\b|\bdatabase\s*\.\s*Filename\b|\.PathName\b)',
        'System\.Diagnostics\.Process|NamedPipe|AnonymousPipe|PipeStream|MemoryMappedFile|System\.Net\.|HttpClient|WebRequest|WebClient|HttpListener|Socket|TcpClient|UdpClient|Microsoft\.Win32|RegistryKey|System\.IO\.(?:File|Directory|FileInfo|DirectoryInfo|FileStream|StreamReader|StreamWriter)',
        'DllImport|Marshal\s*\.|LoadLibrary|GetProcAddress|Assembly\s*\.\s*Load|Activator\s*\.\s*CreateInstance|MethodInfo\s*\.\s*Invoke|\bunsafe\b|\bdynamic\s+[A-Za-z_][A-Za-z0-9_]*',
        'Task\s*\.\s*Run|ThreadPool|new\s+(?:Thread|Timer)\s*\(|BackgroundWorker|\block\s*\(|Monitor\s*\.',
        'IAgentBridgeClient|Codex\.AutoCAD\.(?:Bridge|AgentRuntime|Ipc|Security)|\bHMAC\w*\b|RandomNumberGenerator|ProtectedData'
    )
    return @($patterns | Where-Object { [regex]::IsMatch($Text, $_) })
}

function Assert-ReadHelper {
    param([string]$Text)
    Assert-True (@([regex]::Matches($Text, 'transaction\s*\.\s*GetObject\s*\(')).Count -eq 1) 'Transaction.GetObject must have one call site.'
    Assert-True ([regex]::IsMatch($Text, 'transaction\s*\.\s*GetObject\s*\(\s*objectId\s*,\s*OpenMode\s*\.\s*ForRead\s*,\s*false\s*\)')) 'Transaction.GetObject must use ForRead,false.'
    Assert-True (@([regex]::Matches($Text, 'StartOpenCloseTransaction\s*\(')).Count -eq 1) 'StartOpenCloseTransaction must have one call site.'
}

function Assert-SourceGate {
    $closure = Get-CompileClosure
    foreach ($path in $closure.Paths) {
        if ($path.EndsWith('Properties\AssemblyInfo.cs', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        $findings = @(Get-SourceFindings -Text (Read-Utf8 -Path $path))
        Assert-True ($findings.Count -eq 0) "Forbidden source API found in $path"
    }
    $capture = Read-Utf8 -Path (Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlySelectionCapture.cs')
    Assert-ReadHelper -Text $capture
    foreach ($type in @('Line', 'Circle', 'Polyline', 'DBText', 'MText', 'BlockReference')) {
        Assert-True ([regex]::IsMatch($capture, 'entity\s+as\s+' + [regex]::Escape($type))) "Entity branch is missing: $type"
    }
    Assert-True (-not [regex]::IsMatch($capture, 'Math\s*\.\s*Min|Substring\s*\(')) 'Capture may not truncate entities.'
    $runtime = Read-Utf8 -Path (Join-Path $projectRoot 'UnifiedReadOnlyContextRuntime.cs')
    Assert-True ($runtime -match 'dbmodBefore\s*!=\s*dbmodAfter') 'DBMOD recheck is missing.'
    Assert-True ($runtime -match 'ReferenceEquals\s*\(\s*AutoCadApplication\.DocumentManager\.MdiActiveDocument\s*,\s*document\s*\)') 'Document identity recheck is missing.'
    Assert-True ($runtime -match 'DocumentToBeDestroyed') 'Document close invalidation is missing.'
    $panel = Read-Utf8 -Path (Join-Path $projectRoot 'UnifiedPalettePanel.cs')
    Assert-True (@([regex]::Matches($panel, 'new\s+TabItem')).Count -eq 2) 'Palette must contain exactly two tabs.'
    Assert-True ($panel -match 'Header\s*=\s*"Canonical JSON"') 'Canonical JSON tab is missing.'
    Assert-True ($panel -match 'IsReadOnly\s*=\s*true') 'Palette context display must be read-only.'
    Assert-True ($panel -notmatch '\bButton\b|\.Click\s*\+=') 'This stage may not add an action button.'
    $all = @($closure.Paths | ForEach-Object { Read-Utf8 -Path $_ }) -join $lf
    Assert-True ($all -notmatch 'GetSystemVariable\s*\(\s*"SAVETIME"') 'SAVETIME may not be queried or modified.'
    Assert-True ($all -notmatch 'TRUSTEDPATHS"\s*\)') 'TRUSTEDPATHS may not be queried.'
    Assert-True ($all -notmatch 'IAgentBridgeClient') 'Agent bridge must remain disabled.'
}

function Assert-NoForbiddenIl {
    param([string]$Text)
    $pattern = 'OpenMode[^;\r\n]*ForWrite|::UpgradeOpen\(|::DowngradeOpen\(|::Commit\(|::Abort\(|Autodesk\.AutoCAD\.DatabaseServices\.[^;\r\n]*::AppendEntity\(|::AddNewlyCreatedDBObject\(|::Erase\(|DocumentLock|::LockDocument\(|::SetSystemVariable\(|::SetImpliedSelection\(|::SendStringToExecute\(|::SaveAs\(|::DwgOut\(|::DxfOut\(|::CloseAndSave\(|ExecuteInCommandContext|Document::get_Name\(|Database::get_Filename\(|::get_PathName\(|System\.Diagnostics\.Process|System\.IO\.Pipes|System\.Net\.|Microsoft\.Win32|pinvokeimpl|\.module extern|IAgentBridgeClient|System\.Threading\.Tasks|System\.Threading\.Thread::|System\.Threading\.Timer::'
    Assert-True (-not [regex]::IsMatch($Text, $pattern)) 'Compiled IL contains a forbidden API.'
}

function Get-CommandBlob {
    param([string]$Name, [int]$Flags)
    $bytes = New-Object Collections.Generic.List[byte]
    $bytes.Add(0x01)
    $bytes.Add(0x00)
    $nameBytes = [Text.Encoding]::ASCII.GetBytes($Name)
    $bytes.Add([byte]$nameBytes.Length)
    foreach ($value in $nameBytes) { $bytes.Add($value) }
    foreach ($value in [BitConverter]::GetBytes([int]$Flags)) { $bytes.Add($value) }
    $bytes.Add(0x00)
    $bytes.Add(0x00)
    return (($bytes.ToArray() | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

function Assert-RuleSelfTests {
    $safe = 'return transaction.GetObject(objectId, OpenMode.ForRead, false); StartOpenCloseTransaction();'
    Assert-True (@(Get-SourceFindings -Text $safe).Count -eq 0) 'Safe source sample was rejected.'
    Assert-ReadHelper -Text $safe
    foreach ($sample in @(
        'transaction.GetObject(id, OpenMode.ForWrite, false);',
        'transaction.Commit();',
        'document.LockDocument();',
        'Application.SetSystemVariable("CLAYER", "0");',
        'document.SendStringToExecute("_.SAVE", true, false, false);',
        'var path = document.Name;',
        'System.Diagnostics.Process.Start("cmd.exe");',
        'new NamedPipeClientStream("x");',
        'new System.IO.FileStream("x", FileMode.Create);',
        'System.Threading.Tasks.Task.Run(() => Work());',
        '[DllImport("kernel32.dll")] static extern void X();',
        'IAgentBridgeClient bridge;'
    )) {
        Assert-True (@(Get-SourceFindings -Text $sample).Count -gt 0) "Dangerous sample was not rejected: $sample"
    }
    $rejected = $false
    try {
        Assert-NoForbiddenIl -Text 'call instance void Autodesk.AutoCAD.DatabaseServices.Database::SaveAs(string)'
    }
    catch {
        $rejected = $true
    }
    Assert-True $rejected 'Dangerous IL sample was not rejected.'
    Assert-NoForbiddenIl -Text 'call void Codex.AutoCAD.Contracts.CadContextJsonV1Codec::AppendEntity(class System.Text.StringBuilder)'
    $appendRejected = $false
    try {
        Assert-NoForbiddenIl -Text 'call instance void Autodesk.AutoCAD.DatabaseServices.BlockTableRecord::AppendEntity(class Autodesk.AutoCAD.DatabaseServices.Entity)'
    }
    catch {
        $appendRejected = $true
    }
    Assert-True $appendRejected 'AutoCAD AppendEntity IL sample was not rejected.'
}

function Invoke-HostBuild {
    param([string]$Label, [string]$ResolvedMsBuild)
    $root = Join-Path $stageRoot $Label
    $out = Join-Path $root 'bin'
    $objBase = Join-Path $root 'obj-base'
    $obj = Join-Path $root 'obj'
    $objExt = Join-Path $root 'obj-ext'
    $packages = Join-Path $root 'packages'
    $cliHome = Join-Path $root 'dotnet-home'
    $httpCache = Join-Path $root 'http-cache'
    New-Item -ItemType Directory -Force -Path $out, $objBase, $obj, $objExt, $packages, $cliHome, $httpCache | Out-Null
    $saved = @{
        DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
        NUGET_PACKAGES = $env:NUGET_PACKAGES
        NUGET_HTTP_CACHE_PATH = $env:NUGET_HTTP_CACHE_PATH
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
        DOTNET_CLI_TELEMETRY_OPTOUT = $env:DOTNET_CLI_TELEMETRY_OPTOUT
        DOTNET_NOLOGO = $env:DOTNET_NOLOGO
    }
    try {
        $env:DOTNET_CLI_HOME = $cliHome
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
        $env:NUGET_PACKAGES = $packages
        $env:NUGET_HTTP_CACHE_PATH = $httpCache
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:DOTNET_NOLOGO = '1'
        Invoke-Captured -FilePath $ResolvedMsBuild -Arguments @(
            $solutionPath,
            '/restore',
            '/t:Rebuild',
            '/m:1',
            "/p:Configuration=$Configuration",
            '/p:Platform=Any CPU',
            "/p:AutoCad2016Dir=$AutoCad2016Dir",
            "/p:RestoreConfigFile=$nuGetConfigPath",
            ("/p:RestorePackagesPath=" + $packages.TrimEnd('\') + '/'),
            '/p:RestoreLockedMode=true',
            '/p:RestorePackagesWithLockFile=true',
            '/p:ImportDirectoryBuildProps=false',
            '/p:ImportDirectoryBuildTargets=false',
            ("/p:OutDir=" + $out.TrimEnd('\') + '/'),
            ("/p:BaseIntermediateOutputPath=" + $objBase.TrimEnd('\') + '/'),
            ("/p:IntermediateOutputPath=" + $obj.TrimEnd('\') + '/'),
            ("/p:MSBuildProjectExtensionsPath=" + $objExt.TrimEnd('\') + '/'),
            '/v:minimal'
        ) -Description "Host build $Label" -WorkingDirectory $repoRoot | Out-Null
    }
    finally {
        foreach ($entry in $saved.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, [EnvironmentVariableTarget]::Process)
        }
    }
    $files = @(Get-ChildItem -LiteralPath $out -Recurse -File)
    Assert-True ($files.Count -eq 1) "Host output must contain one file: $Label"
    Assert-True ($files[0].Name -ceq 'Codex.AutoCAD.Host.2016.dll') "Host output name changed: $Label"
    Assert-True ($files[0].Length -eq $expectedCandidateSize) "Host size changed: $($files[0].Length)"
    $hash = Get-Sha256 -Path $files[0].FullName
    Assert-True ($hash -ceq $expectedCandidateSha256) "Host hash changed: expected=$expectedCandidateSha256 actual=$hash"
    Assert-True ((Get-PeMachine -Path $files[0].FullName) -ceq 'x64') 'Host output is not x64.'
    return [pscustomobject]@{
        Root = $root
        DllPath = $files[0].FullName
        Sha256 = $hash
        Size = $files[0].Length
    }
}

function Normalize-Il {
    param([string]$Path)
    $text = Read-Utf8 -Path $Path
    $text = [regex]::Replace($text, '(?m)^// Image base:.*$', '// Image base: <normalized>')
    $text = [regex]::Replace($text, '(?m)^// .*Win32.*resource.*$', '// Win32 resource: <normalized>')
    $text = [regex]::Replace($text, '(?m)^// .*Win32.*$', '// Win32 resource: <normalized>')
    return $text.Replace($cr + $lf, $lf).Replace($cr, $lf)
}

function Assert-Il {
    param([string]$DllPath, [string]$IldasmPath, [string]$Root)
    $ilPath = Join-Path $Root 'host.il'
    Invoke-Captured -FilePath $IldasmPath -Arguments @('/text', '/tokens', '/nobar', '/utf8', ("/out=$ilPath"), $DllPath) -Description 'ildasm' -WorkingDirectory $repoRoot | Out-Null
    $il = Normalize-Il -Path $ilPath
    $ilHash = Get-TextSha256 -Value $il
    Assert-True ($ilHash -ceq $expectedNormalizedIlSha256) "Normalized IL changed: expected=$expectedNormalizedIlSha256 actual=$ilHash"
    $methodCount = ([regex]::Matches($il, '(?m)^\s*\.method\s')).Count
    $memberRefs = @([regex]::Matches($il, '/\*\s*(0A[0-9A-Fa-f]{6})\s*\*/') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $typeCount = ([regex]::Matches($il, '(?m)^\s*\.class\s')).Count
    $fieldCount = ([regex]::Matches($il, '(?m)^\s*\.field\s')).Count
    Assert-True ($methodCount -eq $expectedMethodCount) "MethodDef count changed: $methodCount"
    Assert-True ($memberRefs.Count -eq $expectedMemberRefCount) "MemberRef count changed: $($memberRefs.Count)"
    Assert-True ($typeCount -eq $expectedTypeCount) "TypeDef count changed: $typeCount"
    Assert-True ($fieldCount -eq $expectedFieldCount) "FieldDef count changed: $fieldCount"
    $assemblyRefs = @([regex]::Matches($il, '(?m)^\.assembly extern /\*[^*]+\*/ ([^\r\n ]+)') | ForEach-Object { $_.Groups[1].Value })
    Assert-Sequence -Actual $assemblyRefs -Expected $expectedAssemblyRefs -Message 'AssemblyRef allowlist changed.'
    Assert-NoForbiddenIl -Text $il
    Assert-True (([regex]::Matches($il, 'Transaction/\*[^*]+\*/::GetObject\(')).Count -eq 1) 'IL must contain one Transaction.GetObject.'
    Assert-True (([regex]::Matches($il, '::StartOpenCloseTransaction\(')).Count -eq 1) 'IL must contain one StartOpenCloseTransaction.'
    Assert-True ([regex]::IsMatch($il, '(?s)OpenObjectForRead.*?ldarg\.0.*?ldarg\.1.*?ldc\.i4\.0.*?ldc\.i4\.0.*?::GetObject\(.*?ret.*?end of method ReadOnlySelectionCapture::OpenObjectForRead')) 'ForRead helper IL changed.'
    Assert-True (([regex]::Matches($il, 'CommandMethodAttribute')).Count -eq $expectedCommands.Count) 'Command attribute count changed.'
    $flat = [regex]::Replace([regex]::Replace($il, '(?m)//.*$', ''), '\s+', ' ')
    foreach ($entry in $expectedCommands.GetEnumerator()) {
        $blob = Get-CommandBlob -Name ([string]$entry.Key) -Flags ([int]$entry.Value)
        Assert-True ($flat.IndexOf($blob, [StringComparison]::Ordinal) -ge 0) "Command metadata changed: $($entry.Key)"
    }
    Assert-True (([regex]::Matches($il, 'ExtensionApplicationAttribute')).Count -eq 1) 'ExtensionApplication attribute count changed.'
    Assert-True (([regex]::Matches($il, 'CommandClassAttribute')).Count -eq 1) 'CommandClass attribute count changed.'
    return [pscustomobject]@{
        Hash = $ilHash
        MethodCount = $methodCount
        MemberRefCount = $memberRefs.Count
        TypeCount = $typeCount
        FieldCount = $fieldCount
    }
}

function Assert-SpecOutput {
    param([string[]]$Lines, [string]$Label)
    Assert-True (@($Lines | Where-Object { $_ -match '^\s*32/32 specs passed\s*$' }).Count -eq 1) "$Label summary changed."
    Assert-True (@($Lines | Where-Object { $_ -match '^PASS\s+' }).Count -eq $expectedSpecCount) "$Label PASS count changed."
    Assert-True (@($Lines | Where-Object { $_ -match '^FAIL\s+' }).Count -eq 0) "$Label emitted a failure."
    Assert-True (@($Lines | Where-Object { $_ -ceq "CAD_CONTEXT_JSON_V1 sha256=$expectedPublicSha256 bytes=$expectedPublicBytes" }).Count -eq 1) "$Label public vector changed."
    Assert-True (@($Lines | Where-Object { $_ -ceq "HOST16_CONTEXT_BYTES=$expectedMappingBytes" }).Count -eq 1) "$Label mapping bytes changed."
    Assert-True (@($Lines | Where-Object { $_ -ceq "HOST16_CONTEXT_SHA256=$expectedMappingSha256" }).Count -eq 1) "$Label mapping hash changed."
}

function Invoke-ContractSpecs {
    param([string]$DotNetPath)
    $root = Join-Path $stageRoot 'contracts'
    $out = Join-Path $root 'out'
    $packages = Join-Path $root 'packages'
    $cliHome = Join-Path $root 'dotnet-home'
    New-Item -ItemType Directory -Force -Path $root, $out, $packages, $cliHome | Out-Null
    $savedHome = $env:DOTNET_CLI_HOME
    $savedAddGlobalToolsToPath = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
    $savedPathMap = $env:PathMap
    try {
        $env:DOTNET_CLI_HOME = $cliHome
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
        $env:PathMap = ($root + '=/_unified_contract/,' + $repoRoot + '=/_/')
        Invoke-Captured -FilePath $DotNetPath -Arguments @(
            'restore', $specProjectPath, '--configfile', $nuGetConfigPath,
            '--packages', $packages, '--force', '--no-cache', '--disable-parallel',
            '-p:EnableAutoCad2016=true', '-p:UseArtifactsOutput=true',
            ('-p:ArtifactsPath=' + $out)
        ) -Description 'Contracts restore' -WorkingDirectory $repoRoot | Out-Null
        Invoke-Captured -FilePath $DotNetPath -Arguments @(
            'build', $specProjectPath, '--configuration', $Configuration,
            '--nologo', '--disable-build-servers', '--no-restore', '-m:1',
            '-p:EnableAutoCad2016=true', '-p:UseArtifactsOutput=true',
            ('-p:ArtifactsPath=' + $out), '-p:ContinuousIntegrationBuild=true'
        ) -Description 'Contracts build' -WorkingDirectory $repoRoot | Out-Null
    }
    finally {
        $env:DOTNET_CLI_HOME = $savedHome
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $savedAddGlobalToolsToPath
        $env:PathMap = $savedPathMap
    }
    $net45 = Join-Path $out 'bin\Codex.AutoCAD.Contracts.Specs\release_net45\Codex.AutoCAD.Contracts.Specs.exe'
    $net8 = Join-Path $out 'bin\Codex.AutoCAD.Contracts.Specs\release_net8.0\Codex.AutoCAD.Contracts.Specs.dll'
    Assert-True (Test-Path -LiteralPath $net45 -PathType Leaf) 'net45 Specs artifact is missing.'
    Assert-True (Test-Path -LiteralPath $net8 -PathType Leaf) 'net8 Specs artifact is missing.'
    $net45Output = Invoke-Captured -FilePath $net45 -Arguments @() -Description 'net45 Specs' -WorkingDirectory $repoRoot
    $net8Output = Invoke-Captured -FilePath $DotNetPath -Arguments @($net8) -Description 'net8 Specs' -WorkingDirectory $repoRoot
    Assert-SpecOutput -Lines $net45Output -Label 'net45'
    Assert-SpecOutput -Lines $net8Output -Label 'net8'
    Assert-True (($net45Output -join $lf) -ceq ($net8Output -join $lf)) 'net45 and net8 Specs output differs.'
}

function Assert-NoSecret {
    $patterns = @(
        '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
        '\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b',
        '\bgh[pousr]_[A-Za-z0-9]{20,}\b',
        '\bAKIA[0-9A-Z]{16}\b'
    )
    $safeRoot = $repoRoot.Replace('\', '/')
    $files = Invoke-Captured -FilePath 'git' -Arguments @(
        '-c', ('safe.directory=' + $safeRoot), '-C', $repoRoot,
        'ls-files', '--cached', '--others', '--exclude-standard'
    ) -Description 'Secret scan file list' -WorkingDirectory $repoRoot
    foreach ($relative in $files) {
        if ([string]::IsNullOrWhiteSpace($relative)) { continue }
        $normalized = $relative.Replace('\', '/')
        if ($normalized -match '(?:^|/)(?:artifacts|bin|obj)(?:/|$)') { continue }
        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
        if (@('.cs', '.csproj', '.json', '.md', '.ps1', '.sln', '.xml', '.config') -notcontains $extension) { continue }
        $path = Join-Path $repoRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $content = Read-Utf8 -Path $path
        foreach ($pattern in $patterns) {
            Assert-True (-not [regex]::IsMatch($content, $pattern)) "Possible secret found: $relative"
        }
    }
}

Push-Location $repoRoot
try {
    Assert-RuleSelfTests
    if ($RuleSelfTestOnly) {
        Write-Host 'Unified Host verifier rule self-tests passed.'
        return
    }

    foreach ($path in @($projectPath, $solutionPath, $mainSolutionPath, $specProjectPath, $nuGetConfigPath, $offlinePackagePath, $phase2Script, $readOnlyContextScript)) {
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Required input is missing: $path"
    }
    $manifestBefore = Get-ReviewedManifest
    Assert-True ($manifestBefore -ceq $expectedManifestSha256) "Reviewed manifest changed: $manifestBefore"
    $cadBefore = Get-AcadProcessState
    Assert-ProjectGraph
    Assert-SourceGate

    $acadPath = Join-Path $AutoCad2016Dir 'acad.exe'
    $acadEvidence = Assert-Signed -Path $acadPath -PublisherPattern 'Autodesk' -Label 'acad.exe'
    Assert-True ((Get-PeMachine -Path $acadPath) -ceq 'x64') 'AutoCAD 2016 must be x64.'
    Assert-True ((Get-Item -LiteralPath $acadPath).VersionInfo.FileVersion -match '^R?20\.1\.') 'acad.exe must be R20.1.'
    $apiEvidence = @(
        foreach ($name in @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
            $path = Join-Path $AutoCad2016Dir $name
            $signed = Assert-Signed -Path $path -PublisherPattern 'Autodesk' -Label $name
            Assert-True ([Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString() -ceq '20.1.0.0') "$name must be 20.1.0.0."
            $signed
        }
    )

    $resolvedMsBuild = Resolve-MsBuild
    $resolvedIldasm = Resolve-Ildasm
    $dotnetPath = (Get-Command 'dotnet.exe' -ErrorAction Stop).Source
    $msbuildEvidence = Assert-Signed -Path $resolvedMsBuild -PublisherPattern 'Microsoft' -Label 'MSBuild'
    $ildasmEvidence = Assert-Signed -Path $resolvedIldasm -PublisherPattern 'Microsoft' -Label 'ildasm'
    $dotnetEvidence = Assert-Signed -Path $dotnetPath -PublisherPattern 'Microsoft' -Label 'dotnet'
    $sdk = ((Invoke-Captured -FilePath $dotnetPath -Arguments @('--version') -Description 'dotnet SDK' -WorkingDirectory $repoRoot) -join '').Trim()
    Assert-True ($sdk -ceq $expectedSdk) ".NET SDK $expectedSdk is required; actual=$sdk"
    Assert-True ((Get-Sha256 -Path $offlinePackagePath) -ceq $expectedPackageSha256) 'Offline package hash changed.'
    Invoke-Captured -FilePath $dotnetPath -Arguments @('nuget', 'verify', $offlinePackagePath, '--all', '--configfile', $nuGetConfigPath) -Description 'Package signature' -WorkingDirectory $repoRoot | Out-Null

    New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
    $projectObj = Join-Path $projectRoot 'obj'
    $objBefore = Get-DirectoryManifest -Path $projectObj
    $buildA = Invoke-HostBuild -Label 'build-a' -ResolvedMsBuild $resolvedMsBuild
    $buildB = Invoke-HostBuild -Label 'build-b' -ResolvedMsBuild $resolvedMsBuild
    Assert-True ($buildA.Sha256 -ceq $buildB.Sha256) 'Host builds are not bit-for-bit identical.'
    Assert-True ((Get-DirectoryManifest -Path $projectObj) -ceq $objBefore) 'Host builds modified project-local obj.'
    $ilA = Assert-Il -DllPath $buildA.DllPath -IldasmPath $resolvedIldasm -Root $buildA.Root
    $ilB = Assert-Il -DllPath $buildB.DllPath -IldasmPath $resolvedIldasm -Root $buildB.Root
    Assert-True ($ilA.Hash -ceq $ilB.Hash) 'Host IL differs across builds.'

    Invoke-ContractSpecs -DotNetPath $dotnetPath
    $powerShellExe = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' })
    Assert-True (Test-Path -LiteralPath $powerShellExe -PathType Leaf) 'Current PowerShell executable was not found.'
    $subGateHome = Join-Path $stageRoot 'subgate-dotnet-home'
    New-Item -ItemType Directory -Force -Path $subGateHome | Out-Null
    $savedSubGateEnvironment = @{
        DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
        DOTNET_CLI_TELEMETRY_OPTOUT = $env:DOTNET_CLI_TELEMETRY_OPTOUT
        DOTNET_NOLOGO = $env:DOTNET_NOLOGO
    }
    try {
        $env:DOTNET_CLI_HOME = $subGateHome
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:DOTNET_NOLOGO = '1'
        $readOnlyOutput = Invoke-Captured -FilePath $powerShellExe -Arguments @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $readOnlyContextScript,
            '-AutoCad2016Dir', $AutoCad2016Dir, '-Configuration', $Configuration,
            '-MsBuildPath', $resolvedMsBuild
        ) -Description 'ReadOnlyContext regression' -WorkingDirectory $repoRoot
        Assert-True (($readOnlyOutput -join $lf) -match "Specs:\s*$expectedReadOnlyCount/$expectedReadOnlyCount") 'ReadOnlyContext did not prove 25/25.'
        Assert-True (($readOnlyOutput -join $lf) -match 'AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8') 'ReadOnlyContext identity changed.'
        $phase2Output = Invoke-Captured -FilePath $powerShellExe -Arguments @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $phase2Script,
            '-Configuration', $Configuration
        ) -Description 'Phase2 regression' -WorkingDirectory $repoRoot
    }
    finally {
        foreach ($entry in $savedSubGateEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, [EnvironmentVariableTarget]::Process)
        }
    }
    $phase2Text = $phase2Output -join $lf
    Assert-True ($phase2Text -match (([string]$expectedPhase2Count) + "/" + ([string]$expectedPhase2Count))) 'Phase2 did not prove 162/162.'

    $safeRoot = $repoRoot.Replace('\', '/')
    Invoke-Captured -FilePath 'git' -Arguments @('-c', ('safe.directory=' + $safeRoot), '-C', $repoRoot, 'diff', '--check') -Description 'git diff --check' -WorkingDirectory $repoRoot | Out-Null
    Invoke-Captured -FilePath 'git' -Arguments @('-c', ('safe.directory=' + $safeRoot), '-C', $repoRoot, 'diff', '--cached', '--check') -Description 'git cached diff --check' -WorkingDirectory $repoRoot | Out-Null
    Assert-NoSecret
    Assert-True ((Get-ReviewedManifest) -ceq $manifestBefore) 'Reviewed files changed during verification.'
    $cadAfter = Get-AcadProcessState
    Assert-True ($cadAfter.Count -eq $cadBefore.Count -and $cadAfter.Hash -ceq $cadBefore.Hash) 'AutoCAD process set changed.'

    $evidence = [ordered]@{
        SchemaVersion = 1
        Stage = 'autocad2016-unified-readonly-host'
        Status = 'compiled-unified-readonly-candidate-not-runtime-verified'
        Shell = [ordered]@{
            PSEdition = $PSVersionTable.PSEdition
            PSVersion = $PSVersionTable.PSVersion.ToString()
        }
        Toolchain = [ordered]@{
            MSBuild = $msbuildEvidence
            DotNet = $dotnetEvidence
            Ildasm = $ildasmEvidence
            ResolvedSdk = $sdk
        }
        AutoCad = $acadEvidence
        ManagedApis = $apiEvidence
        ReviewedManifestSha256 = $manifestBefore
        Host = [ordered]@{
            TargetFramework = '.NETFramework,Version=v4.5'
            Architecture = 'x64'
            LanguageVersion = '12.0'
            CandidateSha256 = $buildA.Sha256
            CandidateSize = $buildA.Size
            CandidatePath = $buildA.DllPath
            BitForBitRebuild = $true
            OutputFileCount = 1
            PdbProduced = $false
            AutodeskAssembliesCopied = $false
        }
        Il = [ordered]@{
            NormalizedSha256 = $ilA.Hash
            MethodDefinitionCount = $ilA.MethodCount
            MemberReferenceCount = $ilA.MemberRefCount
            TypeDefinitionCount = $ilA.TypeCount
            FieldDefinitionCount = $ilA.FieldCount
            CommandCount = $expectedCommands.Count
            Commands = @($expectedCommands.Keys)
        }
        Contracts = [ordered]@{
            Net45Specs = "$expectedSpecCount/$expectedSpecCount"
            Net8Specs = "$expectedSpecCount/$expectedSpecCount"
            RuntimeOutputsIdentical = $true
            PublicVectorBytes = $expectedPublicBytes
            PublicVectorSha256 = $expectedPublicSha256
            HostMappingVectorBytes = $expectedMappingBytes
            HostMappingVectorSha256 = $expectedMappingSha256
        }
        Regressions = [ordered]@{
            Phase2Specs = "$expectedPhase2Count/$expectedPhase2Count"
            ReadOnlyContextSpecs = "$expectedReadOnlyCount/$expectedReadOnlyCount"
            AgentHostDoctor = $true
            GitDiffCheck = $true
            SecretScan = $true
        }
        Safety = [ordered]@{
            ReadOnlyGetObjectForReadVerified = $true
            CadWriteApiAbsent = $true
            SaveApiAbsent = $true
            DrawingPathReadAbsent = $true
            ProcessNetworkIpcAbsent = $true
            AgentDisabled = $true
            PluginInitiatedSaveDisabled = $true
            AutoCadSaveTimeNotModified = $true
            AutoCadProcessSetUnchanged = $true
            AutoCadProcessCount = $cadBefore.Count
        }
        Boundaries = [ordered]@{
            AutoCadStartedOrRestarted = $false
            CadCommandsSent = $false
            DrawingReadOrWrittenByVerifier = $false
            NetLoadVerified = $false
            RuntimeToArtifactBindingVerified = $false
            PaletteJsonRuntimeVerified = $false
            AgentBridgeRuntimeVerified = $false
            CompleteAutoCad2016Support = $false
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $resolvedEvidence = [IO.Path]::GetFullPath($EvidencePath)
        $parent = Split-Path -Parent $resolvedEvidence
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
        }
        $encoding = New-Object Text.UTF8Encoding($false, $true)
        [IO.File]::WriteAllText($resolvedEvidence, ($evidence | ConvertTo-Json -Depth 8), $encoding)
    }
    Write-Host '--- AutoCAD 2016 Unified Host Verification ---'
    Write-Host 'Status: compiled-unified-readonly-candidate-not-runtime-verified'
    Write-Host 'NetLoadVerified: false'
    Write-Host "Candidate: $($buildA.DllPath)"
    Write-Host "Candidate SHA-256: $($buildA.Sha256)"
    Write-Host "Candidate size: $($buildA.Size)"
    Write-Host "Normalized IL SHA-256: $($ilA.Hash)"
    Write-Host "MethodDef/MemberRef/TypeDef/FieldDef: $expectedMethodCount/$expectedMemberRefCount/$expectedTypeCount/$expectedFieldCount"
    Write-Host "Contracts Specs net45/net8: $expectedSpecCount/$expectedSpecCount"
    Write-Host "Phase2 Specs: $expectedPhase2Count/$expectedPhase2Count"
    Write-Host "ReadOnlyContext Specs: $expectedReadOnlyCount/$expectedReadOnlyCount"
    Write-Host 'AutoCAD process started/restarted: false'
    Write-Host 'CAD commands sent: false'
    Write-Host 'Drawing read/written by verifier: false'
    Write-Host '--- End Verification ---'
}
finally {
    Pop-Location
}

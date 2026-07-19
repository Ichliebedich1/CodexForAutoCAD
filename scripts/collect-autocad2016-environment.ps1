[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string[]]$AdditionalInstallDirectory = @(),
    [switch]$RunDiscoverySelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts'
}

function Get-OptionalPropertyValue {
    param(
        [AllowNull()][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary] -and $InputObject.Contains($Name)) {
        return $InputObject[$Name]
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-AuthenticodeEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $signature = Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop
        $signer = Get-OptionalPropertyValue -InputObject $signature -Name 'SignerCertificate'
        $timestampSigner = Get-OptionalPropertyValue -InputObject $signature -Name 'TimeStamperCertificate'
        $signerSubject = Get-OptionalPropertyValue -InputObject $signer -Name 'Subject'

        return [pscustomobject]@{
            Status = [string](Get-OptionalPropertyValue -InputObject $signature -Name 'Status')
            StatusMessage = [string](Get-OptionalPropertyValue -InputObject $signature -Name 'StatusMessage')
            SignatureType = [string](Get-OptionalPropertyValue -InputObject $signature -Name 'SignatureType')
            SignerSubject = if ($null -ne $signerSubject) { [string]$signerSubject } else { $null }
            SignerThumbprint = Get-OptionalPropertyValue -InputObject $signer -Name 'Thumbprint'
            TimestampSignerSubject = Get-OptionalPropertyValue -InputObject $timestampSigner -Name 'Subject'
            IsValid = (([string](Get-OptionalPropertyValue -InputObject $signature -Name 'Status')) -eq 'Valid')
            IsAutodeskPublisher = ($null -ne $signerSubject -and ([string]$signerSubject) -match '(?i)(?:^|,\s*)O=(?:"Autodesk, Inc"|Autodesk, Inc\.?)(?:,|$)')
            IsMicrosoftPublisher = ($null -ne $signerSubject -and ([string]$signerSubject) -match '(?i)(?:^|,\s*)O=(?:"Microsoft Corporation"|Microsoft Corporation)(?:,|$)')
            InspectionError = $null
        }
    }
    catch {
        return [pscustomobject]@{
            Status = 'InspectionFailed'
            StatusMessage = $null
            SignatureType = $null
            SignerSubject = $null
            SignerThumbprint = $null
            TimestampSignerSubject = $null
            IsValid = $false
            IsAutodeskPublisher = $false
            IsMicrosoftPublisher = $false
            InspectionError = $_.Exception.Message
        }
    }
}

function Get-PeArchitecture {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite
        )
        $reader = New-Object System.IO.BinaryReader($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            return $null
        }

        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or ($peOffset + 6) -gt $stream.Length) {
            return $null
        }

        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            return $null
        }

        switch ($reader.ReadUInt16()) {
            0x014C { return 'x86' }
            0x8664 { return 'x64' }
            0x01C4 { return 'ARM' }
            0xAA64 { return 'ARM64' }
            default { return 'Unknown' }
        }
    }
    catch {
        return $null
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        elseif ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Get-SafeFileDetails {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    $assemblyName = $null

    if ($item.Extension -ieq '.dll') {
        try {
            $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($item.FullName)
        }
        catch {
            $assemblyName = $null
        }
    }

    $hash = $null
    try {
        $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
    }
    catch {
        $hash = $null
    }

    [pscustomobject]@{
        Name = $item.Name
        Path = $item.FullName
        Length = $item.Length
        Architecture = Get-PeArchitecture -Path $item.FullName
        FileVersion = $item.VersionInfo.FileVersion
        ProductVersion = $item.VersionInfo.ProductVersion
        CompanyName = $item.VersionInfo.CompanyName
        AssemblyName = if ($null -ne $assemblyName) { $assemblyName.Name } else { $null }
        AssemblyVersion = if ($null -ne $assemblyName) { $assemblyName.Version.ToString() } else { $null }
        Sha256 = $hash
        Authenticode = Get-AuthenticodeEvidence -Path $item.FullName
    }
}

function ConvertTo-AutoCadRegistryHintsFromRecords {
    param([object[]]$Records)

    $results = @()
    foreach ($record in @($Records)) {
        $properties = Get-OptionalPropertyValue -InputObject $record -Name 'Properties'
        if ($null -eq $properties) {
            continue
        }

        foreach ($valueName in @('AcadLocation', 'InstallLocation', 'Location')) {
            $candidateValue = Get-OptionalPropertyValue -InputObject $properties -Name $valueName
            foreach ($candidate in @($candidateValue)) {
                if ([string]::IsNullOrWhiteSpace([string]$candidate)) {
                    continue
                }

                $results += [pscustomobject]@{
                    RegistryKey = [string](Get-OptionalPropertyValue -InputObject $record -Name 'RegistryKey')
                    ReleaseKey = 'R20.1'
                    ValueName = $valueName
                    CandidatePath = [Environment]::ExpandEnvironmentVariables(([string]$candidate).Trim().Trim('"'))
                    ProductName = Get-OptionalPropertyValue -InputObject $properties -Name 'ProductName'
                    ProductRelease = Get-OptionalPropertyValue -InputObject $properties -Name 'Release'
                    Language = Get-OptionalPropertyValue -InputObject $properties -Name 'Language'
                }
            }
        }
    }

    return @($results | Sort-Object RegistryKey, ValueName, CandidatePath -Unique)
}

function New-DefaultAutoCadRegistryAccessor {
    return [pscustomobject]@{
        TestPath = {
            param([string]$Path)
            Test-Path -LiteralPath $Path -ErrorAction Stop
        }
        GetItem = {
            param([string]$Path)
            Get-Item -LiteralPath $Path -ErrorAction Stop
        }
        GetChildItems = {
            param([string]$Path)
            @(Get-ChildItem -LiteralPath $Path -ErrorAction Stop)
        }
        GetItemProperty = {
            param([string]$Path)
            Get-ItemProperty -LiteralPath $Path -ErrorAction Stop
        }
    }
}

function Get-RequiredRegistryOperation {
    param(
        [Parameter(Mandatory = $true)][object]$RegistryAccessor,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $operation = Get-OptionalPropertyValue -InputObject $RegistryAccessor -Name $Name
    if ($operation -isnot [scriptblock]) {
        throw ("Registry accessor operation '{0}' must be a script block." -f $Name)
    }

    return $operation
}

function Get-AutoCadRegistryHints {
    param(
        [ref]$Diagnostics,
        [AllowNull()][object]$RegistryAccessor,
        [AllowEmptyCollection()][string[]]$AutoCadRoots
    )

    if ($null -eq $RegistryAccessor) {
        $RegistryAccessor = New-DefaultAutoCadRegistryAccessor
    }
    if (-not $PSBoundParameters.ContainsKey('AutoCadRoots')) {
        $AutoCadRoots = @(
            'HKLM:\SOFTWARE\Autodesk\AutoCAD',
            'HKLM:\SOFTWARE\WOW6432Node\Autodesk\AutoCAD'
        )
    }

    $testPathOperation = Get-RequiredRegistryOperation -RegistryAccessor $RegistryAccessor -Name 'TestPath'
    $getItemOperation = Get-RequiredRegistryOperation -RegistryAccessor $RegistryAccessor -Name 'GetItem'
    $getChildItemsOperation = Get-RequiredRegistryOperation -RegistryAccessor $RegistryAccessor -Name 'GetChildItems'
    $getItemPropertyOperation = Get-RequiredRegistryOperation -RegistryAccessor $RegistryAccessor -Name 'GetItemProperty'

    $records = @()
    $releaseRootsPresent = 0
    $releaseRootProbeFailureCount = 0
    $releaseRootReadFailureCount = 0
    $childEnumerationFailureCount = 0
    $keysInspected = 0
    $keysRead = 0
    $propertyReadFailureCount = 0

    foreach ($autoCadRoot in @($AutoCadRoots)) {
        $releaseRoot = Join-Path $autoCadRoot 'R20.1'
        $releaseRootExists = $false
        try {
            $releaseRootExists = [bool](& $testPathOperation $releaseRoot)
        }
        catch {
            $releaseRootProbeFailureCount++
            continue
        }
        if (-not $releaseRootExists) {
            continue
        }
        $releaseRootsPresent++

        $keys = @()
        try {
            $keys += & $getItemOperation $releaseRoot
        }
        catch {
            $releaseRootReadFailureCount++
        }

        try {
            $keys += @(& $getChildItemsOperation $releaseRoot)
        }
        catch {
            $childEnumerationFailureCount++
        }

        foreach ($key in $keys) {
            $keysInspected++
            try {
                $properties = & $getItemPropertyOperation $key.PSPath
                $keysRead++
            }
            catch {
                $propertyReadFailureCount++
                continue
            }

            $records += [pscustomobject]@{
                RegistryKey = $key.Name
                Properties = $properties
            }
        }
    }

    $hints = @(ConvertTo-AutoCadRegistryHintsFromRecords -Records $records)
    if ($PSBoundParameters.ContainsKey('Diagnostics')) {
        $Diagnostics.Value = [pscustomobject]@{
            RegistryRootsConfigured = @($AutoCadRoots).Count
            ReleaseRootsPresent = $releaseRootsPresent
            ReleaseRootProbeFailureCount = $releaseRootProbeFailureCount
            ReleaseRootReadFailureCount = $releaseRootReadFailureCount
            ChildEnumerationFailureCount = $childEnumerationFailureCount
            KeysInspected = $keysInspected
            KeysRead = $keysRead
            PropertyReadFailureCount = $propertyReadFailureCount
            AcadLocationHintCount = @($hints | Where-Object { $_.ValueName -eq 'AcadLocation' }).Count
            InstallLocationHintCount = @($hints | Where-Object { $_.ValueName -eq 'InstallLocation' }).Count
            LocationHintCount = @($hints | Where-Object { $_.ValueName -eq 'Location' }).Count
            TotalHintCount = $hints.Count
        }
    }

    return $hints
}

function ConvertTo-InstallDirectory {
    param([AllowNull()][string]$CandidatePath)

    if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
        return $null
    }

    $expandedPath = [Environment]::ExpandEnvironmentVariables($CandidatePath.Trim().Trim('"'))
    if (Test-Path -LiteralPath $expandedPath -PathType Leaf) {
        return (Split-Path -Parent (Get-Item -LiteralPath $expandedPath -ErrorAction Stop).FullName)
    }

    if (Test-Path -LiteralPath $expandedPath -PathType Container) {
        return (Get-Item -LiteralPath $expandedPath -ErrorAction Stop).FullName.TrimEnd('\')
    }

    if ((Split-Path -Leaf $expandedPath) -ieq 'acad.exe') {
        return (Split-Path -Parent $expandedPath).TrimEnd('\')
    }

    return $expandedPath.TrimEnd('\')
}

function Get-CandidateInstallDirectories {
    param(
        [object[]]$RegistryHints,
        [string[]]$AdditionalDirectories,
        [AllowEmptyCollection()][string[]]$ProgramRoots
    )

    $candidateMap = @{}

    foreach ($hint in @($RegistryHints)) {
        $directory = ConvertTo-InstallDirectory -CandidatePath ([string]$hint.CandidatePath)
        if ([string]::IsNullOrWhiteSpace($directory)) {
            continue
        }

        if (-not $candidateMap.ContainsKey($directory)) {
            $candidateMap[$directory] = New-Object 'System.Collections.Generic.List[string]'
        }
        $candidateMap[$directory].Add(("Registry:{0}:{1}" -f $hint.ReleaseKey, $hint.ValueName))
    }

    foreach ($additionalDirectory in @($AdditionalDirectories)) {
        $directory = ConvertTo-InstallDirectory -CandidatePath $additionalDirectory
        if ([string]::IsNullOrWhiteSpace($directory)) {
            continue
        }

        if (-not $candidateMap.ContainsKey($directory)) {
            $candidateMap[$directory] = New-Object 'System.Collections.Generic.List[string]'
        }
        $candidateMap[$directory].Add('ExplicitParameter')
    }

    if (-not $PSBoundParameters.ContainsKey('ProgramRoots')) {
        $ProgramRoots = @(
            [Environment]::GetEnvironmentVariable('ProgramFiles'),
            [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
        )
    }
    $ProgramRoots = @($ProgramRoots |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Sort-Object -Unique)

    foreach ($programRoot in $ProgramRoots) {
        $autodeskRoot = Join-Path $programRoot 'Autodesk'
        if (-not (Test-Path -LiteralPath $autodeskRoot -PathType Container)) {
            continue
        }

        foreach ($directoryItem in @(Get-ChildItem -LiteralPath $autodeskRoot -Directory -ErrorAction SilentlyContinue)) {
            if ($directoryItem.Name -notmatch '(?i)AutoCAD.*2016|2016.*AutoCAD') {
                continue
            }

            $directory = $directoryItem.FullName.TrimEnd('\')
            if (-not $candidateMap.ContainsKey($directory)) {
                $candidateMap[$directory] = New-Object 'System.Collections.Generic.List[string]'
            }
            $candidateMap[$directory].Add('ProgramFilesNameMatch')
        }
    }

    $results = @()
    foreach ($directory in @($candidateMap.Keys | Sort-Object)) {
        $results += [pscustomobject]@{
            InstallDirectory = $directory
            DiscoverySources = @($candidateMap[$directory] | Sort-Object -Unique)
        }
    }

    return @($results)
}

function Test-VersionTextIsR201 {
    param([AllowNull()][string]$VersionText)

    return (-not [string]::IsNullOrWhiteSpace($VersionText) -and $VersionText -match '(?i)(^|[^0-9])20\.1([^0-9]|$)')
}

function Get-R201ReleaseEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$AcadPath,
        [Parameter(Mandatory = $true)][string]$InstallDirectory
    )

    $evidence = @()
    $acadItem = Get-Item -LiteralPath $AcadPath -ErrorAction Stop
    if (Test-VersionTextIsR201 -VersionText $acadItem.VersionInfo.FileVersion) {
        $evidence += ("acad.exe FileVersion={0}" -f $acadItem.VersionInfo.FileVersion)
    }
    if (Test-VersionTextIsR201 -VersionText $acadItem.VersionInfo.ProductVersion) {
        $evidence += ("acad.exe ProductVersion={0}" -f $acadItem.VersionInfo.ProductVersion)
    }

    foreach ($managedApiName in @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
        $managedApiPath = Join-Path $InstallDirectory $managedApiName
        if (-not (Test-Path -LiteralPath $managedApiPath -PathType Leaf)) {
            continue
        }

        try {
            $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($managedApiPath).Version.ToString()
            if (Test-VersionTextIsR201 -VersionText $assemblyVersion) {
                $evidence += ("{0} AssemblyVersion={1}" -f $managedApiName, $assemblyVersion)
            }
        }
        catch {
            continue
        }
    }

    return @($evidence | Sort-Object -Unique)
}

function Get-DotNetToolchainEvidence {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1
    $dotnetPath = if ($null -ne $dotnetCommand) {
        [string](Get-OptionalPropertyValue -InputObject $dotnetCommand -Name 'Source')
    }
    else {
        $null
    }

    $sdkOutput = @()
    $sdkExitCode = $null
    $runtimeOutput = @()
    $runtimeExitCode = $null
    $resolutionOutput = @()
    $resolutionExitCode = $null

    if (-not [string]::IsNullOrWhiteSpace($dotnetPath)) {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $sdkOutput = @(& $dotnetPath --list-sdks 2>&1 | ForEach-Object { [string]$_ })
            $sdkExitCode = $LASTEXITCODE
            $runtimeOutput = @(& $dotnetPath --list-runtimes 2>&1 | ForEach-Object { [string]$_ })
            $runtimeExitCode = $LASTEXITCODE

            Push-Location -LiteralPath $RepositoryRoot
            try {
                $resolutionOutput = @(& $dotnetPath --version 2>&1 | ForEach-Object { [string]$_ })
                $resolutionExitCode = $LASTEXITCODE
            }
            finally {
                Pop-Location
            }
        }
        catch {
            $resolutionOutput += $_.Exception.Message
            if ($null -eq $resolutionExitCode) {
                $resolutionExitCode = 1
            }
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }

    $globalJsonPath = Join-Path $RepositoryRoot 'global.json'
    $requestedSdkVersion = $null
    $rollForward = $null
    $allowPrerelease = $null
    $globalJsonError = $null
    if (Test-Path -LiteralPath $globalJsonPath -PathType Leaf) {
        try {
            $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $sdkSettings = Get-OptionalPropertyValue -InputObject $globalJson -Name 'sdk'
            $requestedSdkVersion = Get-OptionalPropertyValue -InputObject $sdkSettings -Name 'version'
            $rollForward = Get-OptionalPropertyValue -InputObject $sdkSettings -Name 'rollForward'
            $allowPrerelease = Get-OptionalPropertyValue -InputObject $sdkSettings -Name 'allowPrerelease'
        }
        catch {
            $globalJsonError = $_.Exception.Message
        }
    }

    [pscustomobject]@{
        CommandFound = (-not [string]::IsNullOrWhiteSpace($dotnetPath))
        CommandPath = $dotnetPath
        CommandFileVersion = if (-not [string]::IsNullOrWhiteSpace($dotnetPath) -and (Test-Path -LiteralPath $dotnetPath)) {
            (Get-Item -LiteralPath $dotnetPath).VersionInfo.FileVersion
        }
        else {
            $null
        }
        InstalledSdks = $sdkOutput
        ListSdksExitCode = $sdkExitCode
        InstalledRuntimes = $runtimeOutput
        ListRuntimesExitCode = $runtimeExitCode
        GlobalJson = [pscustomobject]@{
            Path = $globalJsonPath
            Exists = (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)
            RequestedSdkVersion = $requestedSdkVersion
            RollForward = $rollForward
            AllowPrerelease = $allowPrerelease
            ParseError = $globalJsonError
        }
        RepositorySdkResolution = [pscustomobject]@{
            Succeeded = ($resolutionExitCode -eq 0)
            ExitCode = $resolutionExitCode
            Output = $resolutionOutput
        }
    }
}

function Get-MSBuildToolchainEvidence {
    $candidatePaths = New-Object 'System.Collections.Generic.List[string]'
    $msbuildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $msbuildCommand) {
        $commandPath = [string](Get-OptionalPropertyValue -InputObject $msbuildCommand -Name 'Source')
        if (-not [string]::IsNullOrWhiteSpace($commandPath)) {
            $candidatePaths.Add($commandPath)
        }
    }

    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $vswherePath = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswherePath -PathType Leaf) {
            $previousErrorActionPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $foundPaths = @(& $vswherePath -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>&1)
                foreach ($foundPath in $foundPaths) {
                    if (Test-Path -LiteralPath ([string]$foundPath) -PathType Leaf) {
                        $candidatePaths.Add([string]$foundPath)
                    }
                }
            }
            catch {
                # Absence or failure of vswhere is represented by an empty candidate list.
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
        }
    }

    $results = @()
    foreach ($candidatePath in @($candidatePaths | Sort-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            continue
        }

        $item = Get-Item -LiteralPath $candidatePath -ErrorAction Stop
        $fileVersionText = [string]$item.VersionInfo.FileVersion
        $versionMatch = [regex]::Match($fileVersionText, '^\s*(?<major>\d+)')
        $versionMajor = if ($versionMatch.Success) { [int]$versionMatch.Groups['major'].Value } else { $null }
        $results += [pscustomobject]@{
            Path = $item.FullName
            SHA256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
            VersionMajor = $versionMajor
            VersionSupported = ($null -ne $versionMajor -and $versionMajor -ge 17)
            FileVersion = $item.VersionInfo.FileVersion
            ProductVersion = $item.VersionInfo.ProductVersion
            Architecture = Get-PeArchitecture -Path $item.FullName
            Authenticode = Get-AuthenticodeEvidence -Path $item.FullName
        }
    }

    return @($results)
}

function Get-ReferenceAssemblyCandidateEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $exists = Test-Path -LiteralPath $Path -PathType Container
    $mscorlibPath = Join-Path $Path 'mscorlib.dll'
    $systemPath = Join-Path $Path 'System.dll'
    $referenceAssemblyCount = 0
    if ($exists) {
        $referenceAssemblyCount = @(Get-ChildItem -LiteralPath $Path -Filter '*.dll' -File -ErrorAction SilentlyContinue).Count
    }

    [pscustomobject]@{
        Kind = $Kind
        Path = $Path
        Exists = $exists
        MscorlibExists = (Test-Path -LiteralPath $mscorlibPath -PathType Leaf)
        SystemExists = (Test-Path -LiteralPath $systemPath -PathType Leaf)
        ReferenceAssemblyCount = $referenceAssemblyCount
        Usable = ($exists -and (Test-Path -LiteralPath $mscorlibPath -PathType Leaf) -and (Test-Path -LiteralPath $systemPath -PathType Leaf))
    }
}

function Get-Net45ReferenceAssemblyEvidence {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $results = @()
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $machinePath = Join-Path $programFilesX86 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5'
        $results += Get-ReferenceAssemblyCandidateEvidence -Kind 'MachineTargetingPack' -Path $machinePath
    }

    $packageRoot = Join-Path $RepositoryRoot 'packages\microsoft.netframework.referenceassemblies.net45'
    if (Test-Path -LiteralPath $packageRoot -PathType Container) {
        foreach ($versionDirectory in @(Get-ChildItem -LiteralPath $packageRoot -Directory -ErrorAction SilentlyContinue)) {
            $packageReferencePath = Join-Path $versionDirectory.FullName 'build\.NETFramework\v4.5'
            $results += Get-ReferenceAssemblyCandidateEvidence -Kind 'NuGetCompileOnlyPackage' -Path $packageReferencePath
        }
    }

    $vendoredPackagePath = Join-Path $RepositoryRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
    $expectedVendoredPackageSha256 = '23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3'
    $vendoredPackageExists = Test-Path -LiteralPath $vendoredPackagePath -PathType Leaf
    $vendoredPackageSha256 = if ($vendoredPackageExists) {
        (Get-FileHash -LiteralPath $vendoredPackagePath -Algorithm SHA256 -ErrorAction Stop).Hash
    }
    else {
        $null
    }
    $results += [pscustomobject]@{
        Kind = 'VendoredLockedNuGetPackage'
        Path = $vendoredPackagePath
        Exists = $vendoredPackageExists
        MscorlibExists = $null
        SystemExists = $null
        ReferenceAssemblyCount = $null
        Sha256 = $vendoredPackageSha256
        ExpectedSha256 = $expectedVendoredPackageSha256
        Usable = ($vendoredPackageExists -and $vendoredPackageSha256 -eq $expectedVendoredPackageSha256)
    }

    return @($results)
}

function Assert-DiscoverySelfTest {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw ("AutoCAD 2016 discovery self-test failed: {0}" -f $Message)
    }

    $script:DiscoverySelfTestAssertionCount++
}

function Invoke-AutoCad2016DiscoverySelfTest {
    $script:DiscoverySelfTestAssertionCount = 0

    $records = @(
        [pscustomobject]@{
            RegistryKey = 'fixture-acad-location'
            Properties = [pscustomobject]@{ AcadLocation = 'C:\Fixture\Acad'; ProductName = 'Fixture A' }
        },
        [pscustomobject]@{
            RegistryKey = 'fixture-install-location'
            Properties = [pscustomobject]@{ InstallLocation = 'C:\Fixture\Install'; ProductName = 'Fixture B' }
        },
        [pscustomobject]@{
            RegistryKey = 'fixture-location'
            Properties = [pscustomobject]@{ Location = 'C:\Fixture\Location\acad.exe'; ProductName = 'Fixture C' }
        }
    )

    $hints = @(ConvertTo-AutoCadRegistryHintsFromRecords -Records $records)
    Assert-DiscoverySelfTest -Condition ($hints.Count -eq 3) -Message 'all three supported registry value names must produce hints.'
    Assert-DiscoverySelfTest -Condition (@($hints | Where-Object { $_.ValueName -eq 'AcadLocation' }).Count -eq 1) -Message 'AcadLocation must be recognized.'
    Assert-DiscoverySelfTest -Condition (@($hints | Where-Object { $_.ValueName -eq 'InstallLocation' }).Count -eq 1) -Message 'InstallLocation must be recognized.'
    Assert-DiscoverySelfTest -Condition (@($hints | Where-Object { $_.ValueName -eq 'Location' }).Count -eq 1) -Message 'Location must be recognized.'

    $locationOnlyHints = @(ConvertTo-AutoCadRegistryHintsFromRecords -Records @(
        [pscustomobject]@{
            RegistryKey = 'fixture-location-only'
            Properties = [pscustomobject]@{ Location = 'C:\OutsideProgramFiles\AutoCAD 2016\acad.exe' }
        }
    ))
    Assert-DiscoverySelfTest -Condition ($locationOnlyHints.Count -eq 1) -Message 'a Location-only non-Program-Files installation must remain discoverable.'
    Assert-DiscoverySelfTest -Condition ($locationOnlyHints[0].ValueName -eq 'Location') -Message 'the Location-only discovery source must be preserved.'
    Assert-DiscoverySelfTest -Condition ((ConvertTo-InstallDirectory -CandidatePath $locationOnlyHints[0].CandidatePath) -eq 'C:\OutsideProgramFiles\AutoCAD 2016') -Message 'a Location value pointing to acad.exe must normalize to its installation directory.'

    $deduplicated = @(Get-CandidateInstallDirectories -RegistryHints @(
        [pscustomobject]@{ CandidatePath = 'C:\Fixture\Same'; ReleaseKey = 'R20.1'; ValueName = 'AcadLocation' },
        [pscustomobject]@{ CandidatePath = 'C:\Fixture\Same\acad.exe'; ReleaseKey = 'R20.1'; ValueName = 'Location' }
    ) -AdditionalDirectories @('C:\Fixture\Same') -ProgramRoots @())
    Assert-DiscoverySelfTest -Condition ($deduplicated.Count -eq 1) -Message 'equivalent directory and acad.exe hints must be deduplicated.'
    Assert-DiscoverySelfTest -Condition ($deduplicated[0].DiscoverySources.Count -eq 3) -Message 'all deduplicated discovery sources must be retained.'

    $ignored = @(ConvertTo-AutoCadRegistryHintsFromRecords -Records @(
        [pscustomobject]@{
            RegistryKey = 'fixture-empty-values'
            Properties = [pscustomobject]@{ AcadLocation = ' '; InstallLocation = $null; Location = '' }
        }
    ))
    Assert-DiscoverySelfTest -Condition ($ignored.Count -eq 0) -Message 'empty registry values must not become candidates.'

    $probeFailureDiagnostics = $null
    $probeFailureHints = @(Get-AutoCadRegistryHints `
        -AutoCadRoots @('C:\FixtureRegistry\ProbeFailure') `
        -RegistryAccessor ([pscustomobject]@{
            TestPath = { param([string]$Path) throw 'fixture release-root probe failure' }
            GetItem = { param([string]$Path) throw 'unexpected GetItem call after probe failure' }
            GetChildItems = { param([string]$Path) throw 'unexpected GetChildItems call after probe failure' }
            GetItemProperty = { param([string]$Path) throw 'unexpected GetItemProperty call after probe failure' }
        }) `
        -Diagnostics ([ref]$probeFailureDiagnostics))
    Assert-DiscoverySelfTest -Condition ($probeFailureHints.Count -eq 0) -Message 'a release-root probe failure must not publish registry hints.'
    Assert-DiscoverySelfTest -Condition ($probeFailureDiagnostics.ReleaseRootProbeFailureCount -eq 1) -Message 'a release-root probe failure must be counted.'
    Assert-DiscoverySelfTest -Condition ($probeFailureDiagnostics.ReleaseRootsPresent -eq 0) -Message 'a failed release-root probe must not be reported as present.'

    $rootReadFailureDiagnostics = $null
    $rootReadFailureHints = @(Get-AutoCadRegistryHints `
        -AutoCadRoots @('C:\FixtureRegistry\ReadFailure') `
        -RegistryAccessor ([pscustomobject]@{
            TestPath = { param([string]$Path) $true }
            GetItem = { param([string]$Path) throw 'fixture release-root read failure' }
            GetChildItems = { param([string]$Path) throw 'fixture child enumeration failure' }
            GetItemProperty = { param([string]$Path) throw 'unexpected GetItemProperty call without keys' }
        }) `
        -Diagnostics ([ref]$rootReadFailureDiagnostics))
    Assert-DiscoverySelfTest -Condition ($rootReadFailureHints.Count -eq 0) -Message 'root and child read failures must not publish registry hints.'
    Assert-DiscoverySelfTest -Condition ($rootReadFailureDiagnostics.ReleaseRootsPresent -eq 1) -Message 'a successfully probed release root must remain observable when reads fail.'
    Assert-DiscoverySelfTest -Condition ($rootReadFailureDiagnostics.ReleaseRootReadFailureCount -eq 1) -Message 'a release-root item read failure must be counted.'
    Assert-DiscoverySelfTest -Condition ($rootReadFailureDiagnostics.ChildEnumerationFailureCount -eq 1) -Message 'a child enumeration failure must be counted independently.'
    Assert-DiscoverySelfTest -Condition ($rootReadFailureDiagnostics.KeysInspected -eq 0) -Message 'failed root and child reads must not fabricate inspected keys.'

    $propertyReadFailureDiagnostics = $null
    $propertyReadFailureHints = @(Get-AutoCadRegistryHints `
        -AutoCadRoots @('C:\FixtureRegistry\PropertyFailure') `
        -RegistryAccessor ([pscustomobject]@{
            TestPath = { param([string]$Path) $true }
            GetItem = {
                param([string]$Path)
                [pscustomobject]@{ Name = 'fixture-root-key'; PSPath = 'fixture-root-key' }
            }
            GetChildItems = {
                param([string]$Path)
                @(
                    [pscustomobject]@{ Name = 'fixture-good-key'; PSPath = 'fixture-good-key' },
                    [pscustomobject]@{ Name = 'fixture-bad-key'; PSPath = 'fixture-bad-key' }
                )
            }
            GetItemProperty = {
                param([string]$Path)
                if ($Path -eq 'fixture-bad-key') {
                    throw 'fixture property read failure'
                }
                if ($Path -eq 'fixture-root-key') {
                    return [pscustomobject]@{ AcadLocation = 'C:\Fixture\Root' }
                }
                if ($Path -eq 'fixture-good-key') {
                    return [pscustomobject]@{ Location = 'C:\Fixture\Good\acad.exe' }
                }
                throw ("unexpected fixture key: {0}" -f $Path)
            }
        }) `
        -Diagnostics ([ref]$propertyReadFailureDiagnostics))
    Assert-DiscoverySelfTest -Condition ($propertyReadFailureHints.Count -eq 2) -Message 'a failed sibling property read must not discard valid registry hints.'
    Assert-DiscoverySelfTest -Condition ($propertyReadFailureDiagnostics.PropertyReadFailureCount -eq 1) -Message 'a property read failure must be counted.'
    Assert-DiscoverySelfTest -Condition ($propertyReadFailureDiagnostics.KeysInspected -eq 3) -Message 'all returned keys must be counted as inspected.'
    Assert-DiscoverySelfTest -Condition ($propertyReadFailureDiagnostics.KeysRead -eq 2) -Message 'only successfully read keys must be counted as read.'
    Assert-DiscoverySelfTest -Condition ($propertyReadFailureDiagnostics.AcadLocationHintCount -eq 1) -Message 'valid AcadLocation data must survive a sibling property read failure.'
    Assert-DiscoverySelfTest -Condition ($propertyReadFailureDiagnostics.LocationHintCount -eq 1) -Message 'valid Location data must survive a sibling property read failure.'

    $expectedAssertionCount = 24
    if ($script:DiscoverySelfTestAssertionCount -ne $expectedAssertionCount) {
        throw ("AutoCAD 2016 discovery self-test assertion count mismatch: {0}/{1}." -f $script:DiscoverySelfTestAssertionCount, $expectedAssertionCount)
    }

    Write-Host ("AutoCAD 2016 environment discovery self-test passed ({0}/{1})." -f $script:DiscoverySelfTestAssertionCount, $expectedAssertionCount)
}

if ($RunDiscoverySelfTest) {
    Invoke-AutoCad2016DiscoverySelfTest
    return
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$outputDirectory = Join-Path $OutputRoot ("autocad2016-environment-{0}" -f $timestamp)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$dotNetEvidence = Get-DotNetToolchainEvidence -RepositoryRoot $repoRoot
$msBuildEvidence = @(Get-MSBuildToolchainEvidence)
$net45ReferenceEvidence = @(Get-Net45ReferenceAssemblyEvidence -RepositoryRoot $repoRoot)
$msBuildAvailable = ($msBuildEvidence.Count -gt 0)
$trustedMsBuildEvidence = @($msBuildEvidence | Where-Object {
    $_.Authenticode.IsValid -and $_.Authenticode.IsMicrosoftPublisher
})
$supportedMsBuildEvidence = @($trustedMsBuildEvidence | Where-Object { $_.VersionSupported })
$msBuildTrusted = ($trustedMsBuildEvidence.Count -gt 0)
$msBuildReady = ($supportedMsBuildEvidence.Count -gt 0)
$net45ReferencesReady = (@($net45ReferenceEvidence | Where-Object { $_.Usable }).Count -gt 0)

$registryDiscoveryDiagnostics = $null
$registryHints = @(Get-AutoCadRegistryHints -Diagnostics ([ref]$registryDiscoveryDiagnostics))
$candidateDirectories = @(Get-CandidateInstallDirectories -RegistryHints $registryHints -AdditionalDirectories $AdditionalInstallDirectory)
$installations = @()
$rejectedCandidates = @()
$seenExecutablePaths = @{}

foreach ($candidateDirectory in $candidateDirectories) {
    if (-not (Test-Path -LiteralPath $candidateDirectory.InstallDirectory -PathType Container)) {
        $rejectedCandidates += [pscustomobject]@{
            InstallDirectory = $candidateDirectory.InstallDirectory
            DiscoverySources = $candidateDirectory.DiscoverySources
            Reason = 'Candidate directory does not exist.'
        }
        continue
    }

    $acadPaths = @()
    $acadSearchFailed = $false
    $directAcadPath = Join-Path $candidateDirectory.InstallDirectory 'acad.exe'
    if (Test-Path -LiteralPath $directAcadPath -PathType Leaf) {
        $acadPaths = @((Get-Item -LiteralPath $directAcadPath -ErrorAction Stop).FullName)
    }
    else {
        try {
            $acadPaths = @(Get-ChildItem -LiteralPath $candidateDirectory.InstallDirectory -Filter 'acad.exe' -File -Recurse -ErrorAction Stop |
                ForEach-Object { $_.FullName })
        }
        catch {
            $acadPaths = @()
            $acadSearchFailed = $true
        }
    }

    if ($acadPaths.Count -eq 0) {
        $rejectedCandidates += [pscustomobject]@{
            InstallDirectory = $candidateDirectory.InstallDirectory
            DiscoverySources = $candidateDirectory.DiscoverySources
            Reason = if ($acadSearchFailed) {
                'acad.exe discovery failed because the candidate directory could not be read.'
            }
            else {
                'acad.exe was not found.'
            }
        }
        continue
    }

    foreach ($acadPath in $acadPaths) {
        if ($seenExecutablePaths.ContainsKey($acadPath)) {
            continue
        }
        $seenExecutablePaths[$acadPath] = $true

        $installDirectory = Split-Path -Parent $acadPath
        $releaseEvidence = @(Get-R201ReleaseEvidence -AcadPath $acadPath -InstallDirectory $installDirectory)
        if ($releaseEvidence.Count -eq 0) {
            $acadItem = Get-Item -LiteralPath $acadPath -ErrorAction Stop
            $rejectedCandidates += [pscustomobject]@{
                InstallDirectory = $installDirectory
                DiscoverySources = $candidateDirectory.DiscoverySources
                Reason = ("Executable and managed assemblies do not prove R20.1. FileVersion={0}; ProductVersion={1}" -f $acadItem.VersionInfo.FileVersion, $acadItem.VersionInfo.ProductVersion)
            }
            continue
        }

        $acadDetails = Get-SafeFileDetails -Path $acadPath
        $managedApiNames = @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')
        $managedApis = @()
        $missingManagedApis = @()
        foreach ($managedApiName in $managedApiNames) {
            $managedApiPath = Join-Path $installDirectory $managedApiName
            if (Test-Path -LiteralPath $managedApiPath -PathType Leaf) {
                $managedApis += Get-SafeFileDetails -Path $managedApiPath
            }
            else {
                $missingManagedApis += $managedApiName
            }
        }

        $consolePath = Join-Path $installDirectory 'accoreconsole.exe'
        $accoreConsole = if (Test-Path -LiteralPath $consolePath -PathType Leaf) {
            Get-SafeFileDetails -Path $consolePath
        }
        else {
            $null
        }

        $managedVersionsMatch = ($managedApis.Count -eq $managedApiNames.Count)
        foreach ($managedApi in $managedApis) {
            if (-not (Test-VersionTextIsR201 -VersionText $managedApi.AssemblyVersion)) {
                $managedVersionsMatch = $false
            }
        }

        $requiredFileDetails = @($acadDetails) + @($managedApis)
        $requiredSignaturesValid = ($requiredFileDetails.Count -eq 4)
        foreach ($requiredFile in $requiredFileDetails) {
            if (-not $requiredFile.Authenticode.IsValid -or -not $requiredFile.Authenticode.IsAutodeskPublisher) {
                $requiredSignaturesValid = $false
            }
        }

        $acadArchitectureX64 = ($acadDetails.Architecture -eq 'x64')
        $acadVersionMatchesR201 = ((Test-VersionTextIsR201 -VersionText $acadDetails.FileVersion) -or
            (Test-VersionTextIsR201 -VersionText $acadDetails.ProductVersion))
        $autodeskInputReady = ($acadArchitectureX64 -and $acadVersionMatchesR201 -and
            ($missingManagedApis.Count -eq 0) -and $managedVersionsMatch -and $requiredSignaturesValid)
        $buildReady = ($autodeskInputReady -and $msBuildReady -and $net45ReferencesReady)

        $installations += [pscustomobject]@{
            InstallDirectory = $installDirectory
            DiscoverySources = $candidateDirectory.DiscoverySources
            ReleaseEvidence = $releaseEvidence
            Acad = $acadDetails
            AccoreConsole = $accoreConsole
            ManagedApis = $managedApis
            Validation = [pscustomobject]@{
                AcadArchitectureX64 = $acadArchitectureX64
                AcadVersionMatchesR201 = $acadVersionMatchesR201
                RequiredManagedApisPresent = ($missingManagedApis.Count -eq 0)
                MissingManagedApis = $missingManagedApis
                ManagedAssemblyVersionsMatchR201 = $managedVersionsMatch
                RequiredFileSignaturesValidAndAutodesk = $requiredSignaturesValid
                AutodeskInputReady = $autodeskInputReady
                MSBuildAvailable = $msBuildAvailable
                MSBuildSignatureValidAndMicrosoft = $msBuildTrusted
                MSBuildVersionSupported = $msBuildReady
                Net45ReferenceAssembliesUsable = $net45ReferencesReady
                BuildReady = $buildReady
                ReadyForHost2016Build = $buildReady
            }
        }
    }
}

$operatingSystem = $null
try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $operatingSystem = [pscustomobject]@{
        Caption = Get-OptionalPropertyValue -InputObject $os -Name 'Caption'
        Version = Get-OptionalPropertyValue -InputObject $os -Name 'Version'
        BuildNumber = Get-OptionalPropertyValue -InputObject $os -Name 'BuildNumber'
        OSArchitecture = Get-OptionalPropertyValue -InputObject $os -Name 'OSArchitecture'
    }
}
catch {
    $operatingSystem = [pscustomobject]@{
        Caption = [Environment]::OSVersion.VersionString
        Version = [Environment]::OSVersion.Version.ToString()
        BuildNumber = $null
        OSArchitecture = if ([Environment]::Is64BitOperatingSystem) { '64-bit' } else { '32-bit' }
    }
}

$dotNetFramework = $null
try {
    $frameworkKey = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop
    $dotNetFramework = [pscustomobject]@{
        Release = Get-OptionalPropertyValue -InputObject $frameworkKey -Name 'Release'
        Version = Get-OptionalPropertyValue -InputObject $frameworkKey -Name 'Version'
        Install = Get-OptionalPropertyValue -InputObject $frameworkKey -Name 'Install'
    }
}
catch {
    $dotNetFramework = $null
}

$readyInstallationCount = @($installations | Where-Object { $_.Validation.BuildReady }).Count
$collectionSucceeded = ($readyInstallationCount -gt 0)
$failureReason = if ($collectionSucceeded) {
    $null
}
elseif ($installations.Count -eq 0) {
    'No installed AutoCAD 2016 (R20.1) executable was proven by registry/program-files discovery and acad.exe version evidence.'
}
else {
    'AutoCAD 2016 candidates were found, but none passed the complete Host.2016 build-readiness gate (x64 acad.exe R20.1, exact managed APIs, Autodesk signatures, a valid Microsoft-signed MSBuild version 17 or later, and usable .NET Framework 4.5 reference assemblies).'
}

$report = [pscustomobject]@{
    SchemaVersion = 5
    CollectedAt = (Get-Date).ToString('o')
    Purpose = 'Codex for AutoCAD 2016 compatibility probe'
    CollectionSucceeded = $collectionSucceeded
    FailureReason = $failureReason
    Safety = [pscustomobject]@{
        SystemAndCadStateReadOnly = $true
        AutoCadProcessStarted = $false
        AutoCadCommandsSent = $false
        TrustedPathsHandling = 'The TRUSTEDPATHS value is intentionally neither queried nor requested because it can contain sensitive local or network paths.'
    }
    PowerShell = [pscustomobject]@{
        Version = $PSVersionTable.PSVersion.ToString()
        Edition = Get-OptionalPropertyValue -InputObject $PSVersionTable -Name 'PSEdition'
        ProcessIs64Bit = [Environment]::Is64BitProcess
    }
    OperatingSystem = $operatingSystem
    DotNetFramework = $dotNetFramework
    Toolchain = [pscustomobject]@{
        DotNet = $dotNetEvidence
        MSBuild = $msBuildEvidence
        Net45ReferenceAssemblies = $net45ReferenceEvidence
    }
    AutoCadRegistryScope = @(
        'HKLM:\SOFTWARE\Autodesk\AutoCAD\R20.1',
        'HKLM:\SOFTWARE\WOW6432Node\Autodesk\AutoCAD\R20.1'
    )
    DiscoveryDiagnostics = $registryDiscoveryDiagnostics
    AutoCadRegistryHints = $registryHints
    RejectedCandidates = $rejectedCandidates
    AutoCad2016Installations = $installations
    ManualAutoCadValuesRequired = @('ACADVER', 'VERNUM', 'SECURELOAD', 'APPAUTOLOAD', 'DBMOD-before', 'DBMOD-after')
}

$jsonPath = Join-Path $outputDirectory 'environment.json'
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$summaryStatus = if ($collectionSucceeded) { 'PASS' } else { 'FAIL' }
$textPath = Join-Path $outputDirectory 'SUMMARY.txt'
@(
    ("Codex for AutoCAD 2016 environment collection: {0}" -f $summaryStatus),
    ('Collected at: {0}' -f $report.CollectedAt),
    ('AutoCAD 2016 R20.1 installations: {0}' -f $installations.Count),
    ('Installations ready for Host.2016 compilation: {0}' -f $readyInstallationCount),
    ('Registry hints: AcadLocation={0}; InstallLocation={1}; Location={2}; read failures={3}' -f
        $registryDiscoveryDiagnostics.AcadLocationHintCount,
        $registryDiscoveryDiagnostics.InstallLocationHintCount,
        $registryDiscoveryDiagnostics.LocationHintCount,
        ($registryDiscoveryDiagnostics.ReleaseRootProbeFailureCount +
            $registryDiscoveryDiagnostics.ReleaseRootReadFailureCount +
            $registryDiscoveryDiagnostics.ChildEnumerationFailureCount +
            $registryDiscoveryDiagnostics.PropertyReadFailureCount)),
    ('Detailed JSON: {0}' -f $jsonPath),
    '',
    'This collector did not start AutoCAD, send commands, or modify AutoCAD/system settings.',
    'TRUSTEDPATHS is intentionally omitted because it can contain sensitive local or network paths.',
    'Before sharing, remove API keys, tokens, license serials, real drawing paths, and internal addresses.',
    $(if ($collectionSucceeded) { '' } else { $failureReason })
) | Set-Content -LiteralPath $textPath -Encoding UTF8

$captureOutput = Join-Path $outputDirectory 'autocad-console.txt'
@(
    'Codex for AutoCAD 2016 manual read-only command capture',
    '',
    'Do not change SECURELOAD, APPAUTOLOAD, or other AutoCAD settings.',
    'TRUSTEDPATHS is intentionally omitted; do not paste its value.',
    '',
    'ACADVER:',
    'VERNUM:',
    'SECURELOAD:',
    'APPAUTOLOAD:',
    'DBMOD before diagnostic load:',
    'DBMOD after diagnostic commands:'
) | Set-Content -LiteralPath $captureOutput -Encoding UTF8

if (-not $collectionSucceeded) {
    Write-Host 'AutoCAD 2016 environment collection failed.' -ForegroundColor Red
    Write-Host ("Output directory: {0}" -f $outputDirectory)
    throw $failureReason
}

Write-Host 'AutoCAD 2016 environment collection completed.' -ForegroundColor Green
Write-Host ("AutoCAD 2016 R20.1 installations: {0}" -f $installations.Count)
Write-Host ("Ready for Host.2016 compilation: {0}" -f $readyInstallationCount)
Write-Host ("Output directory: {0}" -f $outputDirectory)
Write-Host 'No AutoCAD process was started; TRUSTEDPATHS was not collected.'

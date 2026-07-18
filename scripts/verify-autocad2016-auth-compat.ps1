[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$safeRepoRoot = $repoRoot.Replace("\", "/")
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$solutionPath = Join-Path $repoRoot "Codex.AutoCAD.sln"
$contractsProject = Join-Path $repoRoot "src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj"
$ipcProject = Join-Path $repoRoot "src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj"
$specProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Ipc.Specs\Codex.AutoCAD.Ipc.Specs.csproj"
$bridgeProject = Join-Path $repoRoot "src\Codex.AutoCAD.Bridge\Codex.AutoCAD.Bridge.csproj"
$bridgeSpecProject = Join-Path $repoRoot "tests\Codex.AutoCAD.Bridge.Specs\Codex.AutoCAD.Bridge.Specs.csproj"
$globalJsonPath = Join-Path $repoRoot "global.json"
$directoryBuildPropsPath = Join-Path $repoRoot "Directory.Build.props"
$nugetConfig = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\NuGet.Config"
$offlinePackage = Join-Path $repoRoot "third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg"
$expectedSdk = "8.0.319"
$expectedPackageSha256 = "23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3"
$expectedCanonical = "313A31383A6D73672DCEB12DF09F9880363A636F72722DE4B8AD31323A73657373696F6E2D32303136323A343231313A6361642E636F6E7465787432343A7B2274657874223A22E4B8ADE69687F09F9880222C226C696E65223A317D33323A3030313132323333343435353636373738383939414142424343444445454646"
$expectedMac = "46FFA5506FD595BA64CEAD67EDBAF8707E1A585988BC80298EBF569F69B38400"
$expectedSpecCount = 17
$runId = [Guid]::NewGuid().ToString("N")
$stageRoot = Join-Path $repoRoot ("artifacts\autocad2016-auth-compat-" + $runId)
$evidencePath = Join-Path $stageRoot "verification.json"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

function Invoke-Captured {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [string[]] $Arguments = @(),

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    Write-Host ("`n==> " + $Description) -ForegroundColor Cyan
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 wraps native stderr as ErrorRecord. Capture it and
        # decide solely from the native exit code so benign warnings do not abort.
        $ErrorActionPreference = "Continue"
        $raw = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $lines = @($raw | ForEach-Object { [string] $_ })
    foreach ($line in $lines) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw "$Description 失败，退出码：$exitCode"
    }

    return $lines
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少预期文件：$Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-SourceSnapshot {
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    $snapshot = [ordered]@{}
    foreach ($path in $Paths) {
        $absolutePath = [IO.Path]::GetFullPath($path)
        $relativePath = $absolutePath.Substring($repoRoot.Length + 1).Replace("\", "/")
        $snapshot[$relativePath] = Get-Sha256 -Path $absolutePath
    }

    return $snapshot
}

function Get-TreeSnapshot {
    param([Parameter(Mandatory = $true)][string[]] $Roots)

    $snapshot = [ordered]@{}
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)) {
            $relativePath = $file.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")
            $snapshot[$relativePath] = [ordered]@{
                Length = $file.Length
                LastWriteUtcTicks = $file.LastWriteTimeUtc.Ticks
                Sha256 = Get-Sha256 -Path $file.FullName
            }
        }
    }

    return $snapshot
}

function Assert-SnapshotsEqual {
    param(
        [Parameter(Mandatory = $true)] $Before,
        [Parameter(Mandatory = $true)] $After,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $beforeJson = $Before | ConvertTo-Json -Depth 8 -Compress
    $afterJson = $After | ConvertTo-Json -Depth 8 -Compress
    if ($beforeJson -cne $afterJson) {
        throw "$Label 在隔离验证期间发生变化。"
    }
}

function Assert-AuthenticationSourceBoundary {
    $sourcePath = Join-Path $repoRoot "src\Codex.AutoCAD.Ipc\IpcAuthentication.cs"
    $source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8
    $forbiddenRules = [ordered]@{
        "进程或 Shell" = "(?i)(?:System\s*\.\s*Diagnostics\s*\.\s*Process|ProcessStartInfo|Process\s*\.\s*Start|ShellExecute|CreateProcess)"
        "文件或注册表" = "(?i)(?:System\s*\.\s*IO\s*\.|\bFile\s*\.|\bDirectory\s*\.|Microsoft\s*\.\s*Win32|Registry(?:Key)?)"
        "网络或 IPC" = "(?i)(?:System\s*\.\s*Net|HttpClient|WebRequest|Socket|TcpClient|UdpClient|System\s*\.\s*IO\s*\.\s*Pipes|NamedPipe|MemoryMappedFile)"
        "动态或原生加载" = "(?i)(?:Assembly\s*\.\s*(?:Load|LoadFrom|LoadFile)|DllImport|LoadLibrary|GetProcAddress|NativeLibrary)"
        "后台执行" = "(?i)(?:Task\s*\.\s*Run|Thread\b|ThreadPool|Timer\s*\()"
    }
    $requiredDetections = [ordered]@{
        "Process.Start" = "Process.Start(info);"
        "File.WriteAllText" = "File.WriteAllText(path, secret);"
        "NamedPipeClientStream" = "new NamedPipeClientStream(name);"
        "HttpClient" = "new HttpClient();"
        "Assembly.LoadFrom" = "Assembly.LoadFrom(path);"
        "Task.Run" = "Task.Run(work);"
    }

    foreach ($sample in $requiredDetections.GetEnumerator()) {
        $matched = $false
        foreach ($rule in $forbiddenRules.GetEnumerator()) {
            if ([regex]::IsMatch([string] $sample.Value, [string] $rule.Value)) {
                $matched = $true
                break
            }
        }
        if (-not $matched) {
            throw "认证源码禁止 API 自检未覆盖：$($sample.Key)"
        }
    }

    foreach ($rule in $forbiddenRules.GetEnumerator()) {
        if ([regex]::IsMatch($source, [string] $rule.Value)) {
            throw "认证源码命中禁止边界：$($rule.Key)"
        }
    }

    if ($source -notmatch "(?m)^public static class IpcCanonicalEnvelopeEncoding\s*$" -or
        $source -notmatch "UTF-16 code-unit counts" -or
        $source -notmatch "new UTF8Encoding\(false, true\)" -or
        $source -notmatch "CryptographicOperations\.FixedTimeEquals" -or
        $source -notmatch "Array\.Clear\(_sessionSecret") {
        throw "认证源码缺少协议冻结、严格 UTF-8、定时比较或密钥清零边界。"
    }

    if ($source -match "ReadOnlySpan<byte>\s+sessionSecret") {
        throw "认证器不得通过未清零的 ReadOnlySpan.ToArray 中间副本引导会话密钥。"
    }

    Write-Host "认证源码边界检查通过。" -ForegroundColor Green
}

function Assert-ProjectShape {
    foreach ($projectPath in @($contractsProject, $ipcProject, $specProject)) {
        $projectText = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
        if ($projectText -notmatch "EnableAutoCad2016" -or
            $projectText -notmatch "net45;net8\.0" -or
            $projectText -notmatch "Microsoft\.NETFramework\.ReferenceAssemblies\.net45" -or
            $projectText -notmatch 'Version="\[1\.0\.3\]"') {
            throw "项目未精确声明 net45/net8 条件目标及固定 net45 引用包：$projectPath"
        }
    }

    $ipcDefault = Invoke-Captured -FilePath $dotnetCommand -Arguments @(
        "msbuild", $ipcProject, "-nologo", "-getProperty:TargetFramework", "-getProperty:TargetFrameworks"
    ) -Description "检查 IPC 默认目标框架"
    $ipcDefaultJson = ($ipcDefault -join "`n") | ConvertFrom-Json
    if ($ipcDefaultJson.Properties.TargetFramework -cne "net8.0" -or
        -not [string]::IsNullOrEmpty([string] $ipcDefaultJson.Properties.TargetFrameworks)) {
        throw "IPC 默认构建必须仅目标 net8.0。"
    }

    $ipcCompat = Invoke-Captured -FilePath $dotnetCommand -Arguments @(
        "msbuild", $ipcProject, "-nologo", "-p:EnableAutoCad2016=true",
        "-getProperty:TargetFramework", "-getProperty:TargetFrameworks"
    ) -Description "检查 IPC AutoCAD 2016 条件目标框架"
    $ipcCompatJson = ($ipcCompat -join "`n") | ConvertFrom-Json
    if ($ipcCompatJson.Properties.TargetFrameworks -cne "net45;net8.0") {
        throw "IPC AutoCAD 2016 条件构建必须精确目标 net45;net8.0。"
    }

    Write-Host "认证项目条件目标框架检查通过。" -ForegroundColor Green
}

function Invoke-IsolatedManagedCoreRegression {
    $regressionRoot = Join-Path $stageRoot "managed-core-regression"
    $outputRoot = Join-Path $regressionRoot "artifacts"
    $cliHome = Join-Path $regressionRoot "dotnet-home"
    New-Item -ItemType Directory -Path $regressionRoot -Force | Out-Null

    $previousPathMap = $env:PathMap
    $previousCliHome = $env:DOTNET_CLI_HOME
    try {
        $env:PathMap = ($regressionRoot + "=/_regression/," + $repoRoot + "=/_/")
        $env:DOTNET_CLI_HOME = $cliHome

        Invoke-Captured -FilePath $dotnetCommand -Arguments @(
            "restore", $solutionPath,
            "--disable-parallel",
            "-p:UseArtifactsOutput=true",
            ("-p:ArtifactsPath=" + $outputRoot)
        ) -Description "隔离恢复托管核心解决方案" | Out-Null

        Invoke-Captured -FilePath $dotnetCommand -Arguments @(
            "build", $solutionPath, "--configuration", $Configuration,
            "--nologo", "--disable-build-servers", "--no-restore", "-m:1",
            "-p:UseArtifactsOutput=true",
            ("-p:ArtifactsPath=" + $outputRoot),
            "-p:ContinuousIntegrationBuild=true"
        ) -Description "隔离回归构建托管核心解决方案" | Out-Null
    }
    finally {
        $env:PathMap = $previousPathMap
        $env:DOTNET_CLI_HOME = $previousCliHome
    }

    $bridgeSpecRoot = Join-Path $outputRoot "bin\Codex.AutoCAD.Bridge.Specs"
    $bridgeSpecCandidates = @(
        Get-ChildItem -LiteralPath $bridgeSpecRoot -Recurse -File -Filter "Codex.AutoCAD.Bridge.Specs.dll"
    )
    if ($bridgeSpecCandidates.Count -ne 1) {
        throw "隔离回归构建必须精确产生一个 Bridge Specs DLL，实际：$($bridgeSpecCandidates.Count)。"
    }

    $bridgeRoot = Join-Path $outputRoot "bin\Codex.AutoCAD.Bridge"
    $bridgeCandidates = @(
        Get-ChildItem -LiteralPath $bridgeRoot -Recurse -File -Filter "Codex.AutoCAD.Bridge.dll"
    )
    if ($bridgeCandidates.Count -ne 1) {
        throw "隔离回归构建必须精确产生一个 Bridge DLL，实际：$($bridgeCandidates.Count)。"
    }

    $bridgeSpecRuntimeDirectory = Split-Path -Parent $bridgeSpecCandidates[0].FullName
    $runtimeArtifactNames = @(
        "Codex.AutoCAD.Bridge.Specs.dll",
        "Codex.AutoCAD.Bridge.dll",
        "Codex.AutoCAD.Ipc.dll",
        "Codex.AutoCAD.Contracts.dll",
        "Codex.AutoCAD.Bridge.Specs.deps.json",
        "Codex.AutoCAD.Bridge.Specs.runtimeconfig.json"
    )
    $runtimeArtifactHashes = [ordered]@{}
    foreach ($artifactName in $runtimeArtifactNames) {
        $artifactPath = Join-Path $bridgeSpecRuntimeDirectory $artifactName
        $runtimeArtifactHashes[$artifactName] = Get-Sha256 -Path $artifactPath
    }

    $bridgeProjectOutputSha256 = Get-Sha256 -Path $bridgeCandidates[0].FullName
    if ($runtimeArtifactHashes["Codex.AutoCAD.Bridge.dll"] -cne $bridgeProjectOutputSha256) {
        throw "Bridge Specs 实际加载目录中的 Bridge DLL 与项目主输出不一致。"
    }

    $bridgeOutput = Invoke-Captured -FilePath $dotnetCommand -Arguments @(
        $bridgeSpecCandidates[0].FullName
    ) -Description "运行隔离 Bridge 规格"
    if (@($bridgeOutput | Where-Object { $_ -match "^\s*29/29 specs passed\s*$" }).Count -ne 1) {
        throw "Bridge 回归必须精确通过 29/29。"
    }

    return [pscustomobject]@{
        BridgeProjectOutputSha256 = $bridgeProjectOutputSha256
        RuntimeArtifactHashes = $runtimeArtifactHashes
        RuntimeBridgeCopyMatchesProjectOutput = $true
    }
}

function Invoke-IsolatedBuild {
    param([Parameter(Mandatory = $true)][string] $Name)

    $buildRoot = Join-Path $stageRoot $Name
    $outputRoot = Join-Path $buildRoot "out"
    $packageRoot = Join-Path $buildRoot "packages"
    $cliHome = Join-Path $buildRoot "dotnet-home"
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null

    $previousPathMap = $env:PathMap
    $previousCliHome = $env:DOTNET_CLI_HOME
    try {
        $env:PathMap = ($buildRoot + "=/_build/," + $repoRoot + "=/_/")
        $env:DOTNET_CLI_HOME = $cliHome

        Invoke-Captured -FilePath $dotnetCommand -Arguments @(
            "restore", $specProject,
            "--configfile", $nugetConfig,
            "--packages", $packageRoot,
            "--force", "--no-cache",
            "-p:EnableAutoCad2016=true",
            "-p:UseArtifactsOutput=true",
            ("-p:ArtifactsPath=" + $outputRoot)
        ) -Description ("离线隔离恢复 " + $Name) | Out-Null

        Invoke-Captured -FilePath $dotnetCommand -Arguments @(
            "build", $specProject,
            "--configuration", $Configuration,
            "--nologo", "--disable-build-servers", "--no-restore",
            "-p:EnableAutoCad2016=true",
            "-p:UseArtifactsOutput=true",
            ("-p:ArtifactsPath=" + $outputRoot),
            "-p:ContinuousIntegrationBuild=true"
        ) -Description ("双目标 Release 构建 " + $Name) | Out-Null
    }
    finally {
        $env:PathMap = $previousPathMap
        $env:DOTNET_CLI_HOME = $previousCliHome
    }

    return [pscustomobject]@{
        Name = $Name
        Root = $buildRoot
        OutputRoot = $outputRoot
        Net45Ipc = Join-Path $outputRoot "bin\Codex.AutoCAD.Ipc\release_net45\Codex.AutoCAD.Ipc.dll"
        Net8Ipc = Join-Path $outputRoot "bin\Codex.AutoCAD.Ipc\release_net8.0\Codex.AutoCAD.Ipc.dll"
        Net45Contracts = Join-Path $outputRoot "bin\Codex.AutoCAD.Contracts\release_net45\Codex.AutoCAD.Contracts.dll"
        Net8Contracts = Join-Path $outputRoot "bin\Codex.AutoCAD.Contracts\release_net8.0\Codex.AutoCAD.Contracts.dll"
        Net45Specs = Join-Path $outputRoot "bin\Codex.AutoCAD.Ipc.Specs\release_net45\Codex.AutoCAD.Ipc.Specs.exe"
        Net8Specs = Join-Path $outputRoot "bin\Codex.AutoCAD.Ipc.Specs\release_net8.0\Codex.AutoCAD.Ipc.Specs.dll"
    }
}

function Assert-SpecOutput {
    param(
        [Parameter(Mandatory = $true)][string[]] $Lines,
        [Parameter(Mandatory = $true)][string] $RuntimeLabel
    )

    $summaryPattern = "^\s*" + $expectedSpecCount + "/" + $expectedSpecCount + " specs passed\s*$"
    $summaries = @($Lines | Where-Object { $_ -match $summaryPattern })
    if ($summaries.Count -ne 1) {
        throw "$RuntimeLabel 必须且只能输出一条 $expectedSpecCount/$expectedSpecCount 规格摘要。"
    }

    $vectorPattern = "^AUTH_VECTOR_V1 canonical=(?<Canonical>[0-9A-F]+) mac=(?<Mac>[0-9A-F]+)$"
    $vectors = @($Lines | Where-Object { $_ -match $vectorPattern })
    if ($vectors.Count -ne 1) {
        throw "$RuntimeLabel 必须且只能输出一条 AUTH_VECTOR_V1。"
    }

    $match = [regex]::Match($vectors[0], $vectorPattern)
    if ($match.Groups["Canonical"].Value -cne $expectedCanonical -or
        $match.Groups["Mac"].Value -cne $expectedMac) {
        throw "$RuntimeLabel 的固定 canonical bytes 或 HMAC 与冻结向量不一致。"
    }

    return $vectors[0]
}

$reviewedProjects = @(
    $contractsProject,
    $ipcProject,
    $specProject,
    $bridgeProject,
    $bridgeSpecProject
)
$reviewedSources = @(
    foreach ($project in $reviewedProjects) {
        Get-ChildItem -LiteralPath (Split-Path -Parent $project) -Recurse -File -Filter "*.cs" |
            Where-Object { $_.FullName -notmatch '\\(?:bin|obj)\\' } |
            Select-Object -ExpandProperty FullName
    }
) | Sort-Object -Unique
$sourcePaths = @(
    $globalJsonPath,
    $solutionPath,
    $directoryBuildPropsPath,
    $nugetConfig,
    $offlinePackage,
    $MyInvocation.MyCommand.Path
) + $reviewedProjects + $reviewedSources
$sourcePaths = @($sourcePaths | Sort-Object -Unique)

$solutionText = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
$solutionProjectMatches = [regex]::Matches(
    $solutionText,
    '(?m)^Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"(?<Path>[^"]+\.csproj)"'
)
$solutionProjectPaths = @(
    foreach ($match in $solutionProjectMatches) {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $match.Groups["Path"].Value))
    }
) | Sort-Object -Unique
if ($solutionProjectPaths.Count -ne 15) {
    throw "主解决方案项目清单必须精确包含15个项目，实际：$($solutionProjectPaths.Count)。"
}
$projectObjRoots = @(
    foreach ($projectPath in $solutionProjectPaths) {
        Join-Path (Split-Path -Parent $projectPath) "obj"
    }
)
$sourceBefore = Get-SourceSnapshot -Paths $sourcePaths
$objBefore = Get-TreeSnapshot -Roots $projectObjRoots
$cadBefore = @(Get-Process -Name acad -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
$previousNoLogo = $env:DOTNET_NOLOGO

try {
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $env:DOTNET_NOLOGO = "1"

    $actualSdk = (& $dotnetCommand --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $expectedSdk) {
        throw "需要 .NET SDK $expectedSdk，当前解析到 '$actualSdk'。"
    }
    Write-Host ".NET SDK 固定版本验证通过：$actualSdk" -ForegroundColor Green

    $dotnetSignature = Get-AuthenticodeSignature -LiteralPath $dotnetCommand
    if ($dotnetSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $dotnetSignature.SignerCertificate -or
        $dotnetSignature.SignerCertificate.Subject -notmatch "Microsoft Corporation") {
        throw "dotnet host 不是有效 Microsoft 签名工具。"
    }

    if ((Get-Sha256 -Path $offlinePackage) -cne $expectedPackageSha256) {
        throw "离线 net45 引用包 SHA-256 与冻结值不一致。"
    }
    Invoke-Captured -FilePath $dotnetCommand -Arguments @(
        "nuget", "verify", $offlinePackage, "--all"
    ) -Description "验证离线 net45 引用包签名" | Out-Null

    Assert-ProjectShape
    Assert-AuthenticationSourceBoundary

    $managedCoreRegression = Invoke-IsolatedManagedCoreRegression

    $buildA = Invoke-IsolatedBuild -Name "build-a"
    $buildB = Invoke-IsolatedBuild -Name "build-b"
    $artifactProperties = @(
        "Net45Contracts", "Net8Contracts", "Net45Ipc", "Net8Ipc", "Net45Specs", "Net8Specs"
    )
    $artifactHashes = [ordered]@{}
    foreach ($propertyName in $artifactProperties) {
        $leftPath = [string] $buildA.$propertyName
        $rightPath = [string] $buildB.$propertyName
        $leftHash = Get-Sha256 -Path $leftPath
        $rightHash = Get-Sha256 -Path $rightPath
        if ($leftHash -cne $rightHash) {
            throw "隔离双构建不一致：$propertyName，$leftHash != $rightHash"
        }
        $artifactHashes[$propertyName] = $leftHash
    }
    Write-Host "net45/net8 六个主产物隔离双构建逐字节一致。" -ForegroundColor Green

    $net45Output = Invoke-Captured -FilePath $buildA.Net45Specs -Arguments @() -Description "运行 net45 认证规格"
    $net8Output = Invoke-Captured -FilePath $dotnetCommand -Arguments @($buildA.Net8Specs) -Description "运行 net8 认证规格"
    $net45Vector = Assert-SpecOutput -Lines $net45Output -RuntimeLabel "net45"
    $net8Vector = Assert-SpecOutput -Lines $net8Output -RuntimeLabel "net8"
    if ($net45Vector -cne $net8Vector) {
        throw "net45 与 net8 固定向量输出不一致。"
    }
    Write-Host "net45/net8 canonical bytes 与 HMAC 固定向量完全一致。" -ForegroundColor Green

    Invoke-Captured -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $safeRepoRoot), "diff", "--check"
    ) -Description "检查未暂存差异格式" | Out-Null
    Invoke-Captured -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $safeRepoRoot), "diff", "--cached", "--check"
    ) -Description "检查已暂存差异格式" | Out-Null

    $sourceAfter = Get-SourceSnapshot -Paths $sourcePaths
    $objAfter = Get-TreeSnapshot -Roots $projectObjRoots
    Assert-SnapshotsEqual -Before $sourceBefore -After $sourceAfter -Label "认证源码/项目输入"
    Assert-SnapshotsEqual -Before $objBefore -After $objAfter -Label "项目本地 obj"

    $cadAfter = @(Get-Process -Name acad -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id | Sort-Object)
    if (($cadBefore -join ",") -cne ($cadAfter -join ",")) {
        throw "认证验证期间 AutoCAD 进程集合发生变化。"
    }

    $evidence = [ordered]@{
        SchemaVersion = 1
        RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
        Scope = "autocad2016-net45-net8-auth-compat"
        Status = "static-and-cross-runtime-auth-gate-passed"
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        DotNetSdk = $actualSdk
        Configuration = $Configuration
        AuthCompatIsolatedRestoreOffline = $true
        ManagedCoreRegressionRestoreOffline = $false
        ManagedCoreRegressionOutputsIsolated = $true
        OfflinePackageSha256 = $expectedPackageSha256
        IsolatedBuildCount = 2
        BitForBitMatch = $true
        ArtifactHashes = $artifactHashes
        Net45Specs = "$expectedSpecCount/$expectedSpecCount"
        Net8Specs = "$expectedSpecCount/$expectedSpecCount"
        BridgeRegressionSpecs = "29/29"
        BridgeRegressionRuntimeArtifactHashes = $managedCoreRegression.RuntimeArtifactHashes
        BridgeProjectOutputSha256 = $managedCoreRegression.BridgeProjectOutputSha256
        BridgeRuntimeCopyMatchesProjectOutput = $managedCoreRegression.RuntimeBridgeCopyMatchesProjectOutput
        CanonicalHex = $expectedCanonical
        HmacSha256 = $expectedMac
        CanonicalLengthRule = "decimal UTF-16 code-unit count prefixes, then strict UTF-8"
        NullSignedFieldsRejected = $true
        ExactSecretBytes = 32
        SequenceStrictlyIncrementsByOne = $true
        NonceReplayRejected = $true
        InvalidMacDoesNotAdvanceState = $true
        SecretPrivateCopyZeroedOnDispose = $true
        SourceInputs = $sourceBefore
        ProjectLocalObjModified = $false
        ProjectLocalObjRootCount = $projectObjRoots.Count
        ProjectLocalObjScope = "all csproj entries in Codex.AutoCAD.sln"
        AutoCadProcessSetChanged = $false
        AutoCadStartedOrRestarted = $false
        CadCommandsSent = $false
        NetLoadAttempted = $false
        NetLoadVerified = $false
        AgentHostLiveBridgeVerified = $false
        RuntimeToCadCandidateBindingVerified = $false
        EvidenceBoundary = "This gate proves only cross-runtime authentication bytes and local fail-closed authentication behavior."
    }
    $evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $evidencePath -Encoding UTF8

    Write-Host "`nAutoCAD 2016 跨框架认证门禁通过。" -ForegroundColor Green
    Write-Host ("AUTH_COMPAT_EVIDENCE=" + $evidencePath)
}
finally {
    $env:DOTNET_NOLOGO = $previousNoLogo
}

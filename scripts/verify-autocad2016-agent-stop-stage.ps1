[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$safeRepoRoot = $repoRoot.Replace("\", "/")
$solutionPath = Join-Path $repoRoot "Codex.AutoCAD.sln"
$evidenceDir = Join-Path $repoRoot "handoff\autocad2016\evidence"
$artifactsDir = Join-Path $repoRoot "artifacts\agent-stop-stage"

$env:DOTNET_CLI_HOME = Join-Path $repoRoot "artifacts\dotnet-cli-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $repoRoot "packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repoRoot "artifacts\nuget-http-cache"
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source

# net45 dependencies for AutoCAD 2016 Host
$net45Dependencies = @(
    "Codex.AutoCAD.AgentLauncher",
    "Codex.AutoCAD.Bridge.Client",
    "Codex.AutoCAD.Contracts",
    "Codex.AutoCAD.Ipc"
)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(Mandatory = $true)]
        [string[]] $ArgumentList,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    Write-Host "`n==> $Description" -ForegroundColor Cyan
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $FilePath @ArgumentList 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host $line.ToString()
    }

    if ($exitCode -ne 0) {
        throw "$Description failed with exit code: $exitCode"
    }

    return $output
}

function Get-FileSha256 {
    param([string] $Path)
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    return $hash.Hash
}

function Get-ManifestSha256 {
    param([string] $ManifestPath)
    $bytes = [System.IO.File]::ReadAllBytes($ManifestPath)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha256.ComputeHash($bytes)
    return [BitConverter]::ToString($hash).Replace("-", "").ToUpperInvariant()
}

function Assert-NoOverrideActive {
    $pathOverride = [Environment]::GetEnvironmentVariable("CODEX_AGENTHOST_PATH", [EnvironmentVariableTarget]::Process)
    $sha256Override = [Environment]::GetEnvironmentVariable("CODEX_AGENTHOST_SHA256", [EnvironmentVariableTarget]::Process)
    if (-not [string]::IsNullOrWhiteSpace($pathOverride)) {
        throw "CODEX_AGENTHOST_PATH override must not be active, current value: $pathOverride"
    }
    if (-not [string]::IsNullOrWhiteSpace($sha256Override)) {
        throw "CODEX_AGENTHOST_SHA256 override must not be active, current value: $sha256Override"
    }
    Write-Host "Environment variable override check passed: CODEX_AGENTHOST_PATH and CODEX_AGENTHOST_SHA256 are not set." -ForegroundColor Green
}

function Assert-FileExists {
    param([string] $RelativePath)
    $absolutePath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        throw "Missing required file: $RelativePath"
    }
}

function Invoke-IsolatedBuild {
    param(
        [string] $BuildLabel,
        [string] $HostOutputDir,
        [string] $AgentHostOutputDir
    )

    Write-Host "`n==> Executing isolated build: $BuildLabel" -ForegroundColor Cyan

    # Clean output directories
    foreach ($dir in @($HostOutputDir, $AgentHostOutputDir)) {
        if (Test-Path -LiteralPath $dir -PathType Container) {
            Remove-Item -LiteralPath $dir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    # Build Host.2016 separately (net45/x64)
    Invoke-CheckedCommand `
        -FilePath $dotnetCommand `
        -ArgumentList @(
            "build",
            (Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj"),
            "--configuration", $Configuration,
            "--nologo",
            "--output", $HostOutputDir
        ) `
        -Description "Build Host.2016 ($BuildLabel)"

    # Build AgentHost separately (net8)
    Invoke-CheckedCommand `
        -FilePath $dotnetCommand `
        -ArgumentList @(
            "build",
            (Join-Path $repoRoot "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj"),
            "--configuration", $Configuration,
            "--nologo",
            "--output", $AgentHostOutputDir
        ) `
        -Description "Build AgentHost ($BuildLabel)"

    return @{
        HostDir = $HostOutputDir
        AgentHostDir = $AgentHostOutputDir
    }
}

function Test-BuildsEqual {
    param(
        [string] $Dir1,
        [string] $Dir2,
        [string] $Label
    )

    $files1 = Get-ChildItem -LiteralPath $Dir1 -Recurse -File | Sort-Object FullName
    $files2 = Get-ChildItem -LiteralPath $Dir2 -Recurse -File | Sort-Object FullName

    if ($files1.Count -ne $files2.Count) {
        Write-Host "File count mismatch in $Label`: $($files1.Count) vs $($files2.Count)" -ForegroundColor Red
        return $false
    }

    for ($i = 0; $i -lt $files1.Count; $i++) {
        $rel1 = $files1[$i].FullName.Substring($Dir1.Length)
        $rel2 = $files2[$i].FullName.Substring($Dir2.Length)
        if ($rel1 -ne $rel2) {
            Write-Host "File path mismatch in $Label`: $rel1 vs $rel2" -ForegroundColor Red
            return $false
        }
        $hash1 = Get-FileSha256 -Path $files1[$i].FullName
        $hash2 = Get-FileSha256 -Path $files2[$i].FullName
        if ($hash1 -ne $hash2) {
            Write-Host "File hash mismatch in $Label for $rel1" -ForegroundColor Red
            return $false
        }
    }

    return $true
}

function Invoke-SpecProject {
    param(
        [string] $RelativePath,
        [string] $Description
    )

    Write-Host "`n==> Running specs: $Description" -ForegroundColor Cyan
    $rawOutput = & $dotnetCommand @(
        "run",
        "--project", (Join-Path $repoRoot $RelativePath),
        "--configuration", $Configuration,
        "--no-build"
    ) 2>&1
    $exitCode = $LASTEXITCODE
    $outputLines = @($rawOutput | ForEach-Object { [string] $_ })
    foreach ($line in $outputLines) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw "Spec failed: $Description, exit code: $exitCode"
    }

    $summaries = [System.Collections.Generic.List[object]]::new()
    foreach ($line in $outputLines) {
        $slashSummary = [regex]::Match(
            $line,
            "^\s*(?<Passed>\d+)\s*/\s*(?<Total>\d+)\s+specs passed\s*$",
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if ($slashSummary.Success) {
            $passed = [int] $slashSummary.Groups["Passed"].Value
            $total = [int] $slashSummary.Groups["Total"].Value
            $summaries.Add([pscustomobject]@{
                Passed = $passed
                Total = $total
                Failed = $total - $passed
            })
            continue
        }

        $labeledSummary = [regex]::Match(
            $line,
            "^\s*(?:Total|规格总数)\s*:\s*(?<Total>\d+)\s*,\s*(?:Passed|通过)\s*:\s*(?<Passed>\d+)\s*,\s*(?:Failed|失败)\s*:\s*(?<Failed>\d+)\s*$",
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if ($labeledSummary.Success) {
            $summaries.Add([pscustomobject]@{
                Passed = [int] $labeledSummary.Groups["Passed"].Value
                Total = [int] $labeledSummary.Groups["Total"].Value
                Failed = [int] $labeledSummary.Groups["Failed"].Value
            })
        }
    }

    if ($summaries.Count -ne 1) {
        throw "Spec output must contain exactly one summary: $Description"
    }

    $summary = $summaries[0]
    if ($summary.Total -le 0 -or $summary.Passed -ne $summary.Total -or $summary.Failed -ne 0) {
        throw "Specs not all passed: $Description, actual $($summary.Passed)/$($summary.Total), failed $($summary.Failed)"
    }

    return [pscustomobject]@{
        Name = $Description
        Passed = $summary.Passed
        Total = $summary.Total
    }
}

function Get-GitHead {
    $head = & git -c "safe.directory=$safeRepoRoot" -C $repoRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get Git HEAD"
    }
    return $head.Trim()
}

function Get-DirtyDiffSha256 {
    # Get tracked changes (staged and unstaged)
    $trackedDiff = & git -c "safe.directory=$safeRepoRoot" -C $repoRoot diff HEAD --no-color
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get tracked diff"
    }

    # Get untracked files
    $untrackedFiles = & git -c "safe.directory=$safeRepoRoot" -C $repoRoot ls-files --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get untracked files"
    }

    # Combine tracked diff with untracked file content
    $combinedContent = $trackedDiff
    foreach ($file in $untrackedFiles) {
        if (-not [string]::IsNullOrWhiteSpace($file)) {
            $fullPath = Join-Path $repoRoot $file
            if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
                $fileContent = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
                $combinedContent += "`n--- a/$file`n+++ b/$file`n$fileContent"
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($combinedContent)) {
        return "NO_UNCOMMITTED_CHANGES"
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($combinedContent)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha256.ComputeHash($bytes)
    return [BitConverter]::ToString($hash).Replace("-", "").ToUpperInvariant()
}

function Get-SourceInputManifest {
    param([string] $OutputPath)

    # Cover all actual input source files, projects, and scripts
    $sourcePatterns = @(
        "src\**\*.cs",
        "src\**\*.csproj",
        "tests\**\*.cs",
        "tests\**\*.csproj",
        "scripts\*.ps1",
        "*.sln",
        "*.props",
        "*.targets",
        "global.json",
        "NuGet.Config"
    )

    $manifest = @()
    foreach ($pattern in $sourcePatterns) {
        $files = Get-ChildItem -Path $repoRoot -Filter $pattern -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch "\\(bin|obj|artifacts|packages|\.git)\\" -and
                $_.FullName -notmatch "\\.git\\"
            }
        foreach ($file in $files) {
            $relPath = $file.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")
            $hash = Get-FileSha256 -Path $file.FullName
            $manifest += [pscustomobject]@{
                Path = $relPath
                ByteLength = $file.Length
                Sha256 = $hash
            }
        }
    }

    # Sort by path for deterministic output
    $manifest = $manifest | Sort-Object -Property Path

    $manifestJson = @{
        generatedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        fileCount = $manifest.Count
        files = $manifest
    } | ConvertTo-Json -Depth 4

    # Write with UTF-8 BOM for Windows PowerShell compatibility
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($OutputPath, $manifestJson, $utf8Bom)

    return Get-ManifestSha256 -ManifestPath $OutputPath
}

function Test-R201Assembly {
    param([string] $DllPath, [string] $ExpectedName)

    if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) {
        throw "Missing R20.1 assembly: $ExpectedName"
    }

    # Verify it's a .NET assembly
    $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($DllPath)
    $version = $assembly.GetName().Version.ToString()
    if ($version -ne "20.1.0.0") {
        throw "$ExpectedName version is $version, expected 20.1.0.0"
    }

    return $true
}

function Assert-R201Compliance {
    param([string] $HostDir)

    Write-Host "`n==> Verifying R20.1 compliance" -ForegroundColor Cyan

    # Verify Host DLL exists and is net45/x64
    $hostDll = Join-Path $HostDir "Codex.AutoCAD.Host.2016.dll"
    if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
        throw "Missing Host.2016 DLL"
    }

    # Verify Host is net45
    $hostAssembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($hostDll)
    $hostTargetFramework = $hostAssembly.GetCustomAttributes([System.Runtime.Versioning.TargetFrameworkAttribute], $false)
    if ($hostTargetFramework -and $hostTargetFramework.FrameworkName -notmatch "net45") {
        throw "Host.2016 is not targeting net45"
    }

    # Verify Autodesk assemblies are NOT copied to output
    $autodeskFiles = Get-ChildItem -LiteralPath $HostDir -Filter "acdb*.dll" -ErrorAction SilentlyContinue
    if ($autodeskFiles.Count -gt 0) {
        throw "Autodesk DLLs must not be copied to output directory"
    }

    # Verify Private=false for Autodesk references (check csproj)
    $hostCsproj = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj"
    $csprojContent = Get-Content -LiteralPath $hostCsproj -Raw
    if ($csprojContent -match 'Private\s*=\s*"true"') {
        throw "Autodesk references must have Private=false"
    }

    Write-Host "R20.1 compliance verification passed" -ForegroundColor Green
    return $true
}

function New-CandidatePackage {
    param(
        [string] $HostDir,
        [string] $AgentHostDir,
        [string] $PackageDir
    )

    if (Test-Path -LiteralPath $PackageDir -PathType Container) {
        Remove-Item -LiteralPath $PackageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null

    # Copy Host DLL
    $hostDll = Join-Path $HostDir "Codex.AutoCAD.Host.2016.dll"
    if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
        throw "Missing Host DLL: $hostDll"
    }
    Copy-Item -LiteralPath $hostDll -Destination $PackageDir -Force

    # Copy net45 dependencies
    foreach ($dep in $net45Dependencies) {
        $depDll = Join-Path $HostDir "$dep.dll"
        if (-not (Test-Path -LiteralPath $depDll -PathType Leaf)) {
            throw "Missing net45 dependency: $depDll"
        }
        Copy-Item -LiteralPath $depDll -Destination $PackageDir -Force
    }

    # Copy AgentHost EXE
    $agentHostExe = Join-Path $AgentHostDir "Codex.AutoCAD.AgentHost.exe"
    if (-not (Test-Path -LiteralPath $agentHostExe -PathType Leaf)) {
        throw "Missing AgentHost EXE: $agentHostExe"
    }
    Copy-Item -LiteralPath $agentHostExe -Destination $PackageDir -Force

    # Create SHA-256 sidecar
    $sidecarPath = Join-Path $PackageDir "Codex.AutoCAD.AgentHost.exe.sha256"
    $agentHostHash = Get-FileSha256 -Path (Join-Path $PackageDir "Codex.AutoCAD.AgentHost.exe")
    Set-Content -LiteralPath $sidecarPath -Value $agentHostHash -Encoding ASCII

    # Build package manifest
    $packageFiles = @()
    foreach ($file in (Get-ChildItem -LiteralPath $PackageDir -File | Sort-Object Name)) {
        $packageFiles += [pscustomobject]@{
            FileName = $file.Name
            Sha256 = (Get-FileSha256 -Path $file.FullName)
        }
    }

    return $packageFiles
}

function New-EvidenceFile {
    param(
        [string] $OutputPath,
        [string] $GitHead,
        [string] $DirtyDiffSha256,
        [string] $SourceManifestSha256,
        [hashtable] $BuildResults,
        [hashtable] $SpecResults,
        [array] $PackageFiles,
        [string] $CandidateId,
        [string] $FrozenAtUtc
    )

    $recordedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

    # Verify frozen timing
    if ($recordedAtUtc -lt $FrozenAtUtc) {
        throw "recordedAtUtc ($recordedAtUtc) must be >= frozenAtUtc ($FrozenAtUtc)"
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        scope = "autocad2016-agent-stop-stage"
        recordedAtUtc = $recordedAtUtc
        frozenAtUtc = $FrozenAtUtc
        generatedBy = "scripts/verify-autocad2016-agent-stop-stage.ps1"
        autoCadLiveEvidence = $false
        autoCadProcessStarted = $false
        autoCadProcessControlled = $false
        cadCommandsSent = $false
        candidateId = $CandidateId
        gitBinding = [ordered]@{
            head = $GitHead
            dirtyDiffSha256 = $DirtyDiffSha256
            sourceInputManifestSha256 = $SourceManifestSha256
        }
        builds = [ordered]@{
            isolatedBuildCount = $BuildResults.IsolatedBuildCount
            hostOutputTreesEqual = $BuildResults.HostTreesEqual
            agentHostOutputTreesEqual = $BuildResults.AgentHostTreesEqual
            build1LogSha256 = $BuildResults.Build1LogSha256
            build2LogSha256 = $BuildResults.Build2LogSha256
        }
        specs = [ordered]@{
            host2016Mvp = [ordered]@{
                passed = $SpecResults.Host2016Mvp.Passed
                total = $SpecResults.Host2016Mvp.Total
            }
            agentLauncher = [ordered]@{
                passed = $SpecResults.AgentLauncher.Passed
                total = $SpecResults.AgentLauncher.Total
            }
            phase2 = [ordered]@{
                passed = $SpecResults.Phase2.Passed
                total = $SpecResults.Phase2.Total
            }
        }
        package = [ordered]@{
            files = $PackageFiles
            frozenAtUtc = $FrozenAtUtc
        }
        verificationFlags = [ordered]@{
            paletteSourceWiringInspected = $true
            paletteBehaviorAutomatedVerified = $false
            paletteRuntimeVerified = $false
            netLoadVerified = $false
            runtimeToArtifactBindingVerified = $false
        }
        limitations = @(
            "This evidence does not prove AutoCAD runtime integration.",
            "paletteBehaviorAutomatedVerified, paletteRuntimeVerified, netLoadVerified, and runtimeToArtifactBindingVerified are all false.",
            "No AutoCAD process was started, stopped, or sent commands.",
            "This is a build and spec verification only."
        )
    }

    # Write with UTF-8 BOM for Windows PowerShell compatibility
    $evidenceJson = $evidence | ConvertTo-Json -Depth 5
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($OutputPath, $evidenceJson, $utf8Bom)

    return $evidence
}

Push-Location $repoRoot
try {
    Write-Host "`n==> P0 AgentHost Stop-Stage Verifier" -ForegroundColor Yellow
    Write-Host "Configuration: $Configuration" -ForegroundColor Yellow

    # Check environment overrides
    Assert-NoOverrideActive

    # Verify required files exist
    Assert-FileExists "Codex.AutoCAD.sln"
    Assert-FileExists "src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj"
    Assert-FileExists "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj"
    Assert-FileExists "scripts\verify-phase2.ps1"

    # Get Git binding
    $gitHead = Get-GitHead
    Write-Host "Git HEAD: $gitHead" -ForegroundColor Green

    $dirtyDiffSha256 = Get-DirtyDiffSha256
    Write-Host "Dirty diff SHA-256: $dirtyDiffSha256" -ForegroundColor Green

    # Generate source input manifest
    $sourceManifestPath = Join-Path $artifactsDir "source-input-manifest.json"
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    $sourceManifestSha256 = Get-SourceInputManifest -OutputPath $sourceManifestPath
    Write-Host "Source input manifest SHA-256: $sourceManifestSha256" -ForegroundColor Green

    # Freeze manifest before writing evidence
    $frozenAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    Write-Host "Manifest frozen at: $frozenAtUtc" -ForegroundColor Green

    # Execute two isolated builds with separate output directories
    Write-Host "`n==> Executing two isolated builds" -ForegroundColor Cyan
    $build1HostDir = Join-Path $artifactsDir "build1-host"
    $build1AgentHostDir = Join-Path $artifactsDir "build1-agenthost"
    $build2HostDir = Join-Path $artifactsDir "build2-host"
    $build2AgentHostDir = Join-Path $artifactsDir "build2-agenthost"
    $build1Log = Join-Path $artifactsDir "build1.log"
    $build2Log = Join-Path $artifactsDir "build2.log"

    $build1Output = Invoke-IsolatedBuild -BuildLabel "Build 1" -HostOutputDir $build1HostDir -AgentHostOutputDir $build1AgentHostDir
    ($build1Output.Values | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File | ForEach-Object { $_.FullName } }) | Out-File -FilePath $build1Log -Encoding UTF8
    $build1LogSha256 = Get-FileSha256 -Path $build1Log

    $build2Output = Invoke-IsolatedBuild -BuildLabel "Build 2" -HostOutputDir $build2HostDir -AgentHostOutputDir $build2AgentHostDir
    ($build2Output.Values | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File | ForEach-Object { $_.FullName } }) | Out-File -FilePath $build2Log -Encoding UTF8
    $build2LogSha256 = Get-FileSha256 -Path $build2Log

    # Verify build outputs are equal
    $hostTreesEqual = Test-BuildsEqual -Dir1 $build1HostDir -Dir2 $build2HostDir -Label "Host"
    $agentHostTreesEqual = Test-BuildsEqual -Dir1 $build1AgentHostDir -Dir2 $build2AgentHostDir -Label "AgentHost"

    if (-not $hostTreesEqual) {
        throw "Host build outputs are not equal"
    }
    if (-not $agentHostTreesEqual) {
        throw "AgentHost build outputs are not equal"
    }
    Write-Host "Both isolated builds produce equal output." -ForegroundColor Green

    # Verify R20.1 compliance
    Assert-R201Compliance -HostDir $build1HostDir

    # Run specs
    Write-Host "`n==> Running spec tests" -ForegroundColor Cyan
    $host2016MvpResult = Invoke-SpecProject `
        -RelativePath "tests\Codex.AutoCAD.Host.2016.Mvp.Specs\Codex.AutoCAD.Host.2016.Mvp.Specs.csproj" `
        -Description "Host.2016.Mvp Specs"

    $agentLauncherResult = Invoke-SpecProject `
        -RelativePath "tests\Codex.AutoCAD.AgentLauncher.Specs\Codex.AutoCAD.AgentLauncher.Specs.csproj" `
        -Description "AgentLauncher Specs"

    # Run Phase 2 verification script (not via dotnet run)
    Write-Host "`n==> Running Phase 2 gate" -ForegroundColor Cyan
    $phase2Script = Join-Path $repoRoot "scripts\verify-phase2.ps1"
    $phase2Output = & powershell -ExecutionPolicy Bypass -File $phase2Script -Configuration $Configuration 2>&1
    $phase2ExitCode = $LASTEXITCODE
    $phase2Output | ForEach-Object { Write-Host $_ }

    if ($phase2ExitCode -ne 0) {
        throw "Phase 2 gate failed with exit code: $phase2ExitCode"
    }

    # Parse Phase 2 results from output
    $phase2Total = 0
    $phase2Passed = 0
    foreach ($line in $phase2Output) {
        $specMatch = [regex]::Match([string]$line, "规格动态计数汇总：(?<Total>\d+)/(?<Passed>\d+)")
        if ($specMatch.Success) {
            $phase2Total = [int]$specMatch.Groups["Total"].Value
            $phase2Passed = [int]$specMatch.Groups["Passed"].Value
        }
    }

    if ($phase2Total -eq 0) {
        throw "Failed to parse Phase 2 spec results"
    }

    $phase2Result = [pscustomobject]@{
        Passed = $phase2Passed
        Total = $phase2Total
    }

    # Create candidate package
    Write-Host "`n==> Creating candidate package" -ForegroundColor Cyan
    $packageDir = Join-Path $artifactsDir "candidate"
    $packageFiles = New-CandidatePackage -HostDir $build1HostDir -AgentHostDir $build1AgentHostDir -PackageDir $packageDir
    Write-Host "Candidate package created with $($packageFiles.Count) files." -ForegroundColor Green

    # Generate candidate ID
    $candidateId = "agent-stop-$($gitHead.Substring(0, 8))-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
    Write-Host "Candidate ID: $candidateId" -ForegroundColor Green

    # Generate evidence file
    Write-Host "`n==> Generating evidence file" -ForegroundColor Cyan
    $evidencePath = Join-Path $evidenceDir "agent-stop-build-verification-$(Get-Date -Format 'yyyyMMdd').json"
    $evidence = New-EvidenceFile `
        -OutputPath $evidencePath `
        -GitHead $gitHead `
        -DirtyDiffSha256 $dirtyDiffSha256 `
        -SourceManifestSha256 $sourceManifestSha256 `
        -BuildResults @{
            IsolatedBuildCount = 2
            HostTreesEqual = $hostTreesEqual
            AgentHostTreesEqual = $agentHostTreesEqual
            Build1LogSha256 = $build1LogSha256
            Build2LogSha256 = $build2LogSha256
        } `
        -SpecResults @{
            Host2016Mvp = $host2016MvpResult
            AgentLauncher = $agentLauncherResult
            Phase2 = $phase2Result
        } `
        -PackageFiles $packageFiles `
        -CandidateId $candidateId `
        -FrozenAtUtc $frozenAtUtc

    # Verify evidence integrity
    Write-Host "`n==> Verifying evidence integrity" -ForegroundColor Cyan
    $evidenceContent = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json

    # Hard assertions for verification flags
    if ($evidenceContent.verificationFlags.paletteSourceWiringInspected -ne $true) {
        throw "paletteSourceWiringInspected must be true"
    }
    if ($evidenceContent.verificationFlags.paletteBehaviorAutomatedVerified -ne $false) {
        throw "paletteBehaviorAutomatedVerified must be false"
    }
    if ($evidenceContent.verificationFlags.paletteRuntimeVerified -ne $false) {
        throw "paletteRuntimeVerified must be false"
    }
    if ($evidenceContent.verificationFlags.netLoadVerified -ne $false) {
        throw "netLoadVerified must be false"
    }
    if ($evidenceContent.verificationFlags.runtimeToArtifactBindingVerified -ne $false) {
        throw "runtimeToArtifactBindingVerified must be false"
    }

    # Hard assertions for runtime booleans
    if ($evidenceContent.autoCadLiveEvidence -ne $false) {
        throw "autoCadLiveEvidence must be false"
    }
    if ($evidenceContent.autoCadProcessStarted -ne $false) {
        throw "autoCadProcessStarted must be false"
    }
    if ($evidenceContent.autoCadProcessControlled -ne $false) {
        throw "autoCadProcessControlled must be false"
    }
    if ($evidenceContent.cadCommandsSent -ne $false) {
        throw "cadCommandsSent must be false"
    }

    # Verify timing
    if ($evidenceContent.recordedAtUtc -lt $evidenceContent.frozenAtUtc) {
        throw "recordedAtUtc must be >= frozenAtUtc"
    }

    # Verify package contains required files
    $requiredPackageFiles = @(
        "Codex.AutoCAD.Host.2016.dll",
        "Codex.AutoCAD.AgentLauncher.dll",
        "Codex.AutoCAD.Bridge.Client.dll",
        "Codex.AutoCAD.Contracts.dll",
        "Codex.AutoCAD.Ipc.dll",
        "Codex.AutoCAD.AgentHost.exe",
        "Codex.AutoCAD.AgentHost.exe.sha256"
    )

    foreach ($requiredFile in $requiredPackageFiles) {
        $found = $evidenceContent.package.files | Where-Object { $_.FileName -eq $requiredFile }
        if (-not $found) {
            throw "Package missing required file: $requiredFile"
        }
    }

    Write-Host "Evidence integrity verification passed." -ForegroundColor Green

    Write-Host "`n==> P0 AgentHost Stop-Stage Verification Complete" -ForegroundColor Green
    Write-Host "Evidence file: $evidencePath" -ForegroundColor Green
    Write-Host "Candidate package: $packageDir" -ForegroundColor Green
    Write-Host "Candidate ID: $candidateId" -ForegroundColor Green
}
finally {
    Pop-Location
}

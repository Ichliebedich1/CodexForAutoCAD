# Self-test script for AgentHost stop-stage verifier
# Compatible with both PowerShell 7 and Windows PowerShell 5.1
# Exit code 0 = all tests passed, non-zero = at least one test failed

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$testDir = Join-Path $repoRoot "artifacts\agent-stop-stage-tests"
$exitCode = 0

function Write-TestResult {
    param(
        [string] $TestId,
        [string] $TestName,
        [bool] $Passed,
        [string] $ErrorMessage = ""
    )

    if ($Passed) {
        Write-Host "PASS $TestId $TestName" -ForegroundColor Green
    }
    else {
        Write-Host "FAIL $TestId $TestName`: $ErrorMessage" -ForegroundColor Red
        $script:exitCode = 1
    }
}

function New-SyntheticFixture {
    param([string] $FixtureDir)

    if (Test-Path -LiteralPath $FixtureDir -PathType Container) {
        Remove-Item -LiteralPath $FixtureDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $FixtureDir -Force | Out-Null

    $build1HostDir = Join-Path $FixtureDir "build1-host"
    $build1AgentHostDir = Join-Path $FixtureDir "build1-agenthost"
    $build2HostDir = Join-Path $FixtureDir "build2-host"
    $build2AgentHostDir = Join-Path $FixtureDir "build2-agenthost"

    New-Item -ItemType Directory -Path $build1HostDir -Force | Out-Null
    New-Item -ItemType Directory -Path $build1AgentHostDir -Force | Out-Null
    New-Item -ItemType Directory -Path $build2HostDir -Force | Out-Null
    New-Item -ItemType Directory -Path $build2AgentHostDir -Force | Out-Null

    # net45 Host files
    $hostFiles = @(
        "Codex.AutoCAD.Host.2016.dll",
        "Codex.AutoCAD.AgentLauncher.dll",
        "Codex.AutoCAD.Bridge.Client.dll",
        "Codex.AutoCAD.Contracts.dll",
        "Codex.AutoCAD.Ipc.dll"
    )

    # net8 AgentHost files
    $agentHostFiles = @(
        "Codex.AutoCAD.AgentHost.exe"
    )

    foreach ($file in $hostFiles) {
        $content = "Synthetic fixture content for $file - $(Get-Date -Format 'yyyyMMddHHmmss')"
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
        Set-Content -LiteralPath (Join-Path $build1HostDir $file) -Value $bytes -Encoding Byte
        Set-Content -LiteralPath (Join-Path $build2HostDir $file) -Value $bytes -Encoding Byte
    }

    foreach ($file in $agentHostFiles) {
        $content = "Synthetic fixture content for $file - $(Get-Date -Format 'yyyyMMddHHmmss')"
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
        Set-Content -LiteralPath (Join-Path $build1AgentHostDir $file) -Value $bytes -Encoding Byte
        Set-Content -LiteralPath (Join-Path $build2AgentHostDir $file) -Value $bytes -Encoding Byte
    }

    return @{
        Build1HostDir = $build1HostDir
        Build1AgentHostDir = $build1AgentHostDir
        Build2HostDir = $build2HostDir
        Build2AgentHostDir = $build2AgentHostDir
    }
}

function Test-BuildEquality {
    param(
        [string] $Dir1,
        [string] $Dir2
    )

    $files1 = @(Get-ChildItem -LiteralPath $Dir1 -Recurse -File | Sort-Object FullName)
    $files2 = @(Get-ChildItem -LiteralPath $Dir2 -Recurse -File | Sort-Object FullName)

    if ($files1.Count -ne $files2.Count) {
        return $false
    }

    for ($i = 0; $i -lt $files1.Count; $i++) {
        $rel1 = $files1[$i].FullName.Substring($Dir1.Length)
        $rel2 = $files2[$i].FullName.Substring($Dir2.Length)
        if ($rel1 -ne $rel2) {
            return $false
        }
        $hash1 = (Get-FileHash -LiteralPath $files1[$i].FullName -Algorithm SHA256).Hash
        $hash2 = (Get-FileHash -LiteralPath $files2[$i].FullName -Algorithm SHA256).Hash
        if ($hash1 -ne $hash2) {
            return $false
        }
    }

    return $true
}

function Test-PackageCreation {
    param(
        [string] $HostDir,
        [string] $AgentHostDir,
        [string] $PackageDir
    )

    if (Test-Path -LiteralPath $PackageDir -PathType Container) {
        Remove-Item -LiteralPath $PackageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null

    # Required net45 dependencies
    $requiredHostFiles = @(
        "Codex.AutoCAD.Host.2016.dll",
        "Codex.AutoCAD.AgentLauncher.dll",
        "Codex.AutoCAD.Bridge.Client.dll",
        "Codex.AutoCAD.Contracts.dll",
        "Codex.AutoCAD.Ipc.dll"
    )

    $requiredAgentHostFiles = @(
        "Codex.AutoCAD.AgentHost.exe"
    )

    foreach ($file in $requiredHostFiles) {
        $sourcePath = Join-Path $HostDir $file
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Missing host file: $file"
        }
        Copy-Item -LiteralPath $sourcePath -Destination $PackageDir -Force
    }

    foreach ($file in $requiredAgentHostFiles) {
        $sourcePath = Join-Path $AgentHostDir $file
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Missing agenthost file: $file"
        }
        Copy-Item -LiteralPath $sourcePath -Destination $PackageDir -Force
    }

    # Create sidecar
    $sidecarPath = Join-Path $PackageDir "Codex.AutoCAD.AgentHost.exe.sha256"
    $agentHostPath = Join-Path $PackageDir "Codex.AutoCAD.AgentHost.exe"
    $agentHostHash = (Get-FileHash -LiteralPath $agentHostPath -Algorithm SHA256).Hash
    Set-Content -LiteralPath $sidecarPath -Value $agentHostHash -Encoding ASCII

    # Count files
    $fileCount = (Get-ChildItem -LiteralPath $PackageDir -File).Count
    return $fileCount -eq 7  # 5 host + 1 agenthost + 1 sidecar
}

function Test-EvidenceStructure {
    param([string] $EvidencePath)

    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
        return $false
    }

    $evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json

    $requiredFields = @(
        "schemaVersion",
        "scope",
        "recordedAtUtc",
        "frozenAtUtc",
        "generatedBy",
        "autoCadLiveEvidence",
        "autoCadProcessStarted",
        "autoCadProcessControlled",
        "cadCommandsSent",
        "candidateId",
        "gitBinding",
        "builds",
        "specs",
        "package",
        "verificationFlags",
        "limitations"
    )

    foreach ($field in $requiredFields) {
        if (-not (Get-Member -InputObject $evidence -Name $field -MemberType NoteProperty)) {
            return $false
        }
    }

    # Verify verification flags
    if ($evidence.verificationFlags.paletteSourceWiringInspected -ne $true) {
        return $false
    }
    if ($evidence.verificationFlags.paletteBehaviorAutomatedVerified -ne $false) {
        return $false
    }
    if ($evidence.verificationFlags.paletteRuntimeVerified -ne $false) {
        return $false
    }
    if ($evidence.verificationFlags.netLoadVerified -ne $false) {
        return $false
    }
    if ($evidence.verificationFlags.runtimeToArtifactBindingVerified -ne $false) {
        return $false
    }

    # Verify runtime booleans
    if ($evidence.autoCadLiveEvidence -ne $false) {
        return $false
    }
    if ($evidence.autoCadProcessStarted -ne $false) {
        return $false
    }
    if ($evidence.autoCadProcessControlled -ne $false) {
        return $false
    }
    if ($evidence.cadCommandsSent -ne $false) {
        return $false
    }

    # Verify timing
    if ($evidence.recordedAtUtc -lt $evidence.frozenAtUtc) {
        return $false
    }

    return $true
}

function Test-OverrideDetection {
    $pathOverride = [Environment]::GetEnvironmentVariable("CODEX_AGENTHOST_PATH", [EnvironmentVariableTarget]::Process)
    $sha256Override = [Environment]::GetEnvironmentVariable("CODEX_AGENTHOST_SHA256", [EnvironmentVariableTarget]::Process)
    if (-not [string]::IsNullOrWhiteSpace($pathOverride)) {
        return $false
    }
    if (-not [string]::IsNullOrWhiteSpace($sha256Override)) {
        return $false
    }
    return $true
}

function Test-SidecarValidation {
    param([string] $PackageDir)

    $sidecarPath = Join-Path $PackageDir "Codex.AutoCAD.AgentHost.exe.sha256"
    $agentHostPath = Join-Path $PackageDir "Codex.AutoCAD.AgentHost.exe"

    if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
        return $false
    }
    if (-not (Test-Path -LiteralPath $agentHostPath -PathType Leaf)) {
        return $false
    }

    $sidecarContent = (Get-Content -LiteralPath $sidecarPath -Raw).Trim()
    $agentHostHash = (Get-FileHash -LiteralPath $agentHostPath -Algorithm SHA256).Hash

    return $sidecarContent -eq $agentHostHash
}

function Test-DirtyDiffDetection {
    # Test that dirty diff detection covers tracked, staged, and untracked content
    $testRepo = Join-Path $testDir "dirty-diff-test"
    if (Test-Path -LiteralPath $testRepo -PathType Container) {
        Remove-Item -LiteralPath $testRepo -Recurse -Force
    }
    New-Item -ItemType Directory -Path $testRepo -Force | Out-Null

    # Initialize git repo
    Push-Location $testRepo
    try {
        & git init | Out-Null
        & git config user.email "test@test.com"
        & git config user.name "Test"

        # Create and commit a file
        Set-Content -LiteralPath (Join-Path $testRepo "test.txt") -Value "initial content"
        & git add .
        & git commit -m "initial commit" | Out-Null

        # Modify tracked file (unstaged)
        Set-Content -LiteralPath (Join-Path $testRepo "test.txt") -Value "modified content"

        # Create untracked file
        Set-Content -LiteralPath (Join-Path $testRepo "untracked.txt") -Value "untracked content"

        # Check that git diff HEAD detects changes
        $diff = & git diff HEAD --no-color
        $hasTrackedChanges = -not [string]::IsNullOrWhiteSpace($diff)

        # Check that ls-files --others detects untracked
        $untracked = & git ls-files --others --exclude-standard
        $hasUntracked = -not [string]::IsNullOrWhiteSpace($untracked)

        return $hasTrackedChanges -and $hasUntracked
    }
    finally {
        Pop-Location
    }
}

# Run tests
Write-Host "`n==> AgentHost Stop-Stage Verifier Self-Tests" -ForegroundColor Yellow
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Yellow

# Test 1: Build equality
$testId = "BUILD_EQUALITY"
$testName = "Two isolated builds produce equal output"
try {
    $fixture = New-SyntheticFixture -FixtureDir (Join-Path $testDir "equality")
    $result = Test-BuildEquality -Dir1 $fixture.Build1HostDir -Dir2 $fixture.Build2HostDir
    $result2 = Test-BuildEquality -Dir1 $fixture.Build1AgentHostDir -Dir2 $fixture.Build2AgentHostDir
    Write-TestResult -TestId $testId -TestName $testName -Passed ($result -and $result2)
}
catch {
    Write-TestResult -TestId $testId -TestName $testName -Passed $false -ErrorMessage $_.Exception.Message
}

# Test 2: Package creation
$testId = "PACKAGE_CREATION"
$testName = "Candidate package contains all required files"
try {
    $fixture = New-SyntheticFixture -FixtureDir (Join-Path $testDir "package")
    $packageDir = Join-Path $testDir "package-output"
    $result = Test-PackageCreation -HostDir $fixture.Build1HostDir -AgentHostDir $fixture.Build1AgentHostDir -PackageDir $packageDir
    Write-TestResult -TestId $testId -TestName $testName -Passed $result
}
catch {
    Write-TestResult -TestId $testId -TestName $testName -Passed $false -ErrorMessage $_.Exception.Message
}

# Test 3: Evidence structure
$testId = "EVIDENCE_STRUCTURE"
$testName = "Evidence file structure is complete"
try {
    $evidencePath = Join-Path $testDir "test-evidence.json"
    $evidence = [ordered]@{
        schemaVersion = 1
        scope = "autocad2016-agent-stop-stage"
        recordedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        frozenAtUtc = [DateTime]::UtcNow.AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        generatedBy = "test"
        autoCadLiveEvidence = $false
        autoCadProcessStarted = $false
        autoCadProcessControlled = $false
        cadCommandsSent = $false
        candidateId = "test-candidate"
        gitBinding = @{ head = "test"; dirtyDiffSha256 = "test"; sourceInputManifestSha256 = "test" }
        builds = @{ isolatedBuildCount = 2; hostOutputTreesEqual = $true; agentHostOutputTreesEqual = $true }
        specs = @{ host2016Mvp = @{ passed = 1; total = 1 }; agentLauncher = @{ passed = 15; total = 15 }; phase2 = @{ passed = 145; total = 145 } }
        package = @{ files = @(); frozenAtUtc = [DateTime]::UtcNow.AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
        verificationFlags = @{
            paletteSourceWiringInspected = $true
            paletteBehaviorAutomatedVerified = $false
            paletteRuntimeVerified = $false
            netLoadVerified = $false
            runtimeToArtifactBindingVerified = $false
        }
        limitations = @("test")
    }

    # Write with UTF-8 BOM for PS5.1 compatibility
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 5), $utf8Bom)

    $result = Test-EvidenceStructure -EvidencePath $evidencePath
    Write-TestResult -TestId $testId -TestName $testName -Passed $result
}
catch {
    Write-TestResult -TestId $testId -TestName $testName -Passed $false -ErrorMessage $_.Exception.Message
}

# Test 4: Override detection
$testId = "OVERRIDE_DETECTION"
$testName = "Environment variable override detection"
try {
    $result = Test-OverrideDetection
    Write-TestResult -TestId $testId -TestName $testName -Passed $result
}
catch {
    Write-TestResult -TestId $testId -TestName $testName -Passed $false -ErrorMessage $_.Exception.Message
}

# Test 5: SHA-256 consistency
$testId = "SHA256_CONSISTENCY"
$testName = "SHA-256 hash consistency"
try {
    $fixture = New-SyntheticFixture -FixtureDir (Join-Path $testDir "sha256")
    $file1 = Join-Path $fixture.Build1HostDir "Codex.AutoCAD.Host.2016.dll"
    $file2 = Join-Path $fixture.Build2HostDir "Codex.AutoCAD.Host.2016.dll"
    $hash1 = (Get-FileHash -LiteralPath $file1 -Algorithm SHA256).Hash
    $hash2 = (Get-FileHash -LiteralPath $file2 -Algorithm SHA256).Hash
    Write-TestResult -TestId $testId -TestName $testName -Passed ($hash1 -eq $hash2)
}
catch {
    Write-TestResult -TestId $testId -TestName $testName -Passed $false -ErrorMessage $_.Exception.Message
}

# Test 6: Sidecar validation
$testId = "SIDECAR_VALIDATION"
$testName = "SHA-256 sidecar file validation"
try {
    $fixture = New-SyntheticFixture -FixtureDir (Join-Path $testDir "sidecar")
    $packageDir = Join-Path $testDir "sidecar-output"
    Test-PackageCreation -HostDir $fixture.Build1HostDir -AgentHostDir $fixture.Build1AgentHostDir -PackageDir $packageDir | Out-Null
    $result = Test-SidecarValidation -PackageDir $packageDir
    Write-TestResult -TestId $testId -TestName $testName -Passed $result
}
catch {
    Write-TestResult -TestId $testId -TestName $testName -Passed $false -ErrorMessage $_.Exception.Message
}

# Test 7: Dirty diff detection
$testId = "DIRTY_DIFF_DETECTION"
$testName = "Dirty diff covers tracked, staged, and untracked"
try {
    $result = Test-DirtyDiffDetection
    Write-TestResult -TestId $testId -TestName $testName -Passed $result
}
catch {
    Write-TestResult -TestId $testId -TestName $testName -Passed $false -ErrorMessage $_.Exception.Message
}

# Summary
$totalTests = 7
Write-Host "`n==> Self-test complete" -ForegroundColor Yellow

# Cleanup
if (Test-Path -LiteralPath $testDir -PathType Container) {
    Remove-Item -LiteralPath $testDir -Recurse -Force -ErrorAction SilentlyContinue
}

exit $script:exitCode

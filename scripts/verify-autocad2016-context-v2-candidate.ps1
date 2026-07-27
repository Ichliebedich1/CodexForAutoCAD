[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',
    [string] $AutoCad2016Dir = 'D:\AutoCAD 2016',
    [string] $CodexExecutable,
    [ValidateSet('m1-readonly', 'm4-live')]
    [string] $CandidateProfile = 'm1-readonly',
    [string] $ReadinessEvidencePath,
    [string] $SuiteEvidencePath,
    [string] $EvidenceDirectory,
    [switch] $SelfTestOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'build-safety.ps1')
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactsRoot = $buildSafety.ArtifactRoot
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$powerShell = (Get-Process -Id $PID).Path
$net45ReferencePath = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net45\1.0.3\build\.NETFramework\v4.5'
$runId = [Guid]::NewGuid().ToString('N')
$stageRoot = Join-Path $artifactsRoot ("autocad2016-context-v2-candidate-" + $runId)
$publishRoot = Join-Path $stageRoot 'agenthost-publish'
$candidateStage = Join-Path $stageRoot 'candidate'
$isM4LiveCandidate = $CandidateProfile -ceq 'm4-live'
$effectiveEvidenceDirectory = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    if ($isM4LiveCandidate) {
        Join-Path $artifactsRoot 'candidate-evidence'
    }
    else {
        Join-Path $repoRoot 'handoff\autocad2016\evidence'
    }
}
elseif ([IO.Path]::IsPathRooted($EvidenceDirectory)) {
    [IO.Path]::GetFullPath($EvidenceDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
}
$phase2Script = Join-Path $repoRoot 'scripts\verify-phase2.ps1'
$assemblyInfoPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Properties\AssemblyInfo.cs'
$sourceHeadCommit = $null
$readiness = $null
$readinessSha256 = $null
$suiteEvidence = $null
$suiteEvidenceSha256 = $null
$sourceManifest = $null

function Get-Sha256([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "缺少文件：$Path" }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-BytesSha256([byte[]] $Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Read-StrictJsonEvidence(
    [string] $Path,
    [string] $Label,
    [int] $MaximumBytes = 4194304
) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label 不能是 reparse 文件。"
    }

    $stream = New-Object IO.FileStream(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    $bytes = $null
    try {
        if ($stream.Length -lt 2 -or $stream.Length -gt $MaximumBytes) {
            throw "$Label 字节长度超出允许范围。"
        }
        $bytes = New-Object byte[] ([int] $stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -le 0) {
                throw "$Label 读取未完整结束。"
            }
            $offset += $read
        }
        $strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
        try {
            $text = $strictUtf8.GetString($bytes)
        }
        catch {
            throw "$Label 不是严格 UTF-8。"
        }
        if ($text.Length -gt 0 -and $text[0] -eq [char] 0xFEFF) {
            $text = $text.Substring(1)
        }
        try {
            $json = $text | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "$Label 不是有效 JSON。"
        }
        return [pscustomobject]@{
            Json = $json
            Sha256 = Get-BytesSha256 $bytes
        }
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
        $stream.Dispose()
    }
}

function Get-TextSha256([string] $Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
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

function Get-SourceManifestFingerprint {
    [string[]] $files = @(
        & git -c "safe.directory=$repoRoot" -C $repoRoot `
            ls-files --cached --others --exclude-standard |
            ForEach-Object { [string] $_ }
    )
    if ($LASTEXITCODE -ne 0) {
        throw '无法枚举当前源码以验证 M4 readiness manifest。'
    }
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $entries = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in $files) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }
        $normalized = $relativePath.Replace('\', '/')
        if ($normalized -match '(?:^|/)(?:artifacts|bin|obj)(?:/|$)') {
            continue
        }
        $absolutePath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            continue
        }
        $item = Get-Item -LiteralPath $absolutePath
        $entries.Add(
            $normalized + "`t" + [string] $item.Length + "`t" + (Get-Sha256 $absolutePath))
    }
    return [pscustomobject]@{
        FileCount = $entries.Count
        Sha256 = Get-TextSha256 ($entries -join "`n")
    }
}

function Invoke-Captured([string] $FilePath, [string[]] $Arguments, [string] $Description) {
    Write-Host "==> $Description" -ForegroundColor Cyan
    $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string] $_ })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "$Description 失败，退出码：$exitCode" }
}

function Invoke-CapturedOutput([string] $FilePath, [string[]] $Arguments, [string] $Description) {
    Write-Host "==> $Description" -ForegroundColor Cyan
    $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string] $_ })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "$Description 失败，退出码：$exitCode" }
    return $lines
}

function Assert-HostReadOnlySource([string] $ProjectPath) {
    [xml] $project = Get-Content -LiteralPath $ProjectPath -Raw -Encoding UTF8
    $namespace = New-Object Xml.XmlNamespaceManager($project.NameTable)
    $namespace.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $projectDirectory = Split-Path -Parent $ProjectPath
    $sourceFiles = @(
        foreach ($node in @($project.SelectNodes('//m:Compile[@Include]', $namespace))) {
            $path = [IO.Path]::GetFullPath((Join-Path $projectDirectory $node.Include))
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Host.2016 Compile 项不存在：$path"
            }
            $path
        }
    ) | Sort-Object -Unique
    if ($sourceFiles.Count -eq 0) { throw 'Host.2016 Compile 闭包为空。' }

    $forbidden = [ordered]@{
        'CAD ForWrite' = '(?i)\bOpenMode\s*\.\s*ForWrite\b'
        'CAD mutation' = '(?i)\.\s*(?:UpgradeOpen|DowngradeOpen|AppendEntity|AddNewlyCreatedDBObject|Erase|WblockCloneObjects|DeepCloneObjects|TransformBy)\s*\('
        'CAD command/save' = '(?i)\.\s*(?:LockDocument|SetSystemVariable|SetImpliedSelection|SendStringToExecute|Save|SaveAs|DwgOut|DxfOut|CloseAndSave|Command|CommandAsync|ExecuteInCommandContextAsync)\s*\('
    }
    $findings = @(
        foreach ($rule in $forbidden.GetEnumerator()) {
            foreach ($match in @($sourceFiles | Select-String -Pattern ([string] $rule.Value))) {
                [pscustomobject]@{
                    Rule = [string] $rule.Key
                    Path = $match.Path
                    Line = $match.LineNumber
                }
            }
        }
    )
    if ($findings.Count -ne 0) {
        throw "Host.2016 只读源码扫描失败：$($findings | ConvertTo-Json -Compress)"
    }
    Write-Host "Host.2016 只读 Compile 闭包扫描通过：$($sourceFiles.Count) 个源文件。" -ForegroundColor Green
    return $sourceFiles.Count
}

function Test-IsFullyQualifiedWindowsPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return [regex]::IsMatch(
        $Path,
        '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$))'
    )
}

function Resolve-CodexExecutable([string] $RequestedPath) {
    $candidates = [Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath)
    }
    $environmentCandidate = [Environment]::GetEnvironmentVariable('CODEX_EXECUTABLE')
    if (-not [string]::IsNullOrWhiteSpace($environmentCandidate)) {
        $candidates.Add($environmentCandidate)
    }

    $npmCommand = Get-Command codex.cmd -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $npmCommand) {
        $packageRoot = Join-Path (Split-Path -Parent $npmCommand.Source) 'node_modules\@openai\codex'
        if (Test-Path -LiteralPath $packageRoot -PathType Container) {
            foreach ($native in @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter 'codex.exe' -ErrorAction SilentlyContinue)) {
                $candidates.Add($native.FullName)
            }
        }
    }

    foreach ($command in @(Get-Command codex.exe -All -ErrorAction SilentlyContinue)) {
        $candidates.Add($command.Source)
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if ((Test-IsFullyQualifiedWindowsPath $candidate) -and
            [IO.Path]::GetExtension($candidate) -ieq '.exe' -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    throw '未找到可供候选 AgentHost doctor 使用的本机 codex.exe。'
}

function Get-FilesSnapshot([string] $Root) {
    $map = [ordered]@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Root.Length + 1).Replace('\','/')
        $map[$relative] = [ordered]@{ Length = $file.Length; Sha256 = Get-Sha256 $file.FullName }
    }
    return $map
}

function Assert-Same($left, $right, [string] $label) {
    if (($left | ConvertTo-Json -Depth 20 -Compress) -cne ($right | ConvertTo-Json -Depth 20 -Compress)) {
        throw "$label 不一致。"
    }
}

function Copy-VerifiedFileTree([string] $SourceRoot, [string] $DestinationRoot) {
    foreach ($directory in @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -Directory)) {
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw '已验证 AgentHost 输出不能包含 reparse 目录。'
        }
        $relative = $directory.FullName.Substring($SourceRoot.Length + 1)
        New-Item -ItemType Directory -Path (Join-Path $DestinationRoot $relative) `
            -Force | Out-Null
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File)) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw '已验证 AgentHost 输出不能包含 reparse 文件。'
        }
        $relative = $file.FullName.Substring($SourceRoot.Length + 1)
        $target = Join-Path $DestinationRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}

function Assert-PathWithinRoot([string] $Path, [string] $Root, [string] $Label) {
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not $resolvedPath.StartsWith($resolvedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label 必须位于统一构建安全产物根内。"
    }
}

function Assert-JsonBoolean($Value, [bool] $Expected, [string] $Label) {
    if ($Value -isnot [bool] -or [bool] $Value -ne $Expected) {
        throw "$Label 与 M4 实机候选要求不一致。"
    }
}

function Test-IsJsonInteger($Value) {
    return ($Value -is [int] -or $Value -is [long])
}

function Assert-M4SourceState([string] $ExpectedHeadCommit) {
    $actualHeadCommit = @(
        & git -c "safe.directory=$repoRoot" -C $repoRoot rev-parse HEAD 2>&1 |
            ForEach-Object { [string] $_ }
    ) | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($actualHeadCommit) -or
        $actualHeadCommit.Trim() -cne $ExpectedHeadCommit) {
        throw 'M4 实机候选构建期间源码提交发生变化。'
    }

    $gitStatus = @(
        & git -c "safe.directory=$repoRoot" -C $repoRoot status --porcelain=v1 `
            --untracked-files=all 2>&1 | ForEach-Object { [string] $_ }
    )
    if ($LASTEXITCODE -ne 0) {
        throw '无法确认 M4 候选源码工作树状态。'
    }
    if ($gitStatus.Count -ne 0) {
        throw 'M4 实机候选只能从干净的已提交源码构建。'
    }
}

function Resolve-CorrelatedAgentHostOutput($ReadinessEvidence) {
    $expectedRunId = [string] $ReadinessEvidence.RunCorrelation.Id
    $expectedEvidenceSha256 = [string] $ReadinessEvidence.InputEvidence.AgentBootstrapSha256
    $expectedAgentHostDllSha256 =
        [string] $ReadinessEvidence.CandidateHashes.AgentHostDllSha256
    if ($expectedEvidenceSha256 -cnotmatch '^[0-9A-F]{64}$') {
        throw 'M4 readiness 缺少有效 AgentHost bootstrap evidence 哈希。'
    }

    $matches = @(
        foreach ($directory in @(Get-ChildItem -LiteralPath $artifactsRoot -Directory `
            -Filter 'autocad2016-agent-bootstrap-*' -ErrorAction SilentlyContinue)) {
            if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                continue
            }
            $verificationPath = Join-Path $directory.FullName 'verification.json'
            if (-not (Test-Path -LiteralPath $verificationPath -PathType Leaf)) {
                continue
            }
            try {
                $verificationRead = Read-StrictJsonEvidence `
                    -Path $verificationPath -Label 'AgentHost bootstrap evidence'
                if ($verificationRead.Sha256 -cne $expectedEvidenceSha256) {
                    continue
                }
                $verification = $verificationRead.Json
                if ([string] $verification.RunCorrelationId -cne $expectedRunId -or
                    [string] $verification.Status -cne
                        'live-agenthost-bootstrap-doctor-gate-passed' -or
                    $verification.BitForBitMatch -isnot [bool] -or
                    -not [bool] $verification.BitForBitMatch -or
                    $verification.RunnableOutputTreeComparedByRelativePathAndSha256 `
                        -isnot [bool] -or
                    -not [bool] `
                        $verification.RunnableOutputTreeComparedByRelativePathAndSha256 -or
                    $verification.RunnableOutputTreesRecheckedAfterSpecs -isnot [bool] -or
                    -not [bool] $verification.RunnableOutputTreesRecheckedAfterSpecs -or
                    -not (Test-IsJsonInteger $verification.RunnableOutputTreeFileCount) -or
                    [int] $verification.RunnableOutputTreeFileCount -le 0 -or
                    @($verification.RunnableOutputTreeExclusions).Count -ne 0 -or
                    [string] $verification.ArtifactHashes.AgentHostDll -cne
                        $expectedAgentHostDllSha256) {
                    continue
                }
                $runnableTreeRoot = Join-Path $directory.FullName 'build-a\out\bin'
                $secondRunnableTreeRoot = Join-Path $directory.FullName 'build-b\out\bin'
                $runnableRoot = Join-Path $runnableTreeRoot `
                    'Codex.AutoCAD.AgentHost\release_win-x64'
                $secondRunnableRoot = Join-Path $secondRunnableTreeRoot `
                    'Codex.AutoCAD.AgentHost\release_win-x64'
                $candidateDll = Join-Path $runnableRoot 'Codex.AutoCAD.AgentHost.dll'
                $candidateExe = Join-Path $runnableRoot 'Codex.AutoCAD.AgentHost.exe'
                if (-not (Test-Path -LiteralPath $candidateDll -PathType Leaf) -or
                    -not (Test-Path -LiteralPath $candidateExe -PathType Leaf) -or
                    -not (Test-Path -LiteralPath $secondRunnableRoot -PathType Container) -or
                    (Get-Sha256 $candidateDll) -cne $expectedAgentHostDllSha256 -or
                    (Get-Sha256 $candidateExe) -cne
                        [string] $verification.ArtifactHashes.AgentHostExe) {
                    continue
                }
                $firstTreeSnapshot = Get-FilesSnapshot $runnableTreeRoot
                $secondTreeSnapshot = Get-FilesSnapshot $secondRunnableTreeRoot
                if ($firstTreeSnapshot.Count -ne
                        [int] $verification.RunnableOutputTreeFileCount -or
                    ($firstTreeSnapshot | ConvertTo-Json -Depth 20 -Compress) -cne
                        ($secondTreeSnapshot | ConvertTo-Json -Depth 20 -Compress)) {
                    continue
                }
                [pscustomobject]@{
                    Root = $runnableRoot
                    VerificationPath = $verificationPath
                    Snapshot = Get-FilesSnapshot $runnableRoot
                }
            }
            catch {
                continue
            }
        }
    )
    if ($matches.Count -ne 1) {
        throw ("M4 实机候选必须精确找到一份与 readiness 同 Run ID、evidence 哈希及 " +
            "AgentHost DLL 哈希绑定的 bootstrap 输出；实际：$($matches.Count)。")
    }
    return $matches[0]
}

function Assert-CandidateSelfTestThrows(
    [scriptblock] $Action,
    [string] $ExpectedPattern,
    [string] $Label
) {
    $message = $null
    try {
        & $Action
    }
    catch {
        $message = $_.Exception.Message
    }
    if ([string]::IsNullOrWhiteSpace($message) -or
        $message -cnotmatch $ExpectedPattern) {
        throw "候选打包自检失败：$Label 未按预期拒绝。"
    }
}

function Invoke-CandidatePackagerSelfTests {
    $selfTestId = [Guid]::NewGuid().ToString('N')
    $selfTestRoot = Join-Path $artifactsRoot `
        ('candidate-packager-selftest-' + $selfTestId)
    $bootstrapRootA = Join-Path $artifactsRoot `
        ('autocad2016-agent-bootstrap-selftest-a-' + $selfTestId)
    $bootstrapRootB = Join-Path $artifactsRoot `
        ('autocad2016-agent-bootstrap-selftest-b-' + $selfTestId)
    $ownedRoots = @($selfTestRoot, $bootstrapRootA, $bootstrapRootB)

    foreach ($ownedRoot in $ownedRoots) {
        Assert-PathWithinRoot -Path $ownedRoot -Root $artifactsRoot `
            -Label '候选打包自检目录'
        if (Test-Path -LiteralPath $ownedRoot) {
            throw '候选打包自检目录意外已存在。'
        }
    }

    try {
        New-Item -ItemType Directory -Path $selfTestRoot -Force | Out-Null

        $validJsonPath = Join-Path $selfTestRoot 'valid.json'
        $validJsonText = '{"SchemaVersion":1,"Ready":true,"Count":9}'
        [IO.File]::WriteAllText(
            $validJsonPath,
            $validJsonText,
            (New-Object Text.UTF8Encoding($false)))
        $validJson = Read-StrictJsonEvidence `
            -Path $validJsonPath -Label '候选打包自检 JSON'
        if ($validJson.Sha256 -cne (Get-Sha256 $validJsonPath) -or
            $validJson.Json.Ready -isnot [bool] -or
            -not [bool] $validJson.Json.Ready -or
            -not (Test-IsJsonInteger $validJson.Json.Count) -or
            [int] $validJson.Json.Count -ne 9) {
            throw '候选打包自检失败：单次读取 JSON/哈希或强类型值不一致。'
        }

        $invalidUtf8Path = Join-Path $selfTestRoot 'invalid-utf8.json'
        [IO.File]::WriteAllBytes(
            $invalidUtf8Path,
            [byte[]]@(0x7B, 0x22, 0x78, 0x22, 0x3A, 0xC3, 0x28, 0x7D))
        Assert-CandidateSelfTestThrows -Label '非法 UTF-8 evidence' `
            -ExpectedPattern '不是严格 UTF-8' -Action {
                $null = Read-StrictJsonEvidence `
                    -Path $invalidUtf8Path -Label '候选打包自检非法 JSON'
            }

        $boundedJsonPath = Join-Path $selfTestRoot 'bounded.json'
        [IO.File]::WriteAllText(
            $boundedJsonPath,
            '{"Value":"012345678901234567890123456789"}',
            (New-Object Text.UTF8Encoding($false)))
        Assert-CandidateSelfTestThrows -Label '超限 evidence' `
            -ExpectedPattern '字节长度超出允许范围' -Action {
                $null = Read-StrictJsonEvidence -Path $boundedJsonPath `
                    -Label '候选打包自检超限 JSON' -MaximumBytes 16
            }

        $outsidePath = Join-Path ([IO.Path]::GetPathRoot($artifactsRoot)) `
            ('candidate-packager-outside-' + $selfTestId + '.json')
        Assert-CandidateSelfTestThrows -Label '产物根逃逸路径' `
            -ExpectedPattern '必须位于统一构建安全产物根内' -Action {
                Assert-PathWithinRoot -Path $outsidePath -Root $artifactsRoot `
                    -Label '候选打包自检逃逸'
            }

        $copySource = Join-Path $selfTestRoot 'copy-source'
        $copyTarget = Join-Path $selfTestRoot 'copy-target'
        New-Item -ItemType Directory -Path (Join-Path $copySource 'nested') `
            -Force | Out-Null
        [IO.File]::WriteAllText(
            (Join-Path $copySource 'root.txt'),
            'root',
            (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText(
            (Join-Path $copySource 'nested\leaf.txt'),
            'leaf',
            (New-Object Text.UTF8Encoding($false)))
        New-Item -ItemType Directory -Path $copyTarget -Force | Out-Null
        Copy-VerifiedFileTree -SourceRoot $copySource -DestinationRoot $copyTarget
        Assert-Same (Get-FilesSnapshot $copySource) `
            (Get-FilesSnapshot $copyTarget) '候选打包自检递归复制'

        $treeA = Join-Path $bootstrapRootA 'build-a\out\bin'
        $treeB = Join-Path $bootstrapRootA 'build-b\out\bin'
        $agentRelative = 'Codex.AutoCAD.AgentHost\release_win-x64'
        $otherRelative = 'Codex.AutoCAD.AgentLauncher\release_net45'
        foreach ($treeRoot in @($treeA, $treeB)) {
            New-Item -ItemType Directory -Path `
                (Join-Path $treeRoot $agentRelative) -Force | Out-Null
            New-Item -ItemType Directory -Path `
                (Join-Path $treeRoot $otherRelative) -Force | Out-Null
            [IO.File]::WriteAllText(
                (Join-Path $treeRoot `
                    ($agentRelative + '\Codex.AutoCAD.AgentHost.dll')),
                'agent-dll',
                (New-Object Text.UTF8Encoding($false)))
            [IO.File]::WriteAllText(
                (Join-Path $treeRoot `
                    ($agentRelative + '\Codex.AutoCAD.AgentHost.exe')),
                'agent-exe',
                (New-Object Text.UTF8Encoding($false)))
            [IO.File]::WriteAllText(
                (Join-Path $treeRoot ($otherRelative + '\Launcher.dll')),
                'launcher',
                (New-Object Text.UTF8Encoding($false)))
        }

        $agentDllPath = Join-Path $treeA `
            ($agentRelative + '\Codex.AutoCAD.AgentHost.dll')
        $agentExePath = Join-Path $treeA `
            ($agentRelative + '\Codex.AutoCAD.AgentHost.exe')
        $runCorrelationId = 'run-' + $selfTestId
        $verification = [ordered]@{
            RunCorrelationId = $runCorrelationId
            Status = 'live-agenthost-bootstrap-doctor-gate-passed'
            BitForBitMatch = $true
            RunnableOutputTreeComparedByRelativePathAndSha256 = $true
            RunnableOutputTreesRecheckedAfterSpecs = $true
            RunnableOutputTreeFileCount = 3
            RunnableOutputTreeExclusions = @()
            ArtifactHashes = [ordered]@{
                AgentHostDll = Get-Sha256 $agentDllPath
                AgentHostExe = Get-Sha256 $agentExePath
            }
        }
        $verificationPath = Join-Path $bootstrapRootA 'verification.json'
        [IO.File]::WriteAllText(
            $verificationPath,
            ($verification | ConvertTo-Json -Depth 10) + "`n",
            (New-Object Text.UTF8Encoding($false)))
        $readiness = [pscustomobject]@{
            RunCorrelation = [pscustomobject]@{ Id = $runCorrelationId }
            InputEvidence = [pscustomobject]@{
                AgentBootstrapSha256 = Get-Sha256 $verificationPath
            }
            CandidateHashes = [pscustomobject]@{
                AgentHostDllSha256 = Get-Sha256 $agentDllPath
            }
        }

        $resolved = Resolve-CorrelatedAgentHostOutput `
            -ReadinessEvidence $readiness
        if ($resolved.Root -cne
                (Join-Path $treeA $agentRelative) -or
            $resolved.Snapshot.Count -ne 2) {
            throw '候选打包自检失败：唯一 correlated bootstrap 未被精确解析。'
        }

        $treeBLauncher = Join-Path $treeB ($otherRelative + '\Launcher.dll')
        [IO.File]::WriteAllText(
            $treeBLauncher,
            'tampered',
            (New-Object Text.UTF8Encoding($false)))
        Assert-CandidateSelfTestThrows -Label 'bootstrap A/B 文件树漂移' `
            -ExpectedPattern '实际：0' -Action {
                $null = Resolve-CorrelatedAgentHostOutput `
                    -ReadinessEvidence $readiness
            }
        [IO.File]::WriteAllText(
            $treeBLauncher,
            'launcher',
            (New-Object Text.UTF8Encoding($false)))

        New-Item -ItemType Directory -Path $bootstrapRootB -Force | Out-Null
        Copy-VerifiedFileTree -SourceRoot $bootstrapRootA `
            -DestinationRoot $bootstrapRootB
        Assert-CandidateSelfTestThrows -Label '多个 correlated bootstrap' `
            -ExpectedPattern '实际：2' -Action {
                $null = Resolve-CorrelatedAgentHostOutput `
                    -ReadinessEvidence $readiness
            }
    }
    finally {
        foreach ($ownedRoot in $ownedRoots) {
            $resolvedOwnedRoot = [IO.Path]::GetFullPath($ownedRoot)
            Assert-PathWithinRoot -Path $resolvedOwnedRoot -Root $artifactsRoot `
                -Label '候选打包自检清理目录'
            if ((Split-Path -Leaf $resolvedOwnedRoot) -cnotmatch
                '^(?:candidate-packager-selftest|autocad2016-agent-bootstrap-selftest-[ab])-[0-9a-f]{32}$') {
                throw '候选打包自检拒绝清理非自有目录。'
            }
            if (Test-Path -LiteralPath $resolvedOwnedRoot) {
                Remove-Item -LiteralPath $resolvedOwnedRoot -Recurse -Force
            }
        }
    }
}

if ($SelfTestOnly) {
    try {
        Invoke-CandidatePackagerSelfTests
        Write-Host 'AUTOCAD2016_CANDIDATE_PACKAGER_SELF_TEST=passed'
    }
    finally {
        Complete-CodexBuildSafety -State $buildSafety `
            -Stage 'context-v2-candidate-self-test' | Out-Null
    }
    return
}

if ($isM4LiveCandidate) {
    Assert-PathWithinRoot -Path $effectiveEvidenceDirectory -Root $artifactsRoot `
        -Label 'M4 候选 evidence 目录'

    if ([string]::IsNullOrWhiteSpace($ReadinessEvidencePath)) {
        $ReadinessEvidencePath = Join-Path $artifactsRoot 'gate-evidence\m4-readiness.json'
    }
    $resolvedReadinessPath = if ([IO.Path]::IsPathRooted($ReadinessEvidencePath)) {
        [IO.Path]::GetFullPath($ReadinessEvidencePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $ReadinessEvidencePath))
    }
    Assert-PathWithinRoot -Path $resolvedReadinessPath -Root $artifactsRoot `
        -Label 'M4 readiness evidence'
    if (-not (Test-Path -LiteralPath $resolvedReadinessPath -PathType Leaf)) {
        throw 'M4 readiness evidence 不存在。'
    }
    $readinessRead = Read-StrictJsonEvidence `
        -Path $resolvedReadinessPath -Label 'M4 readiness evidence'
    $readiness = $readinessRead.Json
    $readinessSha256 = $readinessRead.Sha256

    if ([string]::IsNullOrWhiteSpace($SuiteEvidencePath)) {
        $SuiteEvidencePath = Join-Path $artifactsRoot 'gate-evidence\all-gates.json'
    }
    $resolvedSuitePath = if ([IO.Path]::IsPathRooted($SuiteEvidencePath)) {
        [IO.Path]::GetFullPath($SuiteEvidencePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $SuiteEvidencePath))
    }
    Assert-PathWithinRoot -Path $resolvedSuitePath -Root $artifactsRoot `
        -Label '统一门禁 suite evidence'
    if (-not (Test-Path -LiteralPath $resolvedSuitePath -PathType Leaf)) {
        throw '统一门禁 suite evidence 不存在。'
    }
    $suiteRead = Read-StrictJsonEvidence `
        -Path $resolvedSuitePath -Label '统一门禁 suite evidence'
    $suiteEvidence = $suiteRead.Json
    $suiteEvidenceSha256 = $suiteRead.Sha256

    $sourceHeadCommit = @(
        & git -c "safe.directory=$repoRoot" -C $repoRoot rev-parse HEAD 2>&1 |
            ForEach-Object { [string] $_ }
    ) | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0 -or $sourceHeadCommit -cnotmatch '^[0-9a-f]{40}$') {
        throw '无法确定 M4 候选源码提交。'
    }
    $sourceHeadCommit = $sourceHeadCommit.Trim()
    Assert-M4SourceState -ExpectedHeadCommit $sourceHeadCommit
    $sourceManifest = Get-SourceManifestFingerprint
    if (-not (Test-IsJsonInteger $readiness.Source.ManifestFileCount) -or
        [int] $readiness.Source.ManifestFileCount -ne [int] $sourceManifest.FileCount -or
        [string] $readiness.Source.ManifestSha256 -cne [string] $sourceManifest.Sha256) {
        throw 'M4 readiness 源码 manifest 与当前干净提交不一致。'
    }
    $bridgeLockPath = Join-Path $repoRoot `
        'src\Codex.AutoCAD.Bridge.Client\packages.lock.json'
    if (-not (Test-Path -LiteralPath $bridgeLockPath -PathType Leaf) -or
        (Get-Sha256 $bridgeLockPath) -cne
            [string] $readiness.Source.BridgeClientLockSha256) {
        throw 'M4 readiness Bridge.Client lock 哈希与当前源码不一致。'
    }

    if ([string] $readiness.Status -cne 'automated_readiness_only' -or
        [string] $readiness.Source.HeadCommit -cne $sourceHeadCommit -or
        [string] $readiness.RunCorrelation.Mode -cne 'Correlated' -or
        [string] $readiness.RunCorrelation.Id -cnotmatch '^run-[0-9a-f]{32}$') {
        throw 'M4 readiness 未精确绑定当前干净源码提交或同一次关联门禁。'
    }
    Assert-JsonBoolean $readiness.Source.WorkingTreeDirty $false `
        'M4 readiness Source.WorkingTreeDirty'
    Assert-JsonBoolean $readiness.AutomatedGatesPassed $true `
        'M4 readiness AutomatedGatesPassed'
    Assert-JsonBoolean $readiness.AutoCadStartedOrCommanded $false `
        'M4 readiness AutoCadStartedOrCommanded'
    Assert-JsonBoolean $readiness.CadWriteEnabled $false `
        'M4 readiness CadWriteEnabled'
    Assert-JsonBoolean $readiness.PluginInitiatedSaveEnabled $false `
        'M4 readiness PluginInitiatedSaveEnabled'
    Assert-JsonBoolean $readiness.M4Complete $false 'M4 readiness M4Complete'
    Assert-JsonBoolean $readiness.M416Frozen $false 'M4 readiness M416Frozen'

    if ([string] $suiteEvidence.Scope -cne
            'codex-autocad-implemented-automated-gate-suite' -or
        [string] $suiteEvidence.RunCorrelationId -cne
            [string] $readiness.RunCorrelation.Id -or
        -not (Test-IsJsonInteger $suiteEvidence.GateDefinitionTotal) -or
        [int] $suiteEvidence.GateDefinitionTotal -ne 9 -or
        -not (Test-IsJsonInteger $suiteEvidence.GateTotal) -or
        [int] $suiteEvidence.GateTotal -ne 9 -or
        -not (Test-IsJsonInteger $suiteEvidence.GatePassed) -or
        [int] $suiteEvidence.GatePassed -ne 9 -or
        -not (Test-IsJsonInteger $suiteEvidence.GateFailed) -or
        [int] $suiteEvidence.GateFailed -ne 0 -or
        -not (Test-IsJsonInteger $suiteEvidence.IntroducedResidualProcessCount) -or
        [int] $suiteEvidence.IntroducedResidualProcessCount -ne 0) {
        throw '统一门禁 suite evidence 未证明同一次 9/9 且新增残留为 0。'
    }
    Assert-JsonBoolean $suiteEvidence.UserPathUnchanged $true `
        '统一门禁 suite UserPathUnchanged'
    Assert-JsonBoolean $suiteEvidence.AutoCadStartedOrCommanded $false `
        '统一门禁 suite AutoCadStartedOrCommanded'
    $readinessGate = @(
        $suiteEvidence.Gates | Where-Object { [string] $_.Name -ceq 'm4-readiness' }
    )
    if ($readinessGate.Count -ne 1 -or
        -not (Test-IsJsonInteger $readinessGate[0].ExitCode) -or
        [int] $readinessGate[0].ExitCode -ne 0 -or
        $readinessGate[0].EvidenceBoundToRun -isnot [bool] -or
        -not [bool] $readinessGate[0].EvidenceBoundToRun -or
        [string] $readinessGate[0].EvidenceSha256 -cne $readinessSha256) {
        throw '统一门禁 suite 没有精确绑定当前 M4 readiness evidence。'
    }

    foreach ($hash in @(
        [string] $readiness.CandidateHashes.R201HostDllSha256,
        [string] $readiness.CandidateHashes.AgentHostDllSha256)) {
        if ($hash -cnotmatch '^[0-9A-F]{64}$' -or $hash -match '^([0-9A-F])\1{63}$') {
            throw 'M4 readiness 包含无效或占位候选 SHA-256。'
        }
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($ReadinessEvidencePath) -or
    -not [string]::IsNullOrWhiteSpace($SuiteEvidencePath)) {
    throw '只有 CandidateProfile=m4-live 才接受 readiness/suite evidence 路径。'
}

foreach ($path in @($AutoCad2016Dir, (Join-Path $AutoCad2016Dir 'acad.exe'), $net45ReferencePath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺少必要路径：$path" }
}
foreach ($path in @($phase2Script, $assemblyInfoPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少必要文件：$path" }
}

$assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw -Encoding UTF8
$versionMatch = [regex]::Match($assemblyInfo, 'AssemblyVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
$fileVersionMatch = [regex]::Match($assemblyInfo, 'AssemblyFileVersion\("(?<Version>\d+\.\d+\.\d+\.\d+)"\)')
if (-not $versionMatch.Success -or -not $fileVersionMatch.Success -or
    $versionMatch.Groups['Version'].Value -cne $fileVersionMatch.Groups['Version'].Value) {
    throw 'Host.2016 AssemblyVersion 与 AssemblyFileVersion 必须存在且完全一致。'
}
$hostVersion = $versionMatch.Groups['Version'].Value
$versionParts = $hostVersion.Split('.')
$versionTag = 'v' + $versionParts[0] + $versionParts[1] + $versionParts[2]

$lockSnapshots = [ordered]@{}
foreach ($lockFile in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter 'packages.lock.json')) {
    $lockSnapshots[$lockFile.FullName] = [IO.File]::ReadAllBytes($lockFile.FullName)
}
try {
    $phase2Output = Invoke-CapturedOutput $powerShell @(
        '-NoProfile', '-File', $phase2Script, '-Configuration', $Configuration
    ) 'Phase 2 动态门禁'
}
finally {
    foreach ($snapshot in $lockSnapshots.GetEnumerator()) {
        [IO.File]::WriteAllBytes([string] $snapshot.Key, [byte[]] $snapshot.Value)
    }
}
$phase2Summary = @(
    foreach ($line in $phase2Output) {
        $match = [regex]::Match($line, '规格动态计数汇总：(?<Passed>\d+)/(?<Total>\d+)')
        if ($match.Success -and $match.Groups['Passed'].Value -ceq $match.Groups['Total'].Value) {
            $match.Groups['Passed'].Value + '/' + $match.Groups['Total'].Value
        }
    }
) | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($phase2Summary)) {
    throw 'Phase 2 输出缺少全部通过的动态规格摘要。'
}

New-Item -ItemType Directory -Path $stageRoot,$candidateStage,$publishRoot -Force | Out-Null
$sourceSnapshotAtUtc = [DateTimeOffset]::UtcNow.ToString('o')

$hostProject = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj'
$nugetConfig = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016\NuGet.Config'
$hostReadOnlySourceCount = Assert-HostReadOnlySource $hostProject
$dependencyProjects = @(
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj'),
    (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj')
)

foreach ($project in @($dependencyProjects + $hostProject)) {
    # dotnet restore 会沿 ProjectReference 更新其他项目的 lock file，因此只保存当前
    # project 的 packages.lock.json 不足以保持候选源码工作树干净。每次 restore 前
    # 快照全部 src lock file，并在 finally 中逐字节恢复。
    $restoreLockSnapshots = [ordered]@{}
    foreach ($lockFile in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') `
        -Recurse -File -Filter 'packages.lock.json')) {
        $restoreLockSnapshots[$lockFile.FullName] =
            [IO.File]::ReadAllBytes($lockFile.FullName)
    }
    $restoreArgs = @('restore', $project, '--configfile', $nugetConfig, '-p:EnableAutoCad2016=true', '--force', '--no-cache')
    if ($project -eq $hostProject) { $restoreArgs += '-p:RestoreLockedMode=false' }
    try {
        Invoke-Captured $dotnet $restoreArgs ("恢复 " + (Split-Path -Leaf $project))
    }
    finally {
        foreach ($snapshot in $restoreLockSnapshots.GetEnumerator()) {
            [IO.File]::WriteAllBytes([string] $snapshot.Key, [byte[]] $snapshot.Value)
        }
    }
}

foreach ($dependency in $dependencyProjects) {
    Invoke-Captured $dotnet @(
        'build', $dependency, '--configuration', $Configuration, '--framework', 'net45', '--no-restore',
        '--nologo', '-m:1', '-p:BuildProjectReferences=false', '-p:EnableAutoCad2016=true',
        ("-p:FrameworkPathOverride=" + $net45ReferencePath)
    ) ("net45 依赖编译 " + (Split-Path -Leaf $dependency))
}

$hostBuilds = @()
foreach ($label in @('A','B')) {
    $out = Join-Path $stageRoot ("host-" + $label)
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    foreach ($dependencyName in @('Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.AgentLauncher.dll')) {
        $dependencyPath = Join-Path $repoRoot 'src'
        $dependency = Get-ChildItem -LiteralPath $dependencyPath -Recurse -File -Filter $dependencyName |
            Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]net45[\\/]' } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $dependency) { throw "缺少 Host.2016 依赖：$dependencyName" }
        Copy-Item -LiteralPath $dependency.FullName -Destination (Join-Path $out $dependencyName) -Force
    }
    Invoke-Captured $dotnet @(
        'msbuild', $hostProject, '/t:Rebuild', "/p:Configuration=$Configuration", '/p:Platform=x64',
        ("/p:AutoCad2016Dir=" + $AutoCad2016Dir), '/p:EnableAutoCad2016=true',
        '/p:AutomaticallyUseReferenceAssemblyPackages=true', ("/p:FrameworkPathOverride=" + $net45ReferencePath),
        '/p:BuildProjectReferences=false', ("/p:OutputPath=" + $out + '\'), '/p:DebugSymbols=false',
        '/p:DebugType=None', '/p:ContinuousIntegrationBuild=true', '/m:1', '/nologo'
    ) ("Host.2016 R20.1 编译 $label")
    $hostDll = Join-Path $out 'Codex.AutoCAD.Host.2016.dll'
    $hostBuilds += [pscustomobject]@{ Label = $label; Root = $out; Dll = $hostDll; Sha256 = Get-Sha256 $hostDll }
}
Assert-Same (Get-FilesSnapshot $hostBuilds[0].Root) (Get-FilesSnapshot $hostBuilds[1].Root) 'Host.2016 A/B 输出'

if ($isM4LiveCandidate) {
    $verifiedAgentHost = Resolve-CorrelatedAgentHostOutput -ReadinessEvidence $readiness
    Copy-VerifiedFileTree -SourceRoot $verifiedAgentHost.Root `
        -DestinationRoot $publishRoot
    Assert-Same $verifiedAgentHost.Snapshot (Get-FilesSnapshot $publishRoot) `
        'AgentHost 已验证输出复制'
}
else {
    Invoke-Captured $dotnet @(
        'publish', (Join-Path $repoRoot 'src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj'),
        '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'false', '--no-restore',
        '--output', $publishRoot, '--nologo', '-m:1', '-p:BuildInParallel=false',
        '-p:UseSharedCompilation=false'
    ) 'AgentHost framework-dependent 发布'
}

$hostDll = $hostBuilds[0].Dll
$hostSha = $hostBuilds[0].Sha256
$agentExe = Join-Path $publishRoot 'Codex.AutoCAD.AgentHost.exe'
$agentSha = Get-Sha256 $agentExe
$agentDll = Join-Path $publishRoot 'Codex.AutoCAD.AgentHost.dll'
$agentDllSha = Get-Sha256 $agentDll
if ($isM4LiveCandidate -and
    ($hostSha -cne [string] $readiness.CandidateHashes.R201HostDllSha256 -or
     $agentDllSha -cne [string] $readiness.CandidateHashes.AgentHostDllSha256)) {
    throw 'M4 实机候选的 Host/AgentHost DLL 哈希与 readiness 不一致。'
}
$candidateDoctorWorkspace = Join-Path $stageRoot 'candidate-doctor-workspace'
New-Item -ItemType Directory -Path $candidateDoctorWorkspace -Force | Out-Null
$codexExecutable = Resolve-CodexExecutable $CodexExecutable
$candidateDoctorOutput = Invoke-CapturedOutput $agentExe @(
    'doctor', '--workspace', $candidateDoctorWorkspace, '--codex', $codexExecutable
) '候选 AgentHost doctor'
$candidateDoctor = @(
    foreach ($line in $candidateDoctorOutput) {
        if ($line.TrimStart().StartsWith('{')) {
            try { $line | ConvertFrom-Json } catch { }
        }
    }
) | Select-Object -Last 1
if ($null -eq $candidateDoctor -or -not $candidateDoctor.ok -or
    [string] $candidateDoctor.state -cne 'Running') {
    throw '候选 AgentHost doctor 未返回 ok=true/state=Running。'
}
if ($isM4LiveCandidate) {
    Assert-M4SourceState -ExpectedHeadCommit $sourceHeadCommit
}
$candidatePrefix = if ($isM4LiveCandidate) { 'autocad2016-m4-live' } else { 'autocad2016-m1-readonly' }
$candidateId = $candidatePrefix + '-' + $versionTag + '-' + $hostSha.Substring(0,8).ToLowerInvariant() + '-' + $agentDllSha.Substring(0,8).ToLowerInvariant() + '-' + $runId.Substring(0,8)
$candidateRoot = Join-Path $artifactsRoot $candidateId
if (Test-Path -LiteralPath $candidateRoot) { throw "候选目录已存在，拒绝覆盖：$candidateRoot" }
New-Item -ItemType Directory -Path $candidateRoot -Force | Out-Null

Copy-Item -LiteralPath $hostDll -Destination (Join-Path $candidateRoot 'Codex.AutoCAD.Host.2016.dll')
foreach ($name in @('Codex.AutoCAD.Contracts.dll','Codex.AutoCAD.Ipc.dll','Codex.AutoCAD.Bridge.Client.dll','Codex.AutoCAD.AgentLauncher.dll')) {
    $source = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File -Filter $name |
        Where-Object { $_.FullName -match '[\\/]bin[\\/]Release[\\/]net45[\\/]' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $source) { throw "找不到 net45 依赖产物：$name" }
    Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $candidateRoot $name)
}

$agentTarget = Join-Path $candidateRoot 'AgentHost'
New-Item -ItemType Directory -Path $agentTarget -Force | Out-Null
Copy-VerifiedFileTree -SourceRoot $publishRoot -DestinationRoot $agentTarget
Assert-Same (Get-FilesSnapshot $publishRoot) (Get-FilesSnapshot $agentTarget) `
    'AgentHost 候选目录复制'
[IO.File]::WriteAllText((Join-Path $agentTarget 'Codex.AutoCAD.AgentHost.exe.sha256'), $agentSha + "  Codex.AutoCAD.AgentHost.exe`n", (New-Object Text.UTF8Encoding($false)))

$files = [ordered]@{}
foreach ($file in @(Get-ChildItem -LiteralPath $candidateRoot -Recurse -File | Sort-Object FullName)) {
    if ($file.Name -ieq 'manifest.json') { continue }
    $relative = $file.FullName.Substring($candidateRoot.Length + 1).Replace('\','/')
    $files[$relative] = [ordered]@{ Length = $file.Length; Sha256 = Get-Sha256 $file.FullName }
}
$manifest = [ordered]@{
    schemaVersion = if ($isM4LiveCandidate) { 2 } else { 1 }
    candidateId = $candidateId
    hostVersion = $hostVersion
    cadContextSchema = 'codex.autocad.cad-context/2'
    targetApi = 'AutoCAD R20.1 / managed 20.1.0.0 / net45 / x64'
    agentHostMode = 'framework-dependent-net8-win-x64'
    files = $files
}
if ($isM4LiveCandidate) {
    $manifest['m4Binding'] = [ordered]@{
        sourceHeadCommit = $sourceHeadCommit
        sourceManifestFileCount = [int] $sourceManifest.FileCount
        sourceManifestSha256 = [string] $sourceManifest.Sha256
        bridgeClientLockSha256 = [string] $readiness.Source.BridgeClientLockSha256
        readinessRunCorrelationId = [string] $readiness.RunCorrelation.Id
        readinessEvidenceSha256 = $readinessSha256
        suiteEvidenceSha256 = $suiteEvidenceSha256
        agentBootstrapEvidenceSha256 =
            [string] $readiness.InputEvidence.AgentBootstrapSha256
        hostDllSha256 = $hostSha
        agentHostDllSha256 = $agentDllSha
        agentHostExecutableSha256 = $agentSha
        cadWriteEnabled = $false
        pluginInitiatedSaveEnabled = $false
    }
}
$manifestPath = Join-Path $candidateRoot 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 30) + "`n", (New-Object Text.UTF8Encoding($false)))

$evidenceScope = if ($isM4LiveCandidate) {
    'autocad2016-m4-live-candidate-build'
}
else {
    'autocad2016-m1-readonly-candidate-build'
}
$evidence = [ordered]@{
    schemaVersion = if ($isM4LiveCandidate) { 2 } else { 1 }
    scope = $evidenceScope
    sourceSnapshotAtUtc = $sourceSnapshotAtUtc
    candidateFrozenAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    candidateId = $candidateId
    hostVersion = $hostVersion
    cadContextSchema = 'codex.autocad.cad-context/2'
    autoCadLiveEvidence = $false
    netLoadVerified = $false
    build = [ordered]@{
        hostR20_1A_BBitForBitEqual = $true
        hostDllSha256 = $hostSha
        agentHostDllSha256 = $agentDllSha
        agentHostExeSha256 = $agentSha
    }
    candidate = [ordered]@{ root = ('artifacts/' + $candidateId); manifestSha256 = Get-Sha256 $manifestPath; files = $files }
    gates = [ordered]@{
        phase2Specs = $phase2Summary
        r20_1ReleaseX64Build = $true
        hostReadOnlySourceScan = $true
        hostReadOnlySourceCount = $hostReadOnlySourceCount
        candidateAgentHostDoctor = $true
        sensitiveScan = $true
        gitDiffCheck = $true
        AutoCADStartedOrRestarted = $false; commandsSent = $false
    }
    limitations = if ($isM4LiveCandidate) {
        @(
            '本证据覆盖同一次 readiness 绑定、干净提交、Phase 2、Host.2016 双构建、AgentHost doctor、完整候选布局和最终重哈希；未启动、重启或操作 AutoCAD。',
            '该候选只供 M4.15.3 真实异常退出矩阵使用；未形成 live-matrix-results.json 前 M4Complete 与 M416Frozen 均保持 false。',
            '候选保持 CAD 写入与插件保存禁用；本证据不创建回滚 ref、不替代实机矩阵，也不允许进入 M5。'
        )
    }
    else {
        @(
            '本证据覆盖 Phase 2 动态门禁、Host.2016 只读源码闭包、R20.1 net45/x64 双构建、候选布局和最终重哈希；未启动、重启或操作 AutoCAD。',
            'M1 候选仍必须由用户在 AutoCAD 2016 中人工 NETLOAD，验证新建对话、全部清除、图纸隔离、取消、超时、断线、退出和 DPI。',
            '整图 1k/10k/50k 扫描、DrawingIndex/CadQuery、CAD 写入、每会话 CODEX_HOME 与完整进程沙箱不属于本候选。'
        )
    }
}
if ($isM4LiveCandidate) {
    $evidence['source'] = [ordered]@{
        headCommit = $sourceHeadCommit
        workingTreeDirty = $false
        readinessRunCorrelationId = [string] $readiness.RunCorrelation.Id
        readinessEvidenceSha256 = $readinessSha256
        suiteEvidenceSha256 = $suiteEvidenceSha256
    }
}
New-Item -ItemType Directory -Path $effectiveEvidenceDirectory -Force | Out-Null
$evidenceFilePrefix = if ($isM4LiveCandidate) {
    'm4-live-candidate-build-'
}
else {
    'cad-context-v2-candidate-build-'
}
$evidencePath = Join-Path $effectiveEvidenceDirectory ($evidenceFilePrefix + $candidateId + '.json')
[IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 40) + "`n", (New-Object Text.UTF8Encoding($false)))

if ($isM4LiveCandidate) {
    Write-Host 'AutoCAD 2016 M4.15.3 实机异常退出候选打包通过。' -ForegroundColor Green
    Write-Host "SOURCE_HEAD=$sourceHeadCommit"
    Write-Host "READINESS_SHA256=$readinessSha256"
}
else {
    Write-Host 'AutoCAD 2016 M1 只读稳定化候选自动化冻结通过。' -ForegroundColor Green
}
Write-Host "CANDIDATE_ROOT=$candidateRoot"
Write-Host "CANDIDATE_ID=$candidateId"
Write-Host "HOST_VERSION=$hostVersion"
Write-Host "PHASE2_SPECS=$phase2Summary"
Write-Host "HOST_SHA256=$hostSha"
Write-Host "AGENTHOST_DLL_SHA256=$agentDllSha"
Write-Host "AGENTHOST_EXE_SHA256=$agentSha"
Write-Host "MANIFEST_SHA256=$(Get-Sha256 $manifestPath)"
Write-Host "EVIDENCE=$evidencePath"
Complete-CodexBuildSafety -State $buildSafety -Stage 'context-v2-candidate' | Out-Null

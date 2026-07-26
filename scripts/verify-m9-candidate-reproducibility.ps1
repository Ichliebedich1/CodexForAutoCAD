[CmdletBinding()]
param(
    [string] $CandidateRoot,
    [string] $EvidencePath,
    [switch] $SelfTestOnly
)

# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。
#
# M9.9：候选 manifest 的可复现性门禁。
#
# 当前 manifest 本来就是确定的——没有时间戳、机器名、用户名或路径，只有版本标识和
# 一张排序过的文件哈希表。所以本门禁不是去"修好"什么，而是**锁住这个性质**：明天
# 有人加一个 builtAt 或 buildMachine，相同提交就再也得不到相同 manifest，而那种回归
# 不会自己报错，只会让"可复现"这个说法悄悄变成假的。
#
# 三项检查：
#   1. manifest 里不得出现非确定字段（时间、机器、用户、路径、环境）。
#   2. 文件集合双向相等——磁盘上有而 manifest 没记，和 manifest 记了而磁盘没有，
#      同样是问题。只查一个方向会漏掉"候选目录里多出一个没人审过的文件"。
#   3. 每个文件的长度与 SHA-256 与 manifest 一致。
#   4. candidateId 必须包含 Host 与 AgentHost 哈希前缀，使目录名无法与内容脱节。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot

# 键名里出现这些词，几乎必然带来一次构建一个值。
# 按 camelCase / 下划线 / 连字符切词后**整词**比对，不做子串匹配：`candidateId` 里含
# "date"（can-di-date-Id），`validated` 同样含 "date"。子串规则会把它们全报成非确定
# 字段，而一个总在误报的规则最后一定会被关掉——那时它一条真的也拦不住。
$NonDeterministicKeyTokens = @(
    "time", "times", "timestamp", "date", "datetime", "stamp", "clock",
    "built", "buildtime", "started", "ended", "created", "modified", "generated", "recorded",
    "machine", "hostname", "computer", "user", "username", "account",
    "path", "paths", "directory", "folder", "environment", "env",
    "elapsed", "duration", "seed", "random", "guid", "uuid"
)

function Split-CodexKeyTokens {
    param([Parameter(Mandatory = $true)][string] $Key)
    $spaced = [regex]::Replace($Key, '([a-z0-9])([A-Z])', '$1 $2')
    $spaced = [regex]::Replace($spaced, '[_\-\.]+', ' ')
    return @($spaced.Split(' ') |
        Where-Object { $_.Length -gt 0 } |
        ForEach-Object { $_.ToLowerInvariant() })
}

function Test-CodexKeyIsNonDeterministic {
    param([Parameter(Mandatory = $true)][string] $Key)
    foreach ($token in (Split-CodexKeyTokens -Key $Key)) {
        if ($NonDeterministicKeyTokens -contains $token) {
            return $true
        }
    }

    return $false
}

function Test-CodexManifestDeterminism {
    <#
    .SYNOPSIS
        递归检查 manifest 的键名，返回可疑的非确定字段路径。
    .DESCRIPTION
        只看键名不看值：值可能这次恰好稳定，键名却已经宣告了意图。files 映射下的键是
        文件相对路径，属于内容本身，不适用该规则，因此调用方将其排除。
    #>
    param(
        [Parameter(Mandatory = $true)] $Node,
        [string] $Prefix = "",
        [string[]] $ExcludedSubtrees = @()
    )

    $findings = New-Object System.Collections.ArrayList
    if ($null -eq $Node -or $Node -isnot [psobject]) {
        return @($findings)
    }

    foreach ($property in $Node.PSObject.Properties) {
        $path = if ($Prefix.Length -eq 0) { $property.Name } else { $Prefix + "." + $property.Name }
        if ($ExcludedSubtrees -contains $path) {
            continue
        }
        if (Test-CodexKeyIsNonDeterministic -Key $property.Name) {
            $null = $findings.Add($path)
            continue
        }
        if ($property.Value -is [psobject] -and $property.Value -isnot [string]) {
            foreach ($nested in (Test-CodexManifestDeterminism -Node $property.Value `
                        -Prefix $path -ExcludedSubtrees $ExcludedSubtrees)) {
                $null = $findings.Add($nested)
            }
        }
    }

    return @($findings)
}

function Test-CodexCandidateIdBindsHashes {
    <#
    .SYNOPSIS
        候选 ID 必须包含 Host 与 AgentHost 哈希前缀，否则目录名可以与内容无关。
    #>
    param(
        [Parameter(Mandatory = $true)][string] $CandidateId,
        [Parameter(Mandatory = $true)][string] $HostSha256,
        [Parameter(Mandatory = $true)][string] $AgentHostSha256
    )

    $id = $CandidateId.ToLowerInvariant()
    $hostPrefix = $HostSha256.Substring(0, 8).ToLowerInvariant()
    $agentPrefix = $AgentHostSha256.Substring(0, 8).ToLowerInvariant()
    return ($id.Contains($hostPrefix) -and $id.Contains($agentPrefix))
}

if ($SelfTestOnly) {
    $clean = [pscustomobject]@{
        schemaVersion = 1
        candidateId = "autocad2016-x-aaaaaaaa-bbbbbbbb-cccccccc"
        hostVersion = "0.4.2.0"
        files = [pscustomobject]@{
            "AgentHost/x.dll" = [pscustomobject]@{ Length = 1; Sha256 = "AA" }
        }
    }
    $findings = @(Test-CodexManifestDeterminism -Node $clean -ExcludedSubtrees @("files"))
    if ($findings.Count -ne 0) {
        throw ("自检失败：确定的 manifest 被报出非确定字段：" + ($findings -join ", "))
    }

    # files 下的键是文件相对路径，含 AgentHost/ 之类的目录名，不得被规则误伤。
    $withPathKeys = [pscustomobject]@{
        files = [pscustomobject]@{
            "AgentHost/Codex.AutoCAD.AgentHost.exe" = [pscustomobject]@{ Length = 1; Sha256 = "AA" }
        }
    }
    if (@(Test-CodexManifestDeterminism -Node $withPathKeys -ExcludedSubtrees @("files")).Count -ne 0) {
        throw "自检失败：files 子树下的路径键被误判为非确定字段。"
    }

    foreach ($badKey in @("builtAt", "buildMachine", "userName", "outputPath", "elapsedMs", "buildGuid")) {
        $dirty = [pscustomobject]@{ schemaVersion = 1 }
        $dirty | Add-Member -NotePropertyName $badKey -NotePropertyValue "x"
        if (@(Test-CodexManifestDeterminism -Node $dirty -ExcludedSubtrees @("files")).Count -ne 1) {
            throw ("自检失败：非确定字段 " + $badKey + " 没有被发现。")
        }
    }

    # 嵌套层里的非确定字段同样要抓到。
    $nested = [pscustomobject]@{
        build = [pscustomobject]@{ startedAt = "2026-01-01" }
    }
    if (@(Test-CodexManifestDeterminism -Node $nested -ExcludedSubtrees @("files")).Count -ne 1) {
        throw "自检失败：嵌套层的非确定字段没有被发现。"
    }

    $hostSha = "BC5DA318" + ("0" * 56)
    $agentSha = "EF079C01" + ("0" * 56)
    if (-not (Test-CodexCandidateIdBindsHashes `
            -CandidateId "autocad2016-m1-readonly-v042-bc5da318-ef079c01-31233295" `
            -HostSha256 $hostSha -AgentHostSha256 $agentSha)) {
        throw "自检失败：合法候选 ID 未被识别为绑定哈希。"
    }
    if (Test-CodexCandidateIdBindsHashes -CandidateId "autocad2016-something-else" `
            -HostSha256 $hostSha -AgentHostSha256 $agentSha) {
        throw "自检失败：与内容无关的候选 ID 被接受。"
    }
    # 只带其中一个前缀也不算绑定。
    if (Test-CodexCandidateIdBindsHashes -CandidateId "autocad2016-v042-bc5da318" `
            -HostSha256 $hostSha -AgentHostSha256 $agentSha) {
        throw "自检失败：只包含 Host 前缀的候选 ID 被接受。"
    }

    Write-Host "M9_CANDIDATE_REPRODUCIBILITY_SELF_TEST=passed"
    return
}

if ([string]::IsNullOrWhiteSpace($CandidateRoot)) {
    throw "缺少 -CandidateRoot。"
}
if (-not (Test-Path -LiteralPath $CandidateRoot -PathType Container)) {
    throw "候选目录不存在。"
}
$manifestPath = Join-Path $CandidateRoot "manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "候选目录缺少 manifest.json。"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$problems = New-Object System.Collections.ArrayList

foreach ($finding in (Test-CodexManifestDeterminism -Node $manifest -ExcludedSubtrees @("files"))) {
    $null = $problems.Add("manifest 含非确定字段：$finding")
}

if (-not ($manifest.PSObject.Properties.Name -contains "files")) {
    $null = $problems.Add("manifest 缺少 files 映射。")
}
else {
    $manifestFiles = @{}
    foreach ($property in $manifest.files.PSObject.Properties) {
        $manifestFiles[$property.Name.Replace("/", "\")] = $property.Value
    }

    $diskFiles = @(Get-ChildItem -LiteralPath $CandidateRoot -Recurse -File |
        Where-Object { $_.Name -cne "manifest.json" })
    $diskRelative = @{}
    foreach ($file in $diskFiles) {
        $relative = $file.FullName.Substring($CandidateRoot.Length).TrimStart("\")
        $diskRelative[$relative] = $file
    }

    # 方向一：manifest 记了但磁盘没有 —— 候选不完整。
    foreach ($key in ($manifestFiles.Keys | Sort-Object)) {
        if (-not $diskRelative.ContainsKey($key)) {
            $null = $problems.Add("manifest 记录的文件在候选目录中不存在：$key")
            continue
        }
        $file = $diskRelative[$key]
        $entry = $manifestFiles[$key]
        if ([long] $entry.Length -ne $file.Length) {
            $null = $problems.Add("文件长度与 manifest 不一致：$key")
        }
        $actual = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actual -cne ([string] $entry.Sha256).ToUpperInvariant()) {
            $null = $problems.Add("文件 SHA-256 与 manifest 不一致：$key")
        }
    }

    # 方向二：磁盘上有但 manifest 没记 —— 候选目录里躺着一份没人审过的文件。
    foreach ($key in ($diskRelative.Keys | Sort-Object)) {
        if (-not $manifestFiles.ContainsKey($key)) {
            $null = $problems.Add("候选目录中存在 manifest 未记录的文件：$key")
        }
    }
}

$hostEntryKey = "Codex.AutoCAD.Host.2016.dll"
$agentEntryKey = "AgentHost/Codex.AutoCAD.AgentHost.exe"
if (($manifest.PSObject.Properties.Name -contains "files") -and
    ($manifest.files.PSObject.Properties.Name -contains $hostEntryKey) -and
    ($manifest.files.PSObject.Properties.Name -contains $agentEntryKey) -and
    ($manifest.PSObject.Properties.Name -contains "candidateId")) {
    $bound = Test-CodexCandidateIdBindsHashes `
        -CandidateId ([string] $manifest.candidateId) `
        -HostSha256 ([string] $manifest.files.$hostEntryKey.Sha256) `
        -AgentHostSha256 ([string] $manifest.files.$agentEntryKey.Sha256)
    if (-not $bound) {
        $null = $problems.Add("candidateId 未包含 Host 与 AgentHost 哈希前缀，目录名可与内容脱节。")
    }
}
else {
    $null = $problems.Add("manifest 缺少 candidateId 或 Host/AgentHost 条目，无法绑定身份。")
}

$evidence = [ordered]@{
    SchemaVersion = 1
    RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    RunCorrelationId = Get-CodexGateRunCorrelationId
    Scope = "m9-9-candidate-reproducibility-gate"
    CandidateId = if ($manifest.PSObject.Properties.Name -contains "candidateId") {
        [string] $manifest.candidateId
    }
    else {
        ""
    }
    ManifestFileCount = if ($manifest.PSObject.Properties.Name -contains "files") {
        @($manifest.files.PSObject.Properties).Count
    }
    else {
        0
    }
    ProblemCount = $problems.Count
    Problems = @($problems)
    EvidenceBoundary = "This gate proves the candidate manifest carries no non-deterministic fields, that its file set matches the candidate directory in both directions with matching lengths and hashes, and that the candidate id embeds the Host and AgentHost hash prefixes. It does NOT rebuild the candidate, so it does not by itself prove that the same commit produces the same bytes - the R20.1 A/B gate covers that for the Host. It does not start or command AutoCAD."
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) {
        [IO.Path]::GetFullPath($EvidencePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidencePath))
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) -Force | Out-Null
    [IO.File]::WriteAllText($resolvedEvidencePath, ($evidence | ConvertTo-Json -Depth 8),
        (New-Object Text.UTF8Encoding($false)))
    Write-Host ("M9_CANDIDATE_REPRODUCIBILITY_EVIDENCE=" + $resolvedEvidencePath)
}

Complete-CodexBuildSafety -State $buildSafety -Stage "m9-9-candidate-reproducibility" | Out-Null

Write-Host ("M9_CANDIDATE_MANIFEST_FILES=" + $evidence.ManifestFileCount)

if ($problems.Count -ne 0) {
    Write-Host "`nM9.9 候选可复现性门禁未通过，共 $($problems.Count) 项：" -ForegroundColor Yellow
    foreach ($problem in $problems) {
        Write-Host ("  - " + $problem)
    }
    Write-Host "M9_CANDIDATE_REPRODUCIBILITY=failed"
    exit 1
}

Write-Host "`nM9.9 候选可复现性门禁通过；本门禁不重建候选，位级可复现由 R20.1 A/B 门禁负责。" -ForegroundColor Green
Write-Host "M9_CANDIDATE_REPRODUCIBILITY=passed"

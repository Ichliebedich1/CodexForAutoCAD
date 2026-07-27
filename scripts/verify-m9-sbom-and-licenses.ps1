[CmdletBinding()]
param(
    [string] $EvidencePath,
    [switch] $SelfTestOnly,
    [ValidateRange(0, 40)]
    [double] $MinimumFreeGiB = 40
)

# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。
#
# M9.8 的 SBOM 与许可证部分。秘密扫描和禁用 API 已在 verify-phase2.ps1 中，本脚本补上
# 缺的两项：可复现的依赖清单，以及 fail-closed 的许可证判定。
#
# 供应链边界：本仓库的 NuGet.Config 使用 <clear /> + 单一离线 feed +
# signatureValidationMode=require，因此「解析出的包」应当与「feed 里的包」严格相等。
# 两个方向都要查：
#   - 锁文件引用了 feed 里没有的包 -> 构建会去别处取，离线边界已破；
#   - feed 里有没被任何锁文件引用的包 -> 仓库里躺着一份没人审过的二进制。
# 只查一个方向的门禁会漏掉后者，而后者正是供应链攻击最省事的落脚点。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot `
    -MinimumFreeGiB $MinimumFreeGiB
$feedRoot = Join-Path $repoRoot "third_party\nuget"

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
}
catch {
    # PowerShell 7 已内建该程序集；5.1 需要显式加载。两种情况都不该中断门禁。
}

# 许可证策略。SPDX 表达式按标识符判定；licenseUrl 是 NuGet 的历史形式，URL 本身不是
# 许可证标识，无法从中推断条款，因此只接受逐条人工复核过的精确 URL。
$AllowedLicenseExpressions = @("MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "MS-PL")
$AllowedLicenseUrls = @{
    "https://github.com/Microsoft/dotnet/blob/master/LICENSE" =
        "MIT；Microsoft .NET 参考程序集，2026-07-26 人工复核"
}

function Get-NuspecMetadata {
    param([Parameter(Mandatory = $true)][string] $PackagePath)

    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        # 只取包根目录下的 .nuspec：包内其他位置的同名文件不是包身份来源。
        $entry = $archive.Entries |
            Where-Object { $_.FullName -like "*.nuspec" -and $_.FullName -notlike "*/*" } |
            Select-Object -First 1
        if ($null -eq $entry) {
            throw "包内没有根级 .nuspec：$([IO.Path]::GetFileName($PackagePath))"
        }
        $reader = New-Object IO.StreamReader($entry.Open())
        try {
            $nuspecText = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $xml = [xml] $nuspecText
    $metadata = $xml.package.metadata

    $licenseKind = "none"
    $licenseValue = ""
    if ($metadata.PSObject.Properties.Name -contains "license" -and $null -ne $metadata.license) {
        $license = $metadata.license
        if ($license -is [string]) {
            # 没有 type 属性时 NuGet 按 expression 处理。
            $licenseKind = "expression"
            $licenseValue = [string] $license
        }
        else {
            $licenseKind = [string] $license.type
            $licenseValue = [string] $license.InnerText
        }
    }
    elseif ($metadata.PSObject.Properties.Name -contains "licenseUrl" -and
        -not [string]::IsNullOrWhiteSpace([string] $metadata.licenseUrl)) {
        $licenseKind = "url"
        $licenseValue = ([string] $metadata.licenseUrl).Trim()
    }

    return [pscustomobject]@{
        Id = [string] $metadata.id
        Version = [string] $metadata.version
        Authors = [string] $metadata.authors
        LicenseKind = $licenseKind
        LicenseValue = $licenseValue
    }
}

function Resolve-LicenseDecision {
    <#
    .SYNOPSIS
        判定单个包的许可证是否可接受。未知一律拒绝。
    #>
    param([Parameter(Mandatory = $true)] $Metadata)

    switch ($Metadata.LicenseKind) {
        "expression" {
            # SPDX 复合表达式（OR/AND/WITH）需要人工判断，不做自动拆解。
            if ($Metadata.LicenseValue -match "\s(OR|AND|WITH)\s") {
                return [pscustomobject]@{
                    Allowed = $false
                    Reason = "复合 SPDX 表达式需人工复核"
                }
            }
            if ($AllowedLicenseExpressions -ccontains $Metadata.LicenseValue) {
                return [pscustomobject]@{ Allowed = $true; Reason = "SPDX 允许清单" }
            }
            return [pscustomobject]@{ Allowed = $false; Reason = "SPDX 标识不在允许清单内" }
        }
        "url" {
            if ($AllowedLicenseUrls.ContainsKey($Metadata.LicenseValue)) {
                return [pscustomobject]@{
                    Allowed = $true
                    Reason = $AllowedLicenseUrls[$Metadata.LicenseValue]
                }
            }
            return [pscustomobject]@{ Allowed = $false; Reason = "licenseUrl 未经人工复核" }
        }
        "file" {
            # 包内许可证文件必须有人读过才能判定，门禁不猜。
            return [pscustomobject]@{ Allowed = $false; Reason = "包内许可证文件需人工提取" }
        }
        default {
            return [pscustomobject]@{ Allowed = $false; Reason = "缺少许可证声明" }
        }
    }
}

function Get-LockedComponents {
    param([Parameter(Mandatory = $true)][string] $RepoRoot)

    $components = New-Object System.Collections.ArrayList
    $lockFiles = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Filter "packages.lock.json" |
        Sort-Object FullName)
    foreach ($lockFile in $lockFiles) {
        $relative = $lockFile.FullName.Substring($RepoRoot.Length).TrimStart("\")
        $lockJson = Get-Content -LiteralPath $lockFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($framework in $lockJson.dependencies.PSObject.Properties) {
            foreach ($package in $framework.Value.PSObject.Properties) {
                $entry = $package.Value
                $type = if ($entry.PSObject.Properties.Name -contains "type") {
                    [string] $entry.type
                }
                else {
                    "Unknown"
                }
                $resolved = if ($entry.PSObject.Properties.Name -contains "resolved") {
                    [string] $entry.resolved
                }
                else {
                    ""
                }
                $contentHash = if ($entry.PSObject.Properties.Name -contains "contentHash") {
                    [string] $entry.contentHash
                }
                else {
                    ""
                }
                $null = $components.Add([pscustomobject]@{
                    Id = $package.Name
                    Type = $type
                    TargetFramework = $framework.Name
                    ResolvedVersion = $resolved
                    ContentHash = $contentHash
                    LockFile = $relative
                })
            }
        }
    }
    return ,@($components)
}

if ($SelfTestOnly) {
    function New-LicenseProbe {
        param([string] $Kind, [string] $Value)
        return [pscustomobject]@{ LicenseKind = $Kind; LicenseValue = $Value }
    }

    if (-not (Resolve-LicenseDecision (New-LicenseProbe "expression" "MIT")).Allowed) {
        throw "自检失败：允许清单内的 SPDX 标识被拒绝。"
    }
    foreach ($rejected in @(
            (New-LicenseProbe "expression" "GPL-3.0-only"),
            (New-LicenseProbe "expression" "MIT OR Apache-2.0"),
            (New-LicenseProbe "url" "https://example.invalid/license"),
            (New-LicenseProbe "file" "LICENSE.txt"),
            (New-LicenseProbe "none" ""))) {
        if ((Resolve-LicenseDecision $rejected).Allowed) {
            throw ("自检失败：未复核的许可证被接受：" + $rejected.LicenseKind + " " + $rejected.LicenseValue)
        }
    }
    $reviewedUrlProbe = New-LicenseProbe "url" "https://github.com/Microsoft/dotnet/blob/master/LICENSE"
    if (-not (Resolve-LicenseDecision $reviewedUrlProbe).Allowed) {
        throw "自检失败：已人工复核的 licenseUrl 被拒绝。"
    }
    # 大小写不同的 SPDX 标识是不同标识，不能被当成同一个。
    if ((Resolve-LicenseDecision (New-LicenseProbe "expression" "mit")).Allowed) {
        throw "自检失败：SPDX 标识按大小写不敏感匹配。"
    }

    Write-Host "M9_SBOM_LICENSE_SELF_TEST=passed"
    return
}

$components = Get-LockedComponents -RepoRoot $repoRoot
$externalComponents = @($components | Where-Object { $_.Type -cne "Project" })
$internalComponents = @($components | Where-Object { $_.Type -ceq "Project" })

# 锁文件里同一个包可能出现在多个项目/框架下；按 id 归一，版本冲突要显式暴露。
$externalById = @{}
foreach ($component in $externalComponents) {
    $key = $component.Id.ToLowerInvariant()
    if (-not $externalById.ContainsKey($key)) {
        $externalById[$key] = New-Object System.Collections.ArrayList
    }
    $null = $externalById[$key].Add($component)
}

$feedPackages = @()
if (Test-Path -LiteralPath $feedRoot -PathType Container) {
    $feedPackages = @(Get-ChildItem -LiteralPath $feedRoot -File -Filter "*.nupkg" | Sort-Object Name)
}

$sbomEntries = New-Object System.Collections.ArrayList
$violations = New-Object System.Collections.ArrayList
$feedIds = New-Object System.Collections.Generic.HashSet[string]

foreach ($package in $feedPackages) {
    $metadata = Get-NuspecMetadata -PackagePath $package.FullName
    $decision = Resolve-LicenseDecision $metadata
    $identityKey = ($metadata.Id + "/" + $metadata.Version).ToLowerInvariant()
    $null = $feedIds.Add($identityKey)

    if (-not $decision.Allowed) {
        $null = $violations.Add(
            "许可证未通过：$($metadata.Id) $($metadata.Version) —— $($decision.Reason)")
    }

    # 文件名可以随便改，包身份只以 .nuspec 为准；两者不一致本身就是信号。
    $expectedName = $metadata.Id + "." + $metadata.Version + ".nupkg"
    if ($package.Name -cne $expectedName) {
        $null = $violations.Add(
            "feed 文件名与包身份不一致：$($package.Name) 对应 $expectedName")
    }

    $null = $sbomEntries.Add([ordered]@{
        Id = $metadata.Id
        Version = $metadata.Version
        Authors = $metadata.Authors
        LicenseKind = $metadata.LicenseKind
        LicenseValue = $metadata.LicenseValue
        LicenseAllowed = $decision.Allowed
        LicenseDecision = $decision.Reason
        FileName = $package.Name
        FileSize = $package.Length
        Sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    })
}

# 方向一：锁文件解析出的外部包必须在 feed 里。
foreach ($key in ($externalById.Keys | Sort-Object)) {
    $instances = @($externalById[$key])
    $versions = @($instances | ForEach-Object { $_.ResolvedVersion } | Sort-Object -Unique)
    if ($versions.Count -gt 1) {
        $null = $violations.Add(
            "同一外部包解析出多个版本：$key —— " + ($versions -join "、"))
    }
    foreach ($version in $versions) {
        if ([string]::IsNullOrWhiteSpace($version)) {
            $null = $violations.Add("锁文件中的外部包缺少 resolved 版本：$key")
            continue
        }
        if (-not $feedIds.Contains(($key + "/" + $version).ToLowerInvariant())) {
            $null = $violations.Add(
                "锁文件引用了离线 feed 中不存在的包：$key $version")
        }
    }
}

# 方向二：feed 里的每个包都必须被某个锁文件引用。
foreach ($entry in $sbomEntries) {
    $key = ($entry.Id + "/" + $entry.Version).ToLowerInvariant()
    $referenced = $false
    foreach ($component in $externalComponents) {
        if ((($component.Id + "/" + $component.ResolvedVersion).ToLowerInvariant()) -ceq $key) {
            $referenced = $true
            break
        }
    }
    if (-not $referenced) {
        $null = $violations.Add(
            "离线 feed 中存在没有任何锁文件引用的包：$($entry.Id) $($entry.Version)")
    }
}

$sbom = [ordered]@{
    SchemaVersion = 1
    RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
    RunCorrelationId = Get-CodexGateRunCorrelationId
    Scope = "m9-8-sbom-and-license-gate"
    Format = "codex-autocad-sbom/1"
    FormatNote = "Purpose-built minimal SBOM. It is deliberately NOT claimed to be CycloneDX or SPDX conformant, because no conformance validation is run here."
    ExternalComponentCount = $sbomEntries.Count
    InternalProjectReferenceCount = $internalComponents.Count
    LockFileCount = @($components | ForEach-Object { $_.LockFile } | Sort-Object -Unique).Count
    ExternalComponents = @($sbomEntries)
    InternalProjects = @($internalComponents |
        ForEach-Object { $_.Id } | Sort-Object -Unique)
    ViolationCount = $violations.Count
    Violations = @($violations)
    ScanBoundary = "This gate reads packages.lock.json files and the offline NuGet feed only. It resolves nothing over the network, does not query any vulnerability database, and does not inspect assembly IL. An empty violation list means the locked graph and the feed agree and every feed licence was previously reviewed - not that the dependencies are free of known vulnerabilities."
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = if ([IO.Path]::IsPathRooted($EvidencePath)) {
        [IO.Path]::GetFullPath($EvidencePath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidencePath))
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidencePath) -Force | Out-Null
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($resolvedEvidencePath, ($sbom | ConvertTo-Json -Depth 10), $encoding)
    Write-Host ("M9_SBOM_EVIDENCE=" + $resolvedEvidencePath)
}

Complete-CodexBuildSafety -State $buildSafety -Stage "m9-8-sbom-and-licenses" | Out-Null

Write-Host ("M9_SBOM_EXTERNAL_COMPONENTS=" + $sbomEntries.Count)
Write-Host ("M9_SBOM_INTERNAL_PROJECTS=" + @($internalComponents | ForEach-Object { $_.Id } | Sort-Object -Unique).Count)
Write-Host ("M9_SBOM_LOCK_FILES=" + @($components | ForEach-Object { $_.LockFile } | Sort-Object -Unique).Count)

if ($violations.Count -ne 0) {
    Write-Host "`nM9.8 供应链门禁未通过，共 $($violations.Count) 项：" -ForegroundColor Yellow
    foreach ($violation in $violations) {
        Write-Host ("  - " + $violation)
    }
    Write-Host "M9_SBOM_LICENSE_GATE=failed"
    exit 1
}

Write-Host "`nM9.8 SBOM 与许可证门禁通过；未做漏洞库查询或 IL 审查。" -ForegroundColor Green
Write-Host "M9_SBOM_LICENSE_GATE=passed"

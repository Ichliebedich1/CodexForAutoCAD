[CmdletBinding()]
param(
    [string] $EvidencePath,
    [switch] $SelfTestOnly,
    [ValidateRange(0, 40)]
    [double] $MinimumFreeGiB = 40
)

# 本文件必须保存为 UTF-8 with BOM，原因见 build-safety.ps1 顶部说明。
#
# M9.1 的本地可信检查器。它验证 GitHub Actions 工作流只运行无凭据、无 AutoCAD、
# 无本机 Codex 依赖的托管核心门禁，并把第三方 Action 锁定到人工复核过的精确提交。
# 它不声称 GitHub 托管 Runner 已经实际执行；真正的远端 run 仍需在推送后单独验收。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $PSScriptRoot "build-safety.ps1")
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot `
    -MinimumFreeGiB $MinimumFreeGiB
$workflowPath = Join-Path $repoRoot ".github\workflows\windows-core.yml"
$globalJsonPath = Join-Path $repoRoot "global.json"
$phase2Path = Join-Path $repoRoot "scripts\verify-phase2.ps1"
$toolchainPath = Join-Path $repoRoot "scripts\verify-m9-toolchain-lock.ps1"
$net45X64Path = Join-Path $repoRoot "scripts\verify-m9-net45-x64.ps1"
$allGatesPath = Join-Path $repoRoot "scripts\verify-all-gates.ps1"

$expectedActions = [ordered]@{
    "actions/checkout" = "11bd71901bbe5b1630ceea73d27597364c9af683" # v4.2.2
    "actions/setup-dotnet" = "67a3573c9a986a3f9c594539f4ab511d57bb3ce9" # v4.3.1
}

$expectedRunCommands = @(
    '.\scripts\verify-m9-windows-ci.ps1 -SelfTestOnly -MinimumFreeGiB 5',
    '.\scripts\verify-build-safety.ps1 -ArtifactBase $env:CODEX_AUTOCAD_ARTIFACT_BASE -MinimumFreeGiB 5',
    '.\scripts\verify-m9-toolchain-lock.ps1 -SkipR201BinaryProbe -MinimumFreeGiB 5',
    '.\scripts\verify-phase2.ps1 -Configuration Release -SkipLiveCodexHandshake -MinimumFreeGiB 5',
    '.\scripts\verify-m9-net45-x64.ps1 -Configuration Release -MinimumFreeGiB 5',
    '.\scripts\verify-m9-sbom-and-licenses.ps1 -MinimumFreeGiB 5'
)

$forbiddenPatterns = [ordered]@{
    "pull_request_target" = "(?im)^\s*pull_request_target\s*:"
    "工作流秘密" = "(?i)\bsecrets\b"
    "写权限" = "(?im)^\s*(?:contents|actions|checks|deployments|id-token|issues|packages|pull-requests|statuses)\s*:\s*write\s*$"
    "权限别名" = "(?im)^\s*permissions\s*:\s*(?:read-all|write-all)\s*$"
    "持久化 Git 凭据" = "(?im)^\s*persist-credentials\s*:\s*true\s*$"
    "可移动 Action 标签" = "(?im)^\s*(?:-\s*)?uses\s*:\s*[^@\s]+@(?:v\d+|main|master|latest)\s*(?:#.*)?$"
    "条件跳过" = "(?im)^\s*(?:if|continue-on-error)\s*:"
    "工作目录覆盖" = "(?im)^\s*working-directory\s*:"
    "PATH 覆盖" = "(?im)^\s+(?:PATH|Path)\s*:"
    "PATH 永久修改" = "(?i)\bsetx(?:\.exe)?\b[^\r\n]*\bpath\b"
    "任意表达式执行" = "(?i)(?:\bInvoke-Expression\b|\biex\b)"
    "Codex 用户目录" = "(?i)\bCODEX_HOME\b"
    "AutoCAD 启动" = "(?i)(?:\bacad(?:\.exe)?\b|\bNETLOAD\b|\baccoreconsole(?:\.exe)?\b)"
}

function Assert-ExactWorkflowShape {
    param([Parameter(Mandatory = $true)][string] $Workflow)

    $lines = @($Workflow -split "\r?\n")
    $permissionIndexes = @(
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -ceq "permissions:") {
                $index
            }
        }
    )
    if ($permissionIndexes.Count -ne 1) {
        throw "M9 Windows CI 必须且只能声明一个顶层 permissions 块。"
    }
    $permissionIndex = [int] $permissionIndexes[0]
    if (($permissionIndex + 1) -ge $lines.Count -or
        $lines[$permissionIndex + 1] -cne "  contents: read" -or
        (($permissionIndex + 2) -lt $lines.Count -and
            $lines[$permissionIndex + 2] -match "^\s+\S")) {
        throw "M9 Windows CI 权限块必须精确为 contents: read。"
    }
    if (@($lines | Where-Object { $_ -match "^\s+permissions\s*:" }).Count -ne 0) {
        throw "M9 Windows CI 禁止 job 或 step 级 permissions 覆盖。"
    }

    $jobsIndex = [Array]::IndexOf($lines, "jobs:")
    if ($jobsIndex -lt 0 -or ($jobsIndex + 1) -ge $lines.Count) {
        throw "M9 Windows CI 缺少 jobs 块。"
    }
    $jobsSection = ($lines[($jobsIndex + 1)..($lines.Count - 1)] -join "`n")
    $jobMatches = [regex]::Matches(
        $jobsSection,
        "(?m)^  (?<Name>[A-Za-z0-9_-]+):\s*$")
    if ($jobMatches.Count -ne 1 -or
        $jobMatches[0].Groups["Name"].Value -cne "managed-core") {
        throw "M9 Windows CI 必须且只能包含 managed-core job。"
    }

    $runLines = @(
        $lines | Where-Object { $_ -match "^\s+(?:-\s*)?run\s*:" } |
            ForEach-Object {
                ($_ -replace "^\s+(?:-\s*)?run\s*:\s*", "").TrimEnd()
            }
    )
    if ($runLines.Count -ne $expectedRunCommands.Count) {
        throw "M9 Windows CI run 步骤数量不匹配。"
    }
    foreach ($expectedCommand in $expectedRunCommands) {
        if (@($runLines | Where-Object { $_ -ceq $expectedCommand }).Count -ne 1) {
            throw "M9 Windows CI 缺少或修改了批准的 run 命令。"
        }
    }

    $matrixShellLines = @(
        $lines | Where-Object {
            $_.Trim() -ceq 'shell: ${{ matrix.shell }}'
        }
    )
    $allShellLines = @($lines | Where-Object { $_ -match "^\s+shell\s*:" })
    if ($matrixShellLines.Count -ne $expectedRunCommands.Count -or
        $allShellLines.Count -ne ($expectedRunCommands.Count + 1) -or
        @($lines | Where-Object {
                $_.Trim() -ceq "shell: [pwsh, powershell]"
            }).Count -ne 1) {
        throw "M9 Windows CI 每个 run 必须且只能由双 Shell 矩阵驱动。"
    }

    $artifactAssignments = @(
        $lines | Where-Object {
            $_ -match "^\s+CODEX_AUTOCAD_ARTIFACT_BASE\s*:"
        }
    )
    if ($artifactAssignments.Count -ne 1 -or
        $artifactAssignments[0].Trim() -cne
            'CODEX_AUTOCAD_ARTIFACT_BASE: ${{ runner.temp }}\cfa-artifacts') {
        throw "M9 Windows CI 产物根必须且只能绑定 runner.temp。"
    }

    $envIndexes = @(
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -ceq "    env:") {
                $index
            }
        }
    )
    $expectedEnvLines = @(
        "      DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: '0'",
        "      DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1'",
        "      DOTNET_CLI_TELEMETRY_OPTOUT: '1'",
        "      DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false'",
        '      CODEX_AUTOCAD_ARTIFACT_BASE: ${{ runner.temp }}\cfa-artifacts'
    )
    if ($envIndexes.Count -ne 1) {
        throw "M9 Windows CI 必须且只能包含一个批准的 job env 块。"
    }
    $envIndex = [int] $envIndexes[0]
    for ($offset = 0; $offset -lt $expectedEnvLines.Count; $offset++) {
        if (($envIndex + 1 + $offset) -ge $lines.Count -or
            $lines[$envIndex + 1 + $offset] -cne $expectedEnvLines[$offset]) {
            throw "M9 Windows CI job env 块包含未批准变量或顺序变化。"
        }
    }
    if (($envIndex + 1 + $expectedEnvLines.Count) -ge $lines.Count -or
        -not [string]::IsNullOrWhiteSpace(
            $lines[$envIndex + 1 + $expectedEnvLines.Count])) {
        throw "M9 Windows CI job env 块必须在批准变量后结束。"
    }
}

function Assert-RegexMatch {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Pattern,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if (-not [regex]::IsMatch(
            $Text,
            $Pattern,
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw $Message
    }
}

function Read-StrictUtf8Text {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][int] $MaximumBytes
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少 M9 Windows CI 工作流。"
    }

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 2 -or $bytes.Length -gt $MaximumBytes) {
        throw "M9 Windows CI 工作流大小不在允许范围内。"
    }

    $utf8 = New-Object Text.UTF8Encoding($false, $true)
    try {
        $text = $utf8.GetString($bytes)
    }
    catch {
        throw "M9 Windows CI 工作流不是严格 UTF-8。"
    }
    if ($text.Length -gt 0 -and $text[0] -eq [char] 0xFEFF) {
        $text = $text.Substring(1)
    }
    return $text
}

function Assert-WorkflowSecurityRules {
    param(
        [Parameter(Mandatory = $true)][string] $Workflow,
        [Parameter(Mandatory = $true)][string] $AllGatesScript
    )

    foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
        if ([regex]::IsMatch(
                $Workflow,
                [string] $entry.Value,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            throw "M9 Windows CI 工作流命中禁止项：$($entry.Key)。"
        }
    }

    Assert-ExactWorkflowShape -Workflow $Workflow
    Assert-RegexMatch $Workflow "(?im)^\s*persist-credentials\s*:\s*false\s*$" `
        "checkout 必须禁用持久化凭据。"

    if ([regex]::IsMatch(
            $AllGatesScript,
            "(?i)\bSkipLiveCodexHandshake\b",
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw "统一正式门禁禁止使用 SkipLiveCodexHandshake。"
    }

    $allUses = [regex]::Matches($Workflow, "(?im)^\s*(?:-\s*)?uses\s*:")
    $actionMatches = [regex]::Matches(
        $Workflow,
        "(?im)^\s*(?:-\s*)?uses\s*:\s*(?<Action>[^@\s]+)@(?<Ref>[0-9a-f]{40})\s*(?:#.*)?$")
    if ($allUses.Count -ne $expectedActions.Count -or
        $actionMatches.Count -ne $expectedActions.Count) {
        throw "M9 Windows CI 第三方 Action 数量不匹配。"
    }
    foreach ($match in $actionMatches) {
        $action = [string] $match.Groups["Action"].Value
        $reference = [string] $match.Groups["Ref"].Value
        if (-not $expectedActions.Contains($action) -or
            [string] $expectedActions[$action] -cne $reference) {
            throw "M9 Windows CI 引用了未冻结的第三方 Action。"
        }
    }
}

function Replace-RequiredText {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $OldValue,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $NewValue
    )

    $changed = $Text.Replace($OldValue, $NewValue)
    if ($changed -ceq $Text) {
        throw "M9 Windows CI 自检无法构造预期变异。"
    }
    return $changed
}

function Assert-SecurityMutationRejected {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Workflow,
        [Parameter(Mandatory = $true)][string] $AllGatesScript
    )

    try {
        Assert-WorkflowSecurityRules -Workflow $Workflow -AllGatesScript $AllGatesScript
    }
    catch {
        return
    }
    throw "M9 Windows CI 自检失败，危险变异未被拒绝：$Name。"
}

function Invoke-WorkflowSecuritySelfTest {
    param(
        [Parameter(Mandatory = $true)][string] $Workflow,
        [Parameter(Mandatory = $true)][string] $AllGatesScript
    )

    $mutations = @(
        [pscustomobject]@{
            Name = "contents-write"
            Workflow = Replace-RequiredText $Workflow "contents: read" "contents: write"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "movable-action-tag"
            Workflow = Replace-RequiredText $Workflow `
                "actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683" `
                "actions/checkout@v4"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "persist-credentials"
            Workflow = Replace-RequiredText $Workflow `
                "persist-credentials: false" `
                "persist-credentials: true"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "pull-request-target"
            Workflow = $Workflow + "`r`npull_request_target:`r`n"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "system-drive-artifacts"
            Workflow = Replace-RequiredText $Workflow `
                '${{ runner.temp }}\cfa-artifacts' `
                'C:\cfa-artifacts'
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "missing-ci-only-boundary"
            Workflow = Replace-RequiredText $Workflow `
                " -SkipLiveCodexHandshake" `
                ""
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "formal-gate-skips-doctor"
            Workflow = $Workflow
            AllGates = $AllGatesScript + "`r`n# SkipLiveCodexHandshake`r`n"
        },
        [pscustomobject]@{
            Name = "extra-read-permission"
            Workflow = Replace-RequiredText $Workflow `
                "  contents: read" `
                "  contents: read`r`n  issues: read"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "job-write-all"
            Workflow = Replace-RequiredText $Workflow `
                '    name: Managed core (${{ matrix.shell }})' `
                "    permissions: write-all`r`n    name: Managed core (`${{ matrix.shell }})"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "bracket-secret"
            Workflow = Replace-RequiredText $Workflow `
                "      DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: '0'" `
                "      CI_TOKEN: `${{ secrets['CI_TOKEN'] }}`r`n      DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: '0'"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "local-action"
            Workflow = Replace-RequiredText $Workflow `
                "      - name: Validate workflow safety contract" `
                "      - uses: ./unsafe-local-action`r`n`r`n      - name: Validate workflow safety contract"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "missing-step-shell"
            Workflow = Replace-RequiredText $Workflow `
                'shell: ${{ matrix.shell }}' `
                "shell: pwsh"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "continue-on-error"
            Workflow = Replace-RequiredText $Workflow `
                "      - name: Validate build environment safety" `
                "      - name: Validate build environment safety`r`n        continue-on-error: true"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "altered-run-command"
            Workflow = Replace-RequiredText $Workflow `
                ".\scripts\verify-m9-sbom-and-licenses.ps1 -MinimumFreeGiB 5" `
                ".\scripts\verify-m9-sbom-and-licenses.ps1 -MinimumFreeGiB 5; exit 0"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "extra-job"
            Workflow = $Workflow + `
                "`r`n  bypass:`r`n    runs-on: windows-2022`r`n    steps: []`r`n"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "removed-runner-disk-floor"
            Workflow = Replace-RequiredText $Workflow `
                " -MinimumFreeGiB 5" `
                " -MinimumFreeGiB 0"
            AllGates = $AllGatesScript
        },
        [pscustomobject]@{
            Name = "short-form-extra-run"
            Workflow = Replace-RequiredText $Workflow `
                "    steps:" `
                "    steps:`r`n      - run: Write-Host bypass"
            AllGates = $AllGatesScript
        }
    )

    foreach ($mutation in $mutations) {
        Assert-SecurityMutationRejected `
            -Name ([string] $mutation.Name) `
            -Workflow ([string] $mutation.Workflow) `
            -AllGatesScript ([string] $mutation.AllGates)
    }
    return $mutations.Count
}

try {
    $workflow = Read-StrictUtf8Text -Path $workflowPath -MaximumBytes 65536

    Assert-RegexMatch $workflow "(?im)^name\s*:\s*Windows core CI\s*$" `
        "M9 Windows CI 工作流名称不匹配。"
    Assert-RegexMatch $workflow "(?im)^\s*runs-on\s*:\s*windows-2022\s*$" `
        "M9 Windows CI 必须锁定 windows-2022。"
    Assert-RegexMatch $workflow "(?im)^\s*shell\s*:\s*\[\s*pwsh\s*,\s*powershell\s*\]\s*$" `
        "M9 Windows CI 必须覆盖 PowerShell 7 与 Windows PowerShell 5.1。"
    Assert-RegexMatch $workflow "(?im)^\s*DOTNET_ADD_GLOBAL_TOOLS_TO_PATH\s*:\s*['""]0['""]\s*$" `
        "M9 Windows CI 缺少 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0。"
    Assert-RegexMatch $workflow "(?im)^\s*DOTNET_GENERATE_ASPNET_CERTIFICATE\s*:\s*['""]false['""]\s*$" `
        "M9 Windows CI 缺少开发证书副作用禁用开关。"
    Assert-RegexMatch $workflow "(?im)^\s*CODEX_AUTOCAD_ARTIFACT_BASE\s*:\s*\$\{\{\s*runner\.temp\s*\}\}[\\/][^\r\n]+\s*$" `
        "M9 Windows CI 产物根必须位于 runner.temp。"
    Assert-RegexMatch $workflow "(?im)^\s*global-json-file\s*:\s*global\.json\s*$" `
        "setup-dotnet 必须读取仓库 global.json。"
    Assert-RegexMatch $workflow "(?im)^\s*persist-credentials\s*:\s*false\s*$" `
        "checkout 必须禁用持久化凭据。"

    $phase2Script = Read-StrictUtf8Text -Path $phase2Path -MaximumBytes 131072
    $toolchainScript = Read-StrictUtf8Text -Path $toolchainPath -MaximumBytes 131072
    $net45X64Script = Read-StrictUtf8Text -Path $net45X64Path -MaximumBytes 131072
    $allGatesScript = Read-StrictUtf8Text -Path $allGatesPath -MaximumBytes 131072
    Assert-WorkflowSecurityRules -Workflow $workflow -AllGatesScript $allGatesScript
    Assert-RegexMatch $phase2Script "(?im)^\s*\[switch\]\s*\`$SkipLiveCodexHandshake\s*,?\s*$" `
        "Phase 2 缺少显式 CI-only doctor 跳过开关。"
    Assert-RegexMatch $phase2Script "(?im)if\s*\(\s*-not\s+\`$SkipLiveCodexHandshake\s*\)" `
        "Phase 2 默认路径没有保持本机 Codex doctor。"
    Assert-RegexMatch $phase2Script "(?im)phase2-managed-core-ci-gate" `
        "Phase 2 CI evidence 未与正式 readiness evidence 分离。"
    Assert-RegexMatch $phase2Script "(?im)RestoreConfigFile" `
        "Phase 2 未显式隔离用户 NuGet 配置。"
    Assert-RegexMatch $toolchainScript "(?im)toolchain-lock\.json" `
        "M9 工具链门禁没有消费版本化输入锁。"
    Assert-RegexMatch $toolchainScript "(?im)NuGetVersion" `
        "M9 工具链门禁没有验证 NuGet 版本。"
    Assert-RegexMatch $toolchainScript "(?im)RestoreLockedMode=true" `
        "M9 工具链门禁没有执行 locked clean-cache restore。"
    Assert-RegexMatch $net45X64Script "(?im)TargetFramework\s*=\s*""net45""" `
        "M9 net45/x64 门禁缺少 net45 evidence。"
    Assert-RegexMatch $net45X64Script "(?im)Architecture\s*=\s*""x64""" `
        "M9 net45/x64 门禁缺少 x64 evidence。"
    if ([regex]::IsMatch(
            $allGatesScript,
            "(?i)\bSkipLiveCodexHandshake\b",
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw "统一正式门禁禁止使用 SkipLiveCodexHandshake。"
    }

    if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
        throw "缺少 global.json。"
    }
    $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw -Encoding UTF8 |
        ConvertFrom-Json -ErrorAction Stop
    if ([string] $globalJson.sdk.version -cne "8.0.319" -or
        [string] $globalJson.sdk.rollForward -cne "disable" -or
        [bool] $globalJson.sdk.allowPrerelease) {
        throw "global.json 未锁定受控 .NET SDK 8.0.319。"
    }

    if ($SelfTestOnly) {
        if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
            throw "-SelfTestOnly 不能与 -EvidencePath 同时使用。"
        }
        $selfTestCases = Invoke-WorkflowSecuritySelfTest `
            -Workflow $workflow `
            -AllGatesScript $allGatesScript
        Complete-CodexBuildSafety -State $buildSafety -Stage "m9-windows-ci-self-test" |
            Out-Null
        Write-Host "M9_WINDOWS_CI_SELF_TEST=passed"
        Write-Host ("M9_WINDOWS_CI_SELF_TEST_CASES=" + $selfTestCases)
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $resolvedEvidence = if ([IO.Path]::IsPathRooted($EvidencePath)) {
            [IO.Path]::GetFullPath($EvidencePath)
        }
        else {
            [IO.Path]::GetFullPath((Join-Path $buildSafety.ArtifactRoot $EvidencePath))
        }
        $artifactRootWithSeparator = $buildSafety.ArtifactRoot.TrimEnd("\", "/") +
            [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedEvidence.StartsWith(
                $artifactRootWithSeparator,
                [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetExtension($resolvedEvidence) -cne ".json") {
            throw "M9 Windows CI evidence 只能写入 build-safety 产物根内 JSON。"
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedEvidence) -Force |
            Out-Null
        $report = [ordered]@{
            Schema = "codex.autocad.m9-windows-ci-definition/3"
            Status = "definition_verified"
            Runner = "windows-2022"
            Shells = @("pwsh", "powershell")
            DotNetSdk = "8.0.319"
            RunnerMinimumFreeGiB = 5
            GitHubPermissions = @("contents:read")
            PersistCredentials = $false
            ExactJobCount = 1
            ExactRunStepCount = $expectedRunCommands.Count
            ExplicitOfflineNuGetConfig = $true
            ToolchainLockGate = $true
            Net45X64ManagedCoreGate = $true
            LocalCodexHandshakeInCi = $false
            AutoCadStartedOrCommanded = $false
            CadWriteEnabled = $false
            RemoteWorkflowRunVerified = $false
            EvidenceBoundary = "This evidence validates the committed workflow definition locally. It does not prove that GitHub Actions executed the workflow, a local Codex handshake, an AutoCAD build or runtime, M4 readiness, or product release readiness."
        }
        $encoding = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText(
            $resolvedEvidence,
            ($report | ConvertTo-Json -Depth 6),
            $encoding)
        Write-Host ("M9_WINDOWS_CI_DEFINITION_EVIDENCE=" + $resolvedEvidence)
    }

    Complete-CodexBuildSafety -State $buildSafety -Stage "m9-windows-ci-definition" |
        Out-Null
    Write-Host "M9 Windows CI 定义门禁通过；这不等同于远端 GitHub Actions 已运行。" `
        -ForegroundColor Green
}
catch {
    try {
        Complete-CodexBuildSafety -State $buildSafety -Stage "m9-windows-ci-definition-failed" |
            Out-Null
    }
    catch {
    }
    throw
}

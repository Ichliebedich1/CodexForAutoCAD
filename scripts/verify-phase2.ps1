[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $CodexExecutable,

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$safeRepoRoot = $repoRoot.Replace("\", "/")
$solutionPath = Join-Path $repoRoot "Codex.AutoCAD.sln"
$doctorWorkspace = Join-Path $repoRoot "artifacts\phase2-doctor-workspace"
$dotnetHome = Join-Path $repoRoot "artifacts\dotnet-cli-home"
$nugetPackages = Join-Path $repoRoot "packages"
$nugetHttpCache = Join-Path $repoRoot "artifacts\nuget-http-cache"
$conditionalLockPath = Join-Path $repoRoot "src\Codex.AutoCAD.Bridge.Client\packages.lock.json"

$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = $nugetPackages
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source

# The default core build restores Bridge.Client as net8 only. Preserve its dual-target lock
# file so this verification does not erase the net45 graph required by the AutoCAD host gate.
if (-not (Test-Path -LiteralPath $conditionalLockPath -PathType Leaf)) {
    throw "缺少 Bridge.Client 条件化锁文件：$conditionalLockPath"
}
$conditionalLockBytes = [IO.File]::ReadAllBytes($conditionalLockPath)

$specProjects = @(
    "tests\Codex.AutoCAD.Contracts.Specs\Codex.AutoCAD.Contracts.Specs.csproj",
    "tests\Codex.AutoCAD.Ipc.Specs\Codex.AutoCAD.Ipc.Specs.csproj",
    "tests\Codex.AutoCAD.Security.Specs\Codex.AutoCAD.Security.Specs.csproj",
    "tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj",
    "tests\Codex.AutoCAD.Bridge.Specs\Codex.AutoCAD.Bridge.Specs.csproj",
    "tests\Codex.AutoCAD.Bridge.Client.Specs\Codex.AutoCAD.Bridge.Client.Specs.csproj",
    "tests\Codex.AutoCAD.AgentRuntime.Specs\Codex.AutoCAD.AgentRuntime.Specs.csproj",
    "tests\Codex.AutoCAD.Chat.Specs\Codex.AutoCAD.Chat.Specs.csproj",
    "tests\Codex.AutoCAD.Host.2016.Mvp.Specs\Codex.AutoCAD.Host.2016.Mvp.Specs.csproj"
)

$solutionRequiredProjects = @(
    "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj",
    "src\Codex.AutoCAD.Bridge\Codex.AutoCAD.Bridge.csproj",
    "src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj",
    "src\Codex.AutoCAD.AgentRuntime\Codex.AutoCAD.AgentRuntime.csproj"
) + @(
    "tests\Codex.AutoCAD.Bridge.Client.TestServer\Codex.AutoCAD.Bridge.Client.TestServer.csproj"
) + $specProjects

$hostProjectsExcludedFromCoreBuild = @(
    "src\Codex.AutoCAD.Host.2016\Codex.AutoCAD.Host.2016.csproj",
    "src\Codex.AutoCAD.Host.2025\Codex.AutoCAD.Host.2025.csproj"
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
        throw "$Description 失败，退出码：$exitCode"
    }
}

function Invoke-ValidatedSpecProject {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    Write-Host "`n==> 运行规格：$RelativePath" -ForegroundColor Cyan
    $rawOutput = & $dotnetCommand @(
        "run",
        "--project", $RelativePath,
        "--configuration", $Configuration,
        "--no-build"
    ) 2>&1
    $exitCode = $LASTEXITCODE
    $outputLines = @($rawOutput | ForEach-Object { [string] $_ })
    foreach ($line in $outputLines) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw "运行规格失败：$RelativePath，退出码：$exitCode"
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
            "^\s*规格总数\s*:\s*(?<Total>\d+)\s*,\s*通过\s*:\s*(?<Passed>\d+)\s*,\s*失败\s*:\s*(?<Failed>\d+)\s*$",
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
        throw "规格输出必须且只能包含一条动态计数摘要：$RelativePath"
    }

    $summary = $summaries[0]
    if ($summary.Total -le 0 -or $summary.Passed -ne $summary.Total -or $summary.Failed -ne 0) {
        throw "规格未全部通过：$RelativePath，实际 $($summary.Passed)/$($summary.Total)，失败 $($summary.Failed)"
    }

    return [pscustomobject]@{
        Project = $RelativePath
        Name = [IO.Path]::GetFileNameWithoutExtension($RelativePath)
        Passed = $summary.Passed
        Total = $summary.Total
    }
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $absolutePath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        throw "缺少验证所需文件：$RelativePath"
    }
}

function Find-SolutionProjectGuid {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $solutionText = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
    $solutionPathText = $RelativePath.Replace("/", "\")
    $pattern = (
        '(?im)^Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"' +
        [regex]::Escape($solutionPathText) +
        '",\s*"\{(?<Guid>[0-9A-F-]+)\}"\s*$'
    )
    $match = [regex]::Match($solutionText, $pattern)
    if (-not $match.Success) {
        return $null
    }

    return $match.Groups["Guid"].Value
}

function Assert-SolutionBuildsProject {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $projectGuid = Find-SolutionProjectGuid -RelativePath $RelativePath
    if ([string]::IsNullOrWhiteSpace($projectGuid)) {
        throw "阶段 2 项目尚未纳入解决方案：$RelativePath"
    }

    $solutionText = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
    $buildEntry = "{$projectGuid}.$Configuration|Any CPU.Build.0"
    if ($solutionText.IndexOf($buildEntry, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "阶段 2 项目未纳入 $Configuration 默认构建：$RelativePath"
    }
}

function Assert-HostProjectsExcludedFromCoreBuild {
    $solutionText = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
    foreach ($relativePath in $hostProjectsExcludedFromCoreBuild) {
        $projectGuid = Find-SolutionProjectGuid -RelativePath $relativePath
        if ([string]::IsNullOrWhiteSpace($projectGuid)) {
            continue
        }

        $buildPattern = (
            "(?im)^\s*\{" +
            [regex]::Escape($projectGuid) +
            "\}\.[^\r\n=]+\.Build\.0\s*="
        )
        if ([regex]::IsMatch($solutionText, $buildPattern)) {
            throw "CAD Host 不得纳入托管核心解决方案默认构建：$relativePath"
        }
    }

    Write-Host "解决方案组成验证通过：托管核心项目默认构建，AutoCAD Host 独立验证。" -ForegroundColor Green
}

function Assert-PinnedSdk {
    $expected = (Get-Content -LiteralPath (Join-Path $repoRoot "global.json") -Raw | ConvertFrom-Json).sdk.version
    $actual = (& $dotnetCommand --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) {
        throw "需要 .NET SDK $expected，当前解析到 '$actual'。"
    }
    Write-Host ".NET SDK 固定版本验证通过：$actual" -ForegroundColor Green
}

function Test-IsFullyQualifiedWindowsPath {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    return [regex]::IsMatch(
        $Path,
        "^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$))"
    )
}

function Resolve-CodexExecutablePath {
    param([string] $RequestedPath)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath)
    }
    $environmentCandidate = [Environment]::GetEnvironmentVariable("CODEX_EXECUTABLE")
    if (-not [string]::IsNullOrWhiteSpace($environmentCandidate)) {
        $candidates.Add($environmentCandidate)
    }

    $npmCommand = Get-Command codex.cmd -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $npmCommand) {
        $packageRoot = Join-Path (Split-Path -Parent $npmCommand.Source) "node_modules\@openai\codex"
        if (Test-Path -LiteralPath $packageRoot -PathType Container) {
            foreach ($nativeExecutable in @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter "codex.exe" -ErrorAction SilentlyContinue)) {
                $candidates.Add($nativeExecutable.FullName)
            }
        }
    }

    foreach ($command in @(Get-Command codex.exe -All -ErrorAction SilentlyContinue)) {
        $candidates.Add($command.Source)
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if ((Test-IsFullyQualifiedWindowsPath -Path $candidate) -and
            [IO.Path]::GetExtension($candidate) -ieq ".exe" -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw "未找到可由 AgentHost 直接启动的 codex.exe；请传入 -CodexExecutable 绝对路径。"
}

function Assert-NoForbiddenHostApi {
    $hostRoot = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2025"
    $hostProject = Join-Path $hostRoot "Codex.AutoCAD.Host.2025.csproj"
    $forbiddenRules = [ordered]@{
        "CAD 命令、保存、导出或退出" = "(?i)(?:SendStringToExecute|ExecuteInCommandContextAsync|SetSystemVariable|\.\s*(?:Save|SaveAs|DwgOut|DxfOut|CloseAndSave|CloseAndDiscard)\b|\bApplication\s*\.\s*(?:Quit|Invoke)\b|\b(?:QSAVE|NETLOAD|ARXLOAD)\b|\.\s*Command(?:Async)?\s*\()"
        "CAD 数据库写入" = "(?i)(?:OpenMode\s*\.\s*ForWrite|\.\s*(?:UpgradeOpen|DowngradeOpen|AppendEntity|AddNewlyCreatedDBObject|Erase|WblockCloneObjects|DeepCloneObjects|TransformBy)\b)"
        "进程或 Shell" = "(?i)(?:System\s*\.\s*Diagnostics\s*\.\s*Process|\bProcessStartInfo\b|\bProcess\b|ShellExecute|CreateProcess|cmd(?:\.exe)?|powershell(?:\.exe)?)"
        "反射、动态或原生加载" = "(?i)(?:System\s*\.\s*Reflection|\b(?:MethodInfo|ConstructorInfo|PropertyInfo|FieldInfo|BindingFlags)\b|\.\s*(?:GetMethod|GetMethods|GetConstructor|GetConstructors|GetProperty|GetProperties|GetField|GetFields|InvokeMember)\s*\(|\bActivator\s*\.\s*CreateInstance\s*\(|\.\s*DynamicInvoke\s*\(|Assembly\s*\.\s*(?:Load|LoadFrom|LoadFile|UnsafeLoadFrom)\s*\(|AppDomain\s*\.\s*CurrentDomain\s*\.\s*Load\s*\(|NativeLibrary|LoadLibrary|GetProcAddress|DllImport|LibraryImport|GetDelegateForFunctionPointer|\bdelegate\s*\*\s*unmanaged)"
        "危险 API 类型别名" = "(?im)^\s*(?:global\s+)?using\s+(?:(?:static\s+)(?:global::)?(?:Autodesk\s*\.\s*AutoCAD|System\s*\.\s*(?:Diagnostics|IO\s*\.\s*Pipes|Net|Reflection|Runtime\s*\.\s*InteropServices)|Microsoft\s*\.\s*Win32)|(?:\w+\s*=\s*)(?:global::)?(?:Autodesk\s*\.\s*AutoCAD|System\s*\.\s*(?:Diagnostics|IO\s*\.\s*Pipes|Net|Reflection|Runtime\s*\.\s*InteropServices)|Microsoft\s*\.\s*Win32))\b"
        "直接 IPC" = "(?i)(?:System\s*\.\s*IO\s*\.\s*Pipes|NamedPipe\w*|AnonymousPipe\w*|PipeStream|MemoryMappedFile|\\\\\.\\pipe\\)"
        "直接网络" = "(?i)(?:System\s*\.\s*Net|HttpClient|WebRequest|WebClient|HttpListener|Socket|TcpClient|UdpClient)"
        "文件或注册表写入" = "(?i)(?:\b(?:FileStream|StreamWriter|BinaryWriter|FileInfo|DirectoryInfo)\b|System\s*\.\s*IO\s*\.\s*File\b|File\s*\.\s*(?:Open|Create|Write|Append|Delete|Move|Copy)\w*\b|Directory\s*\.\s*(?:Create|Delete|Move)\w*\b|Microsoft\s*\.\s*Win32|Registry(?:Key)?)"
    }
    $requiredDetections = [ordered]@{
        "Database.Save" = "database.Save(""drawing.dwg"", version);"
        "Database.DxfOut" = "database.DxfOut(""drawing.dxf"", precision, version);"
        "Application.Quit" = "Application.Quit();"
        "Application.Invoke" = "Application.Invoke(action);"
        "Document.SendStringToExecute" = "document.SendStringToExecute(command, true, false, false);"
        "Application.SetSystemVariable" = "Application.SetSystemVariable(""FILEDIA"", 0);"
        "OpenMode.ForWrite" = "transaction.GetObject(id, OpenMode.ForWrite);"
        "DBObject.UpgradeOpen" = "layer.UpgradeOpen();"
        "DBObject.DowngradeOpen" = "layer.DowngradeOpen();"
        "BlockTableRecord.AppendEntity" = "space.AppendEntity(entity);"
        "Transaction.AddNewlyCreatedDBObject" = "transaction.AddNewlyCreatedDBObject(entity, true);"
        "DBObject.Erase" = "entity.Erase();"
        "Database.WblockCloneObjects" = "database.WblockCloneObjects(ids, ownerId, mapping, DuplicateRecordCloning.Ignore, false);"
        "Database.DeepCloneObjects" = "database.DeepCloneObjects(ids, ownerId, mapping, false);"
        "Entity.TransformBy" = "entity.TransformBy(matrix);"
        "FileStream instance Write" = "using var output = new FileStream(path, FileMode.Create); output.Write(buffer);"
        "Process.Start" = "Process.Start(startInfo);"
        "Assembly.LoadFrom" = "Assembly.LoadFrom(path);"
        "MethodInfo.Invoke" = "type.GetMethod(name).Invoke(target, arguments);"
        "LibraryImport" = "[LibraryImport(""kernel32"")] static partial nint LoadLibrary(string path);"
        "Autodesk type alias" = "using OM = Autodesk.AutoCAD.DatabaseServices.OpenMode;"
        "Process static alias" = "using static System.Diagnostics.Process;"
        "NamedPipeClientStream" = "using var pipe = new NamedPipeClientStream(""."", name, PipeDirection.InOut);"
        "MemoryMappedFile" = "using var map = MemoryMappedFile.CreateOrOpen(name, capacity);"
        "HttpClient" = "using var client = new HttpClient();"
        "Registry write" = "Registry.CurrentUser.CreateSubKey(path);"
    }
    foreach ($sample in $requiredDetections.GetEnumerator()) {
        $detected = $false
        foreach ($rule in $forbiddenRules.GetEnumerator()) {
            if ([regex]::IsMatch([string] $sample.Value, [string] $rule.Value)) {
                $detected = $true
                break
            }
        }
        if (-not $detected) {
            throw "AutoCAD Host 禁用 API 规则自检失败，未覆盖：$($sample.Key)"
        }
    }

    $allowedSamples = @(
        "panel.Dispatcher.Invoke(action);",
        "database.TransactionManager.StartTransaction();",
        "transaction.GetObject(id, OpenMode.ForRead);",
        "using var stream = new MemoryStream(); stream.Write(buffer);"
    )
    foreach ($sample in $allowedSamples) {
        foreach ($rule in $forbiddenRules.GetEnumerator()) {
            if ([regex]::IsMatch($sample, [string] $rule.Value)) {
                throw "AutoCAD Host 禁用 API 规则自检失败，误报安全样例：$sample"
            }
        }
    }
    Write-Host "AutoCAD Host 禁用 API 规则自检通过。" -ForegroundColor Green

    $buildControlFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($candidate in @(
        $hostProject,
        (Join-Path $repoRoot "Directory.Build.props"),
        (Join-Path $repoRoot "Directory.Build.targets")
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $buildControlFiles.Add((Get-Item -LiteralPath $candidate))
        }
    }
    foreach ($candidate in @(
        Get-ChildItem -LiteralPath $hostRoot -Recurse -File -ErrorAction Stop |
            Where-Object { $_.Name -in @("Directory.Build.props", "Directory.Build.targets") }
    )) {
        $buildControlFiles.Add($candidate)
    }

    $buildInjectionRules = [ordered]@{
        "Compile 项显式注入或移除" = "(?i)<\s*Compile\b"
        "关闭默认 Compile 闭包" = "(?i)<\s*EnableDefaultCompileItems\s*>\s*false\s*<"
        "自定义导入或内联构建任务" = "(?i)(?:<\s*Import\b|<\s*UsingTask\b|CodeTaskFactory|RoslynCodeTaskFactory|WriteLinesToFile|WriteCodeFragment|<\s*Exec\b)"
    }
    foreach ($rule in $buildInjectionRules.GetEnumerator()) {
        foreach ($match in @($buildControlFiles | Select-String -Pattern ([string]$rule.Value) -AllMatches)) {
            throw "AutoCAD Host 编译闭包门禁失败：$($rule.Key)，文件 $($match.Path):$($match.LineNumber)。"
        }
    }

    $evaluatedCompileOutput = @(
        & $dotnetCommand @("msbuild", $hostProject, "-nologo", "-verbosity:quiet", "-getItem:Compile") 2>&1 |
            ForEach-Object { [string] $_ }
    )
    if ($LASTEXITCODE -ne 0) {
        throw "无法求值 AutoCAD Host Compile 项，退出码：$LASTEXITCODE。"
    }
    try {
        $evaluatedCompile = ($evaluatedCompileOutput -join [Environment]::NewLine) | ConvertFrom-Json
    }
    catch {
        throw "无法解析 AutoCAD Host Compile 项 JSON：$($_.Exception.Message)"
    }

    $hostRootPrefix = [IO.Path]::GetFullPath($hostRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    $sourceFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($compileItem in @($evaluatedCompile.Items.Compile)) {
        $fullPath = [IO.Path]::GetFullPath([string] $compileItem.FullPath)
        if (-not $fullPath.StartsWith($hostRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "AutoCAD Host Compile 项越出受审源码根：$fullPath"
        }
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "AutoCAD Host Compile 项不存在：$fullPath"
        }
        $sourceFiles.Add((Get-Item -LiteralPath $fullPath))
    }
    if ($sourceFiles.Count -eq 0) {
        throw "AutoCAD Host Compile 项为空，禁止跳过源码扫描。"
    }

    $findings = [System.Collections.Generic.List[object]]::new()
    foreach ($rule in $forbiddenRules.GetEnumerator()) {
        foreach ($match in @($sourceFiles | Select-String -Pattern ([string]$rule.Value) -AllMatches)) {
            $findings.Add([pscustomobject]@{
                Category = [string]$rule.Key
                Path = $match.Path
                LineNumber = $match.LineNumber
                Text = $match.Line.Trim()
            })
        }
    }

    if ($findings.Count -gt 0) {
        foreach ($finding in $findings) {
            Write-Host `
                ("{0}:{1}: [{2}] {3}" -f $finding.Path, $finding.LineNumber, $finding.Category, $finding.Text) `
                -ForegroundColor Red
        }
        throw "AutoCAD Host 禁用 API 扫描失败，共发现 $($findings.Count) 处。"
    }

    Write-Host "AutoCAD Host 受审 Compile 闭包及词法禁用 API 扫描通过（CAD 数据库写入、命令/保存/导出/退出、进程、反射/动态加载、IPC、网络、文件写入、注册表）。" -ForegroundColor Green
}

function Assert-NoLikelySecret {
    $textExtensions = @(
        ".cs", ".csproj", ".config", ".json", ".md", ".props", ".ps1",
        ".sln", ".targets", ".xml", ".yaml", ".yml"
    )
    $secretPatterns = [ordered]@{
        "私钥" = "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"
        "OpenAI API 密钥" = "\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b"
        "GitHub 令牌" = "\bgh[pousr]_[A-Za-z0-9]{20,}\b"
        "AWS Access Key" = "\bAKIA[0-9A-Z]{16}\b"
    }

    $gitFiles = & git -c "safe.directory=$safeRepoRoot" -C $repoRoot ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw "无法枚举 Git 文件以执行敏感信息扫描，退出码：$LASTEXITCODE"
    }

    $findings = [System.Collections.Generic.List[object]]::new()
    foreach ($relativePath in $gitFiles) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }

        $normalizedPath = $relativePath.Replace("\", "/")
        if ($normalizedPath -match "(?:^|/)(?:\.git|artifacts|bin|obj)(?:/|$)") {
            continue
        }

        $extension = [System.IO.Path]::GetExtension($relativePath).ToLowerInvariant()
        if ($textExtensions -notcontains $extension) {
            continue
        }

        $absolutePath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            continue
        }

        $content = Get-Content -LiteralPath $absolutePath -Raw -Encoding UTF8
        foreach ($entry in $secretPatterns.GetEnumerator()) {
            if ([regex]::IsMatch($content, [string] $entry.Value)) {
                $findings.Add([pscustomobject]@{
                    Path = $relativePath
                    Rule = [string] $entry.Key
                })
            }
        }
    }

    if ($findings.Count -gt 0) {
        foreach ($finding in $findings) {
            Write-Host `
                ("{0}: 命中 {1}" -f $finding.Path, $finding.Rule) `
                -ForegroundColor Red
        }
        throw "敏感信息基础扫描失败，共发现 $($findings.Count) 个疑似泄漏。"
    }

    Write-Host "敏感信息基础扫描通过。" -ForegroundColor Green
}

Push-Location $repoRoot
try {
    Assert-FileExists "Codex.AutoCAD.sln"
    foreach ($project in @($solutionRequiredProjects | Select-Object -Unique)) {
        Assert-FileExists $project
    }
    foreach ($project in $solutionRequiredProjects) {
        Assert-SolutionBuildsProject $project
    }
    Assert-HostProjectsExcludedFromCoreBuild

    Assert-PinnedSdk
    $resolvedCodexExecutable = Resolve-CodexExecutablePath -RequestedPath $CodexExecutable

    $solutionBuildArguments = @(
        "build", $solutionPath,
        "--configuration", $Configuration,
        "--nologo", "--disable-build-servers", "-m:1"
    )
    if ($NoRestore) {
        $solutionBuildArguments += "--no-restore"
    }

    Invoke-CheckedCommand `
        -FilePath $dotnetCommand `
        -ArgumentList $solutionBuildArguments `
        -Description "构建托管核心解决方案（CAD Host 按目标版本独立验证，$Configuration）"

    $bridgeClientTestServer = Join-Path $repoRoot (
        "tests\Codex.AutoCAD.Bridge.Client.TestServer\bin\" +
        $Configuration +
        "\net8.0-windows\Codex.AutoCAD.Bridge.Client.TestServer.exe"
    )
    if (-not (Test-Path -LiteralPath $bridgeClientTestServer -PathType Leaf)) {
        throw "Bridge Client TestServer未由解决方案构建产生：$bridgeClientTestServer"
    }

    $previousBridgeTestServer = [Environment]::GetEnvironmentVariable(
        "CODEX_BRIDGE_TEST_SERVER_EXE",
        [EnvironmentVariableTarget]::Process
    )
    $specResults = [System.Collections.Generic.List[object]]::new()
    try {
        $env:CODEX_BRIDGE_TEST_SERVER_EXE = $bridgeClientTestServer
        foreach ($project in $specProjects) {
            $specResults.Add((Invoke-ValidatedSpecProject -RelativePath $project))
        }
    }
    finally {
        if ($null -eq $previousBridgeTestServer) {
            Remove-Item Env:CODEX_BRIDGE_TEST_SERVER_EXE -ErrorAction SilentlyContinue
        }
        else {
            $env:CODEX_BRIDGE_TEST_SERVER_EXE = $previousBridgeTestServer
        }
    }
    $totalSpecs = [int] (($specResults | Measure-Object -Property Total -Sum).Sum)
    Write-Host "`n==> 规格动态计数汇总：$totalSpecs/$totalSpecs" -ForegroundColor Green
    foreach ($result in $specResults) {
        Write-Host ("{0}: {1}/{1}" -f $result.Name, $result.Total) -ForegroundColor Green
    }

    Write-Host "`n==> 扫描 AutoCAD Host 禁用 API" -ForegroundColor Cyan
    Assert-NoForbiddenHostApi

    New-Item -ItemType Directory -Path $doctorWorkspace -Force | Out-Null
    $doctorArguments = @(
        "run",
        "--project", "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj",
        "--configuration", $Configuration,
        "--no-build",
        "--",
        "doctor",
        "--workspace", $doctorWorkspace,
        "--codex", $resolvedCodexExecutable
    )

    Invoke-CheckedCommand `
        -FilePath $dotnetCommand `
        -ArgumentList $doctorArguments `
        -Description "执行 AgentHost doctor 活体握手"
    Write-Warning "doctor 与 AppServer Specs 覆盖环境白名单、默认空 MCP 和可选 session-isolation 配置；本次 doctor 未配置真实 Credential Manager 引用，不能证明真实隔离登录、插件配置隔离或完整 OS 沙箱。"

    Invoke-CheckedCommand `
        -FilePath "git" `
        -ArgumentList @("-c", "safe.directory=$safeRepoRoot", "diff", "--check") `
        -Description "检查未暂存差异格式"

    Invoke-CheckedCommand `
        -FilePath "git" `
        -ArgumentList @("-c", "safe.directory=$safeRepoRoot", "diff", "--cached", "--check") `
        -Description "检查已暂存差异格式"

    Write-Host "`n==> 执行敏感信息基础扫描" -ForegroundColor Cyan
    Assert-NoLikelySecret

    Write-Host "`n阶段 2 托管核心门禁通过：$Configuration 构建、$($specProjects.Count) 个规格项目（动态汇总 $totalSpecs/$totalSpecs）、Host 禁用 API、AgentHost 活体握手、Git 差异及敏感信息检查均通过。" -ForegroundColor Green
    Write-Warning "该门禁不验证 AutoCAD 2016/2025 Host 实机能力，也不表示每会话 Agent 隔离已经完成。"
}
finally {
    [IO.File]::WriteAllBytes($conditionalLockPath, $conditionalLockBytes)
    Pop-Location
}

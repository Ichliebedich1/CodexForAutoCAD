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
$bootstrapSourcePath = Join-Path $repoRoot "src\Codex.AutoCAD.Ipc\AgentBootstrap.cs"
$nugetConfig = Join-Path $repoRoot "src\Codex.AutoCAD.Host.2016\NuGet.Config"
$conditionalLockPath = Join-Path $repoRoot "src\Codex.AutoCAD.Bridge.Client\packages.lock.json"
$offlinePackage = Join-Path $repoRoot "third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg"
$expectedSdk = "8.0.319"
$expectedPackageSha256 = "23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3"
$expectedCanonical = "313A31383A6D73672DCEB12DF09F9880363A636F72722DE4B8AD31323A73657373696F6E2D32303136323A343231313A6361642E636F6E7465787432343A7B2274657874223A22E4B8ADE69687F09F9880222C226C696E65223A317D33323A3030313132323333343435353636373738383939414142424343444445454646"
$expectedMac = "46FFA5506FD595BA64CEAD67EDBAF8707E1A585988BC80298EBF569F69B38400"
$expectedBootstrapFrame = "434458434144423101000000A4000000101112131415161718191A1B1C1D1E1F20002E0020003030313132323333343435353636373738383939616162626363646465656666636F6465782D6175746F6361642D6666656564646363626261613939383837373636353534343333323231313030202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F386E5F60B2DA4B82E600AE2A4F29F7FCA8B8315CB7A5A7EA291CFA1A933A96B3"
$expectedBootstrapTag = "386E5F60B2DA4B82E600AE2A4F29F7FCA8B8315CB7A5A7EA291CFA1A933A96B3"
$expectedHostToAgentContextSha256 = "2C5D369CC406BC57F6484837E17F6C4DFA46B7E18D949A713BC7509EDAA99F75"
$expectedHostToAgentKey = "BD7B3C03ACFCACB201B1967C30EDBE2369D79517BBA069C65B1431AFFF97C253"
$expectedHostToAgentMac = "AFDF6BDED2384AF724D4A0678C1AC6DE076C4E7E7254C6B975972C25D12C3D90"
$expectedAgentToHostContextSha256 = "A52233871F1CE9AF67657B5E61F4E894ECA7EEE599FF86CB2CFFF4A60766319B"
$expectedAgentToHostKey = "89B7E2E5541EFE9FEBBAC62021F764AA91AC31E11342003AC07B43CA19AC03CD"
$expectedAgentToHostMac = "548474E515652C3F182AE099DD2DC6EA99FCDE290F12006380E707ACDAD977D8"
$expectedBootstrapFrameSha256 = "D60FBAFC368EAA86EBFF00AE85DA88D1BE9D1A0B40B4960A5F52E00C82A0B0F4"
$expectedSpecCount = 35
$expectedBridgeSpecCount = 49
$runId = [Guid]::NewGuid().ToString("N")
$stageRoot = Join-Path $repoRoot ("artifacts\autocad2016-auth-compat-" + $runId)
$evidencePath = Join-Path $stageRoot "verification.json"

if (-not (Test-Path -LiteralPath $conditionalLockPath -PathType Leaf)) {
    throw "缺少 Bridge.Client 条件化锁文件：$conditionalLockPath"
}
$conditionalLockBytes = [IO.File]::ReadAllBytes($conditionalLockPath)

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

function Convert-HexToBytes {
    param([Parameter(Mandatory = $true)][string] $Hex)

    if (($Hex.Length % 2) -ne 0 -or $Hex -notmatch '^[0-9A-Fa-f]*$') {
        throw "无效十六进制测试向量。"
    }

    $bytes = New-Object byte[] ($Hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
    }
    Write-Output -NoEnumerate $bytes
}

function Convert-BytesToHex {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)

    return ([BitConverter]::ToString($Bytes)).Replace('-', '')
}

function Write-UInt16LittleEndian {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Bytes,
        [Parameter(Mandatory = $true)][int] $Offset,
        [Parameter(Mandatory = $true)][int] $Value
    )

    $Bytes[$Offset] = [byte]$Value
    $Bytes[$Offset + 1] = [byte]($Value -shr 8)
}

function Write-UInt32LittleEndian {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Bytes,
        [Parameter(Mandatory = $true)][int] $Offset,
        [Parameter(Mandatory = $true)][uint32] $Value
    )

    $Bytes[$Offset] = [byte]$Value
    $Bytes[$Offset + 1] = [byte]($Value -shr 8)
    $Bytes[$Offset + 2] = [byte]($Value -shr 16)
    $Bytes[$Offset + 3] = [byte]($Value -shr 24)
}

function Get-HmacSha256Bytes {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Key,
        [Parameter(Mandatory = $true)][byte[]] $Data
    )

    $keyCopy = [byte[]]$Key.Clone()
    $hmac = New-Object Security.Cryptography.HMACSHA256
    try {
        $hmac.Key = $keyCopy
        Write-Output -NoEnumerate ([byte[]]$hmac.ComputeHash($Data))
    }
    finally {
        $hmac.Dispose()
        [Array]::Clear($keyCopy, 0, $keyCopy.Length)
    }
}

function Get-Sha256Bytes {
    param([Parameter(Mandatory = $true)][byte[]] $Data)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        Write-Output -NoEnumerate ([byte[]]$sha256.ComputeHash($Data))
    }
    finally {
        $sha256.Dispose()
    }
}

function New-DirectionContextBytes {
    param(
        [Parameter(Mandatory = $true)][string] $RoleLabel,
        [Parameter(Mandatory = $true)][byte[]] $BootstrapId,
        [Parameter(Mandatory = $true)][byte[]] $SessionBytes,
        [Parameter(Mandatory = $true)][byte[]] $PipeBytes
    )

    $domain = [Text.Encoding]::ASCII.GetBytes("Codex.AutoCAD.AgentBootstrap.Direction.v1`0")
    $roleBytes = [Text.Encoding]::ASCII.GetBytes($RoleLabel)
    try {
        $context = New-Object byte[] (
            $domain.Length + 2 + 2 + $roleBytes.Length + $BootstrapId.Length +
            2 + $SessionBytes.Length + 2 + $PipeBytes.Length)
        $offset = 0
        [Buffer]::BlockCopy($domain, 0, $context, $offset, $domain.Length)
        $offset += $domain.Length
        Write-UInt16LittleEndian -Bytes $context -Offset $offset -Value 1
        $offset += 2
        Write-UInt16LittleEndian -Bytes $context -Offset $offset -Value $roleBytes.Length
        $offset += 2
        [Buffer]::BlockCopy($roleBytes, 0, $context, $offset, $roleBytes.Length)
        $offset += $roleBytes.Length
        [Buffer]::BlockCopy($BootstrapId, 0, $context, $offset, $BootstrapId.Length)
        $offset += $BootstrapId.Length
        Write-UInt16LittleEndian -Bytes $context -Offset $offset -Value $SessionBytes.Length
        $offset += 2
        [Buffer]::BlockCopy($SessionBytes, 0, $context, $offset, $SessionBytes.Length)
        $offset += $SessionBytes.Length
        Write-UInt16LittleEndian -Bytes $context -Offset $offset -Value $PipeBytes.Length
        $offset += 2
        [Buffer]::BlockCopy($PipeBytes, 0, $context, $offset, $PipeBytes.Length)
        Write-Output -NoEnumerate $context
    }
    finally {
        [Array]::Clear($domain, 0, $domain.Length)
        [Array]::Clear($roleBytes, 0, $roleBytes.Length)
    }
}

function Get-BootstrapEnvelopeCanonicalBytes {
    $values = @(
        '1',
        'bootstrap-vector',
        '',
        '00112233445566778899aabbccddeeff',
        '1',
        'agent.hello',
        '{"bootstrap":"v1"}',
        '0123456789abcdef0123456789abcdef'
    )
    $builder = New-Object Text.StringBuilder
    foreach ($value in $values) {
        [void]$builder.Append($value.Length.ToString([Globalization.CultureInfo]::InvariantCulture))
        [void]$builder.Append(':')
        [void]$builder.Append($value)
    }
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    Write-Output -NoEnumerate ([byte[]]$strictUtf8.GetBytes($builder.ToString()))
}

function Get-IndependentBootstrapVector {
    # This is a public known-answer vector. It is intentionally deterministic and must never
    # be used as production bootstrap material.
    [byte[]]$authenticationKey = Convert-HexToBytes -Hex '000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F'
    [byte[]]$bootstrapId = Convert-HexToBytes -Hex '101112131415161718191A1B1C1D1E1F'
    [byte[]]$sessionSecret = Convert-HexToBytes -Hex '202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F'
    [byte[]]$sessionBytes = [Text.Encoding]::ASCII.GetBytes('00112233445566778899aabbccddeeff')
    [byte[]]$pipeBytes = [Text.Encoding]::ASCII.GetBytes('codex-autocad-ffeeddccbbaa99887766554433221100')
    [byte[]]$magic = [Text.Encoding]::ASCII.GetBytes('CDXCADB1')
    [byte[]]$tagDomain = [Text.Encoding]::ASCII.GetBytes("Codex.AutoCAD.AgentBootstrap.Frame.v1`0")
    [byte[]]$frame = New-Object byte[] 180
    $temporaryBuffers = New-Object 'System.Collections.Generic.List[byte[]]'
    try {
        [Buffer]::BlockCopy($magic, 0, $frame, 0, $magic.Length)
        Write-UInt16LittleEndian -Bytes $frame -Offset 8 -Value 1
        Write-UInt16LittleEndian -Bytes $frame -Offset 10 -Value 0
        Write-UInt32LittleEndian -Bytes $frame -Offset 12 -Value 164

        $offset = 16
        [Buffer]::BlockCopy($bootstrapId, 0, $frame, $offset, $bootstrapId.Length)
        $offset += $bootstrapId.Length
        Write-UInt16LittleEndian -Bytes $frame -Offset $offset -Value $sessionBytes.Length
        $offset += 2
        Write-UInt16LittleEndian -Bytes $frame -Offset $offset -Value $pipeBytes.Length
        $offset += 2
        Write-UInt16LittleEndian -Bytes $frame -Offset $offset -Value $sessionSecret.Length
        $offset += 2
        [Buffer]::BlockCopy($sessionBytes, 0, $frame, $offset, $sessionBytes.Length)
        $offset += $sessionBytes.Length
        [Buffer]::BlockCopy($pipeBytes, 0, $frame, $offset, $pipeBytes.Length)
        $offset += $pipeBytes.Length
        [Buffer]::BlockCopy($sessionSecret, 0, $frame, $offset, $sessionSecret.Length)
        $offset += $sessionSecret.Length
        if ($offset -ne 148) {
            throw "独立 bootstrap frame 偏移错误：$offset"
        }

        [byte[]]$tagInput = New-Object byte[] ($tagDomain.Length + $offset)
        [void]$temporaryBuffers.Add($tagInput)
        [Buffer]::BlockCopy($tagDomain, 0, $tagInput, 0, $tagDomain.Length)
        [Buffer]::BlockCopy($frame, 0, $tagInput, $tagDomain.Length, $offset)
        [byte[]]$tag = Get-HmacSha256Bytes -Key $authenticationKey -Data $tagInput
        [void]$temporaryBuffers.Add($tag)
        [Buffer]::BlockCopy($tag, 0, $frame, $offset, $tag.Length)

        [byte[]]$hostToAgentContext = New-DirectionContextBytes -RoleLabel 'host-to-agent' -BootstrapId $bootstrapId -SessionBytes $sessionBytes -PipeBytes $pipeBytes
        [byte[]]$agentToHostContext = New-DirectionContextBytes -RoleLabel 'agent-to-host' -BootstrapId $bootstrapId -SessionBytes $sessionBytes -PipeBytes $pipeBytes
        [void]$temporaryBuffers.Add($hostToAgentContext)
        [void]$temporaryBuffers.Add($agentToHostContext)
        [byte[]]$hostToAgentContextDigest = Get-Sha256Bytes -Data $hostToAgentContext
        [byte[]]$agentToHostContextDigest = Get-Sha256Bytes -Data $agentToHostContext
        [void]$temporaryBuffers.Add($hostToAgentContextDigest)
        [void]$temporaryBuffers.Add($agentToHostContextDigest)
        [byte[]]$hostToAgentKey = Get-HmacSha256Bytes -Key $sessionSecret -Data $hostToAgentContext
        [byte[]]$agentToHostKey = Get-HmacSha256Bytes -Key $sessionSecret -Data $agentToHostContext
        [void]$temporaryBuffers.Add($hostToAgentKey)
        [void]$temporaryBuffers.Add($agentToHostKey)
        [byte[]]$canonical = Get-BootstrapEnvelopeCanonicalBytes
        [void]$temporaryBuffers.Add($canonical)
        [byte[]]$hostToAgentMac = Get-HmacSha256Bytes -Key $hostToAgentKey -Data $canonical
        [byte[]]$agentToHostMac = Get-HmacSha256Bytes -Key $agentToHostKey -Data $canonical
        [void]$temporaryBuffers.Add($hostToAgentMac)
        [void]$temporaryBuffers.Add($agentToHostMac)
        [byte[]]$frameDigest = Get-Sha256Bytes -Data $frame
        [void]$temporaryBuffers.Add($frameDigest)

        return [pscustomobject]@{
            Version = 1
            Frame = Convert-BytesToHex -Bytes $frame
            Tag = Convert-BytesToHex -Bytes $tag
            HostToAgentContextSha256 = Convert-BytesToHex -Bytes $hostToAgentContextDigest
            HostToAgentKey = Convert-BytesToHex -Bytes $hostToAgentKey
            HostToAgentMac = Convert-BytesToHex -Bytes $hostToAgentMac
            AgentToHostContextSha256 = Convert-BytesToHex -Bytes $agentToHostContextDigest
            AgentToHostKey = Convert-BytesToHex -Bytes $agentToHostKey
            AgentToHostMac = Convert-BytesToHex -Bytes $agentToHostMac
            FrameSha256 = Convert-BytesToHex -Bytes $frameDigest
            FrameBytes = $frame.Length
            BodyBytes = 164
        }
    }
    finally {
        foreach ($buffer in @(
            $authenticationKey, $bootstrapId, $sessionSecret, $sessionBytes, $pipeBytes,
            $magic, $tagDomain, $frame
        )) {
            if ($null -ne $buffer) {
                [Array]::Clear($buffer, 0, $buffer.Length)
            }
        }
        foreach ($buffer in $temporaryBuffers) {
            [Array]::Clear($buffer, 0, $buffer.Length)
        }
    }
}

function Assert-IndependentBootstrapVector {
    $vector = Get-IndependentBootstrapVector
    $expected = [ordered]@{
        Version = 1
        Frame = $expectedBootstrapFrame
        Tag = $expectedBootstrapTag
        HostToAgentContextSha256 = $expectedHostToAgentContextSha256
        HostToAgentKey = $expectedHostToAgentKey
        HostToAgentMac = $expectedHostToAgentMac
        AgentToHostContextSha256 = $expectedAgentToHostContextSha256
        AgentToHostKey = $expectedAgentToHostKey
        AgentToHostMac = $expectedAgentToHostMac
        FrameSha256 = $expectedBootstrapFrameSha256
        FrameBytes = 180
        BodyBytes = 164
    }
    foreach ($entry in $expected.GetEnumerator()) {
        if ([string]$vector.($entry.Key) -cne [string]$entry.Value) {
            throw "独立 bootstrap 参考计算不一致：$($entry.Key)。"
        }
    }
    if ($vector.Tag -cne $vector.Frame.Substring($vector.Frame.Length - 64)) {
        throw "独立 bootstrap tag 不是 frame 的最后 32 字节。"
    }

    Write-Host "独立 PowerShell bootstrap frame/KDF/HMAC 参考计算通过。" -ForegroundColor Green
    return $vector
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

function Assert-BootstrapSourceBoundary {
    $source = Get-Content -LiteralPath $bootstrapSourcePath -Raw -Encoding UTF8
    $forbiddenRules = [ordered]@{
        "文件或目录 API" = "(?i)(?:System\s*\.\s*IO\s*\.\s*(?:File|Directory|Path|FileStream|FileInfo|DirectoryInfo|DriveInfo)|\b(?:File|Directory|Path|FileInfo|DirectoryInfo|DriveInfo)\s*\.\s*[A-Za-z_]|\b(?:FileStream|FileInfo|DirectoryInfo|DriveInfo)\b)"
        "进程或 Shell API" = "(?i)(?:System\s*\.\s*Diagnostics\s*\.\s*Process|ProcessStartInfo|Process\s*\.\s*(?:Start|GetProcess)|ShellExecute|CreateProcess)"
        "网络 API" = "(?i)(?:System\s*\.\s*Net|HttpClient|WebRequest|Socket|TcpClient|UdpClient)"
        "管道或共享内存 API" = "(?i)(?:System\s*\.\s*IO\s*\.\s*Pipes|NamedPipe|MemoryMappedFile)"
        "注册表 API" = "(?i)(?:Microsoft\s*\.\s*Win32|Registry(?:Key)?\s*\.)"
        "动态或原生加载" = "(?i)(?:System\s*\.\s*Reflection|Assembly\s*\.\s*(?:Load|LoadFrom|LoadFile)|\bType\s*\.\s*(?:GetType|GetMethod|GetMethods|InvokeMember)|\bActivator\s*\.|\bMethodInfo\s*\.\s*Invoke|\bConstructorInfo\s*\.\s*Invoke|\bDelegate\s*\.\s*DynamicInvoke|DllImport|LoadLibrary|GetProcAddress|NativeLibrary|Marshal\s*\.)"
        "后台调度 API" = "(?i)(?:Task\s*\.\s*Run|\bThread\s*\.\s*[A-Za-z_]|ThreadPool|\bTimer\s*\()"
        "环境变量读取" = "(?i)(?:Environment\s*\.\s*GetEnvironmentVariable|Environment\s*\.\s*GetEnvironmentVariables)"
        "日志或控制台" = "(?i)(?:Console\s*\.|Trace\s*\.|Debug\s*\.|EventLog|ILogger)"
        "Autodesk API" = "(?i)Autodesk\s*\.\s*AutoCAD"
    }
    $requiredDetections = [ordered]@{
        "File.ReadAllBytes" = "File.ReadAllBytes(path);"
        "FileInfo.OpenRead" = "new FileInfo(path).OpenRead();"
        "DirectoryInfo.GetFiles" = "new DirectoryInfo(path).GetFiles();"
        "Process.Start" = "Process.Start(info);"
        "NamedPipeClientStream" = "new NamedPipeClientStream(name);"
        "HttpClient" = "new HttpClient();"
        "Assembly.LoadFrom" = "Assembly.LoadFrom(path);"
        "Type.GetType" = "Type.GetType(name);"
        "Activator.CreateInstance" = "Activator.CreateInstance(type);"
        "MethodInfo.Invoke" = "methodInfo.Invoke(target, args);"
        "Task.Run" = "Task.Run(work);"
        "Environment.GetEnvironmentVariable" = "Environment.GetEnvironmentVariable(name);"
        "Console.WriteLine" = "Console.WriteLine(secret);"
        "Autodesk" = "Autodesk.AutoCAD.ApplicationServices.Application"
    }
    foreach ($sample in $requiredDetections.GetEnumerator()) {
        $detected = $false
        foreach ($rule in $forbiddenRules.GetEnumerator()) {
            if ([regex]::IsMatch([string]$sample.Value, [string]$rule.Value)) {
                $detected = $true
                break
            }
        }
        if (-not $detected) {
            throw "Bootstrap 禁止 API 自检未覆盖：$($sample.Key)"
        }
    }
    foreach ($rule in $forbiddenRules.GetEnumerator()) {
        if ([regex]::IsMatch($source, [string]$rule.Value)) {
            throw "Bootstrap 源码命中禁止边界：$($rule.Key)"
        }
    }

    $publicFramePattern = '(?ms)public\s+static\s+[^;{}]+?\s+(?<Name>(?:Write|Read|Decode)[A-Za-z0-9_]*)\s*\((?<Parameters>.*?)\)\s*\{'
    $publicFrameMethods = @([regex]::Matches($source, $publicFramePattern))
    $expectedPublicFrameMethods = @(
        'DecodeSingleFrameAndClear',
        'ReadSingleFrameAndClearKey',
        'ReadSingleFrameAndClearKeyAsync',
        'WriteSingleFrameAndClearKey'
    )
    $actualPublicFrameMethods = @($publicFrameMethods | ForEach-Object { $_.Groups['Name'].Value } | Sort-Object -Unique)
    if (@(Compare-Object -ReferenceObject $expectedPublicFrameMethods -DifferenceObject $actualPublicFrameMethods -CaseSensitive).Count -ne 0 -or
        $publicFrameMethods.Count -ne $expectedPublicFrameMethods.Count) {
        throw "Bootstrap 公共 frame API 与精确白名单不一致：$($actualPublicFrameMethods -join ', ')"
    }
    foreach ($method in $publicFrameMethods) {
        if ($method.Groups['Parameters'].Value -notmatch '(?s)byte\s*\[\s*\]\s+authenticationKey\b') {
            throw "Bootstrap 公共 frame API 缺少帧外 authenticationKey：$($method.Groups['Name'].Value)"
        }
    }
    if ($source -match '(?m)^\s*public\s+AgentBootstrapPayload\s*\(' -or
        $source -match '(?m)^\s*public\s+AgentBootstrapDirectionKeys\s*\(' -or
        $source -match '(?m)^\s*public\s+[^\r\n{;]*\sSessionSecret\s*(?:=>|\{)' -or
        $source -match '(?m)^\s*public\s+[^\r\n{;]*\s(?:Get|Copy)SessionSecret\s*\(' -or
        $source -match '(?im)^\s*public\s+[^\r\n{;]*\s(?:Export|Unsafe|Material)[A-Za-z0-9_]*\s*\(' -or
        $source -match 'ComputeFrameTag\s*\(\s*sessionSecret') {
        throw "Bootstrap 公开面或 frame tag 出现未认证秘密旁路。"
    }

    if (@([regex]::Matches($source, 'ComputeFrameTag\s*\(\s*authenticationKey\s*,\s*frame')).Count -ne 2 -or
        @([regex]::Matches($source, 'FixedTimeEquals\s*\(\s*authenticationKey\s*,\s*sessionSecret\s*\)')).Count -ne 2 -or
        @([regex]::Matches($source, 'Clear\s*\(\s*authenticationKey\s*\)\s*;')).Count -ne 4) {
        throw "Bootstrap 帧外认证、key-reuse 拒绝或 caller key 清零调用数发生变化。"
    }
    if ($source -notmatch 'EnsureEndOfStream\s*\(\s*input\s*\)\s*;' -or
        $source -notmatch 'await\s+EnsureEndOfStreamAsync\s*\(\s*input\s*,\s*cancellationToken\s*\)' -or
        $source -notmatch 'DirectionDomain' -or
        $source -notmatch 'WriteUInt16\s*\(\s*input\s*,\s*offset\s*,\s*CurrentVersion\s*\)' -or
        $source -notmatch 'Buffer\.BlockCopy\s*\(\s*roleLabel' -or
        $source -notmatch 'Buffer\.BlockCopy\s*\(\s*bootstrapId' -or
        $source -notmatch 'Buffer\.BlockCopy\s*\(\s*sessionBytes' -or
        $source -notmatch 'Buffer\.BlockCopy\s*\(\s*pipeBytes') {
        throw "Bootstrap 缺少单帧 EOF 或 version/role/bootstrap/session/pipe KDF 绑定。"
    }
    if ($source -notmatch 'HMAC authenticates it but does not provide confidentiality' -or
        $source -notmatch 'dedicated, one-use, process-private channel' -or
        $source -notmatch 'never a command line, environment variable, log' -or
        $source -notmatch 'must not run on the AutoCAD main thread' -or
        $source -notmatch 'exclusive ownership of both arrays') {
        throw "Bootstrap 缺少明文秘密传输、专用机密通道、主线程禁用或数组独占契约。"
    }
    if ($source -notmatch '_consumed\s*=\s*true\s*;' -or
        $source -notmatch 'AgentBootstrapPayloadOrigin\.HostOutbound' -or
        $source -notmatch 'AgentBootstrapPayloadOrigin\.AgentInbound' -or
        $source -notmatch '_writeStarted\s*=\s*true\s*;' -or
        $source -notmatch 'FailSingleFrameWrite\s*\(\s*\)' -or
        $source -notmatch 'AgentBootstrapValidationCode\.AlreadyConsumed' -or
        $source -notmatch 'AgentBootstrapValidationCode\.InvalidPayloadState' -or
        $source -notmatch 'Clear\s*\(\s*bootstrapId\s*\)' -or
        $source -notmatch 'Clear\s*\(\s*sessionSecret\s*\)' -or
        $source -notmatch 'Clear\s*\(\s*hostToAgent\s*\)' -or
        $source -notmatch 'Clear\s*\(\s*agentToHost\s*\)') {
        throw "Bootstrap 缺少 payload 单次消费或秘密副本清零边界。"
    }

    $decodeStart = $source.IndexOf('private static AgentBootstrapPayload DecodeFrameCore', [StringComparison]::Ordinal)
    $encodeStart = $source.IndexOf('private static byte[] EncodeFrame', [StringComparison]::Ordinal)
    if ($decodeStart -lt 0 -or $encodeStart -le $decodeStart) {
        throw "无法定位 Bootstrap DecodeFrameCore 源码边界。"
    }
    $decodeSource = $source.Substring($decodeStart, $encodeStart - $decodeStart)
    $tagVerification = $decodeSource.IndexOf('expectedTag = ComputeFrameTag(authenticationKey', [StringComparison]::Ordinal)
    $semanticParsing = $decodeSource.IndexOf('var offset = HeaderSize;', [StringComparison]::Ordinal)
    if ($tagVerification -lt 0 -or $semanticParsing -lt 0 -or $tagVerification -ge $semanticParsing) {
        throw "Bootstrap 必须先验证 frame tag，再解析未认证字段语义。"
    }

    $syncReadStart = $source.IndexOf(
        'public static AgentBootstrapPayload ReadSingleFrameAndClearKey',
        [StringComparison]::Ordinal)
    $asyncReadStart = $source.IndexOf(
        'public static async Task<AgentBootstrapPayload> ReadSingleFrameAndClearKeyAsync',
        [StringComparison]::Ordinal)
    if ($syncReadStart -lt 0 -or $asyncReadStart -le $syncReadStart) {
        throw "无法定位 Bootstrap 同步读取源码边界。"
    }
    $syncReadSource = $source.Substring($syncReadStart, $asyncReadStart - $syncReadStart)
    $syncAuthenticate = $syncReadSource.IndexOf('payload = DecodeFrameCore(frame, keyCopy);', [StringComparison]::Ordinal)
    $syncEof = $syncReadSource.IndexOf('EnsureEndOfStream(input);', [StringComparison]::Ordinal)
    if ($syncAuthenticate -lt 0 -or $syncEof -lt 0 -or $syncAuthenticate -ge $syncEof) {
        throw "Bootstrap 同步读取必须先认证完整 frame，再等待 EOF。"
    }

    Write-Host "Bootstrap 源码认证、EOF、KDF、单次消费与禁止 API 边界通过。" -ForegroundColor Green
}

function Assert-ReviewedTextInputHygiene {
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    $textExtensions = @('.cs', '.csproj', '.props', '.sln', '.json', '.ps1', '.config')
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    foreach ($path in @($Paths | Sort-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            $textExtensions -notcontains [IO.Path]::GetExtension($path).ToLowerInvariant()) {
            continue
        }
        [byte[]]$bytes = [IO.File]::ReadAllBytes($path)
        try {
            if ($bytes.Length -ge 2 -and
                (($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or
                 ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))) {
                throw "评审输入不得使用 UTF-16：$path"
            }
            if ($bytes -contains [byte]0) {
                throw "评审文本输入包含 NUL：$path"
            }
            try {
                $text = $strictUtf8.GetString($bytes)
            }
            catch [Text.DecoderFallbackException] {
                throw "评审文本输入不是严格 UTF-8：$path"
            }
            $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n")
            $lineNumber = 0
            foreach ($line in $normalized.Split([char]"`n")) {
                $lineNumber++
                if ($line -match '[ \t]+$') {
                    throw "评审文本输入含尾随空白：${path}:$lineNumber"
                }
            }
        }
        finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
    Write-Host "全部评审文本输入的严格 UTF-8/NUL/尾随空白检查通过。" -ForegroundColor Green
}

function Resolve-TrustedIldasm {
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
        throw "ProgramFiles(x86) 不可用，无法发现受信 ildasm。"
    }
    $searchRoots = @(
        (Join-Path $programFilesX86 'Microsoft SDKs\Windows\v10.0A\bin'),
        (Join-Path $programFilesX86 'Windows Kits\10\bin')
    )
    $trusted = @(
        foreach ($root in $searchRoots) {
            if (-not (Test-Path -LiteralPath $root -PathType Container)) {
                continue
            }
            foreach ($candidate in @(Get-ChildItem -LiteralPath $root -Recurse -Filter 'ildasm.exe' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '(?i)\\NETFX\s+[^\\]+\s+Tools(?:\\x64)?\\ildasm\.exe$' })) {
                try {
                    $signature = Get-AuthenticodeSignature -LiteralPath $candidate.FullName
                    $versionMatch = [regex]::Match([string]$candidate.VersionInfo.FileVersion, '\d+(?:[\.,]\d+){1,3}')
                    if ($candidate.VersionInfo.CompanyName -cne 'Microsoft Corporation' -or
                        $candidate.VersionInfo.FileDescription -notmatch '^Microsoft \.NET Framework IL disassembler$' -or
                        $signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
                        $null -eq $signature.SignerCertificate -or
                        $signature.SignerCertificate.Subject -notmatch '(?i)(?:^|,\s*)O="?Microsoft Corporation"?(?:,|$)' -or
                        -not $versionMatch.Success) {
                        continue
                    }
                    $versionText = $versionMatch.Value.Replace(',', '.')
                    $parts = @($versionText -split '\.')
                    while ($parts.Count -lt 4) { $parts += '0' }
                    $version = New-Object Version (($parts | Select-Object -First 4) -join '.')
                    if ($version.Major -ne 4) {
                        continue
                    }
                    [pscustomobject]@{
                        Path = $candidate.FullName
                        Version = $version.ToString()
                        Sha256 = Get-Sha256 -Path $candidate.FullName
                        SignatureStatus = $signature.Status.ToString()
                        SignerSubject = $signature.SignerCertificate.Subject
                    }
                }
                catch {
                    # Untrusted or malformed lookalikes are ignored; another official SDK can win.
                }
            }
        }
    )
    if ($trusted.Count -eq 0) {
        throw "未找到 Microsoft 签名的 .NET Framework ildasm。"
    }
    return @($trusted | Sort-Object @{ Expression = { [Version]$_.Version }; Descending = $true }, Path | Select-Object -First 1)[0]
}

function Get-IlMemberReferenceMap {
    param([Parameter(Mandatory = $true)][string] $IlText)

    $codeOnly = [regex]::Replace($IlText, '//[^\r\n]*(?=\r|\n|$)', '')
    $flat = [regex]::Replace($codeOnly, '\s+', ' ')
    $pattern = '(?<owner>(?:\[[^\]]+\])?[A-Za-z0-9_.$+`''/<>]+(?:/\*[0-9A-Fa-f]{8}\*/)?)(?:/\*[0-9A-Fa-f]{8}\*/)?::(?<method>[A-Za-z0-9_.$+`''<>]+)\s*\((?<args>[^)]{0,1000})\)\s*/\*\s*(?<token>0A[0-9A-Fa-f]{6})\s*\*/'
    $map = @{}
    foreach ($match in [regex]::Matches($flat, $pattern)) {
        $owner = [regex]::Replace($match.Groups['owner'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $owner = [regex]::Replace($owner, '\[([^/\]]+)(?:/\*[0-9A-Fa-f]{8}\*/)?\]', '[$1]')
        $arguments = [regex]::Replace($match.Groups['args'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $arguments = [regex]::Replace($arguments, '\[([^/\]]+)(?:/\*[0-9A-Fa-f]{8}\*/)?\]', '[$1]')
        $arguments = [regex]::Replace($arguments, '\s+', ' ').Trim()
        $token = $match.Groups['token'].Value.ToUpperInvariant()
        $canonical = '{0}::{1}({2})' -f $owner, $match.Groups['method'].Value, $arguments
        if ($map.ContainsKey($token) -and $map[$token] -cne $canonical) {
            throw "ildasm 对 MemberRef $token 输出了冲突身份。"
        }
        $map[$token] = $canonical
    }
    return $map
}

function Get-IlMethodDefinitions {
    param([Parameter(Mandatory = $true)][string] $IlText)

    $definitions = @()
    $pattern = '(?s)\.method\s+/\*(?<token>060[0-9A-Fa-f]{5})\*/(?<body>.*?)}\s*//\s*end of method\s+(?<name>[^\r\n]+)'
    foreach ($match in [regex]::Matches($IlText, $pattern)) {
        $header = ($match.Groups['body'].Value -split '\{', 2)[0]
        $header = [regex]::Replace($header, '/\*[0-9A-Fa-f]{8}\*/', '')
        $header = [regex]::Replace($header, '\s+', ' ').Trim()
        $definitions += [pscustomobject]@{
            Token = $match.Groups['token'].Value.ToUpperInvariant()
            Name = $match.Groups['name'].Value.Trim()
            Header = $header
            Body = $match.Groups['body'].Value
        }
    }
    return $definitions
}

function Normalize-IlSurfaceText {
    param([Parameter(Mandatory = $true)][string] $Text)

    $normalized = [regex]::Replace($Text, '/\*[0-9A-Fa-f]{8}\*/', '')
    $normalized = [regex]::Replace($normalized, '\[[A-Za-z0-9_.-]+\]', '')
    $normalized = [regex]::Replace($normalized, '\s+', ' ').Trim()
    return $normalized
}

function Sort-OrdinalStrings {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $Values
    )

    [string[]] $sorted = @($Values)
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    return $sorted
}

function Sort-OrdinalUniqueStrings {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $Values
    )

    [string[]] $sorted = @(Sort-OrdinalStrings -Values $Values)
    $unique = New-Object 'System.Collections.Generic.List[string]'
    [string] $previous = $null
    $hasPrevious = $false
    foreach ($value in $sorted) {
        if (-not $hasPrevious -or -not [StringComparer]::Ordinal.Equals($previous, $value)) {
            [void] $unique.Add($value)
            $previous = $value
            $hasPrevious = $true
        }
    }
    return $unique.ToArray()
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string] $Text)

    [byte[]]$bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return Convert-BytesToHex -Bytes ([byte[]]$sha256.ComputeHash($bytes))
    }
    finally {
        $sha256.Dispose()
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Get-IlTopLevelTypeDefinitions {
    param([Parameter(Mandatory = $true)][string] $IlText)

    $definitions = @()
    $pattern = '(?ms)^\.class\s+/\*(?<token>020[0-9A-Fa-f]{5})\*/(?<header>.*?)^\{\s*\r?\n(?<body>.*?)^\}\s*//\s*end of class\s+(?<name>[^\r\n]+)'
    foreach ($match in [regex]::Matches($IlText, $pattern)) {
        $definitions += [pscustomobject]@{
            Token = $match.Groups['token'].Value.ToUpperInvariant()
            Name = (Normalize-IlSurfaceText -Text $match.Groups['name'].Value)
            Header = (Normalize-IlSurfaceText -Text $match.Groups['header'].Value)
            Body = $match.Groups['body'].Value
        }
    }
    if ($definitions.Count -eq 0) {
        throw "未从 IPC IL 解析到任何顶层 TypeDef，拒绝假绿。"
    }
    return $definitions
}

function Remove-IlNestedTypeBodies {
    param([Parameter(Mandatory = $true)][string] $TypeBody)

    $nestedPattern = '(?ms)^  \.class\s+/\*020[0-9A-Fa-f]{5}\*/.*?^  \}\s*//\s*end of class\s+[^\r\n]+\r?\n?'
    return [regex]::Replace($TypeBody, $nestedPattern, '')
}

function Get-IlPublicApiSurface {
    param([Parameter(Mandatory = $true)][string] $IlText)

    $publicTypes = @(Get-IlTopLevelTypeDefinitions -IlText $IlText | Where-Object {
        $_.Header -match '\bpublic\b'
    })
    $surface = New-Object 'System.Collections.Generic.List[string]'
    foreach ($type in @($publicTypes | Sort-Object Name)) {
        [void]$surface.Add(('T|{0}|{1}' -f $type.Name, $type.Header))
        $body = Remove-IlNestedTypeBodies -TypeBody $type.Body

        foreach ($method in @(Get-IlMethodDefinitions -IlText $body | Where-Object {
            $_.Header -match '\bpublic\b'
        } | Sort-Object Name, Header)) {
            [void]$surface.Add(('M|{0}|{1}|{2}' -f
                $type.Name,
                $method.Name,
                (Normalize-IlSurfaceText -Text $method.Header)))
        }

        foreach ($fieldMatch in [regex]::Matches($body, '(?m)^  \.field\s+(?<header>[^\r\n]+)\r?$')) {
            $field = Normalize-IlSurfaceText -Text $fieldMatch.Groups['header'].Value
            if ($field -match '\bpublic\b') {
                [void]$surface.Add(('F|{0}|{1}' -f $type.Name, $field))
            }
        }

        foreach ($propertyMatch in [regex]::Matches(
            $body,
            '(?ms)^  \.property\s+(?<header>.*?)\r?\n  \{(?<body>.*?)^  \}')) {
            $propertyBody = Normalize-IlSurfaceText -Text $propertyMatch.Groups['body'].Value
            $hasPublicAccessor = $false
            foreach ($method in @(Get-IlMethodDefinitions -IlText $body | Where-Object {
                $_.Header -match '\bpublic\b'
            })) {
                $memberName = ($method.Name -split '::', 2)[-1]
                if ($propertyBody -match ('::' + [regex]::Escape($memberName) + '\s*\(')) {
                    $hasPublicAccessor = $true
                    break
                }
            }
            if ($hasPublicAccessor) {
                [void]$surface.Add(('P|{0}|{1}' -f
                    $type.Name,
                    (Normalize-IlSurfaceText -Text $propertyMatch.Groups['header'].Value)))
            }
        }
    }

    $sortedSurface = @(Sort-OrdinalUniqueStrings -Values ([string[]] $surface.ToArray()))
    return [pscustomobject]@{
        PublicTypeNames = @(Sort-OrdinalUniqueStrings -Values ([string[]] @($publicTypes.Name)))
        Lines = $sortedSurface
        Count = $sortedSurface.Count
        Sha256 = Get-TextSha256 -Text ($sortedSurface -join "`n")
    }
}

function Get-CriticalMethodBodyHashes {
    param(
        [Parameter(Mandatory = $true)][object[]] $Methods,
        [Parameter(Mandatory = $true)][string[]] $Names
    )

    $hashes = [ordered]@{}
    foreach ($name in $Names) {
        $matches = @($Methods | Where-Object { $_.Name -ceq $name })
        if ($matches.Count -ne 1) {
            throw "关键状态机方法必须精确匹配一次：$name，实际：$($matches.Count)。"
        }
        $canonicalBody = Normalize-IlSurfaceText -Text $matches[0].Body
        $hashes[$name] = Get-TextSha256 -Text $canonicalBody
    }
    return $hashes
}

function Get-BootstrapImplementationIlFingerprint {
    param([Parameter(Mandatory = $true)][object[]] $Methods)

    $bootstrapMethods = @($Methods | Where-Object {
        $_.Name -like 'AgentBootstrap*' -or
        $_.Name -match '^<(?:ReadSingleFrameAndClearKeyAsync|EnsureEndOfStreamAsync|ReadExactAsync)>d__\d+::'
    })
    if ($bootstrapMethods.Count -lt 35) {
        throw "解析到的 bootstrap 实现方法过少：$($bootstrapMethods.Count)，拒绝假绿。"
    }
    $surface = @(Sort-OrdinalStrings -Values ([string[]] @(
        foreach ($method in $bootstrapMethods) {
            '{0}|{1}|{2}' -f
                $method.Name,
                (Normalize-IlSurfaceText -Text $method.Header),
                (Normalize-IlSurfaceText -Text $method.Body)
        }
    )))
    return [pscustomobject]@{
        MethodCount = $bootstrapMethods.Count
        Sha256 = Get-TextSha256 -Text ($surface -join "`n")
    }
}

$expectedIpcPublicTypeNames = @(
    'Codex.AutoCAD.Ipc.AgentBootstrapDirectionKeys',
    'Codex.AutoCAD.Ipc.AgentBootstrapException',
    'Codex.AutoCAD.Ipc.AgentBootstrapPayload',
    'Codex.AutoCAD.Ipc.AgentBootstrapProtocol',
    'Codex.AutoCAD.Ipc.AgentBootstrapValidationCode',
    'Codex.AutoCAD.Ipc.IIpcClock',
    'Codex.AutoCAD.Ipc.IpcCanonicalEnvelopeEncoding',
    'Codex.AutoCAD.Ipc.IpcEnvelopeAuthenticator',
    'Codex.AutoCAD.Ipc.IpcSessionGuard',
    'Codex.AutoCAD.Ipc.IpcSessionGuardOptions',
    'Codex.AutoCAD.Ipc.IpcSessionSecret',
    'Codex.AutoCAD.Ipc.IpcValidationCode',
    'Codex.AutoCAD.Ipc.SystemIpcClock'
)
$criticalBootstrapMethodNames = @(
    'AgentBootstrapPayload::DeriveDirectionKeys',
    'AgentBootstrapPayload::BeginSingleFrameWrite',
    'AgentBootstrapPayload::CompleteSingleFrameWrite',
    'AgentBootstrapPayload::FailSingleFrameWrite',
    'AgentBootstrapDirectionKeys::CreateOutboundAuthenticator',
    'AgentBootstrapDirectionKeys::CreateInboundGuard',
    'AgentBootstrapDirectionKeys::ClaimDirectionalKey',
    'AgentBootstrapProtocol::CreateAuthenticationKey',
    'AgentBootstrapProtocol::WriteSingleFrameAndClearKey',
    'AgentBootstrapProtocol::ReadSingleFrameAndClearKey',
    'AgentBootstrapProtocol::DecodeSingleFrameAndClear',
    'AgentBootstrapProtocol::DecodeFrameCore',
    'AgentBootstrapProtocol::EncodeFrame'
)
$expectedCompiledBoundaries = @{
    net45 = @{
        PublicApiCount = 105
        PublicApiSha256 = '3774045C2DA9F89C61AD80BC9C025B84558B9C9EE036DE2857B4678147AA2491'
        MemberReferenceCount = 95
        MemberReferenceSha256 = '5663EF4CE5C6B0AB9968614703CF087758BE3F37FAAA734436602E2874A15532'
        BootstrapImplementationMethodCount = 57
        BootstrapImplementationIlSha256 = '30B7A3E43B5FF088099FEBA8305A32B44F2A38A725A3CD57B25C5ED955CA7B1C'
        CriticalMethodBodyHashes = @{
            'AgentBootstrapPayload::DeriveDirectionKeys' = 'E9A8690EAE4C0FC8DC77480A2F5D6C946F45F062EF871BF87767999E8AAEED58'
            'AgentBootstrapPayload::BeginSingleFrameWrite' = '6F30DA6B30B34207A499E235C6B73EF88C3500BF029C1EADD230760777908787'
            'AgentBootstrapPayload::CompleteSingleFrameWrite' = '6808F4E6A89353AD2F86889EDE52B942BEB1ADA03C7C969D7425BA04B310638D'
            'AgentBootstrapPayload::FailSingleFrameWrite' = 'A3CEF3D17938F9241E52633A36DBEE0B5F59DFCDEC4F9D6B48DC214AFEEA9F48'
            'AgentBootstrapDirectionKeys::CreateOutboundAuthenticator' = 'CFEA5E960D2B41B6B176B7C01B63C491BC58C118F6413AE631D6424E26A8FB46'
            'AgentBootstrapDirectionKeys::CreateInboundGuard' = '4E731ED913247C15C28AFF7AD8E7461CB6ABA125CD64D84F9338FD640E2F1F99'
            'AgentBootstrapDirectionKeys::ClaimDirectionalKey' = 'E4AE555C67BAAE1DCA515101B4B606BE1CF2A364245CE9E801454FD381667CE4'
            'AgentBootstrapProtocol::CreateAuthenticationKey' = 'DB119D49B5F127F24BF3F4810D9A80AA836EB9920B905A7C177F27714189D7D7'
            'AgentBootstrapProtocol::WriteSingleFrameAndClearKey' = '8C84C05674EF6DA0E005192F5B40B2A79B245F1DED64E52E68B81A18590CBFD7'
            'AgentBootstrapProtocol::ReadSingleFrameAndClearKey' = '35CBEAFA548BFC93D00E220D15A9847F177BA56A96E99D605607B9424D9B52BA'
            'AgentBootstrapProtocol::DecodeSingleFrameAndClear' = 'DB37159A8AF92754CB2B888EF987DB2C4F879F0F8DB92E9F5B2A3352616C5299'
            'AgentBootstrapProtocol::DecodeFrameCore' = '581F91771907CC99E6F3F3F269F4782B19A1492C117B78B1CDF09498FF194201'
            'AgentBootstrapProtocol::EncodeFrame' = '2D9E3AB916F93A9A91B8640E4BEE2C3F484C5D7F13BF71B087C751E586125353'
        }
    }
    net8 = @{
        PublicApiCount = 105
        PublicApiSha256 = '3774045C2DA9F89C61AD80BC9C025B84558B9C9EE036DE2857B4678147AA2491'
        MemberReferenceCount = 99
        MemberReferenceSha256 = '0CCC0AB0DE04BAAD8C3129467E8BD119E2DAC9D9486ACC0EC3E6A8B547759A99'
        BootstrapImplementationMethodCount = 57
        BootstrapImplementationIlSha256 = '3E4264D968CA2D6402E21D9522AFB968BEB5BDA4D17ACA3C68F49B3244562E26'
        CriticalMethodBodyHashes = @{
            'AgentBootstrapPayload::DeriveDirectionKeys' = 'DF0C06C620AF67F41393D1BC546010860ADC9FB703F4B5F8DD67352DD50EF595'
            'AgentBootstrapPayload::BeginSingleFrameWrite' = '403F0F40730D18B48B9335B8CA68B0849A1F433A2743B61008658E8C04F0842C'
            'AgentBootstrapPayload::CompleteSingleFrameWrite' = 'E0CCC53FE1F482FA4AE5E799C97CAC93E65228A8EFB0D8308A2FB8D030E8614E'
            'AgentBootstrapPayload::FailSingleFrameWrite' = '56D82E131B66F82F8D28CF62886A43C23085A508EC33B1B1AA8EA57F5A8808F4'
            'AgentBootstrapDirectionKeys::CreateOutboundAuthenticator' = '65BD107B5229418E4C26E5AD53ABBA8627A8034E8234227A03A681117BD7CC5A'
            'AgentBootstrapDirectionKeys::CreateInboundGuard' = '06BC8CC4C3CA5AB69698CB5AB676C81A568B17A655B23F8449C3832879A2A1C7'
            'AgentBootstrapDirectionKeys::ClaimDirectionalKey' = '3FFFBD2B104F38122B0398C05919D5A4D8AEC1D8360BA4F99B6A75BCF48D6489'
            'AgentBootstrapProtocol::CreateAuthenticationKey' = '7B931702F74A8E859A0BDD221C1A278ED27B2D5E1F045A41662A77743172311A'
            'AgentBootstrapProtocol::WriteSingleFrameAndClearKey' = '55FB0C0F8D62AD9E13742961747AE1D309CDF27AF01ACA343A4949B666DB5A5F'
            'AgentBootstrapProtocol::ReadSingleFrameAndClearKey' = 'EE95FDD3FDFD270CD23C3854994B1DB3CFE280C24B8572E2442E858F15080609'
            'AgentBootstrapProtocol::DecodeSingleFrameAndClear' = '8726CF95B08EB2DEF4378CA51551018BCA878BD197D3CCCE8514F73E5642FB96'
            'AgentBootstrapProtocol::DecodeFrameCore' = '87C5FDE2E083B490B735476DD287518DA43CF01D3D4BA5D17C24E4B0A0452389'
            'AgentBootstrapProtocol::EncodeFrame' = '4306EDF03226E2AC1B5B4C1D17996490E629FD79B9F7E9939585B27E1F8EE37D'
        }
    }
}

function Assert-BootstrapAssemblyBoundary {
    param(
        [Parameter(Mandatory = $true)][string] $IpcAssemblyPath,
        [Parameter(Mandatory = $true)] $Ildasm,
        [Parameter(Mandatory = $true)][ValidateSet('net45', 'net8')][string] $RuntimeLabel
    )

    $ilPath = Join-Path $stageRoot ("Codex.AutoCAD.Ipc." + $RuntimeLabel + ".il")
    Invoke-Captured -FilePath $Ildasm.Path -Arguments @(
        '/text', '/nobar', '/tokens', '/utf8', '/caverbal', ("/out=" + $ilPath), $IpcAssemblyPath
    ) -Description ("反汇编 " + $RuntimeLabel + " IPC 以检查完整程序集边界") | Out-Null
    if (-not (Test-Path -LiteralPath $ilPath -PathType Leaf)) {
        throw "ildasm 未生成 $RuntimeLabel IPC IL 输出。"
    }
    $ilText = Get-Content -LiteralPath $ilPath -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($ilText)) {
        throw "$RuntimeLabel IPC IL 输出为空。"
    }
    if ($ilText -match '(?m)^\s*\.module\s+extern\b' -or $ilText -match '(?i)\bpinvokeimpl\b') {
        throw "$RuntimeLabel IPC 程序集不得包含 ModuleRef、ImplMap 或 P/Invoke。"
    }

    $memberReferences = Get-IlMemberReferenceMap -IlText $ilText
    if ($memberReferences.Count -eq 0) {
        throw "未解析到 $RuntimeLabel IPC MemberRef，拒绝假绿。"
    }
    $forbiddenMemberPattern = '(?i)(?:System\.IO\.(?:File|Directory|Path|FileStream|FileInfo|DirectoryInfo|DriveInfo)|System\.IO\.Pipes|NamedPipe|MemoryMappedFile|System\.Diagnostics\.(?:Process|ProcessStartInfo)|System\.Net\.|HttpClient|WebRequest|Socket|TcpClient|UdpClient|Microsoft\.Win32|RegistryKey|System\.Reflection\.Assembly::(?:Load|LoadFrom|LoadFile|GetType|GetTypes|GetExportedTypes|CreateInstance)\(|System\.Reflection\.(?:MethodBase|MethodInfo|ConstructorInfo|PropertyInfo|FieldInfo|EventInfo|MemberInfo)::|System\.Type::(?:GetType|GetMethod|GetMethods|GetConstructor|GetConstructors|GetProperty|GetProperties|GetField|GetFields|InvokeMember)\(|System\.Activator::|System\.Delegate::DynamicInvoke\(|System\.Linq\.Expressions|System\.Runtime\.InteropServices\.(?:Marshal|NativeLibrary)::|DllImport|LoadLibrary|GetProcAddress|NativeLibrary|System\.Threading\.Tasks\.Task::Run\(|System\.Threading\.(?:Thread|ThreadPool|Timer)|System\.Environment::(?:GetEnvironmentVariable|GetEnvironmentVariables|GetCommandLineArgs)\(|System\.Console::|System\.Diagnostics\.(?:Trace|Debug|EventLog)::|ILogger|Autodesk\.AutoCAD)'
    $selfTestSamples = @(
        '[mscorlib]System.IO.File::ReadAllBytes(string)',
        '[mscorlib]System.IO.FileInfo::.ctor(string)',
        '[mscorlib]System.IO.DirectoryInfo::GetFiles()',
        '[System]System.IO.Pipes.NamedPipeClientStream::.ctor(string)',
        '[System]System.Diagnostics.Process::Start(string)',
        '[mscorlib]System.Type::GetType(string)',
        '[mscorlib]System.Activator::CreateInstance(class System.Type)',
        '[mscorlib]System.Reflection.MethodInfo::Invoke(object, object[])',
        '[mscorlib]System.Threading.Tasks.Task::Run(class System.Action)',
        '[AcMgd]Autodesk.AutoCAD.ApplicationServices.Application::Quit()'
    )
    foreach ($sample in $selfTestSamples) {
        if ($sample -notmatch $forbiddenMemberPattern) {
            throw "$RuntimeLabel IPC IL 禁止 MemberRef 自检未覆盖：$sample"
        }
    }
    $violations = @($memberReferences.Values | Where-Object { $_ -match $forbiddenMemberPattern })
    if ($violations.Count -ne 0) {
        throw "$RuntimeLabel IPC 程序集命中禁止 MemberRef：$($violations -join ', ')"
    }

    $normalizedMemberReferences = @(Sort-OrdinalUniqueStrings -Values ([string[]] @(
        $memberReferences.Values | ForEach-Object { Normalize-IlSurfaceText -Text $_ }
    )))
    $memberReferenceSha256 = Get-TextSha256 -Text ($normalizedMemberReferences -join "`n")

    $publicApi = Get-IlPublicApiSurface -IlText $ilText
    if (@(Compare-Object `
        -ReferenceObject $expectedIpcPublicTypeNames `
        -DifferenceObject $publicApi.PublicTypeNames `
        -CaseSensitive).Count -ne 0 -or
        $publicApi.PublicTypeNames.Count -ne $expectedIpcPublicTypeNames.Count) {
        throw "$RuntimeLabel IPC 公共顶层 TypeDef 与精确白名单不一致：$($publicApi.PublicTypeNames -join ', ')"
    }
    $publicApiParserSelfTests = @(
        '^M\|Codex\.AutoCAD\.Ipc\.AgentBootstrapException\|AgentBootstrapException::\.ctor\|',
        '^P\|Codex\.AutoCAD\.Ipc\.AgentBootstrapException\|.*ValidationCode\(\)',
        '^P\|Codex\.AutoCAD\.Ipc\.AgentBootstrapPayload\|.*SessionId\(\)',
        '^P\|Codex\.AutoCAD\.Ipc\.AgentBootstrapDirectionKeys\|.*PipeName\(\)',
        '^F\|Codex\.AutoCAD\.Ipc\.AgentBootstrapProtocol\|.*CurrentVersion',
        '^F\|Codex\.AutoCAD\.Ipc\.AgentBootstrapValidationCode\|.*InvalidPayloadState'
    )
    foreach ($pattern in $publicApiParserSelfTests) {
        if (@($publicApi.Lines | Where-Object { $_ -match $pattern }).Count -ne 1) {
            throw "$RuntimeLabel IPC 公共 API IL 解析器自检未精确命中：$pattern"
        }
    }
    if (@($publicApi.Lines | Where-Object {
        $_ -match '(?i)(?:BootstrapUnsafe|ExportMaterial|SessionSecret.*(?:Get|Copy)|AgentBootstrapPayload\|.*\.ctor|AgentBootstrapDirectionKeys\|.*\.ctor)'
    }).Count -ne 0) {
        throw "$RuntimeLabel IPC 公共 API 出现 bootstrap 秘密导出、公共构造或绕过面。"
    }

    $methods = @(Get-IlMethodDefinitions -IlText $ilText)
    $criticalMethodBodyHashes = Get-CriticalMethodBodyHashes `
        -Methods $methods `
        -Names $criticalBootstrapMethodNames
    $bootstrapImplementation = Get-BootstrapImplementationIlFingerprint -Methods $methods

    $expected = $expectedCompiledBoundaries[$RuntimeLabel]
    if ($null -eq $expected) {
        throw "缺少 $RuntimeLabel 编译边界冻结值。"
    }
    if ($expected.PublicApiSha256 -ceq 'PENDING' -or
        $expected.MemberReferenceSha256 -ceq 'PENDING' -or
        $expected.BootstrapImplementationIlSha256 -ceq 'PENDING' -or
        $expected.CriticalMethodBodyHashes.Count -eq 0) {
        return [pscustomobject]@{
            Runtime = $RuntimeLabel
            PublicApiCount = $publicApi.Count
            PublicApiSha256 = $publicApi.Sha256
            MemberReferenceCount = $normalizedMemberReferences.Count
            MemberReferenceSha256 = $memberReferenceSha256
            BootstrapImplementationMethodCount = $bootstrapImplementation.MethodCount
            BootstrapImplementationIlSha256 = $bootstrapImplementation.Sha256
            CriticalMethodBodyHashes = $criticalMethodBodyHashes
            MethodDefinitionCount = $methods.Count
            PublicTypeNames = $publicApi.PublicTypeNames
            ModuleRefCount = 0
            ImplMapCount = 0
            BaselinePending = $true
        }
    }
    if ([int]$expected.PublicApiCount -ne $publicApi.Count -or
        [string]$expected.PublicApiSha256 -cne $publicApi.Sha256) {
        throw "$RuntimeLabel IPC 公共 API 冻结值不一致：count=$($publicApi.Count), sha256=$($publicApi.Sha256)。"
    }
    if ([int]$expected.MemberReferenceCount -ne $normalizedMemberReferences.Count -or
        [string]$expected.MemberReferenceSha256 -cne $memberReferenceSha256) {
        throw "$RuntimeLabel IPC MemberRef 精确白名单不一致：count=$($normalizedMemberReferences.Count), sha256=$memberReferenceSha256。"
    }
    if ([int]$expected.BootstrapImplementationMethodCount -ne
            $bootstrapImplementation.MethodCount -or
        [string]$expected.BootstrapImplementationIlSha256 -cne
            $bootstrapImplementation.Sha256) {
        throw "$RuntimeLabel bootstrap 完整实现 IL 冻结值不一致：count=$($bootstrapImplementation.MethodCount), sha256=$($bootstrapImplementation.Sha256)。"
    }
    foreach ($methodName in $criticalBootstrapMethodNames) {
        if (-not $expected.CriticalMethodBodyHashes.ContainsKey($methodName) -or
            [string]$expected.CriticalMethodBodyHashes[$methodName] -cne
            [string]$criticalMethodBodyHashes[$methodName]) {
            throw "$RuntimeLabel 关键状态机 IL 冻结值不一致：$methodName。"
        }
    }

    Write-Host "$RuntimeLabel 编译后 IPC 公共 API、MemberRef 与关键状态机 IL 边界通过。" -ForegroundColor Green
    return [pscustomobject]@{
        Runtime = $RuntimeLabel
        MemberReferenceCount = $normalizedMemberReferences.Count
        MemberReferenceSha256 = $memberReferenceSha256
        MethodDefinitionCount = $methods.Count
        PublicTypeNames = $publicApi.PublicTypeNames
        PublicApiCount = $publicApi.Count
        PublicApiSha256 = $publicApi.Sha256
        BootstrapImplementationMethodCount = $bootstrapImplementation.MethodCount
        BootstrapImplementationIlSha256 = $bootstrapImplementation.Sha256
        CriticalMethodBodyHashes = $criticalMethodBodyHashes
        ModuleRefCount = 0
        ImplMapCount = 0
        BaselinePending = $false
    }
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
    $expectedBridgeSummary = "^\s*$expectedBridgeSpecCount/$expectedBridgeSpecCount specs passed\s*$"
    if (@($bridgeOutput | Where-Object { $_ -match $expectedBridgeSummary }).Count -ne 1) {
        throw "Bridge 回归必须精确通过 $expectedBridgeSpecCount/$expectedBridgeSpecCount。"
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

    $passLines = @($Lines | Where-Object { $_ -match '^PASS\s+' })
    $failLines = @($Lines | Where-Object { $_ -match '^FAIL\s+' })
    if ($passLines.Count -ne $expectedSpecCount -or $failLines.Count -ne 0) {
        throw "$RuntimeLabel 必须精确输出 $expectedSpecCount 条 PASS 且零条 FAIL；实际 PASS=$($passLines.Count)，FAIL=$($failLines.Count)。"
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

    $bootstrapPattern = '^BOOTSTRAP_VECTOR_V1 version=(?<Version>1) frame=(?<Frame>[0-9A-F]{360}) tag=(?<Tag>[0-9A-F]{64}) h2a_ctx_sha256=(?<H2aContext>[0-9A-F]{64}) h2a_key=(?<H2aKey>[0-9A-F]{64}) h2a_mac=(?<H2aMac>[0-9A-F]{64}) a2h_ctx_sha256=(?<A2hContext>[0-9A-F]{64}) a2h_key=(?<A2hKey>[0-9A-F]{64}) a2h_mac=(?<A2hMac>[0-9A-F]{64})$'
    $bootstrapVectors = @($Lines | Where-Object { $_ -match '^BOOTSTRAP_VECTOR_V1\s+' })
    if ($bootstrapVectors.Count -ne 1 -or $bootstrapVectors[0] -notmatch $bootstrapPattern) {
        throw "$RuntimeLabel 必须且只能输出一条严格字段顺序的 BOOTSTRAP_VECTOR_V1。"
    }
    $bootstrapMatch = [regex]::Match($bootstrapVectors[0], $bootstrapPattern)
    $expectedBootstrapFields = [ordered]@{
        Frame = $expectedBootstrapFrame
        Tag = $expectedBootstrapTag
        H2aContext = $expectedHostToAgentContextSha256
        H2aKey = $expectedHostToAgentKey
        H2aMac = $expectedHostToAgentMac
        A2hContext = $expectedAgentToHostContextSha256
        A2hKey = $expectedAgentToHostKey
        A2hMac = $expectedAgentToHostMac
    }
    foreach ($entry in $expectedBootstrapFields.GetEnumerator()) {
        if ($bootstrapMatch.Groups[$entry.Key].Value -cne $entry.Value) {
            throw "$RuntimeLabel 的 bootstrap 固定向量字段不一致：$($entry.Key)。"
        }
    }
    if ($bootstrapMatch.Groups['Tag'].Value -cne
        $bootstrapMatch.Groups['Frame'].Value.Substring($bootstrapMatch.Groups['Frame'].Value.Length - 64)) {
        throw "$RuntimeLabel 的 bootstrap tag 不是 frame 最后 32 字节。"
    }

    return [pscustomobject]@{
        AuthVector = $vectors[0]
        BootstrapVector = $bootstrapVectors[0]
        PassCount = $passLines.Count
    }
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
    $bootstrapSourcePath,
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
$expectedSolutionProjectPaths = @(
    "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj",
    "src\Codex.AutoCAD.AgentLauncher\Codex.AutoCAD.AgentLauncher.csproj",
    "src\Codex.AutoCAD.AgentRuntime\Codex.AutoCAD.AgentRuntime.csproj",
    "src\Codex.AutoCAD.AppServer\Codex.AutoCAD.AppServer.csproj",
    "src\Codex.AutoCAD.Bridge\Codex.AutoCAD.Bridge.csproj",
    "src\Codex.AutoCAD.Bridge.Client\Codex.AutoCAD.Bridge.Client.csproj",
    "src\Codex.AutoCAD.Contracts\Codex.AutoCAD.Contracts.csproj",
    "src\Codex.AutoCAD.Host.2025\Codex.AutoCAD.Host.2025.csproj",
    "src\Codex.AutoCAD.Ipc\Codex.AutoCAD.Ipc.csproj",
    "src\Codex.AutoCAD.Security\Codex.AutoCAD.Security.csproj",
    "tests\Codex.AutoCAD.AgentLauncher.FakeAgentHost\Codex.AutoCAD.AgentLauncher.FakeAgentHost.csproj",
    "tests\Codex.AutoCAD.AgentLauncher.Specs\Codex.AutoCAD.AgentLauncher.Specs.csproj",
    "tests\Codex.AutoCAD.AgentRuntime.Specs\Codex.AutoCAD.AgentRuntime.Specs.csproj",
    "tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj",
    "tests\Codex.AutoCAD.Bridge.Specs\Codex.AutoCAD.Bridge.Specs.csproj",
    "tests\Codex.AutoCAD.Bridge.Client.Specs\Codex.AutoCAD.Bridge.Client.Specs.csproj",
    "tests\Codex.AutoCAD.Bridge.Client.TestServer\Codex.AutoCAD.Bridge.Client.TestServer.csproj",
    "tests\Codex.AutoCAD.Chat.Specs\Codex.AutoCAD.Chat.Specs.csproj",
    "tests\Codex.AutoCAD.Contracts.Specs\Codex.AutoCAD.Contracts.Specs.csproj",
    "tests\Codex.AutoCAD.Ipc.Specs\Codex.AutoCAD.Ipc.Specs.csproj",
    "tests\Codex.AutoCAD.Security.Specs\Codex.AutoCAD.Security.Specs.csproj",
    "tests\Codex.AutoCAD.Host.2016.Mvp.Specs\Codex.AutoCAD.Host.2016.Mvp.Specs.csproj"
) | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $repoRoot $_)) } |
    Sort-Object -Unique
$solutionProjectDifference = @(
    Compare-Object -ReferenceObject $expectedSolutionProjectPaths `
        -DifferenceObject $solutionProjectPaths
)
if ($solutionProjectPaths.Count -ne $expectedSolutionProjectPaths.Count -or
    $solutionProjectDifference.Count -ne 0) {
    $detail = @(
        $solutionProjectDifference |
            ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
    ) -join "; "
    throw "主解决方案项目清单必须精确匹配$($expectedSolutionProjectPaths.Count)个批准项目；实际：$($solutionProjectPaths.Count)；差异：$detail"
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

    Assert-ReviewedTextInputHygiene -Paths $sourcePaths
    $independentBootstrapVector = Assert-IndependentBootstrapVector
    $ildasmEvidence = Resolve-TrustedIldasm
    Assert-ProjectShape
    Assert-AuthenticationSourceBoundary
    Assert-BootstrapSourceBoundary

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

    $bootstrapAssemblyBoundary = [ordered]@{
        Net45 = (Assert-BootstrapAssemblyBoundary `
            -IpcAssemblyPath $buildA.Net45Ipc `
            -Ildasm $ildasmEvidence `
            -RuntimeLabel 'net45')
        Net8 = (Assert-BootstrapAssemblyBoundary `
            -IpcAssemblyPath $buildA.Net8Ipc `
            -Ildasm $ildasmEvidence `
            -RuntimeLabel 'net8')
    }
    $pendingCompiledBoundaries = @($bootstrapAssemblyBoundary.Values | Where-Object {
        $_.BaselinePending
    })
    if ($pendingCompiledBoundaries.Count -ne 0) {
        $diagnostic = $pendingCompiledBoundaries | ConvertTo-Json -Depth 8 -Compress
        throw "PENDING_COMPILED_BOUNDARIES=$diagnostic"
    }
    if ($bootstrapAssemblyBoundary.Net45.PublicApiCount -ne
            $bootstrapAssemblyBoundary.Net8.PublicApiCount -or
        $bootstrapAssemblyBoundary.Net45.PublicApiSha256 -cne
            $bootstrapAssemblyBoundary.Net8.PublicApiSha256) {
        throw "net45 与 net8 IPC 规范化公共 API 面不一致。"
    }

    $net45Output = Invoke-Captured -FilePath $buildA.Net45Specs -Arguments @() -Description "运行 net45 认证规格"
    $net8Output = Invoke-Captured -FilePath $dotnetCommand -Arguments @($buildA.Net8Specs) -Description "运行 net8 认证规格"
    $net45Vector = Assert-SpecOutput -Lines $net45Output -RuntimeLabel "net45"
    $net8Vector = Assert-SpecOutput -Lines $net8Output -RuntimeLabel "net8"
    if ($net45Vector.AuthVector -cne $net8Vector.AuthVector -or
        $net45Vector.BootstrapVector -cne $net8Vector.BootstrapVector) {
        throw "net45 与 net8 认证或 bootstrap 固定向量输出不一致。"
    }
    Write-Host "net45/net8 canonical、bootstrap frame、KDF 与双向 HMAC 固定向量完全一致。" -ForegroundColor Green

    Invoke-Captured -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $safeRepoRoot), "-C", $repoRoot, "diff", "--check"
    ) -Description "检查未暂存差异格式" | Out-Null
    Invoke-Captured -FilePath "git" -Arguments @(
        "-c", ("safe.directory=" + $safeRepoRoot), "-C", $repoRoot, "diff", "--cached", "--check"
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
        SchemaVersion = 3
        RecordedAtLocal = [DateTimeOffset]::Now.ToString("o")
        Scope = "autocad2016-net45-net8-auth-and-bootstrap-primitive"
        Status = "static-and-cross-runtime-bootstrap-primitive-gate-passed"
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
        BridgeRegressionSpecs = "$expectedBridgeSpecCount/$expectedBridgeSpecCount"
        BridgeRegressionRuntimeArtifactHashes = $managedCoreRegression.RuntimeArtifactHashes
        BridgeProjectOutputSha256 = $managedCoreRegression.BridgeProjectOutputSha256
        BridgeRuntimeCopyMatchesProjectOutput = $managedCoreRegression.RuntimeBridgeCopyMatchesProjectOutput
        Ildasm = [ordered]@{
            Version = $ildasmEvidence.Version
            Sha256 = $ildasmEvidence.Sha256
            SignatureStatus = $ildasmEvidence.SignatureStatus
            SignerSubject = $ildasmEvidence.SignerSubject
        }
        BootstrapAssemblyBoundary = $bootstrapAssemblyBoundary
        CanonicalHex = $expectedCanonical
        HmacSha256 = $expectedMac
        CanonicalLengthRule = "decimal UTF-16 code-unit count prefixes, then strict UTF-8"
        NullSignedFieldsRejected = $true
        ExactSecretBytes = 32
        SequenceStrictlyIncrementsByOne = $true
        NonceReplayRejected = $true
        InvalidMacDoesNotAdvanceState = $true
        SecretPrivateCopyZeroedOnDispose = $true
        BootstrapKnownVectorIsPublicTestMaterial = $true
        BootstrapVersion = $independentBootstrapVector.Version
        BootstrapFrameHex = $independentBootstrapVector.Frame
        BootstrapFrameSha256 = $independentBootstrapVector.FrameSha256
        BootstrapFrameBytes = $independentBootstrapVector.FrameBytes
        BootstrapBodyBytes = $independentBootstrapVector.BodyBytes
        BootstrapTag = $independentBootstrapVector.Tag
        HostToAgentContextSha256 = $independentBootstrapVector.HostToAgentContextSha256
        HostToAgentKey = $independentBootstrapVector.HostToAgentKey
        HostToAgentMac = $independentBootstrapVector.HostToAgentMac
        AgentToHostContextSha256 = $independentBootstrapVector.AgentToHostContextSha256
        AgentToHostKey = $independentBootstrapVector.AgentToHostKey
        AgentToHostMac = $independentBootstrapVector.AgentToHostMac
        ExternalAuthenticationKeyRequired = $true
        AuthenticationAndSessionKeyReuseRejected = $true
        SingleFrameAndEofRequired = $true
        SyncAndAsyncAll180TruncationOffsetsRejected = $true
        CancellationTokenPreBodyAndEofPathsVerified = $true
        KdfBoundFields = @('version', 'role', 'bootstrapId', 'sessionId', 'pipeName')
        DirectionReflectionRejectedWithoutStateAdvance = $true
        PayloadSingleUse = $true
        SingleFrameWriteAttempt = $true
        FailedWriteConsumesPayload = $true
        InboundPayloadForwardingRejected = $true
        InboundForwardingAttemptConsumesPayload = $true
        OutboundDerivationRequiresSuccessfulWrite = $true
        EndpointRoleBoundByPayloadOrigin = $true
        InboundAndOutboundClaimedOnce = $true
        CallerSuppliedFrameAndAuthenticationKeyClearedOnPublicProtocolExitPaths = $true
        ProtocolOwnedManagedSecretBuffersClearedOnCoveredPaths = $true
        AllRuntimeAndTargetStreamSecretCopiesEliminated = $false
        BootstrapSourceBoundaryVerified = $true
        BootstrapCompiledMemberRefBoundaryVerifiedForNet45AndNet8 = $true
        BootstrapCompiledPublicApiBoundaryVerifiedForNet45AndNet8 = $true
        BootstrapCriticalStateMachineIlVerifiedForNet45AndNet8 = $true
        BootstrapCompleteImplementationIlFingerprintVerifiedForNet45AndNet8 = $true
        SourceInputs = $sourceBefore
        ProjectLocalObjModified = $false
        ProjectLocalObjRootCount = $projectObjRoots.Count
        ProjectLocalObjScope = "all csproj entries in Codex.AutoCAD.sln"
        AutoCadProcessSetChanged = $false
        AutoCadStartedOrRestarted = $false
        CadCommandsSent = $false
        NetLoadAttempted = $false
        NetLoadVerified = $false
        ExternalAuthenticationKeyDeliveryLiveVerified = $false
        BootstrapTransportConfidentialityLiveVerified = $false
        PendingBootstrapAtomicConsumptionLiveVerified = $false
        ChildProcessIdentityBindingLiveVerified = $false
        HardTimeoutAndProcessLifecycleLiveVerified = $false
        AgentHostLiveBridgeVerified = $false
        RuntimeToCadCandidateBindingVerified = $false
        EvidenceBoundary = "This gate proves the in-memory/Stream bootstrap protocol primitive, public known vectors, cross-runtime bytes, explicit managed-buffer cleanup on covered paths, single-attempt sender and receiver-origin state, endpoint-bound one-time direction claims, and local fail-closed behavior. It does not prove confidentiality of the live bootstrap transport, elimination of runtime or target Stream copies, out-of-band key delivery, PID/start-identity binding, pending bootstrap atomic consumption, hard process deadlines, a live AgentHost handshake, or any AutoCAD runtime integration."
    }
    $evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $evidencePath -Encoding UTF8

    Write-Host "`nAutoCAD 2016 跨框架认证与 bootstrap 协议原语门禁通过。" -ForegroundColor Green
    Write-Host ("AUTH_COMPAT_EVIDENCE=" + $evidencePath)
}
finally {
    [IO.File]::WriteAllBytes($conditionalLockPath, $conditionalLockBytes)
    $env:DOTNET_NOLOGO = $previousNoLogo
}

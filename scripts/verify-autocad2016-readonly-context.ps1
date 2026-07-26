[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$MsBuildPath,

    [switch]$RuleSelfTestOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'build-safety.ps1')
$buildSafety = Initialize-CodexBuildSafety -RepoRoot $repoRoot
$artifactsRoot = $buildSafety.ArtifactRoot
$projectRoot = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016.ReadOnlyContext'
$projectPath = Join-Path $projectRoot 'Codex.AutoCAD.Host.2016.ReadOnlyContext.csproj'
$solutionPath = Join-Path $repoRoot 'Codex.AutoCAD.2016.ReadOnlyContext.sln'
$specProjectPath = Join-Path $repoRoot 'tests\Codex.AutoCAD.Host.2016.ReadOnlyContext.Specs\Codex.AutoCAD.Host.2016.ReadOnlyContext.Specs.csproj'
$nuGetConfigPath = Join-Path $projectRoot 'NuGet.Config'
$packageLockPath = Join-Path $projectRoot 'packages.lock.json'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)

$expectedCandidateSha256 = 'AB3132CF7B0102F9A9B168A76170D074114051D1759391DF9F3C5C6969BAE6B8'
$expectedCandidateSize = 31744
$expectedNormalizedIlSha256 = '84718C560AE997F119EF97A21DB14D52F38635FCE52322AF87DBC5E8605DA5EF'
$expectedMethodDefinitionCount = 181
$expectedMemberReferenceCount = 154
$expectedTypeDefinitionCount = 22
$expectedFieldDefinitionCount = 83

$expectedFileHashes = [ordered]@{
    'Codex.AutoCAD.2016.ReadOnlyContext.sln' = '302DF36EB87701CC6D834C9C3FC73504C5DBAA4A40FF4626523B80087591D30B'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\Codex.AutoCAD.Host.2016.ReadOnlyContext.csproj' = '93D8FE21095218A587F56E1D0056CA3E929BC5FF8FFE12D43A973CE0940634DA'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\NuGet.Config' = '26299200D114FA22041CFC6F17EC31099989244638FACCFE342B8B75D7523E50'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\packages.lock.json' = '1DB305BC7AAFBF0C8873457DDA8950688DC30923C373DC4CDA209016449A59C6'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlyContextExtension.cs' = '1A7A38DA9514E5D473051FFB861A147AD3723502F0F055EA6EFCC9D30050B2CB'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlyContextCommands.cs' = '46C33FD5044E7393CB5EBC17F5142DEBCA02FC5D88A4BA5C36D0F22087BC11B3'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlyContextRuntime.cs' = '56540EDD31CEDA369E1BEA0B3CD32BC5F35AE08BD2A2ADF08616911FAA18E1E6'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlySelectionCapture.cs' = '4B8F26D87D96CC064ED91D16FAAA6A1CF1521400A6B75A2FE27A1A6E7E9BFC0C'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\ReadOnlyContextSnapshot.cs' = '311CE546CE734EAD306582CBB347BEEBE85B351DC8450234BBABC113BB474D0E'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\CanonicalSelectionHash.cs' = '4832D767782340EF3F5CEE08B4428A4036221CD53F3A0BD6859E5B1124967488'
    'src\Codex.AutoCAD.Host.2016.ReadOnlyContext\Properties\AssemblyInfo.cs' = '0C2C708FAEBD998ACE17FF04841A994F63C63E6ABED907096D0986ECC49E4EAC'
    'tests\Codex.AutoCAD.Host.2016.ReadOnlyContext.Specs\Codex.AutoCAD.Host.2016.ReadOnlyContext.Specs.csproj' = 'FF31566444906D0E97ED97354C98740AB2B95D4C7BFE86089BB1908645FE94E1'
    'tests\Codex.AutoCAD.Host.2016.ReadOnlyContext.Specs\Program.cs' = '9A9AD853E2713BCE1D1C928CBF0810C4EE131543B56272285598A6EDAA15BDBF'
    'tests\Codex.AutoCAD.Host.2016.ReadOnlyContext.Specs\ReferenceSelectionEncoder.cs' = 'B7FEEA76ACDBC2562B1F36707AB750F29CF0ABC5A4899DF7B3D55ECFE938AB43'
}

$expectedCompileItems = @(
    'ReadOnlyContextExtension.cs',
    'ReadOnlyContextCommands.cs',
    'ReadOnlyContextRuntime.cs',
    'ReadOnlySelectionCapture.cs',
    'ReadOnlyContextSnapshot.cs',
    'CanonicalSelectionHash.cs',
    'Properties\AssemblyInfo.cs'
)

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-TextSha256 {
    param([string]$Text)
    $bytes = New-Object System.Text.UTF8Encoding($false, $true)
    $encoded = $bytes.GetBytes($Text)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($encoded))).Replace('-', '')
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        [Array]::Clear($encoded, 0, $encoded.Length)
    }
}

function Assert-Authenticode {
    param(
        [string]$Path,
        [string]$ExpectedPublisherPattern
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    Assert-True ($signature.Status -eq 'Valid') "签名无效：$Path ($($signature.Status))"
    Assert-True ($null -ne $signature.SignerCertificate) "缺少签名证书：$Path"
    Assert-True ($signature.SignerCertificate.Subject -match $ExpectedPublisherPattern) "签名发布者不符合预期：$Path"
}

function Resolve-MsBuild {
    if (-not [string]::IsNullOrWhiteSpace($MsBuildPath)) {
        $resolved = [IO.Path]::GetFullPath($MsBuildPath)
        Assert-True (Test-Path -LiteralPath $resolved -PathType Leaf) "MSBuild 不存在：$resolved"
        return $resolved
    }

    $command = Get-Command 'MSBuild.exe' -ErrorAction SilentlyContinue
    Assert-True ($null -ne $command) '未找到 MSBuild.exe；请显式传入 -MsBuildPath。'
    return $command.Source
}

function Resolve-Ildasm {
    $candidates = @(
        'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\ildasm.exe',
        'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw '未找到 Microsoft ildasm.exe。'
}

function Get-CompileClosure {
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    $namespaces = New-Object System.Xml.XmlNamespaceManager($project.NameTable)
    $namespaces.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $nodes = @($project.SelectNodes('//m:Compile', $namespaces))
    $items = @($nodes | ForEach-Object { [string]$_.Include })
    return [pscustomobject]@{
        Xml = $project
        Namespaces = $namespaces
        Items = $items
        Paths = @($items | ForEach-Object { Join-Path $projectRoot $_ })
    }
}

function Assert-SequenceEqual {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [string]$Message
    )

    $difference = @(Compare-Object -ReferenceObject $Expected -DifferenceObject $Actual -SyncWindow 0)
    if ($difference.Count -ne 0) {
        throw "$Message：$($difference | Out-String)"
    }
}

function Get-SourceFindings {
    param(
        [string]$Text,
        [string]$Path
    )

    $patterns = [ordered]@{
        'CAD write/open' = 'OpenMode\s*\.\s*ForWrite|UpgradeOpen|DowngradeOpen|AppendEntity|AddNewlyCreatedDBObject|\.\s*Commit\s*\(|\.\s*Abort\s*\(|\.\s*Erase\s*\('
        'CAD lock/save/command' = 'DocumentLock|LockDocument|SetSystemVariable|SetImpliedSelection|SendStringToExecute|SaveAs|DwgOut|DxfOut|CloseAndSave|ExecuteInCommandContext'
        'Sensitive document identity' = '(?i:document)\s*\.\s*Name|(?i:database)\s*\.\s*Filename|PathName'
        'Runtime type/reflection' = '\.\s*GetType\s*\(|System\.Reflection|Activator|Assembly\s*\.\s*Load|MethodInfo|Type\s*\.\s*GetType'
        'Process/IPC/network/file/registry' = 'System\.Diagnostics\.Process|NamedPipe|System\.IO\.|System\.Net\.|Microsoft\.Win32|Registry|FileStream|MemoryMappedFile'
        'Native/background execution' = 'DllImport|Marshal\s*\.|Task|Thread|Timer|BackgroundWorker'
        'Unapproved entity kind' = '\bArc\b|AttributeReference|AttributeCollection'
    }

    $findings = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $patterns.GetEnumerator()) {
        if ([regex]::IsMatch($Text, [string]$entry.Value)) {
            $findings.Add([pscustomobject]@{
                Path = $Path
                Category = [string]$entry.Key
            })
        }
    }

    return $findings.ToArray()
}

function Assert-ExactReadHelper {
    param([string]$CaptureSource)

    $transactionCalls = @([regex]::Matches($CaptureSource, 'transaction\s*\.\s*GetObject\s*\('))
    Assert-True ($transactionCalls.Count -eq 1) '源码必须只有一个 Transaction.GetObject 调用点。'
    Assert-True ([regex]::IsMatch(
        $CaptureSource,
        'transaction\s*\.\s*GetObject\s*\(\s*objectId\s*,\s*OpenMode\s*\.\s*ForRead\s*,\s*false\s*\)')) `
        'Transaction.GetObject 必须固定为 OpenMode.ForRead,false。'

    $openTransactions = @([regex]::Matches($CaptureSource, 'StartOpenCloseTransaction\s*\('))
    Assert-True ($openTransactions.Count -eq 1) '源码必须只有一个 StartOpenCloseTransaction 调用点。'
}

function Assert-RuleSelfTests {
    $safe = @'
private static DBObject OpenObjectForRead(Transaction transaction, ObjectId objectId)
{
    return transaction.GetObject(objectId, OpenMode.ForRead, false);
}
'@
    Assert-True (@(Get-SourceFindings -Text $safe -Path 'safe').Count -eq 0) '安全样本被源码规则误拒绝。'
    Assert-ExactReadHelper -CaptureSource ($safe + [Environment]::NewLine + 'StartOpenCloseTransaction();')

    $dangerousSamples = @(
        'transaction.GetObject(id, OpenMode.ForWrite, false);',
        'transaction.Commit();',
        'document.LockDocument();',
        'Application.SetSystemVariable("CLAYER", "0");',
        'editor.SetImpliedSelection(ids);',
        'document.SendStringToExecute("_.SAVE", true, false, false);',
        'var path = document.Name;',
        'var path = database.Filename;',
        'var path = record.PathName;',
        'var type = entity.GetType();',
        'System.Diagnostics.Process.Start("cmd.exe");',
        'new System.IO.FileStream("x", FileMode.Create);',
        'new NamedPipeClientStream("x");',
        'System.Threading.Tasks.Task.Run(() => Work());',
        '[DllImport("kernel32.dll")] static extern void X();',
        'Arc arc = entity as Arc;'
    )

    foreach ($sample in $dangerousSamples) {
        Assert-True (@(Get-SourceFindings -Text $sample -Path 'negative').Count -gt 0) "危险样本未被拒绝：$sample"
    }

    $helperRejected = $false
    try {
        Assert-ExactReadHelper -CaptureSource 'return transaction.GetObject(objectId, OpenMode.ForWrite, false); StartOpenCloseTransaction();'
    }
    catch {
        $helperRejected = $true
    }
    Assert-True $helperRejected 'ForWrite helper 负样本未被拒绝。'

    Write-Host 'ReadOnlyContext 源码规则正/负向自测通过。' -ForegroundColor Green
}

function Assert-ProjectGraph {
    $closure = Get-CompileClosure
    Assert-SequenceEqual -Actual $closure.Items -Expected $expectedCompileItems -Message 'Compile 闭包不匹配'

    foreach ($path in $closure.Paths) {
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Compile 文件不存在：$path"
        $resolved = [IO.Path]::GetFullPath($path)
        Assert-True ($resolved.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
            "Compile 文件越出项目根：$resolved"
    }

    $xml = $closure.Xml
    $ns = $closure.Namespaces
    Assert-True (($xml.SelectSingleNode('//m:TargetFrameworkVersion', $ns).'#text') -eq 'v4.5') 'TargetFrameworkVersion 必须为 v4.5。'
    Assert-True (($xml.SelectSingleNode('//m:PlatformTarget', $ns).'#text') -eq 'x64') 'PlatformTarget 必须为 x64。'
    Assert-True (($xml.SelectSingleNode('//m:AssemblyName', $ns).'#text') -eq 'Codex.AutoCAD.Host.2016.ReadOnlyContext') 'AssemblyName 不匹配。'

    $references = @($xml.SelectNodes('//m:Reference', $ns) | ForEach-Object { ([string]$_.Include).Split(',')[0] })
    Assert-SequenceEqual -Actual $references -Expected @('System', 'System.Core', 'accoremgd', 'acdbmgd', 'acmgd') -Message 'Reference 允许清单不匹配'

    foreach ($name in @('accoremgd', 'acdbmgd', 'acmgd')) {
        $node = $xml.SelectSingleNode("//m:Reference[starts-with(@Include,'$name,')]", $ns)
        Assert-True ($null -ne $node) "缺少 Autodesk 引用：$name"
        Assert-True (([string]$node.Private).ToLowerInvariant() -eq 'false') "Autodesk 引用必须 Private=false：$name"
        Assert-True (([string]$node.SpecificVersion).ToLowerInvariant() -eq 'true') "Autodesk 引用必须 SpecificVersion=true：$name"
    }

    Assert-True (($xml.SelectNodes('//m:ProjectReference', $ns).Count) -eq 0) 'Host 不得包含 ProjectReference。'
    Assert-True (($xml.SelectNodes('//m:PackageReference', $ns).Count) -eq 1) 'Host 只允许一个 net45 reference assemblies 包。'
    $package = $xml.SelectSingleNode('//m:PackageReference', $ns)
    Assert-True (([string]$package.Include) -eq 'Microsoft.NETFramework.ReferenceAssemblies.net45') 'PackageReference 不匹配。'
    Assert-True (([string]$package.Version) -eq '[1.0.3]') 'PackageReference 版本必须精确锁定为 [1.0.3]。'

    $imports = @($xml.SelectNodes('//m:Import', $ns) | ForEach-Object { [string]$_.Project })
    Assert-SequenceEqual -Actual $imports -Expected @('$(MSBuildToolsPath)\Microsoft.Common.props', '$(MSBuildToolsPath)\Microsoft.CSharp.targets') -Message 'Import 允许清单不匹配'

    $targets = @($xml.SelectNodes('//m:Target', $ns) | ForEach-Object { [string]$_.Name })
    Assert-SequenceEqual -Actual $targets -Expected @('ValidateAutoCad2016References', 'RejectAutodeskCopyLocal') -Message 'Target 允许清单不匹配'

    $solutionText = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
    Assert-True (([regex]::Matches($solutionText, '(?m)^Project\(')).Count -eq 4) 'solution 必须只含 src/tests 文件夹和两个项目。'
}

function Assert-FrozenInputs {
    foreach ($entry in $expectedFileHashes.GetEnumerator()) {
        $path = Join-Path $repoRoot ([string]$entry.Key)
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "冻结输入不存在：$($entry.Key)"
        $actual = Get-Sha256 $path
        Assert-True ($actual -eq [string]$entry.Value) "冻结输入哈希漂移：$($entry.Key) expected=$($entry.Value) actual=$actual"
    }
}

function Assert-SourceGate {
    $closure = Get-CompileClosure
    foreach ($path in $closure.Paths) {
        if ($path.EndsWith('Properties\AssemblyInfo.cs', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
        $findings = @(Get-SourceFindings -Text $text -Path $path)
        if ($findings.Count -gt 0) {
            foreach ($finding in $findings) {
                Write-Host "$($finding.Path): [$($finding.Category)]" -ForegroundColor Red
            }
            throw "ReadOnlyContext 源码禁止 API 扫描失败：$path"
        }
    }

    $capture = Get-Content -LiteralPath (Join-Path $projectRoot 'ReadOnlySelectionCapture.cs') -Raw -Encoding UTF8
    Assert-ExactReadHelper -CaptureSource $capture
    foreach ($required in @('Line', 'Circle', 'Polyline', 'DBText', 'MText', 'BlockReference')) {
        Assert-True ([regex]::IsMatch($capture, 'entity\s+as\s+' + [regex]::Escape($required))) "缺少固定实体分支：$required"
    }

    Assert-True (-not [regex]::IsMatch($capture, 'Math\s*\.\s*Min|Substring\s*\(')) '捕获路径禁止截断；超限必须整批拒绝。'

    $runtime = Get-Content -LiteralPath (Join-Path $projectRoot 'ReadOnlyContextRuntime.cs') -Raw -Encoding UTF8
    Assert-True (-not [regex]::IsMatch($runtime, '\block\s*\(|Monitor|DocumentCollection\s+documents|public\s+static\s+class\s+ReadOnlyContextFacade')) `
        'Runtime 禁止 lock/Monitor、持久 DocumentCollection 或公开 Facade。'
    Assert-True ([regex]::IsMatch($runtime, 'dbmodBefore\s*!=\s*dbmodAfter')) '发布前必须显式拒绝 DBMOD 变化。'
    Assert-True ([regex]::IsMatch($runtime, 'ReferenceEquals\s*\(\s*AutoCadApplication\.DocumentManager\.MdiActiveDocument\s*,\s*document\s*\)')) `
        '发布前必须验证活动 Document 身份未变化。'
}

function Normalize-IlText {
    param([string]$Path)
    $content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $content = [regex]::Replace($content, '(?m)^// Image base:.*$', '// Image base: <normalized>')
    $content = [regex]::Replace($content, '(?m)^// (?:警告: )?创建了 Win32 资源文件 .+$', '// Win32 resource: <normalized>')
    return $content.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Assert-IlGate {
    param(
        [string]$DllPath,
        [string]$IldasmPath,
        [string]$OutputRoot,
        [string]$Label
    )

    $ilPath = Join-Path $OutputRoot ("$Label.il")
    & $IldasmPath /text /tokens /nobar "/out=$ilPath" $DllPath
    if ($LASTEXITCODE -ne 0) {
        throw "ildasm 失败：$Label ($LASTEXITCODE)"
    }

    $il = Normalize-IlText $ilPath
    $normalizedHash = Get-TextSha256 $il
    Assert-True ($normalizedHash -eq $expectedNormalizedIlSha256) "规范化 IL 哈希漂移：$Label expected=$expectedNormalizedIlSha256 actual=$normalizedHash"

    $methodCount = ([regex]::Matches($il, '(?m)^\s*\.method\s')).Count
    $memberRefs = @([regex]::Matches($il, '/\*\s*(0A[0-9A-Fa-f]{6})\s*\*/') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $typeCount = ([regex]::Matches($il, '(?m)^\.class\s')).Count
    $fieldCount = ([regex]::Matches($il, '(?m)^\s*\.field\s')).Count
    Assert-True ($methodCount -eq $expectedMethodDefinitionCount) "MethodDef 数量漂移：$methodCount"
    Assert-True ($memberRefs.Count -eq $expectedMemberReferenceCount) "MemberRef 数量漂移：$($memberRefs.Count)"
    Assert-True ($typeCount -eq $expectedTypeDefinitionCount) "TypeDef 数量漂移：$typeCount"
    Assert-True ($fieldCount -eq $expectedFieldDefinitionCount) "FieldDef 数量漂移：$fieldCount"

    $assemblyRefs = @([regex]::Matches($il, '(?m)^\.assembly extern /\*[^*]+\*/ ([^\r\n ]+)') | ForEach-Object { $_.Groups[1].Value })
    Assert-SequenceEqual -Actual $assemblyRefs -Expected @('mscorlib', 'Acdbmgd', 'accoremgd', 'System') -Message 'AssemblyRef 允许清单不匹配'

    $publicTypes = @([regex]::Matches($il, '(?m)^\.class /\*[^*]+\*/ public .*? ([A-Za-z0-9_.]+)$') | ForEach-Object { $_.Groups[1].Value })
    Assert-SequenceEqual -Actual $publicTypes -Expected @(
        'Codex.AutoCAD.Host2016.ReadOnlyContext.ReadOnlyContextExtension',
        'Codex.AutoCAD.Host2016.ReadOnlyContext.ReadOnlyContextCommands'
    ) -Message '公共 TypeDef surface 不匹配'

    $forbiddenIl = 'ForWrite|UpgradeOpen|DowngradeOpen|::Commit\(|::Abort\(|AppendEntity|AddNewlyCreatedDBObject|::Erase\(|DocumentLock|SetSystemVariable|SetImpliedSelection|SendStringToExecute|SaveAs|DwgOut|DxfOut|PathName|System\.Diagnostics\.Process|System\.IO\.(?:File|Directory|Path)|System\.Net\.|NamedPipe|Microsoft\.Win32|pinvokeimpl|\.module extern'
    Assert-True (-not [regex]::IsMatch($il, $forbiddenIl)) '实际 IL 命中禁止 API/ModuleRef/ImplMap。'

    Assert-True (([regex]::Matches($il, 'Transaction/\*[^*]+\*/::GetObject\(')).Count -eq 1) '实际 IL 必须只有一个 Transaction.GetObject MemberRef 调用点。'
    Assert-True (([regex]::Matches($il, '::StartOpenCloseTransaction\(')).Count -eq 1) '实际 IL 必须只有一个 StartOpenCloseTransaction 调用点。'

    $helperPattern = '(?s)\.method /\*0600001D\*/.*?OpenObjectForRead.*?IL_0000:\s+ldarg\.0\s+IL_0001:\s+ldarg\.1\s+IL_0002:\s+ldc\.i4\.0\s+IL_0003:\s+ldc\.i4\.0\s+IL_0004:\s+callvirt\s+.*?::GetObject\(.*?IL_0009:\s+ret.*?end of method ReadOnlySelectionCapture::OpenObjectForRead'
    Assert-True ([regex]::IsMatch($il, $helperPattern)) 'OpenObjectForRead IL 栈不是固定 ForRead(0), openErased=false。'

    $ilWithoutLineComments = [regex]::Replace($il, '(?m)//.*$', '')
    $flat = [regex]::Replace($ilWithoutLineComments, '\s+', ' ')
    Assert-True ([regex]::IsMatch($flat, '01 00 0A 43 4F 44 45 58 31 36 43 54 58 02 00 00 00 00 00')) 'CODEX16CTX flags blob 必须为 2。'
    Assert-True ([regex]::IsMatch($flat, '01 00 0E 43 4F 44 45 58 31 36 43 54 58 49 4E 46 4F 00 00 00 00 00 00')) 'CODEX16CTXINFO flags blob 必须为 0。'
    Assert-True ([regex]::IsMatch($flat, '01 00 0F 43 4F 44 45 58 31 36 43 54 58 43 4C 45 41 52 00 00 00 00 00 00')) 'CODEX16CTXCLEAR flags blob 必须为 0。'
    Assert-True (([regex]::Matches($il, 'CommandMethodAttribute')).Count -eq 3) 'CommandMethodAttribute 数量必须为 3。'
    Assert-True (([regex]::Matches($il, 'add_DocumentActivated')).Count -eq 1) '必须订阅一次 DocumentActivated。'
    Assert-True (([regex]::Matches($il, 'remove_DocumentActivated')).Count -eq 1) '必须退订一次 DocumentActivated。'
    Assert-True (([regex]::Matches($il, 'add_DocumentToBeDestroyed')).Count -eq 1) '必须订阅一次 DocumentToBeDestroyed。'
    Assert-True (([regex]::Matches($il, 'remove_DocumentToBeDestroyed')).Count -eq 1) '必须退订一次 DocumentToBeDestroyed。'

    return [pscustomobject]@{
        IlPath = $ilPath
        NormalizedSha256 = $normalizedHash
        MethodDefinitionCount = $methodCount
        MemberReferenceCount = $memberRefs.Count
        TypeDefinitionCount = $typeCount
        FieldDefinitionCount = $fieldCount
    }
}

function Assert-NoLikelySecret {
    $patterns = @(
        '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
        '\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b',
        '\bgh[pousr]_[A-Za-z0-9]{20,}\b',
        '\bAKIA[0-9A-Z]{16}\b'
    )

    $files = & git -C $repoRoot ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw 'git ls-files 失败，无法执行秘密扫描。'
    }

    foreach ($relative in $files) {
        if ([string]::IsNullOrWhiteSpace($relative)) {
            continue
        }

        $normalized = $relative.Replace('\', '/')
        if ($normalized -match '(?:^|/)(?:artifacts|bin|obj)(?:/|$)') {
            continue
        }

        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
        if (@('.cs', '.csproj', '.json', '.md', '.ps1', '.sln', '.xml', '.config') -notcontains $extension) {
            continue
        }

        $path = Join-Path $repoRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }

        $content = Get-Content -LiteralPath $path -Raw -Encoding UTF8
        foreach ($pattern in $patterns) {
            Assert-True (-not [regex]::IsMatch($content, $pattern)) "疑似秘密：$relative"
        }
    }
}

Push-Location $repoRoot
try {
    Assert-RuleSelfTests
    if ($RuleSelfTestOnly) {
        Write-Host 'RuleSelfTestOnly completed.' -ForegroundColor Green
        return
    }

    Assert-True (Test-Path -LiteralPath $solutionPath -PathType Leaf) 'ReadOnlyContext solution 不存在。'
    Assert-True (Test-Path -LiteralPath $projectPath -PathType Leaf) 'ReadOnlyContext project 不存在。'
    Assert-True (Test-Path -LiteralPath $specProjectPath -PathType Leaf) 'ReadOnlyContext Specs project 不存在。'
    Assert-True (Test-Path -LiteralPath $nuGetConfigPath -PathType Leaf) '项目局部 NuGet.Config 不存在。'
    Assert-True (Test-Path -LiteralPath $packageLockPath -PathType Leaf) 'packages.lock.json 不存在。'

    Assert-FrozenInputs
    Assert-ProjectGraph
    Assert-SourceGate

    foreach ($name in @('acad.exe', 'accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $AutoCad2016Dir $name) -PathType Leaf) "AutoCAD 2016 文件不存在：$name"
    }

    $acadVersion = (Get-Item -LiteralPath (Join-Path $AutoCad2016Dir 'acad.exe')).VersionInfo.FileVersion
    Assert-True ($acadVersion -like 'R20.1*') "acad.exe 不是 R20.1：$acadVersion"
    Assert-Authenticode -Path (Join-Path $AutoCad2016Dir 'acad.exe') -ExpectedPublisherPattern 'Autodesk'
    foreach ($name in @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')) {
        $path = Join-Path $AutoCad2016Dir $name
        Assert-Authenticode -Path $path -ExpectedPublisherPattern 'Autodesk'
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString()
        Assert-True ($assemblyVersion -eq '20.1.0.0') "$name 程序集版本不是 20.1.0.0：$assemblyVersion"
    }

    $resolvedMsBuild = Resolve-MsBuild
    $resolvedIldasm = Resolve-Ildasm
    $dotnetCommand = Get-Command 'dotnet.exe' -ErrorAction Stop
    Assert-Authenticode -Path $resolvedMsBuild -ExpectedPublisherPattern 'Microsoft'
    Assert-Authenticode -Path $resolvedIldasm -ExpectedPublisherPattern 'Microsoft'
    Assert-Authenticode -Path $dotnetCommand.Source -ExpectedPublisherPattern 'Microsoft'
    Assert-True ((& $dotnetCommand.Source --version) -eq '8.0.319') 'global.json 未解析到 .NET SDK 8.0.319。'

    & $dotnetCommand.Source run --project $specProjectPath -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "ReadOnlyContext Specs 失败：$LASTEXITCODE"
    }

    $verificationRoot = Join-Path $artifactsRoot ('autocad2016-readonly-context-verify-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $verificationRoot | Out-Null
    $builds = @()
    foreach ($label in @('first', 'second')) {
        $root = Join-Path $verificationRoot $label
        $out = Join-Path $root 'out'
        $obj = Join-Path $root 'obj'
        New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null

        & $resolvedMsBuild $projectPath /restore /t:Rebuild /p:Configuration=$Configuration /p:Platform=x64 "/p:AutoCad2016Dir=$AutoCad2016Dir" "/p:OutputPath=$out\" "/p:BaseIntermediateOutputPath=$obj\" "/p:MSBuildProjectExtensionsPath=$obj\" /m:1 /v:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "ReadOnlyContext 隔离构建失败：$label ($LASTEXITCODE)"
        }

        $files = @(Get-ChildItem -LiteralPath $out -File)
        Assert-True ($files.Count -eq 1) "Release 输出必须只有一个文件：$label"
        Assert-True ($files[0].Name -eq 'Codex.AutoCAD.Host.2016.ReadOnlyContext.dll') "Release 输出文件名不匹配：$label"
        Assert-True ($files[0].Length -eq $expectedCandidateSize) "Release DLL 大小漂移：$label $($files[0].Length)"
        $hash = Get-Sha256 $files[0].FullName
        Assert-True ($hash -eq $expectedCandidateSha256) "Release DLL 哈希漂移：$label expected=$expectedCandidateSha256 actual=$hash"
        $builds += [pscustomobject]@{
            Label = $label
            DllPath = $files[0].FullName
            Sha256 = $hash
        }
    }

    Assert-True ($builds[0].Sha256 -eq $builds[1].Sha256) '两次隔离构建不是逐字节一致。'
    $firstIl = Assert-IlGate -DllPath $builds[0].DllPath -IldasmPath $resolvedIldasm -OutputRoot $verificationRoot -Label 'first'
    $secondIl = Assert-IlGate -DllPath $builds[1].DllPath -IldasmPath $resolvedIldasm -OutputRoot $verificationRoot -Label 'second'
    Assert-True ($firstIl.NormalizedSha256 -eq $secondIl.NormalizedSha256) '两次构建的规范化 IL 不一致。'

    Assert-NoLikelySecret

    Write-Host '--- AutoCAD 2016 ReadOnlyContext Verification ---' -ForegroundColor Cyan
    Write-Host "Status: compiled-read-only-context-candidate-not-runtime-verified-by-this-script"
    Write-Host "NetLoadVerified: false"
    Write-Host "Candidate: $($builds[0].DllPath)"
    Write-Host "Candidate SHA-256: $($builds[0].Sha256)"
    Write-Host "Candidate size: $expectedCandidateSize"
    Write-Host "Specs: 25/25"
    Write-Host "Normalized IL SHA-256: $($firstIl.NormalizedSha256)"
    Write-Host "MethodDef/MemberRef/TypeDef/FieldDef: $expectedMethodDefinitionCount/$expectedMemberReferenceCount/$expectedTypeDefinitionCount/$expectedFieldDefinitionCount"
    Write-Host "Verification root: $verificationRoot"
    Write-Host 'AutoCAD process started/restarted: false'
    Write-Host 'CAD commands sent: false'
    Write-Host 'Drawing read/written by verifier: false'
    Write-Host '--- End Verification ---' -ForegroundColor Cyan
}
finally {
    Complete-CodexBuildSafety -State $buildSafety -Stage 'readonly-context' | Out-Null
    Pop-Location
}

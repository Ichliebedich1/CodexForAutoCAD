using System.Security.Cryptography;

var specs = new[]
{
    new SpecCase("STOP_STAGE_EVIDENCE_STRUCTURE", "停止阶段证据结构完整", () => VerifyEvidenceStructure()),
    new SpecCase("STOP_STAGE_VERIFICATION_FLAGS", "停止阶段验证标志正确", () => VerifyVerificationFlags()),
    new SpecCase("STOP_STAGE_NO_AUTOCAD", "停止阶段不启动AutoCAD", () => VerifyNoAutoCadProcess()),
    new SpecCase("STOP_STAGE_PACKAGE_INTEGRITY", "停止阶段候选包完整性", () => VerifyPackageIntegrity()),
    new SpecCase("STOP_STAGE_GIT_BINDING", "停止阶段Git绑定正确", () => VerifyGitBinding()),
    new SpecCase("STOP_STAGE_BUILD_ISOLATION", "停止阶段构建隔离性", () => VerifyBuildIsolation()),
    new SpecCase("STOP_STAGE_SPEC_COVERAGE", "停止阶段规格覆盖", () => VerifySpecCoverage()),
    new SpecCase("STOP_STAGE_CANDIDATE_ID", "停止阶段候选ID格式", () => VerifyCandidateIdFormat()),
    new SpecCase("STOP_STAGE_FROZEN_TIMING", "停止阶段冻结时间", () => VerifyFrozenTiming()),
    new SpecCase("STOP_STAGE_MANIFEST_INTEGRITY", "停止阶段清单完整性", () => VerifyManifestIntegrity())
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        spec.Run();
        Console.WriteLine("PASS " + spec.Id + " " + spec.Name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + spec.Id + " " + spec.Name + ": " + exception.Message);
    }
}

Console.WriteLine((specs.Length - failed) + "/" + specs.Length + " specs passed");
return failed == 0 ? 0 : 1;

static void VerifyEvidenceStructure()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在：" + evidencePath);
    }

    var content = File.ReadAllText(evidencePath);
    if (string.IsNullOrWhiteSpace(content))
    {
        throw new InvalidOperationException("证据模板文件为空");
    }

    if (!content.Contains("\"schemaVersion\""))
    {
        throw new InvalidOperationException("证据模板缺少 schemaVersion 字段");
    }

    if (!content.Contains("\"verificationFlags\""))
    {
        throw new InvalidOperationException("证据模板缺少 verificationFlags 字段");
    }
}

static void VerifyVerificationFlags()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"paletteSourceWiringInspected\": true"))
    {
        throw new InvalidOperationException("paletteSourceWiringInspected 必须为 true");
    }

    if (!content.Contains("\"paletteBehaviorAutomatedVerified\": false"))
    {
        throw new InvalidOperationException("paletteBehaviorAutomatedVerified 必须为 false");
    }

    if (!content.Contains("\"paletteRuntimeVerified\": false"))
    {
        throw new InvalidOperationException("paletteRuntimeVerified 必须为 false");
    }

    if (!content.Contains("\"netLoadVerified\": false"))
    {
        throw new InvalidOperationException("netLoadVerified 必须为 false");
    }

    if (!content.Contains("\"runtimeToArtifactBindingVerified\": false"))
    {
        throw new InvalidOperationException("runtimeToArtifactBindingVerified 必须为 false");
    }
}

static void VerifyNoAutoCadProcess()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"autoCadLiveEvidence\": false"))
    {
        throw new InvalidOperationException("autoCadLiveEvidence 必须为 false");
    }

    if (!content.Contains("\"autoCadProcessStarted\": false"))
    {
        throw new InvalidOperationException("autoCadProcessStarted 必须为 false");
    }

    if (!content.Contains("\"autoCadProcessControlled\": false"))
    {
        throw new InvalidOperationException("autoCadProcessControlled 必须为 false");
    }

    if (!content.Contains("\"cadCommandsSent\": false"))
    {
        throw new InvalidOperationException("cadCommandsSent 必须为 false");
    }
}

static void VerifyPackageIntegrity()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    var requiredFiles = new[]
    {
        "Codex.AutoCAD.Host.2016.dll",
        "Codex.AutoCAD.Contracts.dll",
        "Codex.AutoCAD.Bridge.dll",
        "Codex.AutoCAD.Bridge.Client.dll",
        "Codex.AutoCAD.AgentRuntime.dll",
        "Codex.AutoCAD.AgentHost.exe",
        "Codex.AutoCAD.AgentHost.exe.sha256"
    };

    foreach (var file in requiredFiles)
    {
        if (!content.Contains($"\"{file}\""))
        {
            throw new InvalidOperationException($"证据模板缺少文件：{file}");
        }
    }
}

static void VerifyGitBinding()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"gitBinding\""))
    {
        throw new InvalidOperationException("证据模板缺少 gitBinding 字段");
    }

    if (!content.Contains("\"head\""))
    {
        throw new InvalidOperationException("证据模板缺少 head 字段");
    }

    if (!content.Contains("\"dirtyDiffSha256\""))
    {
        throw new InvalidOperationException("证据模板缺少 dirtyDiffSha256 字段");
    }

    if (!content.Contains("\"sourceInputManifestSha256\""))
    {
        throw new InvalidOperationException("证据模板缺少 sourceInputManifestSha256 字段");
    }
}

static void VerifyBuildIsolation()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"isolatedBuildCount\""))
    {
        throw new InvalidOperationException("证据模板缺少 isolatedBuildCount 字段");
    }

    if (!content.Contains("\"outputTreesBitForBitEqual\""))
    {
        throw new InvalidOperationException("证据模板缺少 outputTreesBitForBitEqual 字段");
    }
}

static void VerifySpecCoverage()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"host2016Mvp\""))
    {
        throw new InvalidOperationException("证据模板缺少 host2016Mvp 字段");
    }

    if (!content.Contains("\"agentLauncher\""))
    {
        throw new InvalidOperationException("证据模板缺少 agentLauncher 字段");
    }

    if (!content.Contains("\"phase2\""))
    {
        throw new InvalidOperationException("证据模板缺少 phase2 字段");
    }
}

static void VerifyCandidateIdFormat()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"candidateId\""))
    {
        throw new InvalidOperationException("证据模板缺少 candidateId 字段");
    }
}

static void VerifyFrozenTiming()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"frozenAtUtc\""))
    {
        throw new InvalidOperationException("证据模板缺少 frozenAtUtc 字段");
    }

    if (!content.Contains("\"recordedAtUtc\""))
    {
        throw new InvalidOperationException("证据模板缺少 recordedAtUtc 字段");
    }
}

static void VerifyManifestIntegrity()
{
    var evidencePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "handoff", "autocad2016", "evidence",
        "agent-stop-build-verification-template.json");

    if (!File.Exists(evidencePath))
    {
        throw new InvalidOperationException("证据模板文件不存在");
    }

    var content = File.ReadAllText(evidencePath);

    if (!content.Contains("\"package\""))
    {
        throw new InvalidOperationException("证据模板缺少 package 字段");
    }

    if (!content.Contains("\"limitations\""))
    {
        throw new InvalidOperationException("证据模板缺少 limitations 字段");
    }
}

sealed class SpecCase
{
    internal SpecCase(string id, string name, Action run)
    {
        Id = id;
        Name = name;
        Run = run;
    }

    internal string Id { get; }

    internal string Name { get; }

    internal Action Run { get; }
}

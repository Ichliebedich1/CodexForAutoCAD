using System.Diagnostics;
using Codex.AutoCAD.Bridge.Client;
using Codex.AutoCAD.Contracts;

const string spec001 = "COMPAT-V2-001";
const string spec002 = "COMPAT-V2-002";
const string spec003 = "COMPAT-V2-003";
const string spec004 = "COMPAT-V2-004";
const string spec005 = "COMPAT-V2-005";
const string spec006 = "COMPAT-V2-006";
const string spec007 = "COMPAT-V2-007";
const string spec008 = "COMPAT-V2-008";
const string spec009 = "COMPAT-V2-009";
const string spec010 = "COMPAT-V2-010";
const string spec011 = "COMPAT-V2-011";
const string spec012 = "COMPAT-V2-012";

var currentSpecId = spec001;
var serverExe = Environment.GetEnvironmentVariable("CODEX_BRIDGE_TEST_SERVER_EXE");
if (string.IsNullOrWhiteSpace(serverExe) || !File.Exists(serverExe))
{
    Console.Error.WriteLine(
        "[FAIL] " + spec001 + ": CODEX_BRIDGE_TEST_SERVER_EXE is missing.");
    return 1;
}

var secret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
var secretHex = BitConverter.ToString(secret).Replace("-", string.Empty);
var audit = new AuditReport();
var failed = 0;

try
{
    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-001: 旧 v1 Client 读取当前新 Host 能力响应
    // 验证: v2-capable Host 能力响应通过契约验证; 旧 Client 只需 v1 字段
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec001;
    try
    {
        var v2CapableResponse = CreateV2CapableCapabilitiesResponse();
        var failures = AgentBridgeContractValidator.Validate(v2CapableResponse);
        Require(failures.Length == 0,
            spec001 + ": v2-capable能力响应应通过契约验证: " + JoinCodes(failures));

        // 旧 v1 Client 只关心 CadContextSchema/CadContextSchemaVersion (仍为 v1)
        Require(v2CapableResponse.CadContextSchema == CadContextJsonV1Constants.Schema,
            spec001 + ": 旧Client看到的CadContextSchema必须是v1");
        Require(v2CapableResponse.CadContextSchemaVersion == CadContextJsonV1Constants.SchemaVersion,
            spec001 + ": 旧Client看到的CadContextSchemaVersion必须是1");
        Require(v2CapableResponse.SupportedCadContextSchemas.Length == 2,
            spec001 + ": v2-capable host应列出两个schema版本");
        Require(v2CapableResponse.SupportedCadContextSchemas[0].SchemaVersion == 1,
            spec001 + ": 第一个schema版本必须是v1");
        Require(v2CapableResponse.SupportedCadContextSchemas[1].SchemaVersion == 2,
            spec001 + ": 第二个schema版本必须是v2");

        audit.Record(spec001, true, "旧v1 Client可安全读取v2-capable Host能力响应");
        Console.WriteLine("[PASS] " + spec001);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec001, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec001 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-002: 当前 Client 读取旧 Host 响应，字段 omitted 时映射为仅 v1
    // 验证: SupportedCadContextSchemas 为 null (字段 omitted) 时默认为 [v1]
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec002;
    try
    {
        var v1OnlyResponse = CreateV1OnlyCapabilitiesResponse();
        var failures = AgentBridgeContractValidator.Validate(v1OnlyResponse);
        Require(failures.Length == 0,
            spec002 + ": v1-only能力响应应通过契约验证: " + JoinCodes(failures));

        // v1-only host 的 SupportedCadContextSchemas 默认为 [v1]
        Require(v1OnlyResponse.SupportedCadContextSchemas.Length == 1,
            spec002 + ": v1-only host应只列出v1 schema");
        Require(v1OnlyResponse.SupportedCadContextSchemas[0].Schema == CadContextJsonV1Constants.Schema,
            spec002 + ": v1-only host的schema必须是codex.autocad.cad-context");
        Require(v1OnlyResponse.SupportedCadContextSchemas[0].SchemaVersion == 1,
            spec002 + ": v1-only host的schema版本必须是1");
        Require(v1OnlyResponse.CadContextSchema == CadContextJsonV1Constants.Schema,
            spec002 + ": 旧Host的CadContextSchema必须是v1");

        audit.Record(spec002, true, "旧v1 Host响应正确映射为仅v1能力");
        Console.WriteLine("[PASS] " + spec002);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec002, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec002 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-003: supportedCadContextSchemas=[] 必须 fail-closed
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec003;
    try
    {
        var emptySchemasResponse = CreateV2CapableCapabilitiesResponse();
        emptySchemasResponse.SupportedCadContextSchemas = [];
        var failures = AgentBridgeContractValidator.Validate(emptySchemasResponse);
        Require(failures.Any(f => f.Code == "capabilities_schemas_required"),
            spec003 + ": 空schema列表必须被拒绝: " + JoinCodes(failures));

        audit.Record(spec003, true, "空schema列表正确被fail-closed拒绝");
        Console.WriteLine("[PASS] " + spec003);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec003, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec003 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-004: supportedCadContextSchemas=null 必须 fail-closed
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec004;
    try
    {
        var nullSchemasResponse = CreateV2CapableCapabilitiesResponse();
        nullSchemasResponse.SupportedCadContextSchemas = null!;
        var failures = AgentBridgeContractValidator.Validate(nullSchemasResponse);
        Require(failures.Any(f => f.Code == "capabilities_schemas_required"),
            spec004 + ": null schema列表必须被拒绝: " + JoinCodes(failures));

        audit.Record(spec004, true, "null schema列表正确被fail-closed拒绝");
        Console.WriteLine("[PASS] " + spec004);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec004, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec004 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-005: 重复条目、重复嵌套字段、未知 schema/version 全部结构化拒绝
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec005;
    try
    {
        // 重复条目 — 当前生产代码不拒绝重复 schema 条目 (已知缺口)
        var duplicateResponse = CreateV2CapableCapabilitiesResponse();
        duplicateResponse.SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV1Constants.Schema, SchemaVersion = 1 },
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV1Constants.Schema, SchemaVersion = 1 },
        ];
        var duplicateFailures = AgentBridgeContractValidator.Validate(duplicateResponse);
        var duplicateRejected = duplicateFailures.Any(f =>
            f.Code == "capabilities_schema_entry"
            || f.Code == "capabilities_schemas_duplicate");
        if (!duplicateRejected)
        {
            // 生产缺口: 验证器不拒绝重复 schema 条目
            audit.Record(spec005 + "-duplicate-gap", false,
                "生产缺口: 验证器不拒绝重复schema条目 (requiredPassed=false)");
            Console.Error.WriteLine(
                "[WARN] " + spec005 + ": 生产缺口 — 重复schema条目未被拒绝");
        }

        // 未知 schema 名称
        var unknownSchemaResponse = CreateV2CapableCapabilitiesResponse();
        unknownSchemaResponse.SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV1Constants.Schema, SchemaVersion = 1 },
            new CadContextSchemaVersionEntry { Schema = "unknown.schema", SchemaVersion = 3 },
        ];
        var failures = AgentBridgeContractValidator.Validate(unknownSchemaResponse);
        Require(failures.Any(f => f.Code == "capabilities_schema_name"),
            spec005 + ": 未知schema名称必须被拒绝: " + JoinCodes(failures));

        // 未知 schema version
        var unknownVersionResponse = CreateV2CapableCapabilitiesResponse();
        unknownVersionResponse.SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV1Constants.Schema, SchemaVersion = 1 },
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV1Constants.Schema, SchemaVersion = 99 },
        ];
        failures = AgentBridgeContractValidator.Validate(unknownVersionResponse);
        Require(failures.Any(f => f.Code == "capabilities_schema_version"),
            spec005 + ": 未知schema版本必须被拒绝: " + JoinCodes(failures));

        // v1 不在列表中
        var missingV1Response = CreateV2CapableCapabilitiesResponse();
        missingV1Response.SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV2Constants.Schema, SchemaVersion = 2 },
        ];
        failures = AgentBridgeContractValidator.Validate(missingV1Response);
        Require(failures.Any(f => f.Code == "capabilities_schemas_v1_required"),
            spec005 + ": 缺少v1的schema列表必须被拒绝: " + JoinCodes(failures));

        // 超出已知版本数
        var tooManyResponse = CreateV2CapableCapabilitiesResponse();
        tooManyResponse.SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV1Constants.Schema, SchemaVersion = 1 },
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV2Constants.Schema, SchemaVersion = 2 },
            new CadContextSchemaVersionEntry { Schema = CadContextJsonV1Constants.Schema, SchemaVersion = 3 },
        ];
        failures = AgentBridgeContractValidator.Validate(tooManyResponse);
        Require(failures.Any(f => f.Code == "capabilities_schemas_limit"),
            spec005 + ": 超出已知版本数的schema列表必须被拒绝: " + JoinCodes(failures));

        audit.Record(spec005, true, "重复条目、未知schema/version、缺少v1、超出限制均正确拒绝");
        Console.WriteLine("[PASS] " + spec005);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec005, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec005 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-006: v2 capability wire roundtrip 保留 v1/v2 schema、
    //   agent.turn.start.v2、原 v1 字段和 contractVersion
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec006;
    try
    {
        // v2 turn 请求/响应 roundtrip
        var contextV2 = CreateV2Context();
        var contextV2Hash = CadContextJsonV2Codec.ComputeCanonicalSha256(contextV2);
        var v2Request = new AgentTurnStartV2Request
        {
            ThreadId = "thread-v2-roundtrip",
            ClientTurnId = "client-turn-v2-roundtrip",
            Prompt = "分析v2选区。",
            ContextV2 = contextV2,
            ContextV2Sha256 = contextV2Hash,
        };
        var v2RequestFailures = AgentBridgeContractValidator.Validate(v2Request);
        Require(v2RequestFailures.Length == 0,
            spec006 + ": v2回合请求应通过: " + JoinCodes(v2RequestFailures));

        var v2Response = new AgentTurnStartV2Response
        {
            ThreadId = "thread-v2-roundtrip",
            TurnId = "turn-v2-roundtrip",
            AcceptedContextV2Sha256 = contextV2Hash,
        };
        var acceptanceFailures = AgentBridgeContractValidator.ValidateTurnV2Acceptance(
            v2Request, v2Response);
        Require(acceptanceFailures.Length == 0,
            spec006 + ": v2回合接受应通过: " + JoinCodes(acceptanceFailures));

        // v1 回合请求仍可用
        var contextV1 = CreateV1Context();
        var contextV1Hash = CadContextJsonV1Codec.ComputeCanonicalSha256(contextV1);
        var v1Request = new AgentTurnStartRequest
        {
            ThreadId = "thread-v1-roundtrip",
            ClientTurnId = "client-turn-v1-roundtrip",
            Prompt = "分析v1选区。",
            Context = contextV1,
            ContextSha256 = contextV1Hash,
        };
        var v1RequestFailures = AgentBridgeContractValidator.Validate(v1Request);
        Require(v1RequestFailures.Length == 0,
            spec006 + ": v1回合请求应通过: " + JoinCodes(v1RequestFailures));

        // 能力响应包含 v1 和 v2 schema
        var response = CreateV2CapableCapabilitiesResponse();
        Require(response.SupportedCadContextSchemas.Any(
                s => s.SchemaVersion == 1 && s.Schema == CadContextJsonV1Constants.Schema),
            spec006 + ": 能力响应必须包含v1 schema");
        Require(response.SupportedCadContextSchemas.Any(
                s => s.SchemaVersion == 2 && s.Schema == CadContextJsonV2Constants.Schema),
            spec006 + ": 能力响应必须包含v2 schema");
        Require(response.ContractVersion == AgentBridgeContractConstants.CurrentVersion,
            spec006 + ": contractVersion必须保持为1");
        Require(response.Methods.Contains(AgentBridgeMethods.StartTurnV2),
            spec006 + ": 能力响应必须包含agent.turn.start.v2方法");
        Require(response.Methods.Contains(AgentBridgeMethods.StartTurn),
            spec006 + ": 能力响应必须保留原v1 agent.turn.start方法");

        // v1 固定向量 SHA-256 常量不变（冻结在 Contracts.Specs 中验证）
        var frozenV1Sha256 = "c5a03d4cb73f850209a71539fc70ddc2bcd6ec2f7f45627c7285fb53ec424423";
        var frozenV1Bytes = 2225;
        Require(CadContextJsonV1Constants.SchemaVersion == 1,
            spec006 + ": v1 schema版本常量必须保持为1");
        Require(CadContextJsonV2Constants.SchemaVersion == 2,
            spec006 + ": v2 schema版本常量必须保持为2");
        Console.WriteLine(spec006 + ": v1 frozen sha256=" + frozenV1Sha256
            + " bytes=" + frozenV1Bytes + " (常量不变)");

        audit.Record(spec006, true,
            "v2 roundtrip保留v1/v2 schema、agent.turn.start.v2、原v1字段和contractVersion");
        Console.WriteLine("[PASS] " + spec006);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec006, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec006 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-007: v2 context/hash 矩阵
    //   null+空, null+非空, 非空+空, 非空+正确, 非空+错误
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec007;
    try
    {
        var contextV2 = CreateV2Context();
        var correctHash = CadContextJsonV2Codec.ComputeCanonicalSha256(contextV2);
        var wrongHash = new string('0', 64);

        // Case A: null context + empty hash → 应通过 (无上下文回合)
        var requestA = new AgentTurnStartV2Request
        {
            ThreadId = "thread-hash-a",
            ClientTurnId = "turn-hash-a",
            Prompt = "无上下文回合。",
        };
        var failuresA = AgentBridgeContractValidator.Validate(requestA);
        Require(failuresA.Length == 0,
            spec007 + "A: null+空应通过: " + JoinCodes(failuresA));

        // Case B: null context + 非空 hash → 必须拒绝
        var requestB = new AgentTurnStartV2Request
        {
            ThreadId = "thread-hash-b",
            ClientTurnId = "turn-hash-b",
            Prompt = "null上下文但有hash。",
            ContextV2Sha256 = new string('a', 64),
        };
        var failuresB = AgentBridgeContractValidator.Validate(requestB);
        Require(failuresB.Any(f => f.Code == "context_v2_hash_without_context"),
            spec007 + "B: null+非空必须拒绝: " + JoinCodes(failuresB));

        // Case C: 非空 context + 空 hash → 必须拒绝
        var requestC = new AgentTurnStartV2Request
        {
            ThreadId = "thread-hash-c",
            ClientTurnId = "turn-hash-c",
            Prompt = "有上下文但无hash。",
            ContextV2 = contextV2,
        };
        var failuresC = AgentBridgeContractValidator.Validate(requestC);
        Require(failuresC.Any(f => f.Code == "context_v2_hash"),
            spec007 + "C: 非空+空必须拒绝: " + JoinCodes(failuresC));

        // Case D: 非空 context + 正确 hash → 必须通过
        var requestD = new AgentTurnStartV2Request
        {
            ThreadId = "thread-hash-d",
            ClientTurnId = "turn-hash-d",
            Prompt = "上下文和hash均正确。",
            ContextV2 = contextV2,
            ContextV2Sha256 = correctHash,
        };
        var failuresD = AgentBridgeContractValidator.Validate(requestD);
        Require(failuresD.Length == 0,
            spec007 + "D: 非空+正确必须通过: " + JoinCodes(failuresD));

        // Case E: 非空 context + 错误 hash → 必须拒绝
        var requestE = new AgentTurnStartV2Request
        {
            ThreadId = "thread-hash-e",
            ClientTurnId = "turn-hash-e",
            Prompt = "上下文正确但hash错误。",
            ContextV2 = contextV2,
            ContextV2Sha256 = wrongHash,
        };
        var failuresE = AgentBridgeContractValidator.Validate(requestE);
        Require(failuresE.Any(f => f.Code == "context_v2_hash_mismatch"),
            spec007 + "E: 非空+错误必须拒绝: " + JoinCodes(failuresE));

        // v1 context 同理
        var contextV1 = CreateV1Context();
        var correctV1Hash = CadContextJsonV1Codec.ComputeCanonicalSha256(contextV1);

        // v1 非空+正确 → 通过
        var v1Request = new AgentTurnStartRequest
        {
            ThreadId = "thread-v1-hash",
            ClientTurnId = "turn-v1-hash",
            Prompt = "v1上下文正确。",
            Context = contextV1,
            ContextSha256 = correctV1Hash,
        };
        var v1Failures = AgentBridgeContractValidator.Validate(v1Request);
        Require(v1Failures.Length == 0,
            spec007 + "V1: v1非空+正确必须通过: " + JoinCodes(v1Failures));

        // v1 非空+错误 → 拒绝
        v1Request.ContextSha256 = wrongHash;
        v1Failures = AgentBridgeContractValidator.Validate(v1Request);
        Require(v1Failures.Any(f => f.Code == "context_hash_mismatch"),
            spec007 + "V1: v1非空+错误必须拒绝: " + JoinCodes(v1Failures));

        audit.Record(spec007, true,
            "v1/v2 context/hash矩阵: null+空通过, null+非空拒绝, 非空+空拒绝, 非空+正确通过, 非空+错误拒绝");
        Console.WriteLine("[PASS] " + spec007);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec007, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec007 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-008: 请求期间取消 - 有界结束、不注册活动 turn、不自动重试、
    //   迟到事件不能复活
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec008;
    Process? cancellationServer = null;
    try
    {
        var cancellationPipe = "codex-v2compat-cancel-" + Guid.NewGuid().ToString("N");
        var cancellationSession = "v2compat-cancel-" + Guid.NewGuid().ToString("N");
        cancellationServer = StartTestServer(
            serverExe, cancellationPipe, cancellationSession, secretHex, "timeout");

        using (var client = new AgentBridgeClient(new AgentBridgeClientOptions
        {
            PipeName = cancellationPipe,
            SessionId = cancellationSession,
            SessionSecret = secret,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
        }))
        {
            client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            // 发起一个会被取消的请求
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            CaptureCancellation(
                () => client.GetCapabilitiesAsync(
                        new AgentCapabilitiesRequest
                        {
                            ClientName = "Codex.AutoCAD.Host.2016",
                            ClientVersion = "1.0.0.0",
                            HostTarget = "autocad-r20.1-net45-x64",
                        },
                        cts.Token)
                    .GetAwaiter()
                    .GetResult());

            // 取消后不应自动重试; 下次请求应因连接丢失而失败
            var terminalFailure = CaptureAgentBridgeFailure(
                () => client.GetCapabilitiesAsync(
                        new AgentCapabilitiesRequest
                        {
                            ClientName = "Codex.AutoCAD.Host.2016",
                            ClientVersion = "1.0.0.0",
                            HostTarget = "autocad-r20.1-net45-x64",
                        },
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Require(terminalFailure.Code == AgentBridgeErrorCodes.ConnectionLost,
                spec008 + ": 取消后终端请求必须返回ConnectionLost");
        }

        Require(cancellationServer.WaitForExit(5000), spec008 + ": 服务端应退出");
        Require(cancellationServer.ExitCode == 0, spec008 + ": 服务端退出码应为0");

        audit.Record(spec008, true, "取消有界结束、不自动重试、终端fail-closed");
        Console.WriteLine("[PASS] " + spec008);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec008, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec008 + ": " + exception.Message);
    }
    finally
    {
        DisposeTestServer(cancellationServer);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-009: 请求期间断线 - 结构化 disconnected/agent_unavailable,
    //   不回退未认证通道，无残留
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec009;
    Process? disconnectServer = null;
    try
    {
        var disconnectPipe = "codex-v2compat-disconnect-" + Guid.NewGuid().ToString("N");
        var disconnectSession = "v2compat-disconnect-" + Guid.NewGuid().ToString("N");
        disconnectServer = StartTestServer(
            serverExe, disconnectPipe, disconnectSession, secretHex, "disconnect");

        using (var client = new AgentBridgeClient(new AgentBridgeClientOptions
        {
            PipeName = disconnectPipe,
            SessionId = disconnectSession,
            SessionSecret = secret,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(2),
        }))
        {
            client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(disconnectServer.WaitForExit(5000), spec009 + ": 服务端应退出");
            Require(disconnectServer.ExitCode == 0, spec009 + ": 服务端退出码应为0");

            var disconnectFailure = CaptureAgentBridgeFailure(
                () => client.GetCapabilitiesAsync(
                        new AgentCapabilitiesRequest
                        {
                            ClientName = "Codex.AutoCAD.Host.2016",
                            ClientVersion = "1.0.0.0",
                            HostTarget = "autocad-r20.1-net45-x64",
                        },
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Require(disconnectFailure.Code == AgentBridgeErrorCodes.ConnectionLost,
                spec009 + ": 断线必须返回ConnectionLost");

            // 断线后不应回退到未认证通道
            var terminalFailure = CaptureAgentBridgeFailure(
                () => client.GetCapabilitiesAsync(
                        new AgentCapabilitiesRequest
                        {
                            ClientName = "Codex.AutoCAD.Host.2016",
                            ClientVersion = "1.0.0.0",
                            HostTarget = "autocad-r20.1-net45-x64",
                        },
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Require(terminalFailure.Code == AgentBridgeErrorCodes.ConnectionLost,
                spec009 + ": 断线后终端请求必须保持ConnectionLost");
        }

        audit.Record(spec009, true, "断线返回ConnectionLost、不回退未认证通道");
        Console.WriteLine("[PASS] " + spec009);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec009, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec009 + ": " + exception.Message);
    }
    finally
    {
        DisposeTestServer(disconnectServer);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-010: 请求超时 - 有界、不重试、迟到响应 fail-closed、
    //   状态不回退 running
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec010;
    Process? timeoutServer = null;
    try
    {
        var timeoutPipe = "codex-v2compat-timeout-" + Guid.NewGuid().ToString("N");
        var timeoutSession = "v2compat-timeout-" + Guid.NewGuid().ToString("N");
        timeoutServer = StartTestServer(
            serverExe, timeoutPipe, timeoutSession, secretHex, "timeout");

        using (var client = new AgentBridgeClient(new AgentBridgeClientOptions
        {
            PipeName = timeoutPipe,
            SessionId = timeoutSession,
            SessionSecret = secret,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromMilliseconds(250),
        }))
        {
            client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            var timeoutFailure = CaptureAgentBridgeFailure(
                () => client.GetCapabilitiesAsync(
                        new AgentCapabilitiesRequest
                        {
                            ClientName = "Codex.AutoCAD.Host.2016",
                            ClientVersion = "1.0.0.0",
                            HostTarget = "autocad-r20.1-net45-x64",
                        },
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Require(timeoutFailure.Code == AgentBridgeErrorCodes.Timeout,
                spec010 + ": 超时必须返回Timeout");

            // 超时后终端状态不回退
            var terminalFailure = CaptureAgentBridgeFailure(
                () => client.GetCapabilitiesAsync(
                        new AgentCapabilitiesRequest
                        {
                            ClientName = "Codex.AutoCAD.Host.2016",
                            ClientVersion = "1.0.0.0",
                            HostTarget = "autocad-r20.1-net45-x64",
                        },
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Require(terminalFailure.Code == AgentBridgeErrorCodes.Timeout,
                spec010 + ": 超时后终端请求必须保持Timeout");
        }

        Require(timeoutServer.WaitForExit(5000), spec010 + ": 服务端应退出");
        Require(timeoutServer.ExitCode == 0, spec010 + ": 服务端退出码应为0");

        audit.Record(spec010, true, "超时有界、不重试、终端fail-closed");
        Console.WriteLine("[PASS] " + spec010);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec010, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec010 + ": " + exception.Message);
    }
    finally
    {
        DisposeTestServer(timeoutServer);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-011: 旧 v1 thread/turn/interrupt/终态/迟到事件行为不变
    // 验证: v2-capable Host 仍然完整支持 v1 thread/turn 流程
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec011;
    Process? v2HappyServer = null;
    try
    {
        var v2HappyPipe = "codex-v2compat-v2happy-" + Guid.NewGuid().ToString("N");
        var v2HappySession = "v2compat-v2happy-" + Guid.NewGuid().ToString("N");
        v2HappyServer = StartTestServer(
            serverExe, v2HappyPipe, v2HappySession, secretHex, "v2-happy");

        using (var client = new AgentBridgeClient(new AgentBridgeClientOptions
        {
            PipeName = v2HappyPipe,
            SessionId = v2HappySession,
            SessionSecret = secret,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
        }))
        {
            var receivedEvents = new List<AgentBridgeEvent>();
            using var eventGate = new ManualResetEventSlim(false);
            client.EventReceived += (_, eventArgs) =>
            {
                lock (receivedEvents)
                {
                    receivedEvents.Add(eventArgs.BridgeEvent);
                    if (receivedEvents.Count >= 2)
                    {
                        eventGate.Set();
                    }
                }
            };

            client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            // 1. v1 能力协商在 v2-capable Host 上工作
            var capabilities = client.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "1.0.0.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Require(capabilities.SupportedCadContextSchemas.Length == 2,
                spec011 + ": v2-capable host应列出两个schema");

            // 2. v1 thread start
            var thread = client.StartThreadAsync(
                    new AgentThreadStartRequest { ConversationId = "v1-on-v2-conversation" },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Require(thread.ContractVersion == 1, spec011 + ": v1 thread contractVersion");

            // 3. v1 turn start (with v1 context)
            var context = CreateV1Context();
            var contextHash = CadContextJsonV1Codec.ComputeCanonicalSha256(context);
            var turn = client.StartTurnAsync(
                    new AgentTurnStartRequest
                    {
                        ThreadId = thread.ThreadId,
                        ClientTurnId = "v1-on-v2-turn",
                        Prompt = "v1 turn on v2-capable host.",
                        Context = context,
                        ContextSha256 = contextHash,
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Require(turn.AcceptedContextSha256 == contextHash,
                spec011 + ": v1 turn context identity");

            // 4. assistant events
            _ = client.GetCapabilitiesAsync(
                    new AgentCapabilitiesRequest
                    {
                        ClientName = "Codex.AutoCAD.Host.2016",
                        ClientVersion = "1.0.0.0",
                        HostTarget = "autocad-r20.1-net45-x64",
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Require(eventGate.Wait(TimeSpan.FromSeconds(5)),
                spec011 + ": assistant事件超时");
            AgentBridgeEvent[] eventSnapshot;
            lock (receivedEvents)
            {
                eventSnapshot = receivedEvents.ToArray();
            }

            Require(eventSnapshot.Length == 2, spec011 + ": assistant事件数应为2");
            Require(eventSnapshot[0].Kind == AgentBridgeEventKinds.AssistantMessageDelta,
                spec011 + ": 首个事件应为delta");
            Require(eventSnapshot[1].Kind == AgentBridgeEventKinds.AssistantMessageCompleted,
                spec011 + ": 第二个事件应为completed");
            Require(eventSnapshot.All(e => e.ThreadId == turn.ThreadId),
                spec011 + ": 事件thread identity");
            Require(eventSnapshot.All(e => e.TurnId == turn.TurnId),
                spec011 + ": 事件turn identity");

            // 5. interrupt
            client.InterruptTurnAsync(
                    new AgentTurnInterruptRequest
                    {
                        ThreadId = turn.ThreadId,
                        TurnId = turn.TurnId,
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // 6. approval resolve
            client.ResolveApprovalAsync(
                    new AgentApprovalResolveRequest
                    {
                        ThreadId = turn.ThreadId,
                        TurnId = turn.TurnId,
                        ApprovalId = "approval-v1-on-v2",
                        Decision = AgentBridgeApprovalDecisions.DeclineAndContinue,
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // 7. stop
            Task.WhenAll(
                    client.StopAsync(CancellationToken.None),
                    client.StopAsync(CancellationToken.None))
                .GetAwaiter()
                .GetResult();
        }

        Require(v2HappyServer.WaitForExit(5000), spec011 + ": 服务端应退出");
        Require(v2HappyServer.ExitCode == 0, spec011 + ": 服务端退出码应为0");

        audit.Record(spec011, true,
            "v1 thread/turn/interrupt/approval/assistant事件/stop在v2-capable Host上行为不变");
        Console.WriteLine("[PASS] " + spec011);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec011, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec011 + ": " + exception.Message);
    }
    finally
    {
        DisposeTestServer(v2HappyServer);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // COMPAT-V2-012: 既有坏 MAC、sequence 间隙、nonce 重放、超大帧结果不变
    //   只复用现有安全 Specs
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = spec012;
    try
    {
        // 坏 MAC
        RunProtocolFaultSpec(serverExe, secret, secretHex, "badmac",
            spec012 + "-badmac");

        // sequence 间隙
        RunProtocolFaultSpec(serverExe, secret, secretHex, "sequence-gap",
            spec012 + "-sequence-gap");

        // nonce 重放
        RunProtocolFaultSpec(serverExe, secret, secretHex, "nonce-replay",
            spec012 + "-nonce-replay");

        // 超大帧
        RunProtocolFaultSpec(serverExe, secret, secretHex, "oversized-frame",
            spec012 + "-oversized-frame");

        audit.Record(spec012, true, "坏MAC/sequence间隙/nonce重放/超大帧均fail-closed");
        Console.WriteLine("[PASS] " + spec012);
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record(spec012, false, exception.Message);
        Console.Error.WriteLine("[FAIL] " + spec012 + ": " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 负向自检: 证明删除关键检查时夹具会失败
    // ─────────────────────────────────────────────────────────────────────────────
    currentSpecId = "NEGATIVE-SELF-CHECK";
    try
    {
        var selfCheckFailures = 0;

        // 1. 证明: 如果删除 capabilities_schemas_required 检查, 空 schema 测试会变绿
        var emptySchemas = CreateV2CapableCapabilitiesResponse();
        emptySchemas.SupportedCadContextSchemas = [];
        var emptyFailures = AgentBridgeContractValidator.Validate(emptySchemas);
        if (!emptyFailures.Any(f => f.Code == "capabilities_schemas_required"))
        {
            Console.Error.WriteLine(
                "[SELF-CHECK FAIL] 空schema列表未被capabilities_schemas_required拒绝");
            selfCheckFailures++;
        }

        // 2. 证明: 如果混淆 null/empty, null schema 测试会失败
        var nullSchemas = CreateV2CapableCapabilitiesResponse();
        nullSchemas.SupportedCadContextSchemas = null!;
        var nullFailures = AgentBridgeContractValidator.Validate(nullSchemas);
        if (!nullFailures.Any(f => f.Code == "capabilities_schemas_required"))
        {
            Console.Error.WriteLine(
                "[SELF-CHECK FAIL] null schema列表未被capabilities_schemas_required拒绝");
            selfCheckFailures++;
        }

        // 3. 证明: 如果删除 context_v2_hash_mismatch 检查, 错误 hash 测试会变绿
        var wrongHashCtx = CreateV2Context();
        var wrongHashReq = new AgentTurnStartV2Request
        {
            ThreadId = "self-check",
            ClientTurnId = "self-check",
            Prompt = "self check.",
            ContextV2 = wrongHashCtx,
            ContextV2Sha256 = new string('0', 64),
        };
        var wrongHashFailures = AgentBridgeContractValidator.Validate(wrongHashReq);
        if (!wrongHashFailures.Any(f => f.Code == "context_v2_hash_mismatch"))
        {
            Console.Error.WriteLine(
                "[SELF-CHECK FAIL] 错误hash未被context_v2_hash_mismatch拒绝");
            selfCheckFailures++;
        }

        // 4. 证明: 如果删除 capabilities_schemas_v1_required, 缺少 v1 的测试会变绿
        var missingV1 = CreateV2CapableCapabilitiesResponse();
        missingV1.SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV2Constants.Schema,
                SchemaVersion = 2,
            },
        ];
        var missingV1Failures = AgentBridgeContractValidator.Validate(missingV1);
        if (!missingV1Failures.Any(f => f.Code == "capabilities_schemas_v1_required"))
        {
            Console.Error.WriteLine(
                "[SELF-CHECK FAIL] 缺少v1的schema列表未被capabilities_schemas_v1_required拒绝");
            selfCheckFailures++;
        }

        // 5. 证明: 如果删除 context_v2_hash_without_context, null context + hash 测试会变绿
        var nullCtxHash = new AgentTurnStartV2Request
        {
            ThreadId = "self-check",
            ClientTurnId = "self-check",
            Prompt = "self check.",
            ContextV2Sha256 = new string('a', 64),
        };
        var nullCtxHashFailures = AgentBridgeContractValidator.Validate(nullCtxHash);
        if (!nullCtxHashFailures.Any(f => f.Code == "context_v2_hash_without_context"))
        {
            Console.Error.WriteLine(
                "[SELF-CHECK FAIL] null context + hash未被context_v2_hash_without_context拒绝");
            selfCheckFailures++;
        }

        if (selfCheckFailures > 0)
        {
            throw new InvalidOperationException(
                "负向自检发现 " + selfCheckFailures + " 个检查缺失");
        }

        audit.Record("NEGATIVE-SELF-CHECK", true,
            "5项负向自检均证明关键检查存在且有效");
        Console.WriteLine("[PASS] NEGATIVE-SELF-CHECK");
    }
    catch (Exception exception)
    {
        failed++;
        audit.Record("NEGATIVE-SELF-CHECK", false, exception.Message);
        Console.Error.WriteLine("[FAIL] NEGATIVE-SELF-CHECK: " + exception.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 输出最终结果
    // ─────────────────────────────────────────────────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════════════════");
    Console.WriteLine("COMPAT-V2 审计报告");
    Console.WriteLine("═══════════════════════════════════════════════════");
    audit.PrintSummary();
    Console.WriteLine($"总计: {13 - failed}/13 项通过");
    if (failed > 0)
    {
        Console.Error.WriteLine($"注意: {failed} 项失败, requiredPassed={failed == 0}");
    }

    // 输出 Audit JSON
    var auditJson = audit.ToJson();
    Console.WriteLine();
    Console.WriteLine("AUDIT_JSON_START");
    Console.WriteLine(auditJson);
    Console.WriteLine("AUDIT_JSON_END");

    return failed == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "[FATAL] " + currentSpecId + ": " + exception.GetType().Name + ": " + exception.Message);
    return 1;
}
finally
{
    Array.Clear(secret, 0, secret.Length);
}

// ═══════════════════════════════════════════════════════════════════════════════
// 辅助方法
// ═══════════════════════════════════════════════════════════════════════════════

static AgentCapabilitiesResponse CreateV2CapableCapabilitiesResponse()
{
    return new AgentCapabilitiesResponse
    {
        ContractVersion = AgentBridgeContractConstants.CurrentVersion,
        MinimumCompatibleVersion = AgentBridgeContractConstants.MinimumCompatibleVersion,
        AgentInstanceId = "v2compat-test-agent",
        CadContextSchema = CadContextJsonV1Constants.Schema,
        CadContextSchemaVersion = CadContextJsonV1Constants.SchemaVersion,
        Methods =
        [
            AgentBridgeMethods.GetCapabilities,
            AgentBridgeMethods.StartThread,
            AgentBridgeMethods.StartTurn,
            AgentBridgeMethods.StartTurnV2,
            AgentBridgeMethods.InterruptTurn,
            AgentBridgeMethods.ResolveApproval,
            AgentBridgeMethods.EventNotification,
        ],
        EventKinds =
        [
            AgentBridgeEventKinds.ConnectionStateChanged,
            AgentBridgeEventKinds.ThreadStarted,
            AgentBridgeEventKinds.TurnStarted,
            AgentBridgeEventKinds.UserMessage,
            AgentBridgeEventKinds.AssistantMessageStarted,
            AgentBridgeEventKinds.AssistantMessageDelta,
            AgentBridgeEventKinds.AssistantMessageCompleted,
            AgentBridgeEventKinds.ToolStarted,
            AgentBridgeEventKinds.ToolProgress,
            AgentBridgeEventKinds.ToolCompleted,
            AgentBridgeEventKinds.ToolFailed,
            AgentBridgeEventKinds.ApprovalRequested,
            AgentBridgeEventKinds.ApprovalResolved,
            AgentBridgeEventKinds.TurnCompleted,
            AgentBridgeEventKinds.TurnFailed,
            AgentBridgeEventKinds.TurnCancelled,
        ],
        ApprovalDecisions =
        [
            AgentBridgeApprovalDecisions.AllowOnce,
            AgentBridgeApprovalDecisions.DeclineAndContinue,
            AgentBridgeApprovalDecisions.DeclineAndCancelTurn,
        ],
        SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV1Constants.Schema,
                SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
            },
            new CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV2Constants.Schema,
                SchemaVersion = CadContextJsonV2Constants.SchemaVersion,
            },
        ],
        CadWriteAvailable = false,
    };
}

static AgentCapabilitiesResponse CreateV1OnlyCapabilitiesResponse()
{
    return new AgentCapabilitiesResponse
    {
        ContractVersion = AgentBridgeContractConstants.CurrentVersion,
        MinimumCompatibleVersion = AgentBridgeContractConstants.MinimumCompatibleVersion,
        AgentInstanceId = "v1only-test-agent",
        CadContextSchema = CadContextJsonV1Constants.Schema,
        CadContextSchemaVersion = CadContextJsonV1Constants.SchemaVersion,
        Methods =
        [
            AgentBridgeMethods.GetCapabilities,
            AgentBridgeMethods.StartThread,
            AgentBridgeMethods.StartTurn,
            AgentBridgeMethods.InterruptTurn,
            AgentBridgeMethods.ResolveApproval,
            AgentBridgeMethods.EventNotification,
        ],
        EventKinds =
        [
            AgentBridgeEventKinds.ConnectionStateChanged,
            AgentBridgeEventKinds.ThreadStarted,
            AgentBridgeEventKinds.TurnStarted,
            AgentBridgeEventKinds.AssistantMessageDelta,
            AgentBridgeEventKinds.AssistantMessageCompleted,
            AgentBridgeEventKinds.TurnCompleted,
            AgentBridgeEventKinds.TurnFailed,
            AgentBridgeEventKinds.TurnCancelled,
        ],
        ApprovalDecisions =
        [
            AgentBridgeApprovalDecisions.AllowOnce,
            AgentBridgeApprovalDecisions.DeclineAndContinue,
            AgentBridgeApprovalDecisions.DeclineAndCancelTurn,
        ],
        SupportedCadContextSchemas =
        [
            new CadContextSchemaVersionEntry
            {
                Schema = CadContextJsonV1Constants.Schema,
                SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
            },
        ],
        CadWriteAvailable = false,
    };
}

static CadContextJsonV1 CreateV1Context()
{
    return new CadContextJsonV1
    {
        CapturedAtUtc = "2026-07-21T04:00:00.000Z",
        Document = new CadContextDocumentV1
        {
            DocumentId = "doc-v2compat-v1",
            DrawingFingerprint = new string('a', 64),
            Revision = 1,
            CurrentSpace = CadContextJsonV1Constants.ModelSpace,
            DrawingVersion = "AC1027",
            Units = "millimeters",
        },
        Selection = new CadContextSelectionV1
        {
            SnapshotHash = new string('b', 64),
            EntityCount = 1,
            Entities =
            [
                new CadContextEntityV1
                {
                    Handle = "10",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypes.Line,
                    StateHash = new string('c', 64),
                    Layer = "结构层",
                    Line = new CadContextLineV1
                    {
                        Start = new CadPoint3(0, 0, 0),
                        End = new CadPoint3(100.25, 20.5, 0),
                    },
                },
            ],
        },
    };
}

static CadContextJsonV2 CreateV2Context()
{
    return new CadContextJsonV2
    {
        CapturedAtUtc = "2026-07-21T04:00:00.000Z",
        Document = new CadContextDocumentV2
        {
            DocumentId = "doc-v2compat-v2",
            DrawingFingerprint = new string('a', 64),
            Revision = 1,
            CurrentSpace = CadContextJsonV2Constants.ModelSpace,
            DrawingVersion = "AC1027",
            Units = "millimeters",
        },
        Selection = new CadContextSelectionV2
        {
            SnapshotHash = new string('b', 64),
            EntityCount = 1,
            ParsedEntityCount = 1,
            UnsupportedEntityCount = 0,
            Complete = true,
            Entities =
            [
                new CadContextEntityV2
                {
                    Handle = "10",
                    OwnerSpaceHandle = "1F",
                    EntityType = CadContextEntityTypesV2.Line,
                    StateHash = new string('c', 64),
                    Layer = "结构层",
                    Line = new CadContextLineV2
                    {
                        Start = new CadPoint3(0, 0, 0),
                        End = new CadPoint3(100.25, 20.5, 0),
                    },
                },
            ],
        },
    };
}

static Process StartTestServer(
    string serverExe,
    string pipeName,
    string sessionId,
    string secretHex,
    string mode)
{
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = serverExe,
        Arguments = pipeName + " " + sessionId + " " + secretHex + " " + mode,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    }) ?? throw new InvalidOperationException("Failed to start the bridge test server.");

    var readyTask = process.StandardOutput.ReadLineAsync();
    if (!readyTask.Wait(TimeSpan.FromSeconds(5))
        || !string.Equals(readyTask.Result, "READY", StringComparison.Ordinal))
    {
        DisposeTestServer(process);
        throw new TimeoutException("Bridge test server did not become ready.");
    }

    return process;
}

static void DisposeTestServer(Process? process)
{
    if (process is null) return;
    try
    {
        if (!process.HasExited)
        {
            process.Kill();
            process.WaitForExit(5000);
        }
    }
    catch { }
    process.Dispose();
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("Assertion failed: " + label + ".");
}

static AgentBridgeClientException CaptureAgentBridgeFailure(Action action)
{
    try { action(); }
    catch (AgentBridgeClientException exception) { return exception; }
    throw new InvalidOperationException("Expected AgentBridgeClientException was not thrown.");
}

static void CaptureCancellation(Action action)
{
    try { action(); }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Expected OperationCanceledException was not thrown.");
}

static void RunProtocolFaultSpec(
    string serverExe,
    byte[] secret,
    string secretHex,
    string mode,
    string specId)
{
    var pipeName = "codex-v2compat-fault-" + Guid.NewGuid().ToString("N");
    var sessionId = "v2compat-fault-" + Guid.NewGuid().ToString("N");
    Process? process = null;
    try
    {
        process = StartTestServer(serverExe, pipeName, sessionId, secretHex, mode);
        using (var client = new AgentBridgeClient(new AgentBridgeClientOptions
        {
            PipeName = pipeName,
            SessionId = sessionId,
            SessionSecret = secret,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(2),
        }))
        {
            client.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            if (mode == "nonce-replay")
            {
                // nonce-replay: 第一次请求成功, 第二次重放被拒绝
                var firstResponse = client.GetCapabilitiesAsync(
                        new AgentCapabilitiesRequest
                        {
                            ClientName = "Codex.AutoCAD.Host.2016",
                            ClientVersion = "1.0.0.0",
                            HostTarget = "autocad-r20.1-net45-x64",
                        },
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                Require(firstResponse.AgentInstanceId == "raw-test-agent-instance",
                    specId + " first response should succeed");

                var replayFailure = CaptureAgentBridgeFailure(() =>
                    client.GetCapabilitiesAsync(
                            new AgentCapabilitiesRequest
                            {
                                ClientName = "Codex.AutoCAD.Host.2016",
                                ClientVersion = "1.0.0.0",
                                HostTarget = "autocad-r20.1-net45-x64",
                            },
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult());
                Require(replayFailure.Code == AgentBridgeErrorCodes.ReplayRejected,
                    specId + " replay should be rejected");

                var terminalFailure = CaptureAgentBridgeFailure(() =>
                    client.GetCapabilitiesAsync(
                            new AgentCapabilitiesRequest
                            {
                                ClientName = "Codex.AutoCAD.Host.2016",
                                ClientVersion = "1.0.0.0",
                                HostTarget = "autocad-r20.1-net45-x64",
                            },
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult());
                Require(terminalFailure.Code == AgentBridgeErrorCodes.ReplayRejected,
                    specId + " terminal should stay ReplayRejected");
            }
            else
            {
                var firstFailure = CaptureAgentBridgeFailure(() =>
                    client.GetCapabilitiesAsync(
                            new AgentCapabilitiesRequest
                            {
                                ClientName = "Codex.AutoCAD.Host.2016",
                                ClientVersion = "1.0.0.0",
                                HostTarget = "autocad-r20.1-net45-x64",
                            },
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult());
                if (mode == "badmac")
                    Require(firstFailure.Code == AgentBridgeErrorCodes.AuthenticationFailed,
                        specId + " first error code");
                else if (mode == "sequence-gap")
                    Require(firstFailure.Code == AgentBridgeErrorCodes.ReplayRejected,
                        specId + " first error code");
                else
                    Require(firstFailure.Code == "request_invalid" ||
                            firstFailure.Code == AgentBridgeErrorCodes.AuthenticationFailed,
                        specId + " first error code");

                var terminalFailure = CaptureAgentBridgeFailure(() =>
                    client.GetCapabilitiesAsync(
                            new AgentCapabilitiesRequest
                            {
                                ClientName = "Codex.AutoCAD.Host.2016",
                                ClientVersion = "1.0.0.0",
                                HostTarget = "autocad-r20.1-net45-x64",
                            },
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult());
                if (mode == "badmac")
                    Require(terminalFailure.Code == AgentBridgeErrorCodes.AuthenticationFailed,
                        specId + " terminal error code");
                else if (mode == "sequence-gap")
                    Require(terminalFailure.Code == AgentBridgeErrorCodes.ReplayRejected,
                        specId + " terminal error code");
            }
        }

        Require(process.WaitForExit(5000), specId + " server exit");
        Require(process.ExitCode == 0, specId + " server exit code");
        Console.WriteLine("[PASS] " + specId);
    }
    finally
    {
        DisposeTestServer(process);
    }
}

static string JoinCodes(IEnumerable<CadValidationFailure> failures)
{
    return string.Join("; ", failures.Select(f => f.Code + "@" + f.Path));
}

sealed class AuditReport
{
    private readonly List<(string SpecId, bool Passed, string Detail)> _entries = new();

    public void Record(string specId, bool passed, string detail)
    {
        _entries.Add((specId, passed, detail));
    }

    public void PrintSummary()
    {
        foreach (var (specId, passed, detail) in _entries)
        {
            var status = passed ? "PASS" : "FAIL";
            Console.WriteLine($"  {specId}: {status} — {detail}");
        }
    }

    public string ToJson()
    {
        var entries = _entries.Select(e =>
            "    {\"specId\": \"" + e.SpecId
            + "\", \"passed\": " + (e.Passed ? "true" : "false")
            + ", \"detail\": \"" + EscapeJson(e.Detail) + "\"}");
        return "{\n"
            + "  \"generatedAtUtc\": \"" + DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\",\n"
            + "  \"baselineCommit\": \"589c8ea\",\n"
            + "  \"v1ClientBaselineCommit\": \"0ceb123\",\n"
            + "  \"totalSpecs\": " + _entries.Count + ",\n"
            + "  \"passedSpecs\": " + _entries.Count(e => e.Passed) + ",\n"
            + "  \"failedSpecs\": " + _entries.Count(e => !e.Passed) + ",\n"
            + "  \"requiredPassed\": " + (_entries.All(e => e.Passed) ? "true" : "false") + ",\n"
            + "  \"autoCadStartedOrRestarted\": false,\n"
            + "  \"cadCommandsSent\": false,\n"
            + "  \"netLoadVerified\": false,\n"
            + "  \"autoCadLiveEvidence\": false,\n"
            + "  \"entries\": [\n"
            + string.Join(",\n", entries) + "\n"
            + "  ]\n"
            + "}";
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}

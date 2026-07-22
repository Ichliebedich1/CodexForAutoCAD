using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016;

var passed = 0;
var failed = 0;

Run("HOST2016_CAPABILITIES_IDENTITY", () =>
{
    var request = MvpAgentProtocolIdentity.CreateCapabilitiesRequest();
    var failures = AgentBridgeContractValidator.Validate(request);
    return failures.Length == 0;
});

Run("HOST2016_V2_CAPABILITIES_ACCEPT", () =>
    MvpAgentCapabilityPolicy.SupportsCadContextV2(CreateCapabilities(true, true)));

Run("HOST2016_V2_CAPABILITIES_REJECT_NULL", () =>
    !MvpAgentCapabilityPolicy.SupportsCadContextV2(null!));

Run("HOST2016_V2_CAPABILITIES_REJECT_MISSING_METHOD", () =>
    !MvpAgentCapabilityPolicy.SupportsCadContextV2(CreateCapabilities(false, true)));

Run("HOST2016_V2_CAPABILITIES_REJECT_MISSING_SCHEMA", () =>
    !MvpAgentCapabilityPolicy.SupportsCadContextV2(CreateCapabilities(true, false)));

Run("HOST2016_V2_CAPABILITIES_REJECT_EMPTY_SCHEMA_LIST", () =>
{
    var capabilities = CreateCapabilities(true, true);
    capabilities.SupportedCadContextSchemas = Array.Empty<CadContextSchemaVersionEntry>();
    return !MvpAgentCapabilityPolicy.SupportsCadContextV2(capabilities);
});

Console.WriteLine($"{passed}/{passed + failed} specs passed");
return failed == 0 ? 0 : 1;

void Run(string name, Func<bool> test)
{
    try
    {
        if (test())
        {
            passed++;
            Console.WriteLine("PASS " + name);
        }
        else
        {
            failed++;
            Console.Error.WriteLine("FAIL " + name);
        }
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + name + " " + exception.GetType().Name);
    }
}

static AgentCapabilitiesResponse CreateCapabilities(bool includeV2Method, bool includeV2Schema)
{
    return new AgentCapabilitiesResponse
    {
        Methods = includeV2Method
            ? new[] { AgentBridgeMethods.StartTurn, AgentBridgeMethods.StartTurnV2 }
            : new[] { AgentBridgeMethods.StartTurn },
        SupportedCadContextSchemas = includeV2Schema
            ? new[]
            {
                new CadContextSchemaVersionEntry
                {
                    Schema = CadContextJsonV2Constants.Schema,
                    SchemaVersion = CadContextJsonV2Constants.SchemaVersion,
                },
            }
            : new[]
            {
                new CadContextSchemaVersionEntry
                {
                    Schema = CadContextJsonV1Constants.Schema,
                    SchemaVersion = CadContextJsonV1Constants.SchemaVersion,
                },
            },
    };
}

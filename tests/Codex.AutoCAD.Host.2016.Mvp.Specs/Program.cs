using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016;

var request = MvpAgentProtocolIdentity.CreateCapabilitiesRequest();
var failures = AgentBridgeContractValidator.Validate(request);
if (failures.Length != 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(
            "FAIL HOST2016_CAPABILITIES_IDENTITY "
            + failure.Code
            + " "
            + failure.Path);
    }

    Console.WriteLine("0/1 specs passed");
    return 1;
}

Console.WriteLine(
    "PASS HOST2016_CAPABILITIES_IDENTITY Host.2016 capability request satisfies v1");
Console.WriteLine("1/1 specs passed");
return 0;

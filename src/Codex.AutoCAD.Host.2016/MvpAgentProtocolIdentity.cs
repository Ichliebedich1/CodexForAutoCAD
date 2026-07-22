using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal static class MvpAgentProtocolIdentity
    {
        internal const string ClientName = "codex-autocad-2016-mvp";
        internal const string ClientVersion = "0.3.2.0";
        internal const string HostTarget = "autocad-r20.1-net45-x64";

        internal static AgentCapabilitiesRequest CreateCapabilitiesRequest()
        {
            return new AgentCapabilitiesRequest
            {
                ClientName = ClientName,
                ClientVersion = ClientVersion,
                HostTarget = HostTarget,
            };
        }
    }
}

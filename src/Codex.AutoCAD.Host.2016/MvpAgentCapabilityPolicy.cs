using System;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal static class MvpAgentCapabilityPolicy
    {
        internal static bool SupportsCadContextV2(AgentCapabilitiesResponse capabilities)
        {
            if (capabilities == null
                || capabilities.Methods == null
                || Array.IndexOf(capabilities.Methods, AgentBridgeMethods.StartTurnV2) < 0
                || capabilities.SupportedCadContextSchemas == null
                || capabilities.SupportedCadContextSchemas.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < capabilities.SupportedCadContextSchemas.Length; index++)
            {
                var schema = capabilities.SupportedCadContextSchemas[index];
                if (schema != null
                    && string.Equals(
                        schema.Schema,
                        CadContextJsonV2Constants.Schema,
                        StringComparison.Ordinal)
                    && schema.SchemaVersion == CadContextJsonV2Constants.SchemaVersion)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool SupportsDrawingQuery(AgentCapabilitiesResponse capabilities)
        {
            return capabilities != null
                   && capabilities.Methods != null
                   && Array.IndexOf(
                       capabilities.Methods,
                       AgentBridgeMethods.QueryDrawing) >= 0;
        }
    }
}

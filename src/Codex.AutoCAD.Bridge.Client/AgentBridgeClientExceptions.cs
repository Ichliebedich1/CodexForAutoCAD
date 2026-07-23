using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.Bridge.Client;

public class AgentBridgeClientException : IOException
{
    public AgentBridgeClientException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public AgentBridgeClientException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class AgentBridgeAuthenticationException : AgentBridgeClientException
{
    public AgentBridgeAuthenticationException(IpcValidationCode validationCode)
        : base(MapErrorCode(validationCode), "Agent Bridge消息认证失败：" + validationCode + "。")
    {
        ValidationCode = validationCode;
    }

    public IpcValidationCode ValidationCode { get; }

    private static string MapErrorCode(IpcValidationCode validationCode)
    {
        return validationCode == IpcValidationCode.InvalidMac
            ? AgentBridgeErrorCodes.AuthenticationFailed
            : AgentBridgeErrorCodes.ReplayRejected;
    }
}

public sealed class AgentBridgeRemoteException : AgentBridgeClientException
{
    public AgentBridgeRemoteException(string code, string message)
        : base(
            AgentBridgeErrorSanitizer.NormalizeCode(code),
            AgentBridgeErrorSanitizer.GetSafeMessage(code))
    {
    }
}

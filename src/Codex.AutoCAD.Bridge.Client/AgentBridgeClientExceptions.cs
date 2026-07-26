using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.Bridge.Client;

public class AgentBridgeClientException : IOException
{
    public AgentBridgeClientException(string code, string message)
        : this(code, message, DiagnosticDataClassification.Exception, null)
    {
    }

    public AgentBridgeClientException(string code, string message, Exception innerException)
        : this(code, message, DiagnosticDataClassification.Exception, innerException)
    {
    }

    protected AgentBridgeClientException(
        string code,
        string message,
        DiagnosticDataClassification classification)
        : this(code, message, classification, null)
    {
    }

    private AgentBridgeClientException(
        string code,
        string message,
        DiagnosticDataClassification classification,
        Exception? unsafeInnerException)
        : base(DiagnosticSanitizer.SanitizeText(classification, message).SafeText)
    {
        var messageDiagnostic = DiagnosticSanitizer.SanitizeText(classification, message);
        var codeDiagnostic = DiagnosticSanitizer.SanitizeText(classification, code);
        var nestedDiagnostic = DiagnosticSanitizer.SanitizeException(
            classification,
            unsafeInnerException);
        Code = IsStableErrorCode(code) ? code : AgentBridgeErrorCodes.InternalError;
        DiagnosticClassification = messageDiagnostic.Classification;
        DiagnosticRedactions = messageDiagnostic.Redactions
            | codeDiagnostic.Redactions
            | nestedDiagnostic.Redactions;
        // The original exception may contain process arguments, paths, environment data, or
        // credentials in Message/Data/StackTrace. It is deliberately not retained at this
        // public transport boundary; callers receive only the stable code and sanitized evidence.
    }

    public string Code { get; }

    public DiagnosticDataClassification DiagnosticClassification { get; }

    public DiagnosticRedactionKinds DiagnosticRedactions { get; }

    private static bool IsStableErrorCode(string? value)
        => value is not null
            && !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(static character => character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-' or '.');
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
            code,
            string.IsNullOrWhiteSpace(message) ? "Agent Bridge远端请求失败。" : message,
            DiagnosticDataClassification.RemoteError)
    {
    }
}

using Codex.AutoCAD.Ipc;
using DiagnosticDataClassification = Codex.AutoCAD.Contracts.DiagnosticDataClassification;
using DiagnosticRedactionKinds = Codex.AutoCAD.Contracts.DiagnosticRedactionKinds;
using DiagnosticSanitizationResult = Codex.AutoCAD.Contracts.DiagnosticSanitizationResult;
using DiagnosticSanitizer = Codex.AutoCAD.Contracts.DiagnosticSanitizer;

namespace Codex.AutoCAD.Bridge;

public static class BridgeMessageTypes
{
    public const string Request = "bridge.request";
    public const string Response = "bridge.response";
    public const string Notification = "bridge.notification";
    public const string Cancel = "bridge.cancel";
}

public sealed record BridgeRequest(string RequestId, string Method, string BodyJson)
{
    public override string ToString()
        => nameof(BridgeRequest)
            + " { RequestIdConfigured = "
            + BridgeDiagnosticFormatting.Configured(RequestId)
            + ", MethodConfigured = "
            + BridgeDiagnosticFormatting.Configured(Method)
            + ", BodyJsonConfigured = "
            + BridgeDiagnosticFormatting.Configured(BodyJson)
            + " }";
}

public sealed record BridgeNotification(string NotificationId, string Method, string BodyJson)
{
    public override string ToString()
        => nameof(BridgeNotification)
            + " { NotificationIdConfigured = "
            + BridgeDiagnosticFormatting.Configured(NotificationId)
            + ", MethodConfigured = "
            + BridgeDiagnosticFormatting.Configured(Method)
            + ", BodyJsonConfigured = "
            + BridgeDiagnosticFormatting.Configured(BodyJson)
            + " }";
}

internal static class BridgeDiagnosticFormatting
{
    internal static string Configured(string? value)
        => string.IsNullOrWhiteSpace(value) ? "False" : "True";
}

public sealed class BridgeResponseSentEventArgs : EventArgs
{
    public BridgeResponseSentEventArgs(string requestId, bool succeeded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        RequestId = requestId;
        Succeeded = succeeded;
    }

    public string RequestId { get; }

    public bool Succeeded { get; }
}

public delegate ValueTask<string?> BridgeRequestHandler(
    BridgeRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask BridgeNotificationHandler(
    BridgeNotification notification,
    CancellationToken cancellationToken);

public class BridgeProtocolException : IOException
{
    public BridgeProtocolException(string message)
        : this(
            DiagnosticSanitizer.SanitizeText(
                DiagnosticDataClassification.Exception,
                message),
            null)
    {
    }

    public BridgeProtocolException(string message, Exception innerException)
        : this(
            DiagnosticSanitizer.SanitizeText(
                DiagnosticDataClassification.Exception,
                message),
            DiagnosticSanitizer.SanitizeException(
                DiagnosticDataClassification.Exception,
                innerException))
    {
    }

    internal BridgeProtocolException(
        string safeMessage,
        DiagnosticDataClassification diagnosticClassification,
        DiagnosticRedactionKinds diagnosticRedactions)
        : base(safeMessage)
    {
        DiagnosticClassification = diagnosticClassification;
        DiagnosticRedactions = diagnosticRedactions;
    }

    private BridgeProtocolException(
        DiagnosticSanitizationResult message,
        DiagnosticSanitizationResult? sourceException)
        : base(message.SafeText)
    {
        DiagnosticClassification = message.Classification;
        DiagnosticRedactions = message.Redactions
            | (sourceException?.Redactions ?? DiagnosticRedactionKinds.None);
    }

    public DiagnosticDataClassification DiagnosticClassification { get; }

    public DiagnosticRedactionKinds DiagnosticRedactions { get; }
}

public sealed class BridgeAuthenticationException : BridgeProtocolException
{
    public BridgeAuthenticationException(IpcValidationCode validationCode)
        : base($"IPC消息验证失败：{validationCode}。")
    {
        ValidationCode = validationCode;
    }

    public IpcValidationCode ValidationCode { get; }
}

public sealed class BridgeTerminalException : IOException
{
    internal BridgeTerminalException(
        DiagnosticDataClassification diagnosticClassification,
        DiagnosticRedactionKinds diagnosticRedactions)
        : base("Authenticated Bridge transport failed.")
    {
        DiagnosticClassification = diagnosticClassification;
        DiagnosticRedactions = diagnosticRedactions;
    }

    public DiagnosticDataClassification DiagnosticClassification { get; }

    public DiagnosticRedactionKinds DiagnosticRedactions { get; }
}

public sealed class BridgeRemoteException : Exception
{
    public BridgeRemoteException(string code, string message)
        : this(
            code,
            DiagnosticSanitizer.SanitizeText(
                DiagnosticDataClassification.RemoteError,
                message),
            DiagnosticSanitizer.SanitizeText(
                DiagnosticDataClassification.RemoteError,
                code))
    {
    }

    private BridgeRemoteException(
        string code,
        DiagnosticSanitizationResult message,
        DiagnosticSanitizationResult codeDiagnostic)
        : base(message.SafeText)
    {
        Code = IsStableErrorCode(code) ? code : "remote_error";
        DiagnosticClassification = message.Classification;
        DiagnosticRedactions = message.Redactions | codeDiagnostic.Redactions;
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

internal sealed class RequestPayload
{
    public string Method { get; set; } = string.Empty;

    public string BodyJson { get; set; } = "null";
}

internal sealed class ResponsePayload
{
    public string BodyJson { get; set; } = "null";

    public string ErrorCode { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;
}

internal sealed class CancelPayload
{
    public string Reason { get; set; } = string.Empty;
}

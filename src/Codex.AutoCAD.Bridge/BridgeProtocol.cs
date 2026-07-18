using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.Bridge;

public static class BridgeMessageTypes
{
    public const string Request = "bridge.request";
    public const string Response = "bridge.response";
    public const string Notification = "bridge.notification";
    public const string Cancel = "bridge.cancel";
}

public sealed record BridgeRequest(string RequestId, string Method, string BodyJson);

public sealed record BridgeNotification(string NotificationId, string Method, string BodyJson);

public delegate ValueTask<string?> BridgeRequestHandler(
    BridgeRequest request,
    CancellationToken cancellationToken);

public delegate ValueTask BridgeNotificationHandler(
    BridgeNotification notification,
    CancellationToken cancellationToken);

public class BridgeProtocolException : IOException
{
    public BridgeProtocolException(string message)
        : base(message)
    {
    }

    public BridgeProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
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

public sealed class BridgeRemoteException : Exception
{
    public BridgeRemoteException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
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

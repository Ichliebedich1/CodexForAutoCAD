using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AppServer;

namespace Codex.AutoCAD.AppServer.Protocol;

public sealed record AppServerClientInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("version")] string Version);

public sealed record AppServerInitializeCapabilities(
    [property: JsonPropertyName("experimentalApi")] bool ExperimentalApi = false,
    [property: JsonPropertyName("mcpServerOpenaiFormElicitation")] bool? McpServerOpenaiFormElicitation = null,
    [property: JsonPropertyName("optOutNotificationMethods")] IReadOnlyList<string>? OptOutNotificationMethods = null,
    [property: JsonPropertyName("requestAttestation")] bool RequestAttestation = false);

public sealed record AppServerInitializeParams(
    [property: JsonPropertyName("clientInfo")] AppServerClientInfo ClientInfo,
    [property: JsonPropertyName("capabilities")] AppServerInitializeCapabilities? Capabilities = null);

public sealed record AppServerInitializeResponse(
    [property: JsonPropertyName("codexHome")] string CodexHome,
    [property: JsonPropertyName("platformFamily")] string PlatformFamily,
    [property: JsonPropertyName("platformOs")] string PlatformOs,
    [property: JsonPropertyName("userAgent")] string UserAgent);

public sealed record TurnInterruptParams(
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId);

public sealed record EmptyResponse;

public sealed record AppServerNotification(string Method, JsonElement? Params);

public sealed record AppServerServerRequest(JsonRpcId Id, string Method, JsonElement? Params);

public sealed record RpcApprovalEvent<TRequest>(JsonRpcId RequestId, TRequest Request);

public sealed record AppServerRpcError(
    [property: JsonPropertyName("code")] long Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] JsonElement? Data = null);

public sealed record ServerRequestResolution
{
    private ServerRequestResolution(object? result, AppServerRpcError? error)
    {
        Result = result;
        Error = error;
    }

    public object? Result { get; }

    public AppServerRpcError? Error { get; }

    public static ServerRequestResolution Success(object? result = null) => new(result, null);

    public static ServerRequestResolution Failure(long code, string message, JsonElement? data = null)
        => new(null, new AppServerRpcError(code, message, data));
}

public sealed class AppServerProcessExitedEventArgs(
    int? exitCode,
    bool expected,
    IReadOnlyList<AppServerStandardErrorSummary> standardErrorTail) : EventArgs
{
    public int? ExitCode { get; } = exitCode;

    public bool Expected { get; } = expected;

    public IReadOnlyList<AppServerStandardErrorSummary> StandardErrorTail { get; } = standardErrorTail;
}

public sealed class AppServerProtocolFaultEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

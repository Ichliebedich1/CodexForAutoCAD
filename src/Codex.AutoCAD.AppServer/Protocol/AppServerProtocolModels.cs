using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.AppServer.Protocol;

public sealed record AppServerClientInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("version")] string Version)
{
    public override string ToString()
        => nameof(AppServerClientInfo)
            + " { NameConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(Name)
            + ", TitleConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(Title)
            + ", VersionConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(Version)
            + " }";
}

public sealed record AppServerInitializeCapabilities(
    [property: JsonPropertyName("experimentalApi")] bool ExperimentalApi = false,
    [property: JsonPropertyName("mcpServerOpenaiFormElicitation")] bool? McpServerOpenaiFormElicitation = null,
    [property: JsonPropertyName("optOutNotificationMethods")] IReadOnlyList<string>? OptOutNotificationMethods = null,
    [property: JsonPropertyName("requestAttestation")] bool RequestAttestation = false)
{
    public override string ToString()
        => nameof(AppServerInitializeCapabilities)
            + " { ExperimentalApi = "
            + ExperimentalApi
            + ", McpServerOpenaiFormElicitation = "
            + (McpServerOpenaiFormElicitation.HasValue
                ? McpServerOpenaiFormElicitation.Value.ToString()
                : "NotSet")
            + ", OptOutNotificationMethodCount = "
            + (OptOutNotificationMethods?.Count ?? 0).ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            + ", RequestAttestation = "
            + RequestAttestation
            + " }";
}

public sealed record AppServerInitializeParams(
    [property: JsonPropertyName("clientInfo")] AppServerClientInfo ClientInfo,
    [property: JsonPropertyName("capabilities")] AppServerInitializeCapabilities? Capabilities = null)
{
    public override string ToString()
        => nameof(AppServerInitializeParams)
            + " { ClientInfoPresent = "
            + (ClientInfo is not null)
            + ", CapabilitiesPresent = "
            + (Capabilities is not null)
            + " }";
}

public sealed record AppServerInitializeResponse(
    [property: JsonPropertyName("codexHome")] string CodexHome,
    [property: JsonPropertyName("platformFamily")] string PlatformFamily,
    [property: JsonPropertyName("platformOs")] string PlatformOs,
    [property: JsonPropertyName("userAgent")] string UserAgent)
{
    public override string ToString()
        => nameof(AppServerInitializeResponse)
            + " { CodexHomeConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(CodexHome)
            + ", PlatformFamilyConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(PlatformFamily)
            + ", PlatformOsConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(PlatformOs)
            + ", UserAgentConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(UserAgent)
            + " }";
}

public sealed record TurnInterruptParams(
    [property: JsonPropertyName("threadId")] string ThreadId,
    [property: JsonPropertyName("turnId")] string TurnId)
{
    public override string ToString()
        => nameof(TurnInterruptParams)
            + " { ThreadIdConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(ThreadId)
            + ", TurnIdConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(TurnId)
            + " }";
}

public sealed record EmptyResponse;

public sealed record AppServerNotification(string Method, JsonElement? Params)
{
    public override string ToString()
        => nameof(AppServerNotification)
            + " { MethodConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(Method)
            + ", ParamsPresent = "
            + Params.HasValue
            + " }";
}

public sealed record AppServerServerRequest(JsonRpcId Id, string Method, JsonElement? Params)
{
    public override string ToString()
        => nameof(AppServerServerRequest)
            + " { IdConfigured = True, MethodConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(Method)
            + ", ParamsPresent = "
            + Params.HasValue
            + " }";
}

public sealed record RpcApprovalEvent<TRequest>(JsonRpcId RequestId, TRequest Request)
{
    public override string ToString()
        => "RpcApprovalEvent { RequestIdConfigured = True, RequestPresent = "
            + (Request is not null)
            + " }";
}

public sealed record AppServerRpcError(
    [property: JsonPropertyName("code")] long Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] JsonElement? Data = null)
{
    public override string ToString()
        => nameof(AppServerRpcError)
            + " { Code = "
            + Code
            + ", MessageConfigured = "
            + AppServerProtocolDiagnosticFormatting.Configured(Message)
            + ", DataPresent = "
            + Data.HasValue
            + " }";
}

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

    public override string ToString()
        => nameof(ServerRequestResolution)
            + " { Succeeded = "
            + (Error is null)
            + ", ResultPresent = "
            + (Result is not null)
            + ", ErrorPresent = "
            + (Error is not null)
            + " }";
}

internal static class AppServerProtocolDiagnosticFormatting
{
    internal static string Configured(string? value)
        => string.IsNullOrWhiteSpace(value) ? "False" : "True";
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

public sealed class AppServerProtocolFaultEventArgs : EventArgs
{
    public AppServerProtocolFaultEventArgs(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var sourceDiagnostic = exception as AppServerException;
        DiagnosticClassification = sourceDiagnostic?.DiagnosticClassification
            ?? DiagnosticDataClassification.Exception;
        var diagnostic = DiagnosticSanitizer.SanitizeException(
            DiagnosticClassification,
            exception);
        DiagnosticRedactions = diagnostic.Redactions
            | (sourceDiagnostic?.DiagnosticRedactions ?? DiagnosticRedactionKinds.None);
        Exception = new AppServerProtocolException(
            "App Server protocol fault.",
            DiagnosticClassification);
    }

    /// <summary>
    /// Compatibility projection containing a new sanitized exception without the source exception,
    /// stack trace, data dictionary, or inner exception graph.
    /// </summary>
    public Exception Exception { get; }

    public DiagnosticDataClassification DiagnosticClassification { get; }

    public DiagnosticRedactionKinds DiagnosticRedactions { get; }
}

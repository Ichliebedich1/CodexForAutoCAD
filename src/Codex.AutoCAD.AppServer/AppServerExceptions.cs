using System.Text.Json;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.AppServer;

public class AppServerException : Exception
{
    public AppServerException(string message)
        : this(message, DiagnosticDataClassification.Exception)
    {
    }

    public AppServerException(string message, Exception innerException)
        : this(message, DiagnosticDataClassification.Exception)
    {
        // Public AppServer diagnostics must not retain an arbitrary source exception because its
        // message, data, or stack trace can include process arguments, paths, or credentials.
        _ = innerException;
    }

    protected AppServerException(
        string message,
        DiagnosticDataClassification classification)
        : this(DiagnosticSanitizer.SanitizeText(classification, message))
    {
    }

    protected AppServerException(DiagnosticSanitizationResult diagnostic)
        : base((diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).SafeText)
    {
        DiagnosticClassification = diagnostic.Classification;
        DiagnosticRedactions = diagnostic.Redactions;
    }

    public DiagnosticDataClassification DiagnosticClassification { get; }

    public DiagnosticRedactionKinds DiagnosticRedactions { get; }
}

public sealed class AppServerProtocolException : AppServerException
{
    public AppServerProtocolException(string message) : base(message) { }

    public AppServerProtocolException(string message, Exception innerException) : base(message, innerException) { }

    internal AppServerProtocolException(
        string safeMessage,
        DiagnosticDataClassification classification)
        : base(safeMessage, classification)
    {
    }
}

public sealed class AppServerRpcException : AppServerException
{
    public AppServerRpcException(long code, string message, JsonElement? data)
        : this(
            code,
            DiagnosticSanitizer.SanitizeText(
                DiagnosticDataClassification.RemoteError,
                message),
            data.HasValue)
    {
    }

    private AppServerRpcException(
        long code,
        DiagnosticSanitizationResult diagnostic,
        bool dataWasPresent)
        : base(diagnostic)
    {
        Code = code;
        RpcMessage = diagnostic.SafeText;
        DataWasPresent = dataWasPresent;
    }

    public long Code { get; }

    public string RpcMessage { get; }

    public bool DataWasPresent { get; }

    /// <summary>
    /// Raw remote RPC data is intentionally not retained at the public diagnostic boundary.
    /// </summary>
    public JsonElement? DataElement { get; }
}

public sealed class AppServerProcessExitedException : AppServerException
{
    public AppServerProcessExitedException(
        int? exitCode,
        IReadOnlyList<AppServerStandardErrorSummary>? standardErrorTail = null)
        : base(exitCode is null ? "Codex App Server connection closed." : $"Codex App Server exited with code {exitCode}.")
    {
        ExitCode = exitCode;
        StandardErrorTail = standardErrorTail ?? Array.Empty<AppServerStandardErrorSummary>();
    }

    public int? ExitCode { get; }

    public IReadOnlyList<AppServerStandardErrorSummary> StandardErrorTail { get; }
}

public sealed class AppServerProcessTerminationException : AppServerException
{
    public AppServerProcessTerminationException(string message)
        : base(message)
    {
    }
}

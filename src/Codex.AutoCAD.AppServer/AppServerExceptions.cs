using System.Text.Json;

namespace Codex.AutoCAD.AppServer;

public class AppServerException : Exception
{
    public AppServerException(string message) : base(message) { }

    public AppServerException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class AppServerProtocolException : AppServerException
{
    public AppServerProtocolException(string message) : base(message) { }

    public AppServerProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class AppServerRpcException : AppServerException
{
    public AppServerRpcException(long code, string message, JsonElement? data)
        : base($"Codex App Server RPC error {code}: {message}")
    {
        Code = code;
        RpcMessage = message;
        DataElement = data?.Clone();
    }

    public long Code { get; }

    public string RpcMessage { get; }

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

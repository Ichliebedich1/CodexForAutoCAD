namespace Codex.AutoCAD.AppServer;

/// <summary>
/// Validated, process-local Codex state locations and the transient credential supplied to an
/// app-server child. This type is internal so callers cannot accidentally treat these values as
/// normal application configuration or log them as diagnostics.
/// </summary>
internal sealed class CodexSessionIsolation
{
    internal CodexSessionIsolation(
        string codexHomeDirectory,
        string codexSqliteHomeDirectory,
        string codexAccessToken)
    {
        CodexHomeDirectory = codexHomeDirectory;
        CodexSqliteHomeDirectory = codexSqliteHomeDirectory;
        CodexAccessToken = codexAccessToken;
    }

    internal string CodexHomeDirectory { get; }

    internal string CodexSqliteHomeDirectory { get; }

    internal string CodexAccessToken { get; }
}

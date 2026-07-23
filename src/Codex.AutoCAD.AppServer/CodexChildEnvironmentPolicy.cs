using System.Collections.ObjectModel;

namespace Codex.AutoCAD.AppServer;

/// <summary>
/// Builds the complete environment for an AgentHost-owned Codex child. Values are derived from
/// named operating-system locations instead of copying the parent environment.
/// </summary>
internal static class CodexChildEnvironmentPolicy
{
    internal static IReadOnlyDictionary<string, string?> CreateForCurrentProcess(
        string temporaryDirectory,
        CodexSessionIsolation? sessionIsolation = null)
    {
        var systemRoot = RequireDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Windows");
        var userProfile = RequireDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "UserProfile");
        var applicationData = RequireDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ApplicationData");
        var localApplicationData = RequireDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalApplicationData");
        var temp = RequireDirectory(temporaryDirectory, "TemporaryDirectory");
        var system32 = RequireDirectory(Path.Combine(systemRoot, "System32"), "System32");
        var commandProcessor = RequireFile(Path.Combine(system32, "cmd.exe"), "CommandProcessor");

        var pathDirectories = new[]
        {
            system32,
            systemRoot,
            Path.Combine(system32, "Wbem"),
            Path.Combine(system32, "WindowsPowerShell", "v1.0"),
        }
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = systemRoot,
            ["WINDIR"] = systemRoot,
            ["ComSpec"] = commandProcessor,
            ["USERPROFILE"] = userProfile,
            ["HOME"] = userProfile,
            ["APPDATA"] = applicationData,
            ["LOCALAPPDATA"] = localApplicationData,
            ["TEMP"] = temp,
            ["TMP"] = temp,
            ["PATH"] = string.Join(Path.PathSeparator, pathDirectories),
            ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
            ["RUST_LOG"] = "error",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = "NUL",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
        };

        if (sessionIsolation is not null)
        {
            // CODEX_HOME is an explicit Codex override. The home directories are created and
            // ACL-checked by AgentHost before they reach this policy.
            values["CODEX_HOME"] = sessionIsolation.CodexHomeDirectory;
            values["CODEX_SQLITE_HOME"] = sessionIsolation.CodexSqliteHomeDirectory;
            values["CODEX_ACCESS_TOKEN"] = sessionIsolation.CodexAccessToken;
        }

        return new ReadOnlyDictionary<string, string?>(values);
    }

    private static string RequireDirectory(string? value, string locationName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value)
                && Path.IsPathFullyQualified(value)
                && Directory.Exists(value))
            {
                return Path.GetFullPath(value);
            }
        }
        catch (Exception exception) when (IsPathException(exception))
        {
        }

        throw new InvalidOperationException(
            "A required Codex child environment directory is unavailable: " + locationName + ".");
    }

    private static string RequireFile(string value, string locationName)
    {
        try
        {
            if (Path.IsPathFullyQualified(value) && File.Exists(value))
            {
                return Path.GetFullPath(value);
            }
        }
        catch (Exception exception) when (IsPathException(exception))
        {
        }

        throw new InvalidOperationException(
            "A required Codex child environment file is unavailable: " + locationName + ".");
    }

    private static bool IsPathException(Exception exception)
    {
        return exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException;
    }
}

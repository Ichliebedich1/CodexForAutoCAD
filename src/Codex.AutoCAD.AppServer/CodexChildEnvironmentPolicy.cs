using System.Collections.ObjectModel;

namespace Codex.AutoCAD.AppServer;

/// <summary>
/// Builds an environment-variable allowlist for an AgentHost-owned Codex child. The current
/// compatibility policy retains the user's profile locations so the installed Codex login and
/// configuration continue to work; it is not a credential or file-system isolation boundary.
/// </summary>
internal static class CodexChildEnvironmentPolicy
{
    private static readonly string[] ConnectivityVariableNames =
    {
        "ALL_PROXY",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "NO_PROXY",
        "SSL_CERT_FILE",
        "SSL_CERT_DIR",
        "CURL_CA_BUNDLE",
        "REQUESTS_CA_BUNDLE",
        "NODE_EXTRA_CA_CERTS",
    };

    internal static IReadOnlyDictionary<string, string?> CreateForCurrentProcess(
        string temporaryDirectory,
        string? codexHomeDirectory = null)
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

        foreach (var name in ConnectivityVariableNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                values[name] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(codexHomeDirectory))
        {
            values["CODEX_HOME"] = RequireDirectory(codexHomeDirectory, "CodexHome");
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

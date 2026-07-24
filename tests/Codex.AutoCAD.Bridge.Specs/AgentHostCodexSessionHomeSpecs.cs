using System.Text;
using Codex.AutoCAD.AgentHost;
using Codex.AutoCAD.AppServer;

internal static class AgentHostCodexSessionHomeSpecs
{
    public static Task CreatesMinimalHomeAndCleansOnDispose()
    {
        var root = CreateTemporaryDirectory();
        string homePath;
        try
        {
            using (var lease = CodexSessionHomeLease.Create(
                       root,
                       "0123456789abcdef0123456789abcdef"))
            {
                homePath = lease.HomePath;
                True(Directory.Exists(homePath), "Session Codex home was not created.");
                True(Directory.Exists(lease.CachePath), "Session Codex cache was not created.");
                True(Directory.Exists(lease.PluginsPath), "Session plugin directory was not created.");
                Equal(
                    "mcp_servers = {}\r\n\r\n[features]\r\nplugins = false\r\n",
                    File.ReadAllText(lease.ConfigurationPath, Encoding.UTF8));
                Equal(0, Directory.EnumerateFileSystemEntries(lease.PluginsPath).Count());
            }

            True(!Directory.Exists(homePath), "Disposed session Codex home was not removed.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static Task RejectsInvalidIdentityAndConcurrentOwner()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var invalid = Capture<CodexSessionHomeException>(() =>
                CodexSessionHomeLease.Create(root, "not-a-bootstrap-session"));
            Equal(CodexSessionHomeFailure.InvalidSessionId, invalid.Failure);

            using var owner = CodexSessionHomeLease.Create(
                root,
                "abcdef0123456789abcdef0123456789");
            var duplicate = Capture<CodexSessionHomeException>(() =>
                CodexSessionHomeLease.Create(
                    root,
                    "abcdef0123456789abcdef0123456789"));
            Equal(CodexSessionHomeFailure.AlreadyExists, duplicate.Failure);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public static Task FailuresUseStableAuditCodes()
    {
        Equal(
            AgentHostAuditErrorCodes.CodexSessionHomeInvalid,
            AgentHostAuditErrorCodes.FromException(new CodexSessionHomeException(
                CodexSessionHomeFailure.InvalidRoot,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexSessionHomeInUse,
            AgentHostAuditErrorCodes.FromException(new CodexSessionHomeException(
                CodexSessionHomeFailure.AlreadyExists,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexSessionHomeCleanupFailed,
            AgentHostAuditErrorCodes.FromException(new CodexSessionHomeException(
                CodexSessionHomeFailure.CleanupFailed,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexSessionHomeInitializationFailed,
            AgentHostAuditErrorCodes.FromException(new CodexSessionHomeException(
                CodexSessionHomeFailure.InitializationFailed,
                "sanitized")));
        Equal(
            AgentHostAuditErrorCodes.CodexHomeConfigurationInvalid,
            AgentHostAuditErrorCodes.FromException(new CodexLocalConfigurationException(
                CodexLocalConfigurationFailure.InvalidCodexHomeDirectory,
                "sanitized")));
        return Task.CompletedTask;
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "codex-autocad-session-home-spec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException("Expected values to be equal.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

using System.Text;
using System.Security.Cryptography;
using Codex.AutoCAD.AgentLauncher;
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
                    "cli_auth_credentials_store = \"keyring\"\r\n"
                        + "mcp_servers = {}\r\n\r\n[features]\r\nplugins = false\r\n",
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

    public static async Task CodexAccessTokenLoginUsesStdin()
    {
        var root = CreateTemporaryDirectory();
        var executable = Path.Combine(
            AppContext.BaseDirectory,
            "Codex.AutoCAD.Bridge.Specs.exe");
        var tokenBytes = Encoding.UTF8.GetBytes("m4-test-access-token");
        AgentHostCredentialSecret? secret = null;
        CodexSessionHomeLease? home = null;
        try
        {
            True(File.Exists(executable), "The Bridge Specs apphost is unavailable.");
            home = CodexSessionHomeLease.Create(
                root,
                "0123456789abcdef0123456789abcdef");
            secret = new AgentHostCredentialSecret(tokenBytes);
            var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
                new CodexLocalAppServerConfigurationRequest
                {
                    CommandLineExecutablePath = executable,
                    WorkingDirectory = root,
                    TemporaryDirectory = root,
                    CodexHomeDirectory = home.HomePath,
                });

            await CodexCredentialLogin.LoginAsync(
                    configuration,
                    home.HomePath,
                    secret,
                    TimeSpan.FromSeconds(10),
                    CancellationToken.None)
                .ConfigureAwait(false);

            var expectedDigest = SHA256.HashData(tokenBytes);
            try
            {
                Equal(
                    Convert.ToHexString(expectedDigest),
                    File.ReadAllText(
                        Path.Combine(home.HomePath, ".fake-login-sha256"),
                        Encoding.UTF8));
            }
            finally
            {
                Array.Clear(expectedDigest, 0, expectedDigest.Length);
            }

            True(
                !File.Exists(Path.Combine(home.HomePath, "auth.json")),
                "The fake login unexpectedly created auth.json.");
        }
        finally
        {
            secret?.Dispose();
            Array.Clear(tokenBytes, 0, tokenBytes.Length);
            home?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public static async Task CodexAccessTokenLoginFailuresFailClosed()
    {
        var failed = await RunLoginScenarioAsync("fail", TimeSpan.FromSeconds(5), CancellationToken.None)
            .ConfigureAwait(false);
        Equal(AgentBootstrapLaunchFailure.CredentialUnavailable, failed.Exception.Failure);
        True(!failed.Exception.Message.Contains("m4-secret-failure", StringComparison.Ordinal),
            "Credential failure message exposed the token.");
        True(!failed.Exception.ToString().Contains("m4-secret-failure", StringComparison.Ordinal),
            "Credential failure string exposed the token.");
        Equal("argv=False;env=False", failed.Observation);
        True(!failed.AuthFileExists, "Failed login unexpectedly created auth.json.");

        var authFile = await RunLoginScenarioAsync(
                "auth",
                TimeSpan.FromSeconds(5),
                CancellationToken.None)
            .ConfigureAwait(false);
        Equal(AgentBootstrapLaunchFailure.CredentialUnavailable, authFile.Exception.Failure);
        True(authFile.AuthFileExists, "Fake auth scenario did not create auth.json.");
        True(authFile.Exception.Message.Contains(
                "credential", StringComparison.OrdinalIgnoreCase),
            "auth.json creation did not fail closed.");

        var timedOut = await RunLoginScenarioAsync(
                "hang",
                TimeSpan.FromMilliseconds(150),
                CancellationToken.None)
            .ConfigureAwait(false);
        Equal(AgentBootstrapLaunchFailure.CredentialUnavailable, timedOut.Exception.Failure);
        True(timedOut.Exception.Message.Contains("credential", StringComparison.OrdinalIgnoreCase),
            "Timeout did not map to the credential-unavailable boundary.");
        True(!timedOut.AuthFileExists, "Timed-out login left auth.json.");
        Equal("argv=False;env=False", timedOut.Observation);

        using var cancellation = new CancellationTokenSource();
        var cancellationTask = RunLoginScenarioAsync(
            "hang",
            TimeSpan.FromSeconds(30),
            cancellation.Token);
        await Task.Delay(100).ConfigureAwait(false);
        cancellation.Cancel();
        var cancelled = await cancellationTask.ConfigureAwait(false);
        Equal(AgentBootstrapLaunchFailure.CredentialUnavailable, cancelled.Exception.Failure);
        True(!cancelled.AuthFileExists, "Cancelled login left auth.json.");
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

    private static async Task<LoginScenarioResult> RunLoginScenarioAsync(
        string mode,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var root = CreateTemporaryDirectory();
        var executable = Path.Combine(
            AppContext.BaseDirectory,
            "Codex.AutoCAD.Bridge.Specs.exe");
        var tokenBytes = Encoding.UTF8.GetBytes("m4-secret-" + mode);
        CodexSessionHomeLease? home = null;
        AgentHostCredentialSecret? secret = null;
        try
        {
            home = CodexSessionHomeLease.Create(
                root,
                "0123456789abcdef0123456789abcdef");
            File.WriteAllText(
                Path.Combine(home.HomePath, ".fake-login-mode"),
                mode,
                Encoding.UTF8);
            secret = new AgentHostCredentialSecret(tokenBytes);
            var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
                new CodexLocalAppServerConfigurationRequest
                {
                    CommandLineExecutablePath = executable,
                    WorkingDirectory = root,
                    TemporaryDirectory = root,
                    CodexHomeDirectory = home.HomePath,
                });

            AgentBootstrapLaunchException? exception = null;
            try
            {
                await CodexCredentialLogin.LoginAsync(
                        configuration,
                        home.HomePath,
                        secret,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AgentBootstrapLaunchException captured)
            {
                exception = captured;
            }

            if (exception == null)
            {
                throw new InvalidOperationException(
                    "The fake login scenario unexpectedly succeeded: " + mode);
            }

            var observationPath = Path.Combine(home.HomePath, ".fake-login-observation");
            var observation = File.Exists(observationPath)
                ? File.ReadAllText(observationPath, Encoding.UTF8)
                : string.Empty;
            return new LoginScenarioResult(
                exception,
                observation,
                File.Exists(Path.Combine(home.HomePath, "auth.json")));
        }
        finally
        {
            secret?.Dispose();
            Array.Clear(tokenBytes, 0, tokenBytes.Length);
            home?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class LoginScenarioResult
    {
        internal LoginScenarioResult(
            AgentBootstrapLaunchException exception,
            string observation,
            bool authFileExists)
        {
            Exception = exception;
            Observation = observation;
            AuthFileExists = authFileExists;
        }

        internal AgentBootstrapLaunchException Exception { get; }

        internal string Observation { get; }

        internal bool AuthFileExists { get; }
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

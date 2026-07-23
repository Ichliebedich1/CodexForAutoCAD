using System.Security.AccessControl;
using System.Security.Principal;
using Codex.AutoCAD.AppServer;
using Codex.AutoCAD.AgentHost;

internal static class AgentHostStorageSpecs
{
    public static Task WorkspaceUsesPrivateAclAndCleansNormally()
    {
        var fixture = new StorageFixture();
        try
        {
            var sessionId = Id('1');
            var sessionRoot = Path.Combine(fixture.SessionsRoot, sessionId);
            using (var workspace = AgentWorkspace.CreateSession(
                       fixture.SessionsRoot,
                       sessionId))
            {
                Equal(sessionRoot, workspace.Root);
                True(Directory.Exists(workspace.Root), "Session workspace was not created.");
                AssertPrivateDirectory(workspace.Root);
                AssertPrivateDirectory(workspace.Inputs);
                AssertPrivateDirectory(workspace.Work);
                AssertPrivateDirectory(workspace.Outputs);
                AssertPrivateDirectory(workspace.Temp);
                AssertPrivateFile(Path.Combine(workspace.Root, AgentWorkspace.LeaseFileName));
            }

            False(Directory.Exists(sessionRoot), "Normal disposal left the session workspace behind.");
            return Task.CompletedTask;
        }
        finally
        {
            fixture.Dispose();
        }
    }

    public static Task WorkspacePrunesStaleAndProtectsActiveSession()
    {
        var fixture = new StorageFixture();
        AgentWorkspace? active = null;
        AgentWorkspace? current = null;
        try
        {
            var now = new DateTime(2026, 7, 23, 8, 0, 0, DateTimeKind.Utc);
            active = AgentWorkspace.CreateSession(
                fixture.SessionsRoot,
                Id('2'),
                staleSessionAge: TimeSpan.FromDays(1),
                utcNow: now);
            Directory.SetLastWriteTimeUtc(active.Root, now - TimeSpan.FromDays(2));

            var staleRoot = AgentHostPrivateStorage.PreparePrivateDirectory(
                Path.Combine(fixture.SessionsRoot, Id('3')));
            File.WriteAllText(
                Path.Combine(staleRoot, AgentWorkspace.LeaseFileName),
                string.Empty);
            Directory.SetLastWriteTimeUtc(staleRoot, now - TimeSpan.FromDays(2));

            current = AgentWorkspace.CreateSession(
                fixture.SessionsRoot,
                Id('4'),
                staleSessionAge: TimeSpan.FromDays(1),
                utcNow: now);

            True(Directory.Exists(active.Root), "Retention deleted an active session workspace.");
            False(Directory.Exists(staleRoot), "Retention did not delete a stale inactive workspace.");
            True(Directory.Exists(current.Root), "Current session workspace is unavailable.");
            return Task.CompletedTask;
        }
        finally
        {
            current?.Dispose();
            active?.Dispose();
            fixture.Dispose();
        }
    }

    public static Task WorkspaceCleanupDoesNotFollowDirectoryLinks()
    {
        var fixture = new StorageFixture();
        AgentWorkspace? workspace = null;
        try
        {
            var outside = Path.Combine(fixture.Root, "outside");
            Directory.CreateDirectory(outside);
            var outsideSentinel = Path.Combine(outside, "must-survive.txt");
            File.WriteAllText(outsideSentinel, "survives");

            workspace = AgentWorkspace.CreateSession(fixture.SessionsRoot, Id('5'));
            var linkedDirectory = Path.Combine(workspace.Work, "outside-link");
            Directory.CreateSymbolicLink(linkedDirectory, outside);
            True(
                (File.GetAttributes(linkedDirectory) & FileAttributes.ReparsePoint) != 0,
                "The test directory link is not a reparse point.");

            var workspaceRoot = workspace.Root;
            workspace.Dispose();
            workspace = null;

            False(Directory.Exists(workspaceRoot), "Workspace cleanup left its private root behind.");
            True(File.Exists(outsideSentinel), "Workspace cleanup followed a directory link.");
            Equal("survives", File.ReadAllText(outsideSentinel));
            return Task.CompletedTask;
        }
        finally
        {
            workspace?.Dispose();
            fixture.Dispose();
        }
    }

    public static Task WorkspaceCodexStateUsesPrivateAclAndCleansNormally()
    {
        var fixture = new StorageFixture();
        try
        {
            var sessionId = Id('a');
            var sessionRoot = Path.Combine(fixture.SessionsRoot, sessionId);
            string codexHome;
            string codexSqliteHome;
            using (var workspace = AgentWorkspace.CreateSession(fixture.SessionsRoot, sessionId))
            {
                False(
                    Directory.Exists(Path.Combine(workspace.Root, "codex-home")),
                    "Codex state was created before an explicitly credentialed session.");
                var state = workspace.PrepareCodexState();
                codexHome = state.CodexHomeDirectory;
                codexSqliteHome = state.CodexSqliteHomeDirectory;
                AssertPrivateDirectory(codexHome);
                AssertPrivateDirectory(codexSqliteHome);
            }

            False(Directory.Exists(sessionRoot), "Session cleanup left Codex state behind.");
            False(Directory.Exists(codexHome), "Codex home directory survived disposal.");
            False(Directory.Exists(codexSqliteHome), "Codex SQLite directory survived disposal.");
            return Task.CompletedTask;
        }
        finally
        {
            fixture.Dispose();
        }
    }

    public static Task CodexSessionIsolationUsesApprovedCredentialOnly()
    {
        var fixture = new StorageFixture();
        try
        {
            const string credentialTarget = "CodexForAutoCAD/TestAccessToken";
            const string accessToken = "test-session-access-token";
            using var workspace = AgentWorkspace.CreateSession(fixture.SessionsRoot, Id('b'));
            var reader = new FakeCredentialReader(accessToken);
            var isolation = AgentHostCodexSessionIsolation.Create(
                credentialTarget,
                workspace,
                reader)
                ?? throw new InvalidOperationException("Configured credential did not enable isolation.");

            Equal(credentialTarget, reader.LastRequestedTarget);
            AssertPrivateDirectory(isolation.CodexHomeDirectory);
            AssertPrivateDirectory(isolation.CodexSqliteHomeDirectory);

            var executable = Path.Combine(workspace.Work, "codex.exe");
            File.WriteAllBytes(executable, Array.Empty<byte>());
            AgentHostPrivateStorage.ApplyPrivateFileAcl(executable);
            var configuration = CodexLocalAppServerConfigurationResolver.Resolve(
                new CodexLocalAppServerConfigurationRequest
                {
                    CommandLineExecutablePath = executable,
                    ApplicationDataDirectory = null,
                    WorkingDirectory = workspace.Work,
                    TemporaryDirectory = workspace.Temp,
                    CodexHomeDirectory = isolation.CodexHomeDirectory,
                    CodexSqliteHomeDirectory = isolation.CodexSqliteHomeDirectory,
                    CodexAccessToken = isolation.CodexAccessToken,
                    StartupTimeout = TimeSpan.FromSeconds(5),
                    ShutdownTimeout = TimeSpan.FromSeconds(5),
                });
            var runtime = configuration.CreateClientOptions();
            var preflight = configuration.CreateVersionPreflightOptions();
            Equal(accessToken, runtime.Environment["CODEX_ACCESS_TOKEN"]);
            False(
                preflight.Environment.ContainsKey("CODEX_ACCESS_TOKEN"),
                "Version preflight received the fake access token.");
            True(
                !AgentHostAuditErrorCodes.FromException(
                    new AgentHostCodexSessionIsolationException(
                        AgentHostCodexSessionIsolationFailure.CredentialRejected,
                        "sanitized"))
                    .Contains(accessToken, StringComparison.Ordinal),
                "Credential failure code exposed the fake access token.");
            return Task.CompletedTask;
        }
        finally
        {
            fixture.Dispose();
        }
    }

    public static Task CodexSessionIsolationKeepsLegacyModeWhenUnconfigured()
    {
        var fixture = new StorageFixture();
        try
        {
            using var workspace = AgentWorkspace.CreateSession(fixture.SessionsRoot, Id('c'));
            var reader = new FakeCredentialReader("test-session-access-token");
            var isolation = AgentHostCodexSessionIsolation.Create(
                credentialTarget: null,
                workspace,
                reader);

            Equal<AgentHostCodexSessionIsolation?>(null, isolation);
            Equal<string?>(null, reader.LastRequestedTarget);
            False(
                Directory.Exists(Path.Combine(workspace.Root, "codex-home")),
                "Unconfigured compatibility mode created a Codex home directory.");
            False(
                Directory.Exists(Path.Combine(workspace.Root, "codex-sqlite")),
                "Unconfigured compatibility mode created a Codex SQLite directory.");
            return Task.CompletedTask;
        }
        finally
        {
            fixture.Dispose();
        }
    }

    public static Task CodexSessionIsolationRejectsInvalidCredentialReference()
    {
        var fixture = new StorageFixture();
        try
        {
            const string accessToken = "test-session-access-token";
            var invalidReferences = new[]
            {
                string.Empty,
                " ",
                "OtherProduct/not-allowed",
                "CodexForAutoCAD/",
                "CodexForAutoCAD/has/slash",
            };
            var sessionIdCharacters = new[] { '1', '2', '3', '4', '5' };
            for (var index = 0; index < invalidReferences.Length; index++)
            {
                var invalidReference = invalidReferences[index];
                using var workspace = AgentWorkspace.CreateSession(
                    fixture.SessionsRoot,
                    Id(sessionIdCharacters[index]));
                var reader = new FakeCredentialReader(accessToken);
                var exception = Capture<AgentHostCodexSessionIsolationException>(() =>
                    AgentHostCodexSessionIsolation.Create(invalidReference, workspace, reader));

                Equal(
                    AgentHostCodexSessionIsolationFailure.InvalidCredentialReference,
                    exception.Failure);
                Equal<string?>(null, reader.LastRequestedTarget);
                False(
                    Directory.Exists(Path.Combine(workspace.Root, "codex-home")),
                    "Invalid credential reference created a Codex home directory.");
                False(
                    Directory.Exists(Path.Combine(workspace.Root, "codex-sqlite")),
                    "Invalid credential reference created a Codex SQLite directory.");
                if (!string.IsNullOrWhiteSpace(invalidReference))
                {
                    True(
                        !exception.Message.Contains(invalidReference, StringComparison.Ordinal),
                        "Credential-reference failure exposed a private reference.");
                }

                True(
                    !exception.Message.Contains(accessToken, StringComparison.Ordinal),
                    "Credential-reference failure exposed a private token.");
            }
            return Task.CompletedTask;
        }
        finally
        {
            fixture.Dispose();
        }
    }

    public static Task CodexSessionIsolationSanitizesCredentialReaderFailure()
    {
        var fixture = new StorageFixture();
        try
        {
            const string credentialTarget = "CodexForAutoCAD/UnavailableAccessToken";
            const string rawFailureMarker = "credential-reader-private-marker";
            using var workspace = AgentWorkspace.CreateSession(fixture.SessionsRoot, Id('d'));
            var exception = Capture<AgentHostCodexSessionIsolationException>(() =>
                AgentHostCodexSessionIsolation.Create(
                    credentialTarget,
                    workspace,
                    new ThrowingCredentialReader(rawFailureMarker)));

            Equal(
                AgentHostCodexSessionIsolationFailure.CredentialUnavailable,
                exception.Failure);
            Equal(
                AgentHostAuditErrorCodes.CodexCredentialUnavailable,
                AgentHostAuditErrorCodes.FromException(exception));
            True(
                !exception.Message.Contains(credentialTarget, StringComparison.Ordinal)
                && !exception.Message.Contains(rawFailureMarker, StringComparison.Ordinal),
                "Credential-reader failure exposed private diagnostics.");
            False(
                Directory.Exists(Path.Combine(workspace.Root, "codex-home")),
                "Credential-reader failure created a Codex home directory.");
            False(
                Directory.Exists(Path.Combine(workspace.Root, "codex-sqlite")),
                "Credential-reader failure created a Codex SQLite directory.");
            return Task.CompletedTask;
        }
        finally
        {
            fixture.Dispose();
        }
    }

    public static Task AuditUsesPrivateAclAndBoundedRetention()
    {
        var fixture = new StorageFixture();
        AgentHostAuditLog? active = null;
        AgentHostAuditLog? second = null;
        AgentHostAuditLog? third = null;
        try
        {
            var now = new DateTime(2026, 7, 23, 8, 0, 0, DateTimeKind.Utc);
            var activeId = Id('6');
            active = AgentHostAuditLog.CreateForDirectory(
                fixture.AuditRoot,
                activeId,
                retentionAge: TimeSpan.Zero,
                maximumRetainedFiles: 4,
                utcNow: now);

            var staleId = Id('7');
            var stalePath = Path.Combine(fixture.AuditRoot, staleId + ".jsonl");
            File.WriteAllText(stalePath, "stale");
            AgentHostPrivateStorage.ApplyPrivateFileAcl(stalePath);
            File.SetLastWriteTimeUtc(stalePath, now - TimeSpan.FromDays(1));

            var secondId = Id('8');
            second = AgentHostAuditLog.CreateForDirectory(
                fixture.AuditRoot,
                secondId,
                retentionAge: TimeSpan.Zero,
                maximumRetainedFiles: 4,
                utcNow: now);

            True(
                File.Exists(Path.Combine(fixture.AuditRoot, activeId + ".jsonl")),
                "Retention deleted an active audit log.");
            False(File.Exists(stalePath), "Retention did not delete a stale audit log.");
            AssertPrivateDirectory(fixture.AuditRoot);
            AssertPrivateFile(Path.Combine(fixture.AuditRoot, secondId + ".jsonl"));

            active.Complete();
            active.Dispose();
            active = null;
            second.Complete();
            second.Dispose();
            second = null;

            third = AgentHostAuditLog.CreateForDirectory(
                fixture.AuditRoot,
                Id('9'),
                retentionAge: TimeSpan.FromDays(30),
                maximumRetainedFiles: 2,
                utcNow: now);
            Equal(2, Directory.EnumerateFiles(fixture.AuditRoot, "*.jsonl").Count());
            third.Complete();
            return Task.CompletedTask;
        }
        finally
        {
            third?.Dispose();
            second?.Dispose();
            active?.Dispose();
            fixture.Dispose();
        }
    }

    public static Task PrivateStorageRejectsReparseRoot()
    {
        var fixture = new StorageFixture();
        try
        {
            var target = Path.Combine(fixture.Root, "link-target");
            Directory.CreateDirectory(target);
            var link = Path.Combine(fixture.Root, "link-root");
            Directory.CreateSymbolicLink(link, target);

            Expect<AgentHostPrivateStorageException>(
                () => AgentHostPrivateStorage.PreparePrivateDirectory(link));
            False(Directory.Exists(Path.Combine(target, "inputs")),
                "Reparse-root rejection modified the link target.");
            return Task.CompletedTask;
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static void AssertPrivateDirectory(string path)
    {
        var security = FileSystemAclExtensions.GetAccessControl(
            new DirectoryInfo(path),
            AccessControlSections.Access | AccessControlSections.Owner);
        AssertPrivateSecurity(security, requireDirectoryInheritance: true);
    }

    private static void AssertPrivateFile(string path)
    {
        var security = FileSystemAclExtensions.GetAccessControl(
            new FileInfo(path),
            AccessControlSections.Access | AccessControlSections.Owner);
        AssertPrivateSecurity(security, requireDirectoryInheritance: false);
    }

    private static void AssertPrivateSecurity(
        FileSystemSecurity security,
        bool requireDirectoryInheritance)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var currentUser = identity.User ?? throw new InvalidOperationException("Current user SID missing.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            null);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            currentUser.Value,
            system.Value,
            administrators.Value,
        };
        var observed = new HashSet<string>(StringComparer.Ordinal);

        True(security.AreAccessRulesProtected, "Private ACL still inherits from its parent.");
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new InvalidOperationException("Private ACL owner SID is unavailable.");
        Equal(currentUser.Value, owner.Value);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: true,
                     targetType: typeof(SecurityIdentifier)))
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            False(rule.IsInherited, "Private ACL contains an inherited rule.");
            Equal(AccessControlType.Allow, rule.AccessControlType);
            True(expected.Contains(sid.Value), "Private ACL contains an unexpected principal.");
            True(
                (rule.FileSystemRights & FileSystemRights.FullControl)
                    == FileSystemRights.FullControl,
                "Private ACL does not grant the expected complete control boundary.");
            Equal(
                requireDirectoryInheritance
                    ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                    : InheritanceFlags.None,
                rule.InheritanceFlags);
            observed.Add(sid.Value);
        }

        True(observed.SetEquals(expected), "Private ACL principal set is incomplete.");
    }

    private static string Id(char value) => new(value, 32);

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Expected exception was not thrown: " + typeof(TException).Name + ".");
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

        throw new InvalidOperationException(
            "Expected exception was not thrown: " + typeof(TException).Name + ".");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void False(bool condition, string message)
        => True(!condition, message);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                "Expected " + expected + ", got " + actual + ".");
        }
    }

    private sealed class StorageFixture : IDisposable
    {
        internal StorageFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CodexAgentHostStorageSpecs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SessionsRoot = Path.Combine(Root, "sessions");
            AuditRoot = Path.Combine(Root, "audit");
        }

        internal string Root { get; }

        internal string SessionsRoot { get; }

        internal string AuditRoot { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            foreach (var child in Directory.EnumerateDirectories(Root).ToArray())
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(child, recursive: false);
                }
                else
                {
                    AgentHostPrivateStorage.DeletePrivateTree(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(Root))
            {
                File.Delete(file);
            }

            Directory.Delete(Root, recursive: false);
        }
    }

    private sealed class FakeCredentialReader : IAgentHostCredentialReader
    {
        private readonly string secret;

        internal FakeCredentialReader(string secret)
        {
            this.secret = secret;
        }

        internal string? LastRequestedTarget { get; private set; }

        public string ReadGenericSecret(string credentialTarget)
        {
            LastRequestedTarget = credentialTarget;
            return secret;
        }
    }

    private sealed class ThrowingCredentialReader : IAgentHostCredentialReader
    {
        private readonly string failureMarker;

        internal ThrowingCredentialReader(string failureMarker)
        {
            this.failureMarker = failureMarker;
        }

        public string ReadGenericSecret(string credentialTarget)
            => throw new FormatException(failureMarker + credentialTarget);
    }
}

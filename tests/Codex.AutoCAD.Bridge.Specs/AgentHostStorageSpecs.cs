using System.Security.AccessControl;
using System.Security.Principal;
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
}

namespace Codex.AutoCAD.AgentHost;

public sealed class AgentWorkspace : IDisposable
{
    public static readonly TimeSpan DefaultStaleSessionAge = TimeSpan.FromDays(1);
    public const int DefaultMaximumRetainedSessions = 64;
    internal const string LeaseFileName = ".codex-session.lock";

    private readonly object _sync = new();
    private readonly bool _deleteOnDispose;
    private FileStream? _lease;
    private bool _disposed;

    private AgentWorkspace(
        string root,
        FileStream? lease = null,
        bool deleteOnDispose = false)
    {
        Root = root;
        Inputs = Path.Combine(root, "inputs");
        Work = Path.Combine(root, "work");
        Outputs = Path.Combine(root, "outputs");
        Temp = Path.Combine(root, "temp");
        _lease = lease;
        _deleteOnDispose = deleteOnDispose;
    }

    public string Root { get; }

    public string Inputs { get; }

    public string Work { get; }

    public string Outputs { get; }

    public string Temp { get; }

    public static AgentWorkspace Create(string root)
    {
        var workspace = new AgentWorkspace(
            AgentHostPrivateStorage.PreparePrivateDirectory(root));
        workspace.PrepareChildren();
        return workspace;
    }

    internal static AgentWorkspace CreateSession(
        string sessionsRoot,
        string sessionId,
        TimeSpan? staleSessionAge = null,
        int maximumRetainedSessions = DefaultMaximumRetainedSessions,
        DateTime? utcNow = null)
    {
        if (!AgentHostPrivateStorage.IsLowerHexIdentifier(sessionId))
        {
            throw new ArgumentException("Agent workspace session id is invalid.", nameof(sessionId));
        }

        var maximumAge = staleSessionAge ?? DefaultStaleSessionAge;
        if (maximumAge < TimeSpan.Zero || maximumAge > TimeSpan.FromDays(30))
        {
            throw new ArgumentOutOfRangeException(nameof(staleSessionAge));
        }

        if (maximumRetainedSessions is < 2 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedSessions));
        }

        var safeSessionsRoot = AgentHostPrivateStorage.PreparePrivateDirectory(sessionsRoot);
        PruneSessions(
            safeSessionsRoot,
            maximumAge,
            maximumRetainedSessions,
            utcNow ?? DateTime.UtcNow);

        var root = Path.Combine(safeSessionsRoot, sessionId);
        if (Directory.Exists(root) || File.Exists(root))
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost session workspace already exists.");
        }

        FileStream? lease = null;
        try
        {
            var safeRoot = AgentHostPrivateStorage.PreparePrivateDirectory(root);
            var leasePath = Path.Combine(safeRoot, LeaseFileName);
            using (new FileStream(
                       leasePath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       1,
                       FileOptions.WriteThrough))
            {
            }
            AgentHostPrivateStorage.ApplyPrivateFileAcl(leasePath);
            lease = new FileStream(
                leasePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                1,
                FileOptions.WriteThrough);
            var workspace = new AgentWorkspace(
                safeRoot,
                lease,
                deleteOnDispose: true);
            lease = null;
            workspace.PrepareChildren();
            return workspace;
        }
        catch
        {
            lease?.Dispose();
            try
            {
                AgentHostPrivateStorage.DeletePrivateTree(root);
            }
            catch
            {
            }

            throw;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _lease?.Dispose();
            _lease = null;
            if (_deleteOnDispose)
            {
                AgentHostPrivateStorage.DeletePrivateTree(Root);
            }

            _disposed = true;
        }
    }

    private void PrepareChildren()
    {
        AgentHostPrivateStorage.PreparePrivateDirectory(Inputs);
        AgentHostPrivateStorage.PreparePrivateDirectory(Work);
        AgentHostPrivateStorage.PreparePrivateDirectory(Outputs);
        AgentHostPrivateStorage.PreparePrivateDirectory(Temp);
    }

    private static void PruneSessions(
        string sessionsRoot,
        TimeSpan staleSessionAge,
        int maximumRetainedSessions,
        DateTime utcNow)
    {
        var discovered = Directory.EnumerateDirectories(sessionsRoot)
            .Take(4097)
            .ToList();
        if (discovered.Count > 4096)
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost session workspace retention exceeded its scan limit.");
        }

        var sessions = discovered
            .Select(path => new SessionDirectory(path, Path.GetFileName(path)))
            .Where(session => AgentHostPrivateStorage.IsLowerHexIdentifier(session.Id))
            .OrderBy(session => session.LastWriteTimeUtc)
            .ToList();

        foreach (var session in sessions)
        {
            if (utcNow - session.LastWriteTimeUtc < staleSessionAge)
            {
                continue;
            }

            session.TryDeleteIfInactive();
        }

        sessions = sessions.Where(session => Directory.Exists(session.Path))
            .OrderBy(session => session.LastWriteTimeUtc)
            .ToList();
        var remainingCount = sessions.Count;
        foreach (var session in sessions)
        {
            if (remainingCount < maximumRetainedSessions)
            {
                break;
            }

            if (session.TryDeleteIfInactive())
            {
                remainingCount--;
            }
        }

        if (remainingCount >= maximumRetainedSessions)
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost session workspace retention is at capacity.");
        }
    }

    private sealed class SessionDirectory
    {
        internal SessionDirectory(string path, string id)
        {
            Path = path;
            Id = id;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new AgentHostPrivateStorageException(
                    "AgentHost session workspace cannot be a reparse point.");
            }

            LastWriteTimeUtc = Directory.GetLastWriteTimeUtc(path);
        }

        internal string Path { get; }

        internal string Id { get; }

        internal DateTime LastWriteTimeUtc { get; }

        internal bool TryDeleteIfInactive()
        {
            var leasePath = System.IO.Path.Combine(Path, LeaseFileName);
            FileStream? probe = null;
            try
            {
                if (File.Exists(leasePath))
                {
                    if ((File.GetAttributes(leasePath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new AgentHostPrivateStorageException(
                            "AgentHost session lease cannot be a reparse point.");
                    }

                    probe = new FileStream(
                        leasePath,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        1,
                        FileOptions.None);
                }
            }
            catch (IOException exception) when (
                AgentHostPrivateStorage.IsSharingViolation(exception))
            {
                return false;
            }
            finally
            {
                probe?.Dispose();
            }

            AgentHostPrivateStorage.DeletePrivateTree(Path);
            return true;
        }
    }
}

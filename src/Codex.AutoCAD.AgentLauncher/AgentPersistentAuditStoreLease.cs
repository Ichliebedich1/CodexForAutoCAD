using Microsoft.Win32.SafeHandles;

namespace Codex.AutoCAD.AgentLauncher;

/// <summary>
/// Pins the protected, current-user audit store used by AgentHost. Disposing this lease releases
/// directory identity locks but intentionally preserves every audit segment and anchor.
/// </summary>
public sealed class AgentPersistentAuditStoreLease : IDisposable
{
    private SafeFileHandle? _controlDirectoryLock;
    private SafeFileHandle? _anchorDirectoryLock;
    private SafeFileHandle? _segmentDirectoryLock;
    private SafeFileHandle? _rootDirectoryLock;
    private int _disposed;

    private AgentPersistentAuditStoreLease(
        string root,
        string segmentDirectory,
        string anchorDirectory,
        string controlDirectory,
        SafeFileHandle rootDirectoryLock,
        SafeFileHandle segmentDirectoryLock,
        SafeFileHandle anchorDirectoryLock,
        SafeFileHandle controlDirectoryLock)
    {
        Root = root;
        SegmentDirectory = segmentDirectory;
        AnchorDirectory = anchorDirectory;
        ControlDirectory = controlDirectory;
        _rootDirectoryLock = rootDirectoryLock;
        _segmentDirectoryLock = segmentDirectoryLock;
        _anchorDirectoryLock = anchorDirectoryLock;
        _controlDirectoryLock = controlDirectoryLock;
    }

    public string Root { get; }

    public string SegmentDirectory { get; }

    public string AnchorDirectory { get; }

    public string ControlDirectory { get; }

    public static AgentPersistentAuditStoreLease CreateForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw Failure("The current-user persistent audit root is unavailable.");
        }

        return Create(Path.Combine(
            localApplicationData,
            "OpenAI",
            "CodexForAutoCAD",
            "audit",
            "agenthost"));
    }

    internal static AgentPersistentAuditStoreLease Create(string auditRoot)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            throw Failure("The persistent audit store is supported only on Windows.");
        }

        SafeFileHandle? rootLock = null;
        SafeFileHandle? segmentLock = null;
        SafeFileHandle? anchorLock = null;
        SafeFileHandle? controlLock = null;
        try
        {
            var root = ValidateRoot(auditRoot);
            var currentUserSid = WindowsWorkspaceSecurity.GetCurrentUserSidString();
            EnsureProtectedDirectory(root, currentUserSid);
            rootLock = OpenAndValidateDirectory(root, preventReplacement: true);
            WindowsWorkspaceSecurity.VerifyProtectedDirectory(root, currentUserSid);

            var segmentDirectory = Path.Combine(root, "segments");
            EnsureProtectedDirectory(segmentDirectory, currentUserSid);
            segmentLock = OpenAndValidateDirectory(segmentDirectory, preventReplacement: false);
            WindowsWorkspaceSecurity.VerifyProtectedDirectory(segmentDirectory, currentUserSid);

            var anchorDirectory = Path.Combine(root, "anchors");
            EnsureProtectedDirectory(anchorDirectory, currentUserSid);
            anchorLock = OpenAndValidateDirectory(anchorDirectory, preventReplacement: false);
            WindowsWorkspaceSecurity.VerifyProtectedDirectory(anchorDirectory, currentUserSid);

            var controlDirectory = Path.Combine(root, "retention-control");
            EnsureProtectedDirectory(controlDirectory, currentUserSid);
            controlLock = OpenAndValidateDirectory(controlDirectory, preventReplacement: false);
            WindowsWorkspaceSecurity.VerifyProtectedDirectory(controlDirectory, currentUserSid);

            var result = new AgentPersistentAuditStoreLease(
                root,
                segmentDirectory,
                anchorDirectory,
                controlDirectory,
                rootLock,
                segmentLock,
                anchorLock,
                controlLock);
            rootLock = null;
            segmentLock = null;
            anchorLock = null;
            controlLock = null;
            return result;
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Failure("The persistent audit store could not be prepared safely.", exception);
        }
        finally
        {
            controlLock?.Dispose();
            anchorLock?.Dispose();
            segmentLock?.Dispose();
            rootLock?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _controlDirectoryLock, null)?.Dispose();
        Interlocked.Exchange(ref _anchorDirectoryLock, null)?.Dispose();
        Interlocked.Exchange(ref _segmentDirectoryLock, null)?.Dispose();
        Interlocked.Exchange(ref _rootDirectoryLock, null)?.Dispose();
    }

    private static string ValidateRoot(string auditRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(auditRoot)
                || !Path.IsPathRooted(auditRoot)
                || auditRoot.StartsWith(@"\\", StringComparison.Ordinal)
                || auditRoot.StartsWith(@"\\?\", StringComparison.Ordinal)
                || auditRoot.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                throw Failure("The persistent audit root is invalid.");
            }

            var fullPath = TrimTrailingSeparators(Path.GetFullPath(auditRoot));
            var volumeRoot = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(volumeRoot)
                || new DriveInfo(volumeRoot).DriveType != DriveType.Fixed)
            {
                throw Failure("The persistent audit root must use a fixed local drive.");
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw Failure("The persistent audit root is invalid.");
            }

            Directory.CreateDirectory(parent);
            if (ContainsReparsePoint(parent))
            {
                throw Failure("The persistent audit root cannot traverse a reparse point.");
            }

            return fullPath;
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Failure("The persistent audit root is invalid.", exception);
        }
    }

    private static void EnsureProtectedDirectory(string path, string currentUserSid)
    {
        if (!Directory.Exists(path))
        {
            try
            {
                WindowsWorkspaceSecurity.CreateProtectedDirectory(path, currentUserSid);
            }
            catch (AgentBootstrapLaunchException) when (Directory.Exists(path))
            {
                // A concurrent same-user process may have created the directory. Exact ACL and
                // identity verification below decide whether it is safe to use.
            }
        }

        WindowsWorkspaceSecurity.VerifyProtectedDirectory(path, currentUserSid);
    }

    private static SafeFileHandle OpenAndValidateDirectory(
        string path,
        bool preventReplacement)
    {
        var handle = preventReplacement
            ? WindowsWorkspaceSecurity.OpenDirectoryWithoutDeleteSharing(path)
            : WindowsWorkspaceSecurity.OpenDirectoryForIdentity(path);
        try
        {
            if (WindowsWorkspaceSecurity.IsReparsePoint(handle))
            {
                throw Failure("The persistent audit store contains a reparse point.");
            }

            var expected = TrimTrailingSeparators(Path.GetFullPath(path));
            var observed = TrimTrailingSeparators(WindowsWorkspaceSecurity.GetFinalPath(handle));
            if (!string.Equals(expected, observed, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("The persistent audit directory identity changed during validation.");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static bool ContainsReparsePoint(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }

        var current = root;
        foreach (var segment in fullPath.Substring(root.Length).Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string TrimTrailingSeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsFileSystemException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;

    private static AgentBootstrapLaunchException Failure(
        string message,
        Exception? innerException = null)
        => new(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            message,
            innerException);
}

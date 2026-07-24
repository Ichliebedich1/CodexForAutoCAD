using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Codex.AutoCAD.Ipc;
using Microsoft.Win32.SafeHandles;

namespace Codex.AutoCAD.AgentLauncher;

/// <summary>
/// Owns the on-disk workspace used by one authenticated AgentHost session. Directory handles are
/// kept open without FILE_SHARE_DELETE so the path cannot be renamed or replaced while the
/// session is active.
/// </summary>
internal sealed class AgentSessionWorkspaceLease : IDisposable
{
    private const string MarkerSchema = "codex.autocad.session-workspace/1";
    private const int MaximumExpiredCleanupCandidates = 64;
    internal static readonly TimeSpan DefaultExpiredLeaseAge = TimeSpan.FromHours(24);
    private readonly object sync = new object();
    private FileStream? activeLease;
    private SafeFileHandle? codexHomeDirectoryLock;
    private SafeFileHandle? auditDirectoryLock;
    private SafeFileHandle? workspaceDirectoryLock;
    private SafeFileHandle? sessionDirectoryLock;
    private SafeFileHandle? sessionsRootIdentity;
    private FileStream? sessionsRootLease;
    private bool disposed;

    private AgentSessionWorkspaceLease(
        string sessionId,
        string sessionsRoot,
        string sessionPath,
        string currentUserSid,
        FileStream activeLease,
        SafeFileHandle codexHomeDirectoryLock,
        SafeFileHandle auditDirectoryLock,
        SafeFileHandle workspaceDirectoryLock,
        SafeFileHandle sessionDirectoryLock,
        SafeFileHandle sessionsRootIdentity,
        FileStream sessionsRootLease)
    {
        SessionId = sessionId;
        SessionsRoot = sessionsRoot;
        SessionPath = sessionPath;
        CurrentUserSid = currentUserSid;
        WorkspacePath = Path.Combine(sessionPath, "workspace");
        AuditPath = Path.Combine(sessionPath, "audit");
        CodexHomePath = Path.Combine(sessionPath, "codex-home");
        this.activeLease = activeLease;
        this.codexHomeDirectoryLock = codexHomeDirectoryLock;
        this.auditDirectoryLock = auditDirectoryLock;
        this.workspaceDirectoryLock = workspaceDirectoryLock;
        this.sessionDirectoryLock = sessionDirectoryLock;
        this.sessionsRootIdentity = sessionsRootIdentity;
        this.sessionsRootLease = sessionsRootLease;
    }

    internal string SessionId { get; }

    internal string SessionsRoot { get; }

    internal string SessionPath { get; }

    internal string WorkspacePath { get; }

    internal string AuditPath { get; }

    internal string CodexHomePath { get; }

    internal string CurrentUserSid { get; }

    internal static AgentSessionWorkspaceLease CreateForCurrentUser(string sessionId)
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw IsolationFailure(
                "The current-user session workspace root is unavailable.");
        }

        var sessionsRoot = Path.Combine(
            localApplicationData,
            "OpenAI",
            "CodexForAutoCAD",
            "workspace",
            "sessions");
        try
        {
            Directory.CreateDirectory(sessionsRoot);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw IsolationFailure(
                "The current-user session workspace root could not be prepared.",
                exception);
        }

        CleanupExpired(
            sessionsRoot,
            DateTime.UtcNow,
            DefaultExpiredLeaseAge);
        return Create(sessionsRoot, sessionId);
    }

    internal static int CleanupExpired(
        string sessionsRoot,
        DateTime utcNow,
        TimeSpan minimumAge)
    {
        if (utcNow.Kind != DateTimeKind.Utc
            || minimumAge < TimeSpan.Zero
            || minimumAge > TimeSpan.FromDays(30))
        {
            throw IsolationFailure("The expired workspace cleanup policy is invalid.");
        }

        var validatedRoot = ValidateExistingRoot(sessionsRoot);
        var currentUserSid = WindowsWorkspaceSecurity.GetCurrentUserSidString();
        var cutoff = utcNow - minimumAge;
        var cleaned = 0;
        using (OpenAndValidateDirectoryForIdentity(validatedRoot))
        using (OpenSessionsRootLease(validatedRoot))
        {
            string[] candidates;
            try
            {
                candidates = Directory.GetDirectories(validatedRoot)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumExpiredCleanupCandidates)
                    .ToArray();
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                throw IsolationFailure(
                    "Expired session workspaces could not be enumerated safely.",
                    exception);
            }

            foreach (var candidate in candidates)
            {
                if (TryCleanupExpiredCandidate(
                        validatedRoot,
                        candidate,
                        currentUserSid,
                        cutoff))
                {
                    cleaned++;
                }
            }
        }

        return cleaned;
    }

    internal static AgentSessionWorkspaceLease Create(
        string sessionsRoot,
        string sessionId)
    {
        ValidateSessionId(sessionId);
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            throw IsolationFailure(
                "Protected session workspaces are supported only on Windows.");
        }

        var validatedRoot = ValidateExistingRoot(sessionsRoot);
        var sessionPath = Path.Combine(validatedRoot, sessionId);
        SafeFileHandle? rootIdentity = null;
        FileStream? rootLease = null;
        SafeFileHandle? sessionLock = null;
        SafeFileHandle? workspaceLock = null;
        SafeFileHandle? auditLock = null;
        SafeFileHandle? codexHomeLock = null;
        FileStream? leaseStream = null;
        var sessionDirectoryCreated = false;
        try
        {
            rootIdentity = OpenAndValidateDirectoryForIdentity(validatedRoot);
            rootLease = OpenSessionsRootLease(validatedRoot);
            var userSid = WindowsWorkspaceSecurity.GetCurrentUserSidString();
            WindowsWorkspaceSecurity.CreateProtectedDirectory(sessionPath, userSid);
            sessionDirectoryCreated = true;
            sessionLock = OpenAndValidateDirectory(sessionPath);
            WindowsWorkspaceSecurity.VerifyProtectedDirectory(sessionPath, userSid);

            var result = new AgentSessionWorkspaceLease(
                sessionId,
                validatedRoot,
                sessionPath,
                userSid,
                null!,
                null!,
                null!,
                null!,
                sessionLock,
                rootIdentity,
                rootLease);
            Directory.CreateDirectory(result.WorkspacePath);
            Directory.CreateDirectory(result.AuditPath);
            Directory.CreateDirectory(result.CodexHomePath);
            workspaceLock = OpenAndValidateDirectory(result.WorkspacePath);
            auditLock = OpenAndValidateDirectory(result.AuditPath);
            codexHomeLock = OpenAndValidateDirectory(result.CodexHomePath);
            WriteMarker(sessionPath, sessionId);
            leaseStream = new FileStream(
                Path.Combine(sessionPath, ".active"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.WriteThrough);
            leaseStream.WriteByte(1);
            leaseStream.Flush(flushToDisk: true);

            result.activeLease = leaseStream;
            result.codexHomeDirectoryLock = codexHomeLock;
            result.auditDirectoryLock = auditLock;
            result.workspaceDirectoryLock = workspaceLock;
            leaseStream = null;
            codexHomeLock = null;
            auditLock = null;
            workspaceLock = null;
            sessionLock = null;
            rootIdentity = null;
            rootLease = null;
            return result;
        }
        catch (Exception exception)
        {
            leaseStream?.Dispose();
            codexHomeLock?.Dispose();
            auditLock?.Dispose();
            workspaceLock?.Dispose();
            sessionLock?.Dispose();
            Exception? cleanupFailure = null;
            if (sessionDirectoryCreated)
            {
                try
                {
                    DeleteTreeWithoutFollowingReparsePoints(sessionPath);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailure = cleanupException;
                }
            }

            rootLease?.Dispose();
            rootIdentity?.Dispose();
            if (cleanupFailure != null)
            {
                throw IsolationFailure(
                    "The failed session workspace could not be cleaned safely.",
                    new AggregateException(exception, cleanupFailure));
            }

            if (exception is AgentBootstrapLaunchException)
            {
                throw;
            }

            throw IsolationFailure(
                "The protected session workspace could not be initialized.",
                exception);
        }
    }

    internal static void VerifyProtectedDirectory(string path, string expectedOwnerSid)
    {
        WindowsWorkspaceSecurity.VerifyProtectedDirectory(path, expectedOwnerSid);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            activeLease?.Dispose();
            activeLease = null;
            codexHomeDirectoryLock?.Dispose();
            codexHomeDirectoryLock = null;
            auditDirectoryLock?.Dispose();
            auditDirectoryLock = null;
            workspaceDirectoryLock?.Dispose();
            workspaceDirectoryLock = null;
            sessionDirectoryLock?.Dispose();
            sessionDirectoryLock = null;
            try
            {
                DeleteTreeWithoutFollowingReparsePoints(SessionPath);
                disposed = true;
                sessionsRootLease?.Dispose();
                sessionsRootLease = null;
                sessionsRootIdentity?.Dispose();
                sessionsRootIdentity = null;
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                Exception? relockFailure = null;
                try
                {
                    ReacquireCleanupLocks();
                }
                catch (Exception relockException)
                {
                    relockFailure = relockException;
                }

                throw IsolationFailure(
                    "The session workspace could not be cleaned safely.",
                    relockFailure == null
                        ? exception
                        : new AggregateException(exception, relockFailure));
            }
        }
    }

    private void ReacquireCleanupLocks()
    {
        if (!Directory.Exists(SessionPath))
        {
            return;
        }

        sessionDirectoryLock = OpenAndValidateDirectory(SessionPath);
        WindowsWorkspaceSecurity.VerifyProtectedDirectory(SessionPath, CurrentUserSid);
        try
        {
            workspaceDirectoryLock = OpenExistingDirectoryIfPresent(WorkspacePath);
            auditDirectoryLock = OpenExistingDirectoryIfPresent(AuditPath);
            codexHomeDirectoryLock = OpenExistingDirectoryIfPresent(CodexHomePath);
        }
        catch
        {
            codexHomeDirectoryLock?.Dispose();
            codexHomeDirectoryLock = null;
            auditDirectoryLock?.Dispose();
            auditDirectoryLock = null;
            workspaceDirectoryLock?.Dispose();
            workspaceDirectoryLock = null;
            sessionDirectoryLock.Dispose();
            sessionDirectoryLock = null;
            throw;
        }
    }

    private static SafeFileHandle? OpenExistingDirectoryIfPresent(string path)
        => Directory.Exists(path) ? OpenAndValidateDirectory(path) : null;

    private static bool TryCleanupExpiredCandidate(
        string sessionsRoot,
        string candidatePath,
        string currentUserSid,
        DateTime cutoffUtc)
    {
        var sessionId = Path.GetFileName(candidatePath);
        if (!IsValidSessionId(sessionId)
            || !string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(candidatePath)),
                sessionsRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var markerPath = Path.Combine(candidatePath, ".codex-autocad-session");
        var activePath = Path.Combine(candidatePath, ".active");
        if (!IsRegularFile(markerPath) || !IsRegularFile(activePath))
        {
            return false;
        }

        var expectedMarker = MarkerSchema + "\r\n" + sessionId + "\r\n";
        string marker;
        try
        {
            var markerInfo = new FileInfo(markerPath);
            if (markerInfo.Length <= 0 || markerInfo.Length > 128)
            {
                return false;
            }

            marker = File.ReadAllText(markerPath, Encoding.UTF8);
            if (!string.Equals(marker, expectedMarker, StringComparison.Ordinal)
                || File.GetLastWriteTimeUtc(activePath) > cutoffUtc)
            {
                return false;
            }
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return false;
        }

        FileStream? inactiveProof = null;
        try
        {
            inactiveProof = new FileStream(
                activePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            return false;
        }

        SafeFileHandle? sessionLock = null;
        try
        {
            sessionLock = OpenAndValidateDirectory(candidatePath);
            WindowsWorkspaceSecurity.VerifyProtectedDirectory(candidatePath, currentUserSid);
            if (!string.Equals(
                    File.ReadAllText(markerPath, Encoding.UTF8),
                    expectedMarker,
                    StringComparison.Ordinal)
                || File.GetLastWriteTimeUtc(activePath) > cutoffUtc)
            {
                return false;
            }

            sessionLock.Dispose();
            sessionLock = null;
            inactiveProof.Dispose();
            inactiveProof = null;
            DeleteTreeWithoutFollowingReparsePoints(candidatePath);
            return true;
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw IsolationFailure(
                "An expired session workspace could not be cleaned safely.",
                exception);
        }
        finally
        {
            sessionLock?.Dispose();
            inactiveProof?.Dispose();
        }
    }

    private static bool IsRegularFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                           or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string ValidateExistingRoot(string sessionsRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionsRoot)
                || !IsAbsoluteLocalWindowsPath(sessionsRoot)
                || sessionsRoot.StartsWith("\\\\", StringComparison.Ordinal)
                || sessionsRoot.StartsWith("\\\\?\\", StringComparison.Ordinal)
                || sessionsRoot.StartsWith("\\\\.\\", StringComparison.Ordinal))
            {
                throw IsolationFailure("The session workspace root is invalid.");
            }

            var fullPath = Path.GetFullPath(sessionsRoot);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root)
                || !Directory.Exists(fullPath)
                || new DriveInfo(root).DriveType != DriveType.Fixed
                || ContainsReparsePoint(fullPath))
            {
                throw IsolationFailure("The session workspace root is invalid.");
            }

            return TrimTrailingSeparators(fullPath);
        }
        catch (AgentBootstrapLaunchException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw IsolationFailure("The session workspace root is invalid.", exception);
        }
    }

    private static SafeFileHandle OpenAndValidateDirectory(string path)
    {
        var handle = WindowsWorkspaceSecurity.OpenDirectoryWithoutDeleteSharing(path);
        try
        {
            if (WindowsWorkspaceSecurity.IsReparsePoint(handle))
            {
                throw IsolationFailure("The session workspace path contains a reparse point.");
            }

            var finalPath = WindowsWorkspaceSecurity.GetFinalPath(handle);
            if (!string.Equals(
                    TrimTrailingSeparators(finalPath),
                    TrimTrailingSeparators(Path.GetFullPath(path)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw IsolationFailure(
                    "The session workspace directory identity changed during validation.");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenAndValidateDirectoryForIdentity(string path)
    {
        var handle = WindowsWorkspaceSecurity.OpenDirectoryForIdentity(path);
        try
        {
            if (WindowsWorkspaceSecurity.IsReparsePoint(handle)
                || !string.Equals(
                    TrimTrailingSeparators(WindowsWorkspaceSecurity.GetFinalPath(handle)),
                    TrimTrailingSeparators(Path.GetFullPath(path)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw IsolationFailure(
                    "The session workspace root identity changed during validation.");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static FileStream OpenSessionsRootLease(string sessionsRoot)
    {
        var path = Path.Combine(sessionsRoot, ".codex-autocad-root-lock");
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                bufferSize: 1,
                FileOptions.None);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || !string.Equals(
                    WindowsWorkspaceSecurity.GetFinalPath(stream.SafeFileHandle),
                    Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw IsolationFailure("The session workspace root lease is invalid.");
            }

            return stream;
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    private static void WriteMarker(string sessionPath, string sessionId)
    {
        var marker = MarkerSchema + "\r\n" + sessionId + "\r\n";
        using var stream = new FileStream(
            Path.Combine(sessionPath, ".codex-autocad-session"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        var bytes = new UTF8Encoding(false).GetBytes(marker);
        try
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static void ValidateSessionId(string sessionId)
    {
        if (!IsValidSessionId(sessionId))
        {
            throw IsolationFailure("The session workspace identity is invalid.");
        }
    }

    private static bool IsValidSessionId(string? sessionId)
    {
        if (sessionId == null || sessionId.Length != AgentBootstrapProtocol.SessionIdBytes)
        {
            return false;
        }

        foreach (var character in sessionId)
        {
            if (!((character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
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

    private static void DeleteTreeWithoutFollowingReparsePoints(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                           or DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.Delete(path, recursive: false);
            }
            else
            {
                File.Delete(path);
            }

            return;
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.Delete(path);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var entryAttributes = File.GetAttributes(entry);
            if ((entryAttributes & FileAttributes.Directory) != 0
                && (entryAttributes & FileAttributes.ReparsePoint) == 0)
            {
                DeleteTreeWithoutFollowingReparsePoints(entry);
            }
            else if ((entryAttributes & FileAttributes.Directory) != 0)
            {
                Directory.Delete(entry, recursive: false);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(path, recursive: false);
    }

    private static string TrimTrailingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsAbsoluteLocalWindowsPath(string path)
    {
        return path.Length >= 3
            && ((path[0] >= 'A' && path[0] <= 'Z')
                || (path[0] >= 'a' && path[0] <= 'z'))
            && path[1] == ':'
            && (path[2] == Path.DirectorySeparatorChar
                || path[2] == Path.AltDirectorySeparatorChar);
    }

    private static bool IsFileSystemException(Exception exception)
        => exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or System.Security.SecurityException;

    private static AgentBootstrapLaunchException IsolationFailure(
        string message,
        Exception? innerException = null)
        => new(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            message,
            innerException);
}

internal static class WindowsWorkspaceSecurity
{
    private const string LocalSystemSid = "S-1-5-18";
    private const string BuiltinAdministratorsSid = "S-1-5-32-544";
    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;
    private const uint SecurityDescriptorRevision = 1;
    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint DaclSecurityInformation = 0x00000004;
    private const ushort SeDaclProtected = 0x1000;
    private const byte AccessAllowedAceType = 0x00;
    private const byte ObjectInheritAce = 0x01;
    private const byte ContainerInheritAce = 0x02;
    private const byte InheritedAce = 0x10;
    private const uint FileAllAccess = 0x001F01FF;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint DeleteAccess = 0x00010000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint OpenExisting = 3;
    private const uint ErrorAlreadyExists = 183;
    private const uint SeFileObject = 1;

    internal static string GetCurrentUserSidString()
    {
        IntPtr token = IntPtr.Zero;
        IntPtr tokenInformation = IntPtr.Zero;
        try
        {
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), TokenQuery, out token))
            {
                throw Failure("Opening the current process token failed.");
            }

            Native.GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var requiredBytes);
            if (requiredBytes <= 0)
            {
                throw Failure("Reading the current process identity failed.");
            }

            tokenInformation = Marshal.AllocHGlobal(requiredBytes);
            if (!Native.GetTokenInformation(
                    token,
                    TokenUser,
                    tokenInformation,
                    requiredBytes,
                    out _))
            {
                throw Failure("Reading the current process identity failed.");
            }

            var sid = Marshal.ReadIntPtr(tokenInformation);
            return SidToString(sid);
        }
        finally
        {
            if (tokenInformation != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(tokenInformation);
            }

            if (token != IntPtr.Zero)
            {
                Native.CloseHandle(token);
            }
        }
    }

    internal static void CreateProtectedDirectory(string path, string userSid)
    {
        var sddl = "O:" + userSid
            + "G:" + userSid
            + "D:P"
            + "(A;OICI;FA;;;SY)"
            + "(A;OICI;FA;;;BA)"
            + "(A;OICI;FA;;;" + userSid + ")";
        if (!Native.ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                SecurityDescriptorRevision,
                out var securityDescriptor,
                out _))
        {
            throw Failure("Creating the session workspace security descriptor failed.");
        }

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf(typeof(SecurityAttributes)),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = 0,
            };
            if (!Native.CreateDirectory(path, ref attributes))
            {
                var error = Marshal.GetLastWin32Error();
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                    error == ErrorAlreadyExists
                        ? "The session workspace already exists."
                        : "Creating the protected session workspace failed.",
                    new Win32Exception(error));
            }
        }
        finally
        {
            Native.LocalFree(securityDescriptor);
        }
    }

    internal static void VerifyProtectedDirectory(string path, string expectedOwnerSid)
    {
        var error = Native.GetNamedSecurityInfo(
            path,
            SeFileObject,
            OwnerSecurityInformation | DaclSecurityInformation,
            out var ownerSid,
            out _,
            out var dacl,
            out _,
            out var securityDescriptor);
        if (error != 0)
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                "Reading the session workspace security descriptor failed.",
                new Win32Exception(checked((int)error)));
        }

        try
        {
            if (!string.Equals(
                    SidToString(ownerSid),
                    expectedOwnerSid,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("The session workspace owner is invalid.");
            }

            if (!Native.GetSecurityDescriptorControl(
                    securityDescriptor,
                    out var control,
                    out _)
                || (control & SeDaclProtected) == 0
                || dacl == IntPtr.Zero)
            {
                throw Failure("The session workspace DACL is not protected.");
            }

            var expectedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                expectedOwnerSid,
                LocalSystemSid,
                BuiltinAdministratorsSid,
            };
            var observedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aceCount = checked((ushort)Marshal.ReadInt16(dacl, 4));
            if (aceCount != expectedSids.Count)
            {
                throw Failure("The session workspace DACL contains unexpected entries.");
            }

            for (uint index = 0; index < aceCount; index++)
            {
                if (!Native.GetAce(dacl, index, out var ace)
                    || Marshal.ReadByte(ace, 0) != AccessAllowedAceType)
                {
                    throw Failure("The session workspace DACL contains an invalid ACE.");
                }

                var flags = Marshal.ReadByte(ace, 1);
                if ((flags & (ObjectInheritAce | ContainerInheritAce))
                        != (ObjectInheritAce | ContainerInheritAce)
                    || (flags & InheritedAce) != 0)
                {
                    throw Failure("The session workspace ACE inheritance is invalid.");
                }

                var mask = checked((uint)Marshal.ReadInt32(ace, 4));
                if ((mask & FileAllAccess) != FileAllAccess)
                {
                    throw Failure("The session workspace ACE permissions are incomplete.");
                }

                var sid = SidToString(IntPtr.Add(ace, 8));
                if (!expectedSids.Contains(sid) || !observedSids.Add(sid))
                {
                    throw Failure("The session workspace DACL contains an unexpected identity.");
                }
            }

            if (!observedSids.SetEquals(expectedSids))
            {
                throw Failure("The session workspace DACL is incomplete.");
            }
        }
        finally
        {
            if (securityDescriptor != IntPtr.Zero)
            {
                Native.LocalFree(securityDescriptor);
            }
        }
    }

    internal static SafeFileHandle OpenDirectoryWithoutDeleteSharing(string path)
    {
        var handle = Native.CreateFile(
            path,
            DeleteAccess,
            FileShare.Read | FileShare.Write,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            "Opening the session workspace directory failed.",
            new Win32Exception(error));
    }

    internal static SafeFileHandle OpenDirectoryForIdentity(string path)
    {
        var handle = Native.CreateFile(
            path,
            0,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            "Opening the session workspace root identity failed.",
            new Win32Exception(error));
    }

    internal static bool IsReparsePoint(SafeFileHandle handle)
    {
        if (!Native.GetFileInformationByHandle(handle, out var information))
        {
            throw Failure("Reading the session workspace directory identity failed.");
        }

        return (information.FileAttributes & FileAttributeReparsePoint) != 0;
    }

    internal static string GetFinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (capacity <= 32768)
        {
            var builder = new StringBuilder(capacity);
            var length = Native.GetFinalPathNameByHandle(
                handle,
                builder,
                checked((uint)builder.Capacity),
                0);
            if (length == 0)
            {
                throw Failure("Resolving the session workspace directory failed.");
            }

            if (length < builder.Capacity)
            {
                var path = builder.ToString();
                if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                {
                    return "\\\\" + path.Substring(8);
                }

                return path.StartsWith("\\\\?\\", StringComparison.Ordinal)
                    ? path.Substring(4)
                    : path;
            }

            capacity = checked((int)length + 1);
        }

        throw Failure("The session workspace directory path is too long.");
    }

    private static string SidToString(IntPtr sid)
    {
        if (sid == IntPtr.Zero || !Native.ConvertSidToStringSid(sid, out var sidText))
        {
            throw Failure("Converting a session workspace identity failed.");
        }

        try
        {
            return Marshal.PtrToStringUni(sidText)
                ?? throw Failure("Converting a session workspace identity failed.");
        }
        finally
        {
            Native.LocalFree(sidText);
        }
    }

    private static AgentBootstrapLaunchException Failure(string message)
        => new(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            message,
            new Win32Exception(Marshal.GetLastWin32Error()));

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        internal int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal FileTime CreationTime;
        internal FileTime LastAccessTime;
        internal FileTime LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    private static class Native
    {
        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out IntPtr securityDescriptor,
            out uint securityDescriptorSize);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint GetNamedSecurityInfo(
            string objectName,
            uint objectType,
            uint securityInfo,
            out IntPtr owner,
            out IntPtr group,
            out IntPtr dacl,
            out IntPtr sacl,
            out IntPtr securityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSecurityDescriptorControl(
            IntPtr securityDescriptor,
            out ushort control,
            out uint revision);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetAce(
            IntPtr acl,
            uint aceIndex,
            out IntPtr ace);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertSidToStringSid(
            IntPtr sid,
            out IntPtr stringSid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDirectory(
            string pathName,
            ref SecurityAttributes securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation fileInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathLength,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}

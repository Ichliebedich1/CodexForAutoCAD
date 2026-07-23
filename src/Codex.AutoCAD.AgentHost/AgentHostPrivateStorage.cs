using System.Security.AccessControl;
using System.Security.Principal;

namespace Codex.AutoCAD.AgentHost;

internal sealed class AgentHostPrivateStorageException : Exception
{
    internal AgentHostPrivateStorageException(string message)
        : base(message)
    {
    }

    internal AgentHostPrivateStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class AgentHostPrivateStorage
{
    internal const int MaximumDeleteEntries = 50_000;

    private static readonly SecurityIdentifier LocalSystemSid =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier BuiltinAdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    internal static string PreparePrivateDirectory(string directory)
    {
        var fullPath = NormalizeLocalFixedPath(directory);
        try
        {
            EnsureNoReparsePoints(fullPath, requireLeaf: false);
            Directory.CreateDirectory(fullPath);
            EnsureNoReparsePoints(fullPath, requireLeaf: true);
            ApplyPrivateDirectoryAcl(fullPath);
            EnsureNoReparsePoints(fullPath, requireLeaf: true);
            VerifyPrivateDirectoryAcl(fullPath);
            return fullPath;
        }
        catch (AgentHostPrivateStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private directory could not be prepared safely.",
                exception);
        }
    }

    internal static void ApplyPrivateFileAcl(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        try
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) != 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new AgentHostPrivateStorageException(
                    "AgentHost private file cannot be a directory or reparse point.");
            }

            var currentUserSid = GetCurrentUserSid();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(currentUserSid);
            AddFileRule(security, currentUserSid);
            AddFileRule(security, LocalSystemSid);
            AddFileRule(security, BuiltinAdministratorsSid);
            FileSystemAclExtensions.SetAccessControl(new FileInfo(fullPath), security);
            VerifyPrivateFileAcl(fullPath, currentUserSid);
        }
        catch (AgentHostPrivateStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private file permissions could not be applied safely.",
                exception);
        }
    }

    internal static void DeletePrivateTree(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        try
        {
            EnsureNoReparsePoints(fullPath, requireLeaf: true);
            var visitedEntries = 0;
            DeleteDirectoryNoFollow(fullPath, ref visitedEntries);
        }
        catch (AgentHostPrivateStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private directory cleanup could not complete safely.",
                exception);
        }
    }

    internal static bool IsSharingViolation(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var nativeError = exception.HResult & 0xffff;
        return nativeError is 32 or 33;
    }

    internal static bool IsLowerHexIdentifier(string value)
    {
        if (value.Length != 32)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeLocalFixedPath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "AgentHost private storage requires Windows access control.");
        }

        if (!Path.IsPathFullyQualified(directory)
            || directory.StartsWith("\\\\", StringComparison.Ordinal)
            || directory.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || directory.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private storage must use a local absolute path.");
        }

        var fullPath = Path.GetFullPath(directory);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)
            || new DriveInfo(root).DriveType != DriveType.Fixed)
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private storage must use a fixed local drive.");
        }

        var relative = Path.GetRelativePath(root, fullPath);
        if (relative.Contains(':', StringComparison.Ordinal))
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private storage cannot use an alternate data stream path.");
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static void EnsureNoReparsePoints(string fullPath, bool requireLeaf)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private storage has no local drive root.");
        }

        var leafSeen = false;
        for (var current = new DirectoryInfo(fullPath);
             current is not null;
             current = current.Parent)
        {
            if (current.Exists)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AgentHostPrivateStorageException(
                        "AgentHost private storage cannot traverse a reparse point.");
                }

                if (!leafSeen)
                {
                    leafSeen = true;
                }
            }
            else if (requireLeaf && !leafSeen)
            {
                throw new AgentHostPrivateStorageException(
                    "AgentHost private directory was not created.");
            }

            if (string.Equals(current.FullName, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
    }

    private static void ApplyPrivateDirectoryAcl(string fullPath)
    {
        var currentUserSid = GetCurrentUserSid();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUserSid);
        AddDirectoryRule(security, currentUserSid);
        AddDirectoryRule(security, LocalSystemSid);
        AddDirectoryRule(security, BuiltinAdministratorsSid);
        FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(fullPath), security);
    }

    private static void VerifyPrivateDirectoryAcl(string fullPath)
    {
        var currentUserSid = GetCurrentUserSid();
        var security = FileSystemAclExtensions.GetAccessControl(
            new DirectoryInfo(fullPath),
            AccessControlSections.Access | AccessControlSections.Owner);
        VerifyOwnerAndRules(
            security,
            currentUserSid,
            requireDirectoryInheritance: true);
    }

    private static void VerifyPrivateFileAcl(
        string fullPath,
        SecurityIdentifier currentUserSid)
    {
        var security = FileSystemAclExtensions.GetAccessControl(
            new FileInfo(fullPath),
            AccessControlSections.Access | AccessControlSections.Owner);
        VerifyOwnerAndRules(
            security,
            currentUserSid,
            requireDirectoryInheritance: false);
    }

    private static void VerifyOwnerAndRules(
        FileSystemSecurity security,
        SecurityIdentifier currentUserSid,
        bool requireDirectoryInheritance)
    {
        if (!security.AreAccessRulesProtected
            || !currentUserSid.Equals(security.GetOwner(typeof(SecurityIdentifier))))
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private storage permissions were not protected correctly.");
        }

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            currentUserSid.Value,
            LocalSystemSid.Value,
            BuiltinAdministratorsSid.Value,
        };
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: true,
                     targetType: typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference is not SecurityIdentifier sid
                || rule.IsInherited
                || rule.AccessControlType != AccessControlType.Allow
                || !expected.Contains(sid.Value)
                || (rule.FileSystemRights & FileSystemRights.FullControl)
                    != FileSystemRights.FullControl)
            {
                throw new AgentHostPrivateStorageException(
                    "AgentHost private storage contains an unexpected access rule.");
            }

            if (requireDirectoryInheritance
                && rule.InheritanceFlags
                    != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit))
            {
                throw new AgentHostPrivateStorageException(
                    "AgentHost private directory inheritance is incomplete.");
            }

            if (!requireDirectoryInheritance
                && rule.InheritanceFlags != InheritanceFlags.None)
            {
                throw new AgentHostPrivateStorageException(
                    "AgentHost private file contains an inheritable access rule.");
            }

            observed.Add(sid.Value);
        }

        if (!observed.SetEquals(expected))
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private storage permissions are incomplete.");
        }
    }

    private static SecurityIdentifier GetCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User
            ?? throw new AgentHostPrivateStorageException(
                "The current Windows user identity is unavailable.");
    }

    private static void AddDirectoryRule(
        DirectorySecurity security,
        SecurityIdentifier sid)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    private static void AddFileRule(FileSecurity security, SecurityIdentifier sid)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
    }

    private static void DeleteDirectoryNoFollow(string directory, ref int visitedEntries)
    {
        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new AgentHostPrivateStorageException(
                "AgentHost private directory cleanup refused a reparse point.");
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            visitedEntries++;
            if (visitedEntries > MaximumDeleteEntries)
            {
                throw new AgentHostPrivateStorageException(
                    "AgentHost private directory cleanup exceeded its entry limit.");
            }

            var entryAttributes = File.GetAttributes(entry);
            if ((entryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((entryAttributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, recursive: false);
                }
                else
                {
                    File.Delete(entry);
                }

                continue;
            }

            if ((entryAttributes & FileAttributes.Directory) != 0)
            {
                DeleteDirectoryNoFollow(entry, ref visitedEntries);
                continue;
            }

            if ((entryAttributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(entry, entryAttributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(entry);
        }

        Directory.Delete(directory, recursive: false);
    }
}

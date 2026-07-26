using System.Text;
using System.Text.Json;
using Codex.AutoCAD.Ipc;

namespace Codex.AutoCAD.AgentHost;

internal enum CodexSessionHomeFailure
{
    InvalidSessionId,
    InvalidRoot,
    AlreadyExists,
    InitializationFailed,
    CleanupFailed,
}

internal sealed class CodexSessionHomeException : Exception
{
    internal CodexSessionHomeException(
        CodexSessionHomeFailure failure,
        string message)
        : base(message)
    {
        Failure = failure;
    }

    internal CodexSessionHomeFailure Failure { get; }
}

/// <summary>
/// Owns a session-bound Codex home. The home contains only non-secret configuration and is
/// provisioned before the optional one-use credential login.
/// </summary>
internal sealed class CodexSessionHomeLease : IDisposable
{
    private const string ConfigurationText =
        "cli_auth_credentials_store = \"keyring\"\r\n"
        + "mcp_servers = {}\r\n\r\n[features]\r\nplugins = false\r\n";
    private FileStream? _activeLease;
    private int _disposed;

    private CodexSessionHomeLease(
        string sessionId,
        string homePath,
        FileStream activeLease)
    {
        SessionId = sessionId;
        HomePath = homePath;
        ConfigurationPath = Path.Combine(homePath, "config.toml");
        CachePath = Path.Combine(homePath, "cache");
        PluginsPath = Path.Combine(homePath, "plugins");
        _activeLease = activeLease;
    }

    internal string SessionId { get; }

    internal string HomePath { get; }

    internal string ConfigurationPath { get; }

    internal string CachePath { get; }

    internal string PluginsPath { get; }

    internal static CodexSessionHomeLease Create(string sessionRoot, string sessionId)
    {
        ValidateSessionId(sessionId);
        var root = ValidateRoot(sessionRoot);
        var homePath = Path.Combine(root, "codex-home");
        if (Directory.Exists(homePath) || File.Exists(homePath))
        {
            throw Failure(
                CodexSessionHomeFailure.AlreadyExists,
                "The session Codex home already exists.");
        }

        FileStream? activeLease = null;
        var ownsHome = false;
        try
        {
            Directory.CreateDirectory(homePath);
            ownsHome = true;
            var activeLeasePath = Path.Combine(homePath, ".active");
            activeLease = new FileStream(
                activeLeasePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.WriteThrough);

            var result = new CodexSessionHomeLease(sessionId, homePath, activeLease);
            Directory.CreateDirectory(result.CachePath);
            Directory.CreateDirectory(result.PluginsPath);
            File.WriteAllText(
                result.ConfigurationPath,
                ConfigurationText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            WriteMarker(homePath, sessionId);
            activeLease.Flush(flushToDisk: true);
            activeLease = null;
            return result;
        }
        catch (CodexSessionHomeException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Failure(
                CodexSessionHomeFailure.InitializationFailed,
                "The session Codex home could not be initialized.");
        }
        finally
        {
            activeLease?.Dispose();
            if (activeLease is not null && ownsHome)
            {
                TryDeleteOwnedTree(homePath);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _activeLease, null)?.Dispose();
        try
        {
            DeleteTreeWithoutFollowingReparsePoints(HomePath);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Failure(
                CodexSessionHomeFailure.CleanupFailed,
                "The session Codex home could not be cleaned safely.");
        }
    }

    private static void ValidateSessionId(string sessionId)
    {
        if (sessionId is null || sessionId.Length != AgentBootstrapProtocol.SessionIdBytes)
        {
            throw Failure(
                CodexSessionHomeFailure.InvalidSessionId,
                "The session Codex home identity is invalid.");
        }

        foreach (var character in sessionId)
        {
            if (!((character >= '0' && character <= '9')
                  || (character >= 'a' && character <= 'f')))
            {
                throw Failure(
                    CodexSessionHomeFailure.InvalidSessionId,
                    "The session Codex home identity is invalid.");
            }
        }
    }

    private static string ValidateRoot(string sessionRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionRoot)
                || !Path.IsPathFullyQualified(sessionRoot)
                || sessionRoot.StartsWith("\\\\", StringComparison.Ordinal)
                || sessionRoot.StartsWith("\\\\?\\", StringComparison.Ordinal)
                || sessionRoot.StartsWith("\\\\.\\", StringComparison.Ordinal))
            {
                throw Failure(
                    CodexSessionHomeFailure.InvalidRoot,
                    "The session Codex home root is invalid.");
            }

            var fullPath = Path.GetFullPath(sessionRoot);
            if (!Directory.Exists(fullPath)
                || ContainsReparsePoint(fullPath)
                || new DriveInfo(Path.GetPathRoot(fullPath)!).DriveType != DriveType.Fixed)
            {
                throw Failure(
                    CodexSessionHomeFailure.InvalidRoot,
                    "The session Codex home root is invalid.");
            }

            return fullPath;
        }
        catch (CodexSessionHomeException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Failure(
                CodexSessionHomeFailure.InvalidRoot,
                "The session Codex home root is invalid.");
        }
    }

    private static void WriteMarker(string homePath, string sessionId)
    {
        var markerPath = Path.Combine(homePath, ".codex-autocad-session.json");
        using var stream = new FileStream(
            markerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        JsonSerializer.Serialize(
            stream,
            new CodexSessionHomeMarker(1, sessionId));
        stream.Flush(flushToDisk: true);
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
            if ((entryAttributes & FileAttributes.Directory) != 0)
            {
                if ((entryAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(entry, recursive: false);
                }
                else
                {
                    DeleteTreeWithoutFollowingReparsePoints(entry);
                }
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(path, recursive: false);
    }

    private static void TryDeleteOwnedTree(string homePath)
    {
        try
        {
            DeleteTreeWithoutFollowingReparsePoints(homePath);
        }
        catch
        {
        }
    }

    private static bool IsFileSystemException(Exception exception)
        => exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or System.Security.SecurityException;

    private static CodexSessionHomeException Failure(
        CodexSessionHomeFailure failure,
        string message)
        => new(failure, message);

    private sealed record CodexSessionHomeMarker(int SchemaVersion, string SessionId);
}

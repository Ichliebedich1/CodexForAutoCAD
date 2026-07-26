using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Codex.AutoCAD.AppServer;

/// <summary>
/// Holds a deny-write/delete handle to the resolved Codex executable. The lease spans version
/// preflight and App Server startup so both commands are bound to the same Windows file identity.
/// </summary>
internal sealed class CodexExecutableLease : IDisposable
{
    private FileStream? _stream;
    private List<SafeFileHandle>? _directoryHandles;
    private readonly CodexExecutableFileIdentity _identity;
    private int _referenceCount = 1;
    private int _ownerReleased;

    private CodexExecutableLease(
        string executablePath,
        FileStream stream,
        List<SafeFileHandle> directoryHandles,
        CodexExecutableFileIdentity identity)
    {
        ExecutablePath = executablePath;
        _stream = stream;
        _directoryHandles = directoryHandles;
        _identity = identity;
    }

    internal string ExecutablePath { get; }

    internal static CodexExecutableLease Acquire(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        FileStream? stream = null;
        List<SafeFileHandle>? directoryHandles = null;
        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            ValidateLocalExecutablePath(fullPath);
            directoryHandles = LockDirectoryChain(fullPath);
            stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.RandomAccess);
            var identity = GetIdentity(stream.SafeFileHandle);
            if ((identity.FileAttributes & FileAttributeReparsePoint) != 0
                || (identity.FileAttributes & FileAttributeDirectory) != 0)
            {
                throw new IOException("Codex executable identity is not a regular file.");
            }

            return new CodexExecutableLease(fullPath, stream, directoryHandles, identity);
        }
        catch (Exception exception) when (IsLeaseException(exception))
        {
            stream?.Dispose();
            DisposeHandles(directoryHandles);
            throw Failure(
                CodexVersionPreflightFailure.ExecutableIdentityUnavailable,
                "The local Codex executable identity could not be locked.");
        }
    }

    internal void ValidateCurrentPath(string executablePath)
    {
        var stream = _stream
            ?? throw Failure(
                CodexVersionPreflightFailure.ExecutableIdentityUnavailable,
                "The local Codex executable identity lease is no longer available.");

        if (!string.Equals(
                executablePath,
                ExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                CodexVersionPreflightFailure.ExecutableIdentityChanged,
                "The local Codex executable identity changed before startup.");
        }

        try
        {
            using var current = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.RandomAccess);
            var currentIdentity = GetIdentity(current.SafeFileHandle);
            if (currentIdentity != _identity)
            {
                throw Failure(
                    CodexVersionPreflightFailure.ExecutableIdentityChanged,
                    "The local Codex executable identity changed before startup.");
            }

            if (stream.SafeFileHandle.IsInvalid || stream.SafeFileHandle.IsClosed)
            {
                throw Failure(
                    CodexVersionPreflightFailure.ExecutableIdentityUnavailable,
                    "The local Codex executable identity lease is no longer available.");
            }
        }
        catch (CodexVersionPreflightException)
        {
            throw;
        }
        catch (Exception exception) when (IsLeaseException(exception))
        {
            throw Failure(
                CodexVersionPreflightFailure.ExecutableIdentityUnavailable,
                "The local Codex executable identity could not be verified.");
        }
    }

    internal CodexExecutableLeaseReference AcquireReference()
    {
        while (true)
        {
            var current = Volatile.Read(ref _referenceCount);
            if (current == 0)
            {
                throw Failure(
                    CodexVersionPreflightFailure.ExecutableIdentityUnavailable,
                    "The local Codex executable identity lease is no longer available.");
            }

            if (Interlocked.CompareExchange(ref _referenceCount, current + 1, current) == current)
            {
                return new CodexExecutableLeaseReference(this);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _ownerReleased, 1) == 0)
        {
            ReleaseReference();
        }
    }

    internal void ReleaseReference()
    {
        if (Interlocked.Decrement(ref _referenceCount) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _stream, null)?.Dispose();
        DisposeHandles(Interlocked.Exchange(ref _directoryHandles, null));
    }

    private static CodexExecutableFileIdentity GetIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new CodexExecutableFileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow,
            information.FileAttributes);
    }

    private static List<SafeFileHandle> LockDirectoryChain(string executablePath)
    {
        var parent = Directory.GetParent(executablePath)
            ?? throw new IOException("Codex executable parent directory is unavailable.");
        var directories = new Stack<string>();
        for (var current = parent; current is not null; current = current.Parent)
        {
            directories.Push(current.FullName);
        }

        var handles = new List<SafeFileHandle>(directories.Count);
        try
        {
            foreach (var directory in directories)
            {
                var handle = CreateFile(
                    directory,
                    FileReadAttributes,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error);
                }

                handles.Add(handle);
                var identity = GetIdentity(handle);
                if ((identity.FileAttributes & FileAttributeDirectory) == 0
                    || (identity.FileAttributes & FileAttributeReparsePoint) != 0)
                {
                    throw new IOException("Codex executable directory identity is unsafe.");
                }
            }

            return handles;
        }
        catch
        {
            DisposeHandles(handles);
            throw;
        }
    }

    private static void ValidateLocalExecutablePath(string fullPath)
    {
        if (!OperatingSystem.IsWindows()
            || fullPath.Length < 3
            || !char.IsLetter(fullPath[0])
            || fullPath[1] != ':'
            || (fullPath[2] != '\\' && fullPath[2] != '/')
            || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Codex executable path is not an absolute local Windows executable.");
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)
            || new DriveInfo(root).DriveType != DriveType.Fixed)
        {
            throw new IOException("Codex executable is not on a fixed local drive.");
        }
    }

    private static void DisposeHandles(List<SafeFileHandle>? handles)
    {
        if (handles is null)
        {
            return;
        }

        for (var index = handles.Count - 1; index >= 0; index--)
        {
            handles[index].Dispose();
        }
    }

    private static bool IsLeaseException(Exception exception)
        => exception is ArgumentException
            or IOException
            or NotSupportedException
            or ObjectDisposedException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or Win32Exception;

    private static CodexVersionPreflightException Failure(
        CodexVersionPreflightFailure failure,
        string message)
        => new(failure, message);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint FileFlagBackupSemantics = 0x02000000;

    private readonly record struct CodexExecutableFileIdentity(
        uint VolumeSerialNumber,
        ulong FileIndex,
        uint FileAttributes);
}

internal sealed class CodexExecutableLeaseReference : IDisposable
{
    private CodexExecutableLease? _lease;

    internal CodexExecutableLeaseReference(CodexExecutableLease lease)
    {
        _lease = lease;
    }

    internal CodexExecutableLease Lease
        => _lease
            ?? throw new ObjectDisposedException(nameof(CodexExecutableLeaseReference));

    public void Dispose()
    {
        Interlocked.Exchange(ref _lease, null)?.ReleaseReference();
    }
}

using System.Runtime.InteropServices;
using System.Threading;

namespace Codex.AutoCAD.AgentLauncher;

internal interface IWindowsCredentialNativeApi
{
    WindowsCredentialNativeRecord? Read(string credentialTargetName);
}

internal sealed class WindowsCredentialManagerCredentialReader
{
    internal const uint GenericCredentialType = 1;
    internal const int MaximumCredentialBytes = 4 * 1024;

    private readonly IWindowsCredentialNativeApi nativeApi;

    internal WindowsCredentialManagerCredentialReader()
        : this(new WindowsCredentialNativeApi())
    {
    }

    internal WindowsCredentialManagerCredentialReader(IWindowsCredentialNativeApi nativeApi)
    {
        this.nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
    }

    internal AgentHostCredentialSecret Read(ValidatedAgentHostCredentialOptions options)
    {
        if (options == null
            || options.Mode != AgentHostCredentialMode.WindowsCredentialManagerAccessToken)
        {
            throw InvalidConfiguration();
        }

        WindowsCredentialNativeRecord? record;
        try
        {
            record = nativeApi.Read(options.CredentialTargetName);
        }
        catch (Exception exception)
        {
            throw Unavailable(exception);
        }

        if (record == null)
        {
            throw Unavailable();
        }

        using (record)
        {
            if (record.CredentialType != GenericCredentialType
                || record.CredentialBlobSize <= 0
                || record.CredentialBlobSize > MaximumCredentialBytes
                || record.CredentialBlobPointer == IntPtr.Zero)
            {
                throw Unavailable();
            }

            var credentialBytes = new byte[record.CredentialBlobSize];
            try
            {
                Marshal.Copy(
                    record.CredentialBlobPointer,
                    credentialBytes,
                    0,
                    credentialBytes.Length);
                return new AgentHostCredentialSecret(credentialBytes);
            }
            catch (Exception exception)
            {
                Array.Clear(credentialBytes, 0, credentialBytes.Length);
                throw Unavailable(exception);
            }
        }
    }

    private static AgentBootstrapLaunchException InvalidConfiguration()
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.InvalidConfiguration,
            "The credential reader requires an enabled access-token configuration.");
    }

    private static AgentBootstrapLaunchException Unavailable(Exception? exception = null)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.CredentialUnavailable,
            "The configured Windows credential could not be read.",
            exception);
    }
}

public sealed class AgentHostCredentialSecret : IDisposable
{
    private byte[]? credentialBytes;

    internal AgentHostCredentialSecret(byte[] credentialBytes)
    {
        this.credentialBytes = credentialBytes
            ?? throw new ArgumentNullException(nameof(credentialBytes));
    }

    internal int Length
    {
        get
        {
            var current = credentialBytes;
            return current == null ? 0 : current.Length;
        }
    }

    internal bool IsDisposed => credentialBytes == null;

    public void WriteTo(Stream output)
    {
        if (output == null || !output.CanWrite)
        {
            throw new ArgumentException(
                "Credential output stream must be writable.",
                nameof(output));
        }

        UseBytes(bytes =>
        {
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
            return 0;
        });
    }

    internal T UseBytes<T>(Func<byte[], T> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var current = credentialBytes;
        if (current == null)
        {
            throw new ObjectDisposedException(nameof(AgentHostCredentialSecret));
        }

        return action(current);
    }

    public void Dispose()
    {
        var current = Interlocked.Exchange(ref credentialBytes, null);
        if (current != null)
        {
            Array.Clear(current, 0, current.Length);
        }
    }
}

internal sealed class WindowsCredentialNativeRecord : IDisposable
{
    private Action? release;

    internal WindowsCredentialNativeRecord(
        uint credentialType,
        IntPtr credentialBlobPointer,
        int credentialBlobSize,
        Action release)
    {
        CredentialType = credentialType;
        CredentialBlobPointer = credentialBlobPointer;
        CredentialBlobSize = credentialBlobSize;
        this.release = release ?? throw new ArgumentNullException(nameof(release));
    }

    internal uint CredentialType { get; }

    internal IntPtr CredentialBlobPointer { get; }

    internal int CredentialBlobSize { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref release, null)?.Invoke();
    }
}

internal sealed class WindowsCredentialNativeApi : IWindowsCredentialNativeApi
{
    public WindowsCredentialNativeRecord? Read(string credentialTargetName)
    {
        IntPtr credentialPointer;
        if (!CredRead(
                credentialTargetName,
                WindowsCredentialManagerCredentialReader.GenericCredentialType,
                0,
                out credentialPointer))
        {
            return null;
        }

        try
        {
            var credentialObject = Marshal.PtrToStructure(
                credentialPointer,
                typeof(NativeCredential));
            if (credentialObject == null)
            {
                throw new InvalidOperationException(
                    "The Windows credential record could not be decoded.");
            }

            var credential = (NativeCredential)credentialObject;
            var releasePointer = credentialPointer;
            credentialPointer = IntPtr.Zero;
            return new WindowsCredentialNativeRecord(
                credential.Type,
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize),
                () => CredFree(releasePointer));
        }
        finally
        {
            if (credentialPointer != IntPtr.Zero)
            {
                CredFree(credentialPointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        internal uint Flags;
        internal uint Type;
        internal IntPtr TargetName;
        internal IntPtr Comment;
        internal long LastWritten;
        internal uint CredentialBlobSize;
        internal IntPtr CredentialBlob;
        internal uint Persist;
        internal uint AttributeCount;
        internal IntPtr Attributes;
        internal IntPtr TargetAlias;
        internal IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        int reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credentialPointer);
}

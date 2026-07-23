using System.Runtime.InteropServices;
using System.Text;
using Codex.AutoCAD.AppServer;

namespace Codex.AutoCAD.AgentHost;

internal enum AgentHostCodexSessionIsolationFailure
{
    InvalidCredentialReference,
    CredentialUnavailable,
    CredentialRejected,
    WorkspaceUnavailable,
}

internal sealed class AgentHostCodexSessionIsolationException : Exception
{
    internal AgentHostCodexSessionIsolationException(
        AgentHostCodexSessionIsolationFailure failure,
        string message)
        : base(message)
    {
        Failure = failure;
    }

    internal AgentHostCodexSessionIsolationFailure Failure { get; }
}

internal enum AgentHostCredentialReadFailure
{
    NotFound,
    InvalidSecret,
    Unavailable,
}

internal sealed class AgentHostCredentialReadException : Exception
{
    internal AgentHostCredentialReadException(AgentHostCredentialReadFailure failure)
        : base("The requested Windows credential could not be read safely.")
    {
        Failure = failure;
    }

    internal AgentHostCredentialReadFailure Failure { get; }
}

internal interface IAgentHostCredentialReader
{
    string ReadGenericSecret(string credentialTarget);
}

/// <summary>
/// Binds a product-owned Windows Generic Credential to an AgentHost session without placing the
/// secret in command-line arguments, bootstrap packets, audit records, or a persistent workspace
/// file. An absent reference preserves the legacy file-login compatibility path; a supplied but
/// invalid reference fails closed.
/// </summary>
internal sealed class AgentHostCodexSessionIsolation
{
    internal const string CredentialTargetEnvironmentVariable =
        "CODEX_AUTOCAD_CREDENTIAL_TARGET";

    internal const string CredentialTargetPrefix = "CodexForAutoCAD/";
    internal const int MaximumCredentialTargetCharacters = 128;

    private AgentHostCodexSessionIsolation(
        string codexHomeDirectory,
        string codexSqliteHomeDirectory,
        string codexAccessToken)
    {
        CodexHomeDirectory = codexHomeDirectory;
        CodexSqliteHomeDirectory = codexSqliteHomeDirectory;
        CodexAccessToken = codexAccessToken;
    }

    internal string CodexHomeDirectory { get; }

    internal string CodexSqliteHomeDirectory { get; }

    internal string CodexAccessToken { get; }

    internal static AgentHostCodexSessionIsolation? CreateForCurrentProcess(
        AgentWorkspace workspace)
    {
        return Create(
            Environment.GetEnvironmentVariable(CredentialTargetEnvironmentVariable),
            workspace,
            new WindowsCredentialManagerReader());
    }

    internal static AgentHostCodexSessionIsolation? Create(
        string? credentialTarget,
        AgentWorkspace workspace,
        IAgentHostCredentialReader credentialReader)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(credentialReader);
        if (credentialTarget is null)
        {
            return null;
        }

        if (!IsValidCredentialTarget(credentialTarget))
        {
            throw Failure(
                AgentHostCodexSessionIsolationFailure.InvalidCredentialReference,
                "The configured Codex credential reference is invalid.");
        }

        string accessToken;
        try
        {
            accessToken = credentialReader.ReadGenericSecret(credentialTarget);
        }
        catch (AgentHostCredentialReadException exception)
        {
            throw exception.Failure == AgentHostCredentialReadFailure.InvalidSecret
                ? Failure(
                    AgentHostCodexSessionIsolationFailure.CredentialRejected,
                    "The configured Codex credential is invalid.")
                : Failure(
                    AgentHostCodexSessionIsolationFailure.CredentialUnavailable,
                    "The configured Codex credential is unavailable.");
        }
        catch (Exception)
        {
            throw Failure(
                AgentHostCodexSessionIsolationFailure.CredentialUnavailable,
                "The configured Codex credential is unavailable.");
        }

        if (!IsValidAccessToken(accessToken))
        {
            throw Failure(
                AgentHostCodexSessionIsolationFailure.CredentialRejected,
                "The configured Codex credential is invalid.");
        }

        try
        {
            var state = workspace.PrepareCodexState();
            return new AgentHostCodexSessionIsolation(
                state.CodexHomeDirectory,
                state.CodexSqliteHomeDirectory,
                accessToken);
        }
        catch (Exception)
        {
            throw Failure(
                AgentHostCodexSessionIsolationFailure.WorkspaceUnavailable,
                "The Codex session workspace could not be prepared safely.");
        }
    }

    private static bool IsValidCredentialTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumCredentialTargetCharacters
            || !value.StartsWith(CredentialTargetPrefix, StringComparison.Ordinal)
            || value.Length == CredentialTargetPrefix.Length)
        {
            return false;
        }

        foreach (var character in value.AsSpan(CredentialTargetPrefix.Length))
        {
            if (character is not (>= 'a' and <= 'z')
                and not (>= 'A' and <= 'Z')
                and not (>= '0' and <= '9')
                and not '.'
                and not '_'
                and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidAccessToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > CodexLocalAppServerConfiguration.MaximumSessionAccessTokenCharacters
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static AgentHostCodexSessionIsolationException Failure(
        AgentHostCodexSessionIsolationFailure failure,
        string message)
        => new(failure, message);
}

internal sealed class WindowsCredentialManagerReader : IAgentHostCredentialReader
{
    private const uint CredentialTypeGeneric = 1;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes =
        CodexLocalAppServerConfiguration.MaximumSessionAccessTokenCharacters * sizeof(char);

    private static readonly Encoding StrictUnicode = new UnicodeEncoding(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    public string ReadGenericSecret(string credentialTarget)
    {
        IntPtr credentialPointer = IntPtr.Zero;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new AgentHostCredentialReadException(
                    AgentHostCredentialReadFailure.Unavailable);
            }

            if (!NativeMethods.CredRead(
                    credentialTarget,
                    CredentialTypeGeneric,
                    flags: 0,
                    out credentialPointer))
            {
                throw new AgentHostCredentialReadException(
                    Marshal.GetLastWin32Error() == ErrorNotFound
                        ? AgentHostCredentialReadFailure.NotFound
                        : AgentHostCredentialReadFailure.Unavailable);
            }

            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.Type != CredentialTypeGeneric
                || credential.CredentialBlob == IntPtr.Zero
                || credential.CredentialBlobSize == 0
                || credential.CredentialBlobSize > MaximumCredentialBlobBytes
                || (credential.CredentialBlobSize & 1) != 0)
            {
                throw new AgentHostCredentialReadException(
                    AgentHostCredentialReadFailure.InvalidSecret);
            }

            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                var secret = StrictUnicode.GetString(bytes);
                if (secret.Length > 0 && secret[^1] == '\0')
                {
                    secret = secret[..^1];
                }

                if (secret.IndexOf('\0') >= 0)
                {
                    throw new AgentHostCredentialReadException(
                        AgentHostCredentialReadFailure.InvalidSecret);
                }

                return secret;
            }
            catch (DecoderFallbackException)
            {
                throw new AgentHostCredentialReadException(
                    AgentHostCredentialReadFailure.InvalidSecret);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        catch (AgentHostCredentialReadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or OutOfMemoryException
                                          or System.ComponentModel.Win32Exception)
        {
            throw new AgentHostCredentialReadException(
                AgentHostCredentialReadFailure.Unavailable);
        }
        finally
        {
            if (credentialPointer != IntPtr.Zero)
            {
                NativeMethods.CredFree(credentialPointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private static class NativeMethods
    {
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string targetName,
            uint type,
            uint flags,
            out IntPtr credential);

        [DllImport("Advapi32.dll", SetLastError = false)]
        internal static extern void CredFree(IntPtr buffer);
    }
}

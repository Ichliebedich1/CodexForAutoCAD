using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Codex.AutoCAD.Ipc;
using Microsoft.Win32.SafeHandles;

namespace Codex.AutoCAD.AgentLauncher;

public static class AgentBootstrapInheritedChannel
{
    private const int BufferSize = 4096;

    public static FileStream OpenStandardInput()
    {
        return OpenRawHandle(
            WindowsNative.GetStdHandle(WindowsNative.StandardInputHandle),
            FileAccess.Read,
            "Inherited standard input handle is invalid.");
    }

    public static FileStream OpenStandardOutput()
    {
        return OpenRawHandle(
            WindowsNative.GetStdHandle(WindowsNative.StandardOutputHandle),
            FileAccess.Write,
            "Inherited standard output handle is invalid.");
    }

    public static void ClearStandardErrorInheritance()
    {
        var raw = WindowsNative.GetStdHandle(WindowsNative.StandardErrorHandle);
        using (var safeHandle = new SafeFileHandle(raw, false))
        {
            if (safeHandle.IsInvalid)
            {
                throw new InvalidOperationException("Inherited standard error handle is invalid.");
            }

            WindowsNative.ClearInheritFlag(safeHandle);
        }
    }

    public static AgentBootstrapProcessIdentity GetCurrentProcessIdentity()
    {
        var processHandle = WindowsNative.GetCurrentProcess();
        return new AgentBootstrapProcessIdentity(
            checked((int)WindowsNative.GetCurrentProcessId()),
            WindowsNative.GetCreationFileTime(processHandle));
    }

    public static AgentBootstrapPayload ReadSingleBootstrapPacket(FileStream input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var authenticationKey = new byte[AgentBootstrapProtocol.AuthenticationKeySize];
        try
        {
            ReadExact(input, authenticationKey, 0, authenticationKey.Length);
            return AgentBootstrapProtocol.ReadSingleFrameAndClearKey(input, authenticationKey);
        }
        finally
        {
            Array.Clear(authenticationKey, 0, authenticationKey.Length);
        }
    }

    internal static void WriteSingleBootstrapPacket(
        Stream output,
        AgentBootstrapPayload payload)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (!output.CanWrite)
        {
            throw new ArgumentException("Bootstrap output stream must be writable.", nameof(output));
        }

        if (payload == null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var authenticationKey = AgentBootstrapProtocol.CreateAuthenticationKey();
        byte[]? frameAuthenticationKey = null;
        byte[]? frameBufferBytes = null;
        var frameLength = 0;
        try
        {
            frameAuthenticationKey = (byte[])authenticationKey.Clone();
            using (var frameBuffer = new MemoryStream())
            {
                AgentBootstrapProtocol.WriteSingleFrameAndClearKey(
                    frameBuffer,
                    payload,
                    frameAuthenticationKey);
                frameBufferBytes = frameBuffer.GetBuffer();
                frameLength = checked((int)frameBuffer.Length);
                output.Write(authenticationKey, 0, authenticationKey.Length);
                output.Write(frameBufferBytes, 0, frameLength);
                output.Flush();
            }
        }
        finally
        {
            Array.Clear(authenticationKey, 0, authenticationKey.Length);
            if (frameAuthenticationKey != null)
            {
                Array.Clear(frameAuthenticationKey, 0, frameAuthenticationKey.Length);
            }

            if (frameBufferBytes != null)
            {
                Array.Clear(frameBufferBytes, 0, frameBufferBytes.Length);
            }
        }
    }

    private static FileStream OpenRawHandle(
        IntPtr raw,
        FileAccess access,
        string invalidMessage)
    {
        var safeHandle = new SafeFileHandle(raw, true);
        try
        {
            if (safeHandle.IsInvalid)
            {
                throw new InvalidOperationException(invalidMessage);
            }

            WindowsNative.ClearInheritFlag(safeHandle);
            var stream = new FileStream(safeHandle, access, BufferSize, false);
            safeHandle = null!;
            return stream;
        }
        finally
        {
            if (safeHandle != null)
            {
                safeHandle.Dispose();
            }
        }
    }

    private static void ReadExact(Stream input, byte[] buffer, int offset, int count)
    {
        var remaining = count;
        while (remaining > 0)
        {
            var read = input.Read(buffer, offset, remaining);
            if (read <= 0)
            {
                throw new EndOfStreamException(
                    "Bootstrap channel ended before the authentication key completed.");
            }

            offset += read;
            remaining -= read;
        }
    }
}

internal enum AgentHostBootstrapCommand
{
    Doctor = 0,
    Serve = 1,
}

internal sealed class WindowsInheritedBootstrapProcess : IDisposable
{
    private readonly SafeKernelHandle processHandle;
    private readonly WindowsProcessTreeJob processTreeJob;
    private SafeDesktopHandle? privateDesktop;
    private SafeKernelHandle? primaryThreadHandle;
    private FileStream? executableLock;
    private bool disposed;
    private bool confirmed;
    private bool resumed;

    private WindowsInheritedBootstrapProcess(
        SafeKernelHandle processHandle,
        WindowsProcessTreeJob processTreeJob,
        SafeDesktopHandle? privateDesktop,
        SafeKernelHandle primaryThreadHandle,
        FileStream executableLock,
        int processId,
        long processCreationFileTime,
        string executableSha256,
        FileStream bootstrapOutput,
        FileStream confirmationInput,
        FileStream standardErrorInput,
        AgentHostProcessIdentityProfile processIdentityProfile,
        bool processTokenIsRestricted)
    {
        this.processHandle = processHandle;
        this.processTreeJob = processTreeJob;
        this.privateDesktop = privateDesktop;
        this.primaryThreadHandle = primaryThreadHandle;
        this.executableLock = executableLock;
        ProcessId = processId;
        ProcessCreationFileTime = processCreationFileTime;
        ExecutableSha256 = executableSha256;
        BootstrapOutput = bootstrapOutput;
        ConfirmationInput = confirmationInput;
        StandardErrorInput = standardErrorInput;
        ProcessIdentityProfile = processIdentityProfile;
        ProcessTokenIsRestricted = processTokenIsRestricted;
    }

    internal int ProcessId { get; }

    internal long ProcessCreationFileTime { get; }

    internal string ExecutableSha256 { get; }

    internal FileStream BootstrapOutput { get; }

    internal FileStream ConfirmationInput { get; }

    internal FileStream StandardErrorInput { get; }

    internal AgentHostProcessIdentityProfile ProcessIdentityProfile { get; }

    internal bool ProcessTokenIsRestricted { get; }

    internal bool UsesPrivateDesktop => privateDesktop is not null;

    internal static WindowsInheritedBootstrapProcess Start(
        AgentHostExecutableIdentity executableIdentity,
        AgentHostProcessTreeLimits processTreeLimits,
        Action throwIfLaunchAborted)
    {
        return Start(
            executableIdentity,
            AgentHostBootstrapCommand.Doctor,
            processTreeLimits,
            AgentHostProcessIdentityProfile.CurrentUser,
            throwIfLaunchAborted);
    }

    internal static WindowsInheritedBootstrapProcess Start(
        AgentHostExecutableIdentity executableIdentity,
        AgentHostBootstrapCommand command,
        AgentHostProcessTreeLimits processTreeLimits,
        Action throwIfLaunchAborted)
    {
        return Start(
            executableIdentity,
            command,
            processTreeLimits,
            AgentHostProcessIdentityProfile.CurrentUser,
            throwIfLaunchAborted);
    }

    internal static WindowsInheritedBootstrapProcess Start(
        AgentHostExecutableIdentity executableIdentity,
        AgentHostProcessTreeLimits processTreeLimits,
        AgentHostProcessIdentityProfile processIdentityProfile,
        Action throwIfLaunchAborted)
    {
        return Start(
            executableIdentity,
            AgentHostBootstrapCommand.Doctor,
            processTreeLimits,
            processIdentityProfile,
            throwIfLaunchAborted);
    }

    internal static WindowsInheritedBootstrapProcess Start(
        AgentHostExecutableIdentity executableIdentity,
        AgentHostBootstrapCommand command,
        AgentHostProcessTreeLimits processTreeLimits,
        AgentHostProcessIdentityProfile processIdentityProfile,
        Action throwIfLaunchAborted)
    {
        if (processTreeLimits == null)
        {
            throw new ArgumentNullException(nameof(processTreeLimits));
        }
        if (throwIfLaunchAborted == null)
        {
            throw new ArgumentNullException(nameof(throwIfLaunchAborted));
        }
        if (processIdentityProfile != AgentHostProcessIdentityProfile.CurrentUser
            && processIdentityProfile != AgentHostProcessIdentityProfile.RestrictedToken)
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.InvalidConfiguration,
                "AgentHost process identity profile is invalid.");
        }

        throwIfLaunchAborted();
        var executablePath = executableIdentity.FullPath;
        SafeFileHandle? childBootstrapRead = null;
        SafeFileHandle? parentBootstrapWrite = null;
        SafeFileHandle? parentConfirmationRead = null;
        SafeFileHandle? childConfirmationWrite = null;
        SafeFileHandle? parentStandardErrorRead = null;
        SafeFileHandle? childStandardErrorWrite = null;
        SafeKernelHandle? processHandle = null;
        SafeKernelHandle? primaryThreadHandle = null;
        SafeKernelHandle? restrictedToken = null;
        WindowsProcessTreeJob? processTreeJob = null;
        SafeDesktopHandle? privateDesktop = null;
        FileStream? executableLock = null;
        FileStream? bootstrapOutput = null;
        FileStream? confirmationInput = null;
        FileStream? standardErrorInput = null;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr handleList = IntPtr.Zero;
        IntPtr desktopName = IntPtr.Zero;
        WindowsNative.ProcessInformation processInformation = default;
        var processTokenIsRestricted = false;
        try
        {
            executableLock = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var lockedFileIdentity = WindowsNative.GetFileIdentity(
                executableLock.SafeFileHandle);
            var executableSha256 = ComputeSha256(executableLock);
            if (!string.Equals(
                    executableSha256,
                    executableIdentity.ExpectedSha256,
                    StringComparison.Ordinal))
            {
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.IdentityMismatch,
                    "AgentHost executable SHA-256 does not match the approved manifest.");
            }
            throwIfLaunchAborted();

            CreatePipePair(out childBootstrapRead, out parentBootstrapWrite);
            CreatePipePair(out parentConfirmationRead, out childConfirmationWrite);
            CreatePipePair(out parentStandardErrorRead, out childStandardErrorWrite);
            ClearInheritFlag(parentBootstrapWrite);
            ClearInheritFlag(parentConfirmationRead);
            ClearInheritFlag(parentStandardErrorRead);

            var childHandles = new[]
            {
                childBootstrapRead.DangerousGetHandle(),
                childConfirmationWrite.DangerousGetHandle(),
                childStandardErrorWrite.DangerousGetHandle()
            };
            attributeList = CreateAttributeList(childHandles, out handleList);

            var startup = new WindowsNative.StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf(typeof(WindowsNative.StartupInfoEx));
            startup.StartupInfo.dwFlags = WindowsNative.StartfUseStdHandles;
            startup.StartupInfo.hStdInput = childBootstrapRead.DangerousGetHandle();
            startup.StartupInfo.hStdOutput = childConfirmationWrite.DangerousGetHandle();
            startup.StartupInfo.hStdError = childStandardErrorWrite.DangerousGetHandle();
            startup.lpAttributeList = attributeList;

            var commandLine = new StringBuilder();
            commandLine.Append(QuoteCommandLineArgument(executablePath));
            commandLine.Append(command switch
            {
                AgentHostBootstrapCommand.Doctor => " bootstrap-doctor",
                AgentHostBootstrapCommand.Serve => " bootstrap-serve",
                _ => throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.InvalidConfiguration,
                    "AgentHost bootstrap command is invalid."),
            });

            throwIfLaunchAborted();

            var creationFlags = WindowsNative.ExtendedStartupInfoPresent
                | WindowsNative.CreateNoWindow
                | WindowsNative.CreateSuspended;
            bool created;
            if (processIdentityProfile == AgentHostProcessIdentityProfile.RestrictedToken)
            {
                restrictedToken = WindowsRestrictedToken.CreateForCurrentProcess();
                privateDesktop = WindowsPrivateDesktop.Create(out desktopName);
                startup.StartupInfo.lpDesktop = desktopName;
                created = WindowsNative.CreateProcessAsUser(
                    restrictedToken,
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    creationFlags,
                    IntPtr.Zero,
                    Path.GetDirectoryName(executablePath),
                    ref startup,
                    out processInformation);
            }
            else
            {
                created = WindowsNative.CreateProcess(
                    executablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    creationFlags,
                    IntPtr.Zero,
                    Path.GetDirectoryName(executablePath),
                    ref startup,
                    out processInformation);
            }
            if (!created)
            {
                var nativeErrorCode = Marshal.GetLastWin32Error();
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailurePolicy.ClassifyProcessCreationFailure(
                        nativeErrorCode,
                        processIdentityProfile == AgentHostProcessIdentityProfile.RestrictedToken),
                    "Creating the AgentHost process failed.",
                    new Win32Exception(nativeErrorCode));
            }

            processHandle = new SafeKernelHandle(processInformation.hProcess, true);
            processInformation.hProcess = IntPtr.Zero;
            primaryThreadHandle = new SafeKernelHandle(processInformation.hThread, true);
            processInformation.hThread = IntPtr.Zero;
            throwIfLaunchAborted();

            var processId = checked((int)WindowsNative.GetProcessId(processHandle));
            if (processId != checked((int)processInformation.dwProcessId))
            {
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.IdentityMismatch,
                    "Created process identity is inconsistent.");
            }

            var creationFileTime = WindowsNative.GetCreationFileTime(processHandle.DangerousGetHandle());
            var processImagePath = WindowsNative.GetProcessImagePath(processHandle);
            if (!PathsEqual(processImagePath, executablePath))
            {
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.IdentityMismatch,
                    "Created AgentHost image path does not match the validated executable path.");
            }

            if (processIdentityProfile == AgentHostProcessIdentityProfile.RestrictedToken)
            {
                processTokenIsRestricted = WindowsRestrictedToken.IsRestricted(processHandle);
                if (!processTokenIsRestricted)
                {
                    throw new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                        "The AgentHost child token was not restricted.");
                }
            }

            using (var processImage = new FileStream(
                processImagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                var processImageIdentity = WindowsNative.GetFileIdentity(
                    processImage.SafeFileHandle);
                var processImageSha256 = ComputeSha256(processImage);
                if (!lockedFileIdentity.Equals(processImageIdentity)
                    || !string.Equals(
                        executableSha256,
                        processImageSha256,
                        StringComparison.Ordinal))
                {
                    throw new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.IdentityMismatch,
                        "Created AgentHost image does not match the locked approved file identity.");
                }
            }
            throwIfLaunchAborted();

            processTreeJob = WindowsProcessTreeJob.CreateKillOnClose(processTreeLimits);
            processTreeJob.Assign(processHandle);
            throwIfLaunchAborted();

            childBootstrapRead.Dispose();
            childBootstrapRead = null;
            childConfirmationWrite.Dispose();
            childConfirmationWrite = null;
            childStandardErrorWrite.Dispose();
            childStandardErrorWrite = null;
            bootstrapOutput = new FileStream(parentBootstrapWrite, FileAccess.Write, 4096, false);
            parentBootstrapWrite = null;
            confirmationInput = new FileStream(parentConfirmationRead, FileAccess.Read, 4096, false);
            parentConfirmationRead = null;
            standardErrorInput = new FileStream(parentStandardErrorRead, FileAccess.Read, 4096, false);
            parentStandardErrorRead = null;

            var result = new WindowsInheritedBootstrapProcess(
                processHandle,
                processTreeJob,
                privateDesktop,
                primaryThreadHandle,
                executableLock,
                processId,
                creationFileTime,
                executableSha256,
                bootstrapOutput,
                confirmationInput,
                standardErrorInput,
                processIdentityProfile,
                processTokenIsRestricted);
            processHandle = null;
            primaryThreadHandle = null;
            processTreeJob = null;
            privateDesktop = null;
            executableLock = null;
            bootstrapOutput = null;
            confirmationInput = null;
            standardErrorInput = null;
            return result;
        }
        catch (Exception startException)
        {
            if (processHandle != null && !processHandle.IsInvalid)
            {
                try
                {
                    if (!WindowsNative.TerminateAndWait(processHandle, 5000))
                    {
                        throw new InvalidOperationException(
                            "Suspended AgentHost did not terminate during failed startup cleanup.");
                    }
                }
                catch (Exception cleanupException)
                {
                    var terminationFailure = new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.ChildTerminationFailed,
                        "Failed AgentHost startup could not guarantee child termination.",
                        new AggregateException(startException, cleanupException));
                    AgentBootstrapLateFailureRegistry.Record(terminationFailure);
                    throw terminationFailure;
                }
            }

            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                WindowsNative.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (handleList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handleList);
            }

            if (desktopName != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(desktopName);
            }

            if (processInformation.hThread != IntPtr.Zero)
            {
                WindowsNative.CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != IntPtr.Zero)
            {
                WindowsNative.CloseHandle(processInformation.hProcess);
            }

            childBootstrapRead?.Dispose();
            parentBootstrapWrite?.Dispose();
            parentConfirmationRead?.Dispose();
            childConfirmationWrite?.Dispose();
            parentStandardErrorRead?.Dispose();
            childStandardErrorWrite?.Dispose();
            bootstrapOutput?.Dispose();
            confirmationInput?.Dispose();
            standardErrorInput?.Dispose();
            processTreeJob?.Dispose();
            privateDesktop?.Dispose();
            restrictedToken?.Dispose();
            primaryThreadHandle?.Dispose();
            executableLock?.Dispose();
            processHandle?.Dispose();
        }
    }

    internal void Resume()
    {
        ThrowIfDisposed();
        if (resumed || primaryThreadHandle == null || primaryThreadHandle.IsInvalid)
        {
            throw new InvalidOperationException("AgentHost primary thread resume state is invalid.");
        }

        var previousSuspendCount = WindowsNative.ResumeThread(primaryThreadHandle);
        if (previousSuspendCount != 1)
        {
            throw new Win32Exception(
                previousSuspendCount == uint.MaxValue ? Marshal.GetLastWin32Error() : 0,
                "Resuming the validated AgentHost primary thread failed.");
        }

        resumed = true;
        primaryThreadHandle.Dispose();
        primaryThreadHandle = null;
    }

    internal Exception? AbortIo()
    {
        List<Exception>? failures = null;
        try
        {
            BootstrapOutput.Dispose();
        }
        catch (Exception exception)
        {
            failures = new List<Exception> { exception };
        }

        try
        {
            ConfirmationInput.Dispose();
        }
        catch (Exception exception)
        {
            if (failures == null)
            {
                failures = new List<Exception>();
            }
            failures.Add(exception);
        }

        try
        {
            StandardErrorInput.Dispose();
        }
        catch (Exception exception)
        {
            if (failures == null)
            {
                failures = new List<Exception>();
            }
            failures.Add(exception);
        }

        if (failures == null)
        {
            return null;
        }

        return failures.Count == 1
            ? failures[0]
            : new AggregateException("Closing AgentHost inherited channels failed.", failures);
    }

    internal void CloseBootstrapOutput()
    {
        ThrowIfDisposed();
        BootstrapOutput.Dispose();
    }

    internal void MarkConfirmed()
    {
        ThrowIfDisposed();
        if (!resumed)
        {
            throw new InvalidOperationException("A suspended AgentHost cannot be confirmed.");
        }
        confirmed = true;
    }

    internal void RequireTerminationOnDispose()
    {
        ThrowIfDisposed();
        confirmed = false;
    }

    internal bool WaitForExit(int milliseconds, out int exitCode)
    {
        ThrowIfDisposed();
        return WindowsNative.WaitForExit(processHandle, milliseconds, out exitCode);
    }

    internal bool TerminateAndWait(int milliseconds)
    {
        ThrowIfDisposed();
        return WindowsNative.TerminateAndWait(processHandle, milliseconds);
    }

    internal AgentHostProcessTreeLimitNotification WaitForLimitNotification(
        int milliseconds)
    {
        ThrowIfDisposed();
        return processTreeJob.WaitForLimitNotification(milliseconds, ProcessId);
    }

    internal void CancelLimitNotificationWait()
    {
        if (disposed)
        {
            return;
        }

        processTreeJob.CancelLimitNotificationWait();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        AgentBootstrapLaunchException? cleanupFailure = null;
        if (!confirmed && !processHandle.IsInvalid)
        {
            try
            {
                if (!WindowsNative.TerminateAndWait(processHandle, 5000))
                {
                    cleanupFailure = new AgentBootstrapLaunchException(
                        AgentBootstrapLaunchFailure.ChildTerminationFailed,
                        "AgentHost final cleanup could not prove process termination.");
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ChildTerminationFailed,
                    "AgentHost final cleanup failed while proving process termination.",
                    exception);
            }
        }

        var ioFailure = AbortIo();
        if (ioFailure != null)
        {
            cleanupFailure = new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ChildTerminationFailed,
                "AgentHost final inherited-channel cleanup failed.",
                cleanupFailure == null
                    ? ioFailure
                    : new AggregateException(cleanupFailure, ioFailure));
        }
        if (cleanupFailure != null)
        {
            AgentBootstrapLateFailureRegistry.Record(cleanupFailure);
        }

        try
        {
            processTreeJob.Dispose();
        }
        catch (Exception exception)
        {
            AgentBootstrapLateFailureRegistry.Record(
                new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ChildTerminationFailed,
                    "AgentHost final process-tree job cleanup failed.",
                    exception));
        }

        try
        {
            processHandle.Dispose();
        }
        catch
        {
        }

        try
        {
            primaryThreadHandle?.Dispose();
        }
        catch
        {
        }

        try
        {
            executableLock?.Dispose();
        }
        catch
        {
        }

        try
        {
            privateDesktop?.Dispose();
            privateDesktop = null;
        }
        catch
        {
        }
    }

    private static void CreatePipePair(out SafeFileHandle readHandle, out SafeFileHandle writeHandle)
    {
        var attributes = new WindowsNative.SecurityAttributes
        {
            nLength = Marshal.SizeOf(typeof(WindowsNative.SecurityAttributes)),
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero
        };
        IntPtr read;
        IntPtr write;
        if (!WindowsNative.CreatePipe(out read, out write, ref attributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed.");
        }

        readHandle = new SafeFileHandle(read, true);
        writeHandle = new SafeFileHandle(write, true);
    }

    private static void ClearInheritFlag(SafeFileHandle handle)
    {
        WindowsNative.ClearInheritFlag(handle);
    }

    private static IntPtr CreateAttributeList(IntPtr[] childHandles, out IntPtr handleList)
    {
        IntPtr size = IntPtr.Zero;
        WindowsNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var error = Marshal.GetLastWin32Error();
        if (size == IntPtr.Zero || error != WindowsNative.ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "Sizing the process attribute list failed.");
        }

        var attributeList = Marshal.AllocHGlobal(size);
        handleList = IntPtr.Zero;
        var initialized = false;
        try
        {
            if (!WindowsNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Initializing the process attribute list failed.");
            }
            initialized = true;

            var byteCount = checked(IntPtr.Size * childHandles.Length);
            handleList = Marshal.AllocHGlobal(byteCount);
            for (var index = 0; index < childHandles.Length; index++)
            {
                Marshal.WriteIntPtr(handleList, index * IntPtr.Size, childHandles[index]);
            }

            if (!WindowsNative.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    new IntPtr(WindowsNative.ProcThreadAttributeHandleList),
                    handleList,
                    new IntPtr(byteCount),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Applying the inherited handle allowlist failed.");
            }

            return attributeList;
        }
        catch
        {
            if (handleList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handleList);
                handleList = IntPtr.Zero;
            }

            if (initialized)
            {
                WindowsNative.DeleteProcThreadAttributeList(attributeList);
            }

            Marshal.FreeHGlobal(attributeList);
            throw;
        }
    }

    private static string QuoteCommandLineArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string value)
    {
        var fullPath = Path.GetFullPath(value);
        if (fullPath.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            fullPath = fullPath.Substring(4);
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ComputeSha256(Stream input)
    {
        using (var sha256 = SHA256.Create())
        {
            return BitConverter.ToString(sha256.ComputeHash(input)).Replace("-", string.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsInheritedBootstrapProcess));
        }
    }
}

internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeKernelHandle(IntPtr handle, bool ownsHandle)
        : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return WindowsNative.CloseHandle(handle);
    }
}

internal sealed class SafeDesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeDesktopHandle(IntPtr handle, bool ownsHandle)
        : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        return WindowsNative.CloseDesktop(handle);
    }
}

internal static class WindowsRestrictedToken
{
    // SECURITY\RESTRICTED_CODE (S-1-5-12). CreateRestrictedToken only produces a token that
    // Windows identifies as restricted when it contains at least one restricted SID. Disabling
    // privileges alone is not sufficient for IsTokenRestricted.
    private const string RestrictedCodeSid = "S-1-5-12";

    internal static SafeKernelHandle CreateForCurrentProcess()
    {
        IntPtr currentToken = IntPtr.Zero;
        IntPtr restrictedCodeSid = IntPtr.Zero;
        IntPtr restrictedSidAttributes = IntPtr.Zero;
        IntPtr restrictedToken = IntPtr.Zero;
        try
        {
            if (!WindowsNative.OpenProcessToken(
                    WindowsNative.GetCurrentProcess(),
                    WindowsNative.TokenDuplicate | WindowsNative.TokenQuery | WindowsNative.TokenAssignPrimary,
                    out currentToken))
            {
                throw IsolationFailure("Opening the current process token failed.");
            }

            if (!WindowsNative.ConvertStringSidToSid(RestrictedCodeSid, out restrictedCodeSid))
            {
                throw IsolationFailure("Resolving the restricted AgentHost SID failed.");
            }

            var sidAndAttributes = new WindowsNative.SidAndAttributes
            {
                Sid = restrictedCodeSid,
                Attributes = 0,
            };
            restrictedSidAttributes = Marshal.AllocHGlobal(
                Marshal.SizeOf(typeof(WindowsNative.SidAndAttributes)));
            Marshal.StructureToPtr(
                sidAndAttributes,
                restrictedSidAttributes,
                false);

            if (!WindowsNative.CreateRestrictedToken(
                    currentToken,
                    WindowsNative.DisableMaxPrivilege,
                    0,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    1,
                    restrictedSidAttributes,
                    out restrictedToken))
            {
                throw IsolationFailure("Creating a restricted AgentHost token failed.");
            }

            var result = new SafeKernelHandle(restrictedToken, true);
            restrictedToken = IntPtr.Zero;
            if (result.IsInvalid || !WindowsNative.IsTokenRestricted(result))
            {
                result.Dispose();
                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                    "The restricted AgentHost token validation failed.");
            }

            return result;
        }
        finally
        {
            if (restrictedToken != IntPtr.Zero)
            {
                WindowsNative.CloseHandle(restrictedToken);
            }

            if (currentToken != IntPtr.Zero)
            {
                WindowsNative.CloseHandle(currentToken);
            }

            if (restrictedSidAttributes != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(restrictedSidAttributes);
            }

            if (restrictedCodeSid != IntPtr.Zero)
            {
                WindowsNative.LocalFree(restrictedCodeSid);
            }
        }
    }

    internal static bool IsRestricted(SafeKernelHandle processHandle)
    {
        if (processHandle == null)
        {
            throw new ArgumentNullException(nameof(processHandle));
        }

        IntPtr token = IntPtr.Zero;
        try
        {
            if (!WindowsNative.OpenProcessToken(
                    processHandle,
                    WindowsNative.TokenQuery,
                    out token))
            {
                throw IsolationFailure("Opening the AgentHost child token failed.");
            }

            using var safeToken = new SafeKernelHandle(token, true);
            token = IntPtr.Zero;
            return WindowsNative.IsTokenRestricted(safeToken);
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                WindowsNative.CloseHandle(token);
            }
        }
    }

    private static AgentBootstrapLaunchException IsolationFailure(string unsafeDiagnostic)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            unsafeDiagnostic,
            new Win32Exception(Marshal.GetLastWin32Error()));
    }
}

internal static class WindowsPrivateDesktop
{
    private const string DesktopNamePrefix = "CodexAutoCADRestricted-";

    internal static SafeDesktopHandle Create(out IntPtr desktopPath)
    {
        var name = DesktopNamePrefix + Guid.NewGuid().ToString("N");
        desktopPath = Marshal.StringToHGlobalUni("WinSta0\\" + name);
        var rawHandle = WindowsNative.CreateDesktop(
            name,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            WindowsNative.DesktopAllAccess,
            IntPtr.Zero);
        var result = new SafeDesktopHandle(rawHandle, true);
        if (!result.IsInvalid)
        {
            return result;
        }

        result.Dispose();
        Marshal.FreeHGlobal(desktopPath);
        desktopPath = IntPtr.Zero;
        throw new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            "Creating the AgentHost private desktop failed.",
            new Win32Exception(Marshal.GetLastWin32Error()));
    }
}

/// <summary>
/// Owns the Job Object that contains the authenticated AgentHost and every descendant it starts.
/// Closing this handle is intentionally a kill boundary, so a Host process crash cannot leave the
/// AgentHost/Codex subtree running without its owner.
/// </summary>
internal enum AgentHostProcessTreeLimitNotification
{
    None = 0,
    ProcessCountExceeded = 1,
    JobMemoryExceeded = 2,
    JobUserTimeExceeded = 3,
    RootProcessExited = 4,
    MonitorClosed = 5
}

internal sealed class WindowsProcessTreeJob : IDisposable
{
    private const ulong CompletionKey = 1;
    private readonly SafeKernelHandle handle;
    private readonly SafeKernelHandle completionPort;
    private bool disposed;

    private WindowsProcessTreeJob(
        SafeKernelHandle handle,
        SafeKernelHandle completionPort)
    {
        this.handle = handle;
        this.completionPort = completionPort;
    }

    internal static WindowsProcessTreeJob CreateKillOnClose(
        AgentHostProcessTreeLimits processTreeLimits)
    {
        if (processTreeLimits == null)
        {
            throw new ArgumentNullException(nameof(processTreeLimits));
        }

        var rawHandle = WindowsNative.CreateJobObject(IntPtr.Zero, null);
        var safeHandle = new SafeKernelHandle(rawHandle, true);
        SafeKernelHandle? completionPort = null;
        if (safeHandle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            safeHandle.Dispose();
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessStartFailed,
                "Creating the AgentHost process-tree job failed.",
                new Win32Exception(error));
        }

        try
        {
            var rawCompletionPort = WindowsNative.CreateIoCompletionPort(
                new IntPtr(-1),
                IntPtr.Zero,
                UIntPtr.Zero,
                1);
            completionPort = new SafeKernelHandle(rawCompletionPort, true);
            if (completionPort.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Creating the AgentHost Job completion port failed.");
            }

            var association = new WindowsNative.JobObjectAssociateCompletionPort
            {
                CompletionKey = new IntPtr(checked((long)CompletionKey)),
                CompletionPort = completionPort.DangerousGetHandle()
            };
            if (!WindowsNative.SetInformationJobObject(
                    safeHandle,
                    WindowsNative.JobObjectAssociateCompletionPortInformationClass,
                    ref association,
                    checked((uint)Marshal.SizeOf(
                        typeof(WindowsNative.JobObjectAssociateCompletionPort)))))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Associating the AgentHost Job completion port failed.");
            }

            var limits = new WindowsNative.JobObjectExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags =
                WindowsNative.JobObjectLimitKillOnJobClose
                | WindowsNative.JobObjectLimitActiveProcess
                | WindowsNative.JobObjectLimitJobMemory
                | WindowsNative.JobObjectLimitJobTime;
            limits.BasicLimitInformation.ActiveProcessLimit =
                checked((uint)processTreeLimits.MaximumActiveProcesses);
            limits.BasicLimitInformation.PerJobUserTimeLimit =
                processTreeLimits.MaximumJobUserTime.Ticks;
            limits.JobMemoryLimit = ToUIntPtr(processTreeLimits.MaximumJobMemoryBytes);
            if (!WindowsNative.SetInformationJobObject(
                    safeHandle,
                    WindowsNative.JobObjectExtendedLimitInformationClass,
                    ref limits,
                    checked((uint)Marshal.SizeOf(typeof(WindowsNative.JobObjectExtendedLimitInformation)))))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Setting the AgentHost process-tree job limit failed.");
            }

            var cpuRate = new WindowsNative.JobObjectCpuRateControlInformation();
            cpuRate.ControlFlags =
                WindowsNative.JobObjectCpuRateControlEnable
                | WindowsNative.JobObjectCpuRateControlHardCap;
            cpuRate.CpuRate = checked((uint)processTreeLimits.MaximumCpuRatePercent * 100u);
            if (!WindowsNative.SetInformationJobObject(
                    safeHandle,
                    WindowsNative.JobObjectCpuRateControlInformationClass,
                    ref cpuRate,
                    checked((uint)Marshal.SizeOf(typeof(WindowsNative.JobObjectCpuRateControlInformation)))))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Setting the AgentHost process-tree CPU-rate limit failed.");
            }

            return new WindowsProcessTreeJob(safeHandle, completionPort);
        }
        catch (Exception exception)
        {
            completionPort?.Dispose();
            safeHandle.Dispose();
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessStartFailed,
                "Configuring the AgentHost process-tree job failed.",
                exception);
        }
    }

    internal static SafeKernelHandle OpenProcessForAssignment(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var rawHandle = WindowsNative.OpenProcess(
            WindowsNative.ProcessSetQuota
                | WindowsNative.ProcessTerminate
                | WindowsNative.ProcessQueryLimitedInformation
                | WindowsNative.Synchronize,
            false,
            checked((uint)processId));
        var safeHandle = new SafeKernelHandle(rawHandle, true);
        if (!safeHandle.IsInvalid)
        {
            return safeHandle;
        }

        var error = Marshal.GetLastWin32Error();
        safeHandle.Dispose();
        throw new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.ProcessIsolationFailed,
            "Opening the process for Job assignment failed.",
            new Win32Exception(error));
    }

    internal static bool IsProcessInAnyJob(SafeKernelHandle process)
    {
        return QueryMembership(process, IntPtr.Zero);
    }

    internal bool Contains(SafeKernelHandle process)
    {
        ThrowIfDisposed();
        return QueryMembership(process, handle.DangerousGetHandle());
    }

    internal AgentHostProcessTreeLimitSnapshot QueryLimits()
    {
        ThrowIfDisposed();
        var limits = new WindowsNative.JobObjectExtendedLimitInformation();
        uint returnedLength;
        if (!WindowsNative.QueryInformationJobObject(
                handle,
                WindowsNative.JobObjectExtendedLimitInformationClass,
                ref limits,
                checked((uint)Marshal.SizeOf(typeof(WindowsNative.JobObjectExtendedLimitInformation))),
                out returnedLength))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Querying the AgentHost process-tree job limits failed.");
        }

        var cpuRate = new WindowsNative.JobObjectCpuRateControlInformation();
        if (!WindowsNative.QueryInformationJobObject(
                handle,
                WindowsNative.JobObjectCpuRateControlInformationClass,
                ref cpuRate,
                checked((uint)Marshal.SizeOf(typeof(WindowsNative.JobObjectCpuRateControlInformation))),
                out returnedLength))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Querying the AgentHost process-tree CPU-rate limit failed.");
        }

        return new AgentHostProcessTreeLimitSnapshot(
            limits.BasicLimitInformation.LimitFlags,
            checked((int)limits.BasicLimitInformation.ActiveProcessLimit),
            checked((long)limits.JobMemoryLimit.ToUInt64()),
            TimeSpan.FromTicks(limits.BasicLimitInformation.PerJobUserTimeLimit),
            cpuRate.ControlFlags,
            checked((int)cpuRate.CpuRate));
    }

    internal AgentHostProcessTreeLimitNotification WaitForLimitNotification(
        int milliseconds,
        int rootProcessId)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }
        if (rootProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rootProcessId));
        }

        while (true)
        {
            if (disposed)
            {
                return AgentHostProcessTreeLimitNotification.MonitorClosed;
            }

            uint message;
            UIntPtr observedCompletionKey;
            IntPtr overlapped;
            if (!WindowsNative.GetQueuedCompletionStatus(
                    completionPort,
                    out message,
                    out observedCompletionKey,
                    out overlapped,
                    checked((uint)milliseconds)))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == WindowsNative.ErrorWaitTimeout)
                {
                    return AgentHostProcessTreeLimitNotification.None;
                }

                if (disposed || error == WindowsNative.ErrorAbandonedWait)
                {
                    return AgentHostProcessTreeLimitNotification.MonitorClosed;
                }

                throw new AgentBootstrapLaunchException(
                    AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                    "Waiting for an AgentHost Job resource notification failed.",
                    new Win32Exception(error));
            }

            if (observedCompletionKey.ToUInt64() == 0 && message == 0)
            {
                return AgentHostProcessTreeLimitNotification.MonitorClosed;
            }

            if (observedCompletionKey.ToUInt64() != CompletionKey)
            {
                continue;
            }

            switch (message)
            {
                case WindowsNative.JobObjectMessageEndOfJobTime:
                    return AgentHostProcessTreeLimitNotification.JobUserTimeExceeded;
                case WindowsNative.JobObjectMessageActiveProcessLimit:
                    return AgentHostProcessTreeLimitNotification.ProcessCountExceeded;
                case WindowsNative.JobObjectMessageJobMemoryLimit:
                    return AgentHostProcessTreeLimitNotification.JobMemoryExceeded;
                case WindowsNative.JobObjectMessageExitProcess:
                case WindowsNative.JobObjectMessageAbnormalExitProcess:
                    if (overlapped.ToInt64() == rootProcessId)
                    {
                        return AgentHostProcessTreeLimitNotification.RootProcessExited;
                    }
                    break;
            }
        }
    }

    internal void CancelLimitNotificationWait()
    {
        if (disposed)
        {
            return;
        }

        if (!WindowsNative.PostQueuedCompletionStatus(
                completionPort,
                0,
                UIntPtr.Zero,
                IntPtr.Zero))
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                "Cancelling the AgentHost Job resource monitor failed.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    internal void Assign(SafeKernelHandle process)
    {
        if (process == null)
        {
            throw new ArgumentNullException(nameof(process));
        }

        ThrowIfDisposed();
        var wasAlreadyInJob = IsProcessInAnyJob(process);
        if (!WindowsNative.AssignProcessToJobObject(handle, process))
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailurePolicy.ClassifyJobAssignmentFailure(
                    wasAlreadyInJob),
                wasAlreadyInJob
                    ? "Assigning AgentHost to the nested process-tree job failed."
                    : "Assigning AgentHost to the process-tree job failed.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if (!Contains(process))
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                "AgentHost process-tree Job membership could not be confirmed.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            WindowsNative.PostQueuedCompletionStatus(
                completionPort,
                0,
                UIntPtr.Zero,
                IntPtr.Zero);
        }
        catch
        {
        }

        handle.Dispose();
        completionPort.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsProcessTreeJob));
        }
    }

    private static bool QueryMembership(
        SafeKernelHandle process,
        IntPtr job)
    {
        if (process == null)
        {
            throw new ArgumentNullException(nameof(process));
        }

        if (process.IsInvalid || process.IsClosed)
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                "The process handle for Job membership validation is invalid.");
        }

        bool isMember;
        if (!WindowsNative.IsProcessInJob(process, job, out isMember))
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessIsolationFailed,
                "Querying AgentHost process-tree Job membership failed.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return isMember;
    }

    private static UIntPtr ToUIntPtr(long value)
    {
        if (value <= 0 || (IntPtr.Size == 4 && value > uint.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return IntPtr.Size == 4
            ? new UIntPtr(checked((uint)value))
            : new UIntPtr(checked((ulong)value));
    }
}

internal sealed class AgentHostProcessTreeLimitSnapshot
{
    internal AgentHostProcessTreeLimitSnapshot(
        uint limitFlags,
        int maximumActiveProcesses,
        long maximumJobMemoryBytes,
        TimeSpan maximumJobUserTime,
        uint cpuControlFlags,
        int cpuRateBasisPoints)
    {
        LimitFlags = limitFlags;
        MaximumActiveProcesses = maximumActiveProcesses;
        MaximumJobMemoryBytes = maximumJobMemoryBytes;
        MaximumJobUserTime = maximumJobUserTime;
        CpuControlFlags = cpuControlFlags;
        CpuRateBasisPoints = cpuRateBasisPoints;
    }

    internal uint LimitFlags { get; }

    internal int MaximumActiveProcesses { get; }

    internal long MaximumJobMemoryBytes { get; }

    internal TimeSpan MaximumJobUserTime { get; }

    internal uint CpuControlFlags { get; }

    internal int CpuRateBasisPoints { get; }
}

internal static class WindowsNative
{
    internal const int ErrorInsufficientBuffer = 122;
    internal const int ErrorWaitTimeout = 258;
    internal const int ErrorAbandonedWait = 735;
    internal const uint HandleFlagInherit = 0x00000001;
    internal const uint ProcessTerminate = 0x00000001;
    internal const uint ProcessSetQuota = 0x00000100;
    internal const uint ProcessQueryLimitedInformation = 0x00001000;
    internal const uint Synchronize = 0x00100000;
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint StartfUseStdHandles = 0x00000100;
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    internal const uint JobObjectLimitActiveProcess = 0x00000008;
    internal const uint JobObjectLimitJobMemory = 0x00000200;
    internal const uint JobObjectLimitJobTime = 0x00000004;
    internal const uint JobObjectCpuRateControlEnable = 0x00000001;
    internal const uint JobObjectCpuRateControlHardCap = 0x00000004;
    internal const uint TokenAssignPrimary = 0x0001;
    internal const uint TokenDuplicate = 0x0002;
    internal const uint TokenQuery = 0x0008;
    internal const uint DisableMaxPrivilege = 0x00000001;
    internal const uint DesktopAllAccess = 0x000F01FF;
    internal const int ProcThreadAttributeHandleList = 0x00020002;
    internal const int JobObjectAssociateCompletionPortInformationClass = 7;
    internal const int JobObjectExtendedLimitInformationClass = 9;
    internal const int JobObjectCpuRateControlInformationClass = 15;
    internal const uint JobObjectMessageEndOfJobTime = 1;
    internal const uint JobObjectMessageActiveProcessLimit = 3;
    internal const uint JobObjectMessageExitProcess = 7;
    internal const uint JobObjectMessageAbnormalExitProcess = 8;
    internal const uint JobObjectMessageJobMemoryLimit = 10;
    internal const int StandardInputHandle = -10;
    internal const int StandardOutputHandle = -11;
    internal const int StandardErrorHandle = -12;

    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint TerminationExitCode = 0xC0DE2016;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        internal int nLength;
        internal IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] internal bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SidAndAttributes
    {
        internal IntPtr Sid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal int cb;
        internal IntPtr lpReserved;
        internal IntPtr lpDesktop;
        internal IntPtr lpTitle;
        internal int dwX;
        internal int dwY;
        internal int dwXSize;
        internal int dwYSize;
        internal int dwXCountChars;
        internal int dwYCountChars;
        internal int dwFillAttribute;
        internal uint dwFlags;
        internal short wShowWindow;
        internal short cbReserved2;
        internal IntPtr lpReserved2;
        internal IntPtr hStdInput;
        internal IntPtr hStdOutput;
        internal IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfoEx
    {
        internal StartupInfo StartupInfo;
        internal IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal IntPtr hProcess;
        internal IntPtr hThread;
        internal uint dwProcessId;
        internal uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        internal uint Low;
        internal uint High;
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

    internal struct FileIdentity
    {
        internal uint VolumeSerialNumber;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;

        public override bool Equals(object? obj)
        {
            if (!(obj is FileIdentity))
            {
                return false;
            }

            var other = (FileIdentity)obj;
            return VolumeSerialNumber == other.VolumeSerialNumber
                && FileIndexHigh == other.FileIndexHigh
                && FileIndexLow == other.FileIndexLow;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)VolumeSerialNumber;
                hash = (hash * 397) ^ (int)FileIndexHigh;
                return (hash * 397) ^ (int)FileIndexLow;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectCpuRateControlInformation
    {
        internal uint ControlFlags;
        internal uint CpuRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectAssociateCompletionPort
    {
        internal IntPtr CompletionKey;
        internal IntPtr CompletionPort;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        ref SecurityAttributes pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateJobObject(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        SafeKernelHandle job,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        SafeKernelHandle job,
        int jobObjectInformationClass,
        ref JobObjectCpuRateControlInformation jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        SafeKernelHandle job,
        int jobObjectInformationClass,
        ref JobObjectAssociateCompletionPort jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateIoCompletionPort(
        IntPtr fileHandle,
        IntPtr existingCompletionPort,
        UIntPtr completionKey,
        uint numberOfConcurrentThreads);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetQueuedCompletionStatus(
        SafeKernelHandle completionPort,
        out uint numberOfBytesTransferred,
        out UIntPtr completionKey,
        out IntPtr overlapped,
        uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostQueuedCompletionStatus(
        SafeKernelHandle completionPort,
        uint numberOfBytesTransferred,
        UIntPtr completionKey,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryInformationJobObject(
        SafeKernelHandle job,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation jobObjectInformation,
        uint jobObjectInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryInformationJobObject(
        SafeKernelHandle job,
        int jobObjectInformationClass,
        ref JobObjectCpuRateControlInformation jobObjectInformation,
        uint jobObjectInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(
        SafeKernelHandle job,
        SafeKernelHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsProcessInJob(
        SafeKernelHandle process,
        IntPtr job,
        [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(
        SafeFileHandle handle,
        uint mask,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int standardHandle);

    internal static void ClearInheritFlag(SafeFileHandle handle)
    {
        if (!SetHandleInformation(handle, HandleFlagInherit, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation failed.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        int flags,
        ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessAsUser(
        SafeKernelHandle token,
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        SafeKernelHandle processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateRestrictedToken(
        IntPtr existingTokenHandle,
        uint flags,
        uint disableSidCount,
        IntPtr sidsToDisable,
        uint deletePrivilegeCount,
        IntPtr privilegesToDelete,
        uint restrictedSidCount,
        IntPtr sidsToRestrict,
        out IntPtr newTokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ConvertStringSidToSid(
        string stringSid,
        out IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsTokenRestricted(SafeKernelHandle tokenHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateDesktop(
        string desktopName,
        IntPtr device,
        IntPtr deviceMode,
        uint flags,
        uint desiredAccess,
        IntPtr securityAttributes);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(IntPtr desktopHandle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetProcessId(SafeKernelHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(SafeKernelHandle thread);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeKernelHandle process,
        uint flags,
        StringBuilder executablePath,
        ref int size);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr process,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeKernelHandle process,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(SafeKernelHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(SafeKernelHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeKernelHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    internal static long GetCreationFileTime(IntPtr process)
    {
        FileTime creation;
        FileTime exit;
        FileTime kernel;
        FileTime user;
        if (!GetProcessTimes(process, out creation, out exit, out kernel, out user))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcessTimes failed.");
        }

        return CombineFileTime(creation);
    }

    internal static string GetProcessImagePath(SafeKernelHandle process)
    {
        var capacity = 32768;
        var path = new StringBuilder(capacity);
        if (!QueryFullProcessImageName(process, 0, path, ref capacity))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryFullProcessImageNameW failed.");
        }

        if (capacity <= 0 || capacity > path.Capacity)
        {
            throw new InvalidOperationException("Created process image path length is invalid.");
        }

        return path.ToString();
    }

    internal static FileIdentity GetFileIdentity(SafeFileHandle file)
    {
        ByHandleFileInformation information;
        if (!GetFileInformationByHandle(file, out information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandle failed.");
        }

        return new FileIdentity
        {
            VolumeSerialNumber = information.VolumeSerialNumber,
            FileIndexHigh = information.FileIndexHigh,
            FileIndexLow = information.FileIndexLow
        };
    }

    internal static long GetCreationFileTime(SafeKernelHandle process)
    {
        FileTime creation;
        FileTime exit;
        FileTime kernel;
        FileTime user;
        if (!GetProcessTimes(process, out creation, out exit, out kernel, out user))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcessTimes failed.");
        }

        return CombineFileTime(creation);
    }

    internal static bool WaitForExit(SafeKernelHandle process, int milliseconds, out int exitCode)
    {
        var result = WaitForSingleObject(process, checked((uint)Math.Max(0, milliseconds)));
        if (result == WaitTimeout)
        {
            exitCode = 0;
            return false;
        }

        if (result != WaitObject0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for AgentHost failed.");
        }

        uint rawExitCode;
        if (!GetExitCodeProcess(process, out rawExitCode))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Reading AgentHost exit code failed.");
        }

        exitCode = unchecked((int)rawExitCode);
        return true;
    }

    internal static bool TerminateAndWait(SafeKernelHandle process, int milliseconds)
    {
        int ignored;
        if (WaitForExit(process, 0, out ignored))
        {
            return true;
        }

        if (!TerminateProcess(process, TerminationExitCode))
        {
            var error = Marshal.GetLastWin32Error();
            if (!WaitForExit(process, milliseconds, out ignored))
            {
                throw new Win32Exception(error, "Terminating unconfirmed AgentHost failed.");
            }

            return true;
        }

        return WaitForExit(process, milliseconds, out ignored);
    }

    private static long CombineFileTime(FileTime value)
    {
        var combined = ((ulong)value.High << 32) | value.Low;
        if (combined == 0 || combined > long.MaxValue)
        {
            throw new InvalidOperationException("Process creation time is invalid.");
        }

        return (long)combined;
    }
}

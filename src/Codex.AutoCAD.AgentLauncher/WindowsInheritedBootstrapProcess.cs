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
    private SafeKernelHandle? primaryThreadHandle;
    private FileStream? executableLock;
    private bool disposed;
    private bool confirmed;
    private bool resumed;

    private WindowsInheritedBootstrapProcess(
        SafeKernelHandle processHandle,
        WindowsProcessTreeJob processTreeJob,
        SafeKernelHandle primaryThreadHandle,
        FileStream executableLock,
        int processId,
        long processCreationFileTime,
        string executableSha256,
        FileStream bootstrapOutput,
        FileStream confirmationInput,
        FileStream standardErrorInput)
    {
        this.processHandle = processHandle;
        this.processTreeJob = processTreeJob;
        this.primaryThreadHandle = primaryThreadHandle;
        this.executableLock = executableLock;
        ProcessId = processId;
        ProcessCreationFileTime = processCreationFileTime;
        ExecutableSha256 = executableSha256;
        BootstrapOutput = bootstrapOutput;
        ConfirmationInput = confirmationInput;
        StandardErrorInput = standardErrorInput;
    }

    internal int ProcessId { get; }

    internal long ProcessCreationFileTime { get; }

    internal string ExecutableSha256 { get; }

    internal FileStream BootstrapOutput { get; }

    internal FileStream ConfirmationInput { get; }

    internal FileStream StandardErrorInput { get; }

    internal static WindowsInheritedBootstrapProcess Start(
        AgentHostExecutableIdentity executableIdentity,
        AgentHostProcessTreeLimits processTreeLimits,
        Action throwIfLaunchAborted)
    {
        return Start(
            executableIdentity,
            AgentHostBootstrapCommand.Doctor,
            processTreeLimits,
            throwIfLaunchAborted);
    }

    internal static WindowsInheritedBootstrapProcess Start(
        AgentHostExecutableIdentity executableIdentity,
        AgentHostBootstrapCommand command,
        AgentHostProcessTreeLimits processTreeLimits,
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
        WindowsProcessTreeJob? processTreeJob = null;
        FileStream? executableLock = null;
        FileStream? bootstrapOutput = null;
        FileStream? confirmationInput = null;
        FileStream? standardErrorInput = null;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr handleList = IntPtr.Zero;
        WindowsNative.ProcessInformation processInformation = default;
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

            var created = WindowsNative.CreateProcess(
                executablePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                WindowsNative.ExtendedStartupInfoPresent
                    | WindowsNative.CreateNoWindow
                    | WindowsNative.CreateSuspended,
                IntPtr.Zero,
                Path.GetDirectoryName(executablePath),
                ref startup,
                out processInformation);
            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed.");
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
                primaryThreadHandle,
                executableLock,
                processId,
                creationFileTime,
                executableSha256,
                bootstrapOutput,
                confirmationInput,
                standardErrorInput);
            processHandle = null;
            primaryThreadHandle = null;
            processTreeJob = null;
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

/// <summary>
/// Owns the Job Object that contains the authenticated AgentHost and every descendant it starts.
/// Closing this handle is intentionally a kill boundary, so a Host process crash cannot leave the
/// AgentHost/Codex subtree running without its owner.
/// </summary>
internal sealed class WindowsProcessTreeJob : IDisposable
{
    private readonly SafeKernelHandle handle;
    private bool disposed;

    private WindowsProcessTreeJob(SafeKernelHandle handle)
    {
        this.handle = handle;
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
            var limits = new WindowsNative.JobObjectExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags =
                WindowsNative.JobObjectLimitKillOnJobClose
                | WindowsNative.JobObjectLimitActiveProcess
                | WindowsNative.JobObjectLimitJobMemory;
            limits.BasicLimitInformation.ActiveProcessLimit =
                checked((uint)processTreeLimits.MaximumActiveProcesses);
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

            return new WindowsProcessTreeJob(safeHandle);
        }
        catch (Exception exception)
        {
            safeHandle.Dispose();
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessStartFailed,
                "Configuring the AgentHost process-tree job failed.",
                exception);
        }
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

        return new AgentHostProcessTreeLimitSnapshot(
            limits.BasicLimitInformation.LimitFlags,
            checked((int)limits.BasicLimitInformation.ActiveProcessLimit),
            checked((long)limits.JobMemoryLimit.ToUInt64()));
    }

    internal void Assign(SafeKernelHandle process)
    {
        if (process == null)
        {
            throw new ArgumentNullException(nameof(process));
        }

        ThrowIfDisposed();
        if (!WindowsNative.AssignProcessToJobObject(handle, process))
        {
            throw new AgentBootstrapLaunchException(
                AgentBootstrapLaunchFailure.ProcessStartFailed,
                "Assigning AgentHost to the process-tree job failed.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        handle.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsProcessTreeJob));
        }
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
        long maximumJobMemoryBytes)
    {
        LimitFlags = limitFlags;
        MaximumActiveProcesses = maximumActiveProcesses;
        MaximumJobMemoryBytes = maximumJobMemoryBytes;
    }

    internal uint LimitFlags { get; }

    internal int MaximumActiveProcesses { get; }

    internal long MaximumJobMemoryBytes { get; }
}

internal static class WindowsNative
{
    internal const int ErrorInsufficientBuffer = 122;
    internal const uint HandleFlagInherit = 0x00000001;
    internal const uint ExtendedStartupInfoPresent = 0x00080000;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint CreateSuspended = 0x00000004;
    internal const uint StartfUseStdHandles = 0x00000100;
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;
    internal const uint JobObjectLimitActiveProcess = 0x00000008;
    internal const uint JobObjectLimitJobMemory = 0x00000200;
    internal const int ProcThreadAttributeHandleList = 0x00020002;
    internal const int JobObjectExtendedLimitInformationClass = 9;
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
    internal static extern bool QueryInformationJobObject(
        SafeKernelHandle job,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation jobObjectInformation,
        uint jobObjectInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(
        SafeKernelHandle job,
        SafeKernelHandle process);

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

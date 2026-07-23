using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Codex.AutoCAD.AgentLauncher;

return FakeAgentHostProgram.Run(args);

internal static class FakeAgentHostProgram
{
    internal static int Run(string[] args)
    {
        var mode = GetMode();
        if (mode.StartsWith("hang", StringComparison.Ordinal))
        {
            Thread.Sleep(Timeout.Infinite);
            return 99;
        }

        if (mode == "exit42")
        {
            return 42;
        }

        var serve = args.Length == 1
            && string.Equals(args[0], "bootstrap-serve", StringComparison.Ordinal);
        if (!serve
            && (args.Length != 1
                || !string.Equals(args[0], "bootstrap-doctor", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Unexpected fake AgentHost command line.");
        }

        AgentBootstrapInheritedChannel.ClearStandardErrorInheritance();
        if (mode == "garbage")
        {
            using var garbageOutput = AgentBootstrapInheritedChannel.OpenStandardOutput();
            garbageOutput.WriteByte(0x41);
            garbageOutput.Flush();
            return 0;
        }

        using var bootstrapInput = AgentBootstrapInheritedChannel.OpenStandardInput();
        using var confirmationOutput = AgentBootstrapInheritedChannel.OpenStandardOutput();

        if (mode == "canary" && IsParentCanaryInherited())
        {
            return 44;
        }

        if (mode == "inherit"
            && (IsInheritable(bootstrapInput.SafeFileHandle)
                || IsInheritable(confirmationOutput.SafeFileHandle)
                || IsStandardErrorInheritable()))
        {
            return 43;
        }

        using var payload = AgentBootstrapInheritedChannel.ReadSingleBootstrapPacket(
            bootstrapInput);
        var bootstrapId = payload.CopyBootstrapId();
        try
        {
            using var keys = payload.DeriveDirectionKeys();
            using var authenticator = keys.CreateConfirmationOutboundAuthenticator();
            var identity = AgentBootstrapInheritedChannel.GetCurrentProcessIdentity();
            var processId = mode == "identity" ? checked(identity.ProcessId + 1) : identity.ProcessId;
            var confirmation = AgentBootstrapConfirmationProtocol.CreateAgentConfirmation(
                payload.SessionId,
                bootstrapId,
                processId,
                identity.ProcessCreationFileTime,
                authenticator);

            if (mode == "stderr")
            {
                Console.Error.Write(new string('x', 32 * 1024));
            }
            else if (mode == "stderrfail")
            {
                Console.Error.Write(
                    "CODEX_RAW_STDERR_MUST_NOT_ESCAPE"
                    + new string('y', 32 * 1024));
            }

            AgentBootstrapConfirmationProtocol.WriteSingleFrame(
                confirmationOutput,
                confirmation);
            if (mode == "stderrfail")
            {
                return 42;
            }
            if (mode == "confirmhang")
            {
                confirmationOutput.Dispose();
                Thread.Sleep(Timeout.Infinite);
                return 99;
            }
            if (mode == "servechild")
            {
                if (!serve)
                {
                    throw new ArgumentException("servechild fake mode requires bootstrap-serve.");
                }

                StartDescendantAndWriteProcessId();
                confirmationOutput.Dispose();
                Thread.Sleep(Timeout.Infinite);
                return 99;
            }
            if (mode == "trailing")
            {
                confirmationOutput.WriteByte(0x7f);
                confirmationOutput.Flush();
            }
            else if (mode == "double")
            {
                AgentBootstrapConfirmationProtocol.WriteSingleFrame(
                    confirmationOutput,
                    confirmation);
            }

            if (serve)
            {
                confirmationOutput.Dispose();
                Thread.Sleep(Timeout.Infinite);
                return 99;
            }

            return 0;
        }
        finally
        {
            Array.Clear(bootstrapId, 0, bootstrapId.Length);
        }
    }

    private static string GetMode()
    {
        var executable = Environment.ProcessPath ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(executable);
        var separator = name.LastIndexOf('-');
        return separator < 0 ? "success" : name.Substring(separator + 1).ToLowerInvariant();
    }

    private static void StartDescendantAndWriteProcessId()
    {
        const string descendantExecutableVariable =
            "CODEX_AUTOCAD_TEST_DESCENDANT_EXECUTABLE";
        const string descendantProcessIdPathVariable =
            "CODEX_AUTOCAD_TEST_DESCENDANT_PROCESS_ID_PATH";
        var descendantExecutable = Environment.GetEnvironmentVariable(descendantExecutableVariable);
        var processIdPath = Environment.GetEnvironmentVariable(descendantProcessIdPathVariable);
        if (string.IsNullOrWhiteSpace(descendantExecutable)
            || !Path.IsPathFullyQualified(descendantExecutable)
            || string.IsNullOrWhiteSpace(processIdPath)
            || !Path.IsPathFullyQualified(processIdPath))
        {
            throw new InvalidOperationException("Process-tree test configuration is invalid.");
        }

        using var descendant = Process.Start(new ProcessStartInfo
        {
            FileName = descendantExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (descendant == null)
        {
            throw new InvalidOperationException("Starting the process-tree test descendant failed.");
        }

        File.WriteAllText(
            processIdPath,
            descendant.Id.ToString(CultureInfo.InvariantCulture),
            new UTF8Encoding(false));
    }

    private static bool IsInheritable(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        uint flags;
        if (!GetHandleInformation(handle, out flags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return (flags & 1u) != 0;
    }

    private static bool IsStandardErrorInheritable()
    {
        using var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(
            GetStdHandle(-12),
            false);
        return IsInheritable(handle);
    }

    private static bool IsParentCanaryInherited()
    {
        const string handleVariable = "CODEX_AUTOCAD_TEST_CANARY_HANDLE";
        const string pathVariable = "CODEX_AUTOCAD_TEST_CANARY_PATH";
        var handleText = Environment.GetEnvironmentVariable(handleVariable);
        var expectedPath = Environment.GetEnvironmentVariable(pathVariable);
        long handleValue;
        if (string.IsNullOrWhiteSpace(handleText)
            || string.IsNullOrWhiteSpace(expectedPath)
            || !long.TryParse(
                handleText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out handleValue))
        {
            throw new InvalidOperationException("Parent canary metadata is missing or invalid.");
        }

        using var handle = new SafeFileHandle(new IntPtr(handleValue), false);
        var path = TryGetFinalPath(handle);
        return path != null
            && string.Equals(
                NormalizePath(path),
                NormalizePath(expectedPath),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetFinalPath(SafeFileHandle handle)
    {
        const uint fileTypeDisk = 1;
        if (GetFileType(handle) != fileTypeDisk)
        {
            return null;
        }

        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
        if (length == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Inspecting the inherited parent canary file failed.");
        }

        if (length >= buffer.Capacity)
        {
            throw new InvalidOperationException("Parent canary path exceeded the test buffer.");
        }

        return buffer.ToString();
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
        {
            return "\\\\" + path.Substring(8);
        }

        return path.StartsWith("\\\\?\\", StringComparison.Ordinal)
            ? path.Substring(4)
            : path;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        out uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle file);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        int filePathLength,
        uint flags);

}

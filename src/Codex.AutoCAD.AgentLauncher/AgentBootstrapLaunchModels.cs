using System.Globalization;

namespace Codex.AutoCAD.AgentLauncher;

public enum AgentBootstrapLaunchFailure
{
    InvalidConfiguration = 1,
    ProcessStartFailed = 2,
    BootstrapWriteFailed = 3,
    ConfirmationInvalid = 4,
    IdentityMismatch = 5,
    ChildExitedWithError = 6,
    Timeout = 7,
    Cancellation = 8,
    ChildTerminationFailed = 9
}

public sealed class AgentBootstrapLaunchException : Exception
{
    public AgentBootstrapLaunchException(
        AgentBootstrapLaunchFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public AgentBootstrapLaunchFailure Failure { get; }
}

public sealed class AgentHostBootstrapOptions
{
    public static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaximumStartupTimeout = TimeSpan.FromMinutes(1);
    public const int DefaultMaximumActiveProcesses = 16;
    public const int MinimumMaximumActiveProcesses = 2;
    public const int MaximumMaximumActiveProcesses = 64;
    public const long DefaultMaximumJobMemoryBytes = 4L * 1024 * 1024 * 1024;
    public const long MinimumMaximumJobMemoryBytes = 512L * 1024 * 1024;
    public const long MaximumMaximumJobMemoryBytes = 16L * 1024 * 1024 * 1024;

    public AgentHostBootstrapOptions(
        string agentHostExecutablePath,
        string expectedExecutableSha256)
    {
        AgentHostExecutablePath = agentHostExecutablePath;
        ExpectedExecutableSha256 = expectedExecutableSha256;
    }

    public string AgentHostExecutablePath { get; }

    public string ExpectedExecutableSha256 { get; }

    public TimeSpan StartupTimeout { get; set; } = DefaultStartupTimeout;

    public int MaximumStandardErrorBytes { get; set; } = 16 * 1024;

    /// <summary>
    /// Maximum number of processes in the AgentHost/Codex Job Object, including AgentHost itself.
    /// </summary>
    public int MaximumActiveProcesses { get; set; } = DefaultMaximumActiveProcesses;

    /// <summary>Total committed memory allowed for the complete AgentHost/Codex Job Object.</summary>
    public long MaximumJobMemoryBytes { get; set; } = DefaultMaximumJobMemoryBytes;

    internal TimeSpan GetValidatedStartupTimeout()
    {
        if (StartupTimeout <= TimeSpan.Zero || StartupTimeout > MaximumStartupTimeout)
        {
            throw Invalid(
                "AgentHost startup timeout must be positive and no greater than "
                + MaximumStartupTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                + " seconds.");
        }

        return StartupTimeout;
    }

    internal AgentHostExecutableIdentity GetValidatedExecutableIdentity()
    {
        if (string.IsNullOrWhiteSpace(AgentHostExecutablePath))
        {
            throw Invalid("AgentHost executable path is required.");
        }

        if (!IsAbsoluteLocalWindowsPath(AgentHostExecutablePath))
        {
            throw Invalid("AgentHost executable path must be an absolute local-drive path.");
        }

        var fullPath = Path.GetFullPath(AgentHostExecutablePath);
        if (!IsAbsoluteLocalWindowsPath(fullPath))
        {
            throw Invalid("AgentHost executable path must remain on a local drive after normalization.");
        }
        if (GetDriveType(fullPath) != DriveType.Fixed)
        {
            throw Invalid("AgentHost executable must be located on a fixed local drive.");
        }
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("AgentHost bootstrap requires a Windows executable.");
        }

        if (!File.Exists(fullPath))
        {
            throw Invalid("AgentHost executable does not exist.");
        }

        GetValidatedStartupTimeout();

        if (MaximumStandardErrorBytes < 0 || MaximumStandardErrorBytes > 1024 * 1024)
        {
            throw Invalid("AgentHost stderr capture limit must be between 0 and 1048576 bytes.");
        }

        GetValidatedProcessTreeLimits();

        var expectedSha256 = NormalizeSha256(ExpectedExecutableSha256);
        return new AgentHostExecutableIdentity(fullPath, expectedSha256);
    }

    internal AgentHostProcessTreeLimits GetValidatedProcessTreeLimits()
    {
        if (MaximumActiveProcesses < MinimumMaximumActiveProcesses
            || MaximumActiveProcesses > MaximumMaximumActiveProcesses)
        {
            throw Invalid(
                "AgentHost process-tree limit must be between "
                + MinimumMaximumActiveProcesses.ToString(CultureInfo.InvariantCulture)
                + " and "
                + MaximumMaximumActiveProcesses.ToString(CultureInfo.InvariantCulture)
                + " processes.");
        }

        if (MaximumJobMemoryBytes < MinimumMaximumJobMemoryBytes
            || MaximumJobMemoryBytes > MaximumMaximumJobMemoryBytes
            || (IntPtr.Size == 4 && MaximumJobMemoryBytes > uint.MaxValue))
        {
            throw Invalid(
                "AgentHost process-tree memory limit is outside the supported range for this process architecture.");
        }

        return new AgentHostProcessTreeLimits(
            MaximumActiveProcesses,
            MaximumJobMemoryBytes);
    }

    private static DriveType GetDriveType(string path)
    {
        var candidate = path.StartsWith("\\\\?\\", StringComparison.Ordinal)
            ? path.Substring(4)
            : path;
        var root = candidate.Substring(0, 3);
        return new DriveInfo(root).DriveType;
    }

    private static string NormalizeSha256(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            throw Invalid("AgentHost expected SHA-256 must contain exactly 64 hexadecimal characters.");
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F')))
            {
                throw Invalid("AgentHost expected SHA-256 is invalid.");
            }
        }

        return value.ToUpperInvariant();
    }

    private static bool IsAbsoluteLocalWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var candidate = path;
        if (candidate.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (candidate.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            candidate = candidate.Substring(4);
        }

        return candidate.Length >= 3
            && char.IsLetter(candidate[0])
            && candidate[1] == ':'
            && (candidate[2] == '\\' || candidate[2] == '/');
    }

    private static AgentBootstrapLaunchException Invalid(string message)
    {
        return new AgentBootstrapLaunchException(
            AgentBootstrapLaunchFailure.InvalidConfiguration,
            message);
    }
}

internal sealed class AgentHostProcessTreeLimits
{
    internal AgentHostProcessTreeLimits(
        int maximumActiveProcesses,
        long maximumJobMemoryBytes)
    {
        MaximumActiveProcesses = maximumActiveProcesses;
        MaximumJobMemoryBytes = maximumJobMemoryBytes;
    }

    internal int MaximumActiveProcesses { get; }

    internal long MaximumJobMemoryBytes { get; }
}

internal sealed class AgentHostExecutableIdentity
{
    internal AgentHostExecutableIdentity(string fullPath, string expectedSha256)
    {
        FullPath = fullPath;
        ExpectedSha256 = expectedSha256;
    }

    internal string FullPath { get; }

    internal string ExpectedSha256 { get; }
}

public sealed class AgentBootstrapDoctorResult
{
    internal AgentBootstrapDoctorResult(
        int processId,
        long processCreationFileTime,
        string bootstrapId,
        string sessionId,
        string pipeName,
        string executableSha256,
        int standardErrorBytes,
        bool standardErrorTruncated)
    {
        ProcessId = processId;
        ProcessCreationFileTime = processCreationFileTime;
        BootstrapId = bootstrapId;
        SessionId = sessionId;
        PipeName = pipeName;
        ExecutableSha256 = executableSha256;
        StandardErrorBytes = standardErrorBytes;
        StandardErrorTruncated = standardErrorTruncated;
    }

    public int ProcessId { get; }

    public long ProcessCreationFileTime { get; }

    public string BootstrapId { get; }

    public string SessionId { get; }

    public string PipeName { get; }

    public string ExecutableSha256 { get; }

    public int StandardErrorBytes { get; }

    public bool StandardErrorTruncated { get; }
}

public sealed class AgentBootstrapProcessIdentity
{
    internal AgentBootstrapProcessIdentity(int processId, long processCreationFileTime)
    {
        ProcessId = processId;
        ProcessCreationFileTime = processCreationFileTime;
    }

    public int ProcessId { get; }

    public long ProcessCreationFileTime { get; }
}

using System.Collections.ObjectModel;

namespace Codex.AutoCAD.AppServer;

/// <summary>Identifies the approved local source of the Codex executable without exposing its path to callers.</summary>
public enum CodexExecutableSource
{
    CommandLine,
    Environment,
    NpmPackage,
    Path,
}

/// <summary>Stable, path-free configuration failures for a local Codex child process.</summary>
public enum CodexLocalConfigurationFailure
{
    UnsupportedPlatform,
    InvalidConfiguredExecutable,
    CodexExecutableNotFound,
    InvalidWorkingDirectory,
    InvalidTemporaryDirectory,
    InvalidChildEnvironment,
    IncompleteSessionIsolation,
    InvalidSessionIsolationDirectory,
    InvalidSessionIsolationToken,
    InvalidStartupTimeout,
    InvalidShutdownTimeout,
    InvalidConfiguration,
}

/// <summary>
/// Converts local Codex configuration failures into the only codes and messages that may be
/// surfaced outside configuration resolution. The resolver must never preserve a path, token,
/// environment value, or arbitrary exception text in an exception message.
/// </summary>
public static class CodexLocalConfigurationFailurePolicy
{
    public static CodexLocalConfigurationFailure Normalize(CodexLocalConfigurationFailure failure)
        => failure switch
        {
            CodexLocalConfigurationFailure.UnsupportedPlatform
                or CodexLocalConfigurationFailure.InvalidConfiguredExecutable
                or CodexLocalConfigurationFailure.CodexExecutableNotFound
                or CodexLocalConfigurationFailure.InvalidWorkingDirectory
                or CodexLocalConfigurationFailure.InvalidTemporaryDirectory
                or CodexLocalConfigurationFailure.InvalidChildEnvironment
                or CodexLocalConfigurationFailure.IncompleteSessionIsolation
                or CodexLocalConfigurationFailure.InvalidSessionIsolationDirectory
                or CodexLocalConfigurationFailure.InvalidSessionIsolationToken
                or CodexLocalConfigurationFailure.InvalidStartupTimeout
                or CodexLocalConfigurationFailure.InvalidShutdownTimeout
                or CodexLocalConfigurationFailure.InvalidConfiguration => failure,
            _ => CodexLocalConfigurationFailure.InvalidConfiguration,
        };

    public static string GetErrorCode(CodexLocalConfigurationFailure failure)
        => Normalize(failure) switch
        {
            CodexLocalConfigurationFailure.UnsupportedPlatform => "unsupported_platform",
            CodexLocalConfigurationFailure.InvalidConfiguredExecutable => "invalid_configured_executable",
            CodexLocalConfigurationFailure.CodexExecutableNotFound => "codex_executable_not_found",
            CodexLocalConfigurationFailure.InvalidWorkingDirectory => "invalid_working_directory",
            CodexLocalConfigurationFailure.InvalidTemporaryDirectory => "invalid_temporary_directory",
            CodexLocalConfigurationFailure.InvalidChildEnvironment => "invalid_child_environment",
            CodexLocalConfigurationFailure.IncompleteSessionIsolation => "incomplete_session_isolation",
            CodexLocalConfigurationFailure.InvalidSessionIsolationDirectory => "invalid_session_isolation_directory",
            CodexLocalConfigurationFailure.InvalidSessionIsolationToken => "invalid_session_isolation_token",
            CodexLocalConfigurationFailure.InvalidStartupTimeout => "invalid_startup_timeout",
            CodexLocalConfigurationFailure.InvalidShutdownTimeout => "invalid_shutdown_timeout",
            _ => "invalid_configuration",
        };

    public static string GetSafeMessage(CodexLocalConfigurationFailure failure)
        => Normalize(failure) switch
        {
            CodexLocalConfigurationFailure.UnsupportedPlatform =>
                "The local Codex configuration is not supported on this platform.",
            CodexLocalConfigurationFailure.InvalidConfiguredExecutable =>
                "The local Codex executable configuration is invalid.",
            CodexLocalConfigurationFailure.CodexExecutableNotFound =>
                "No approved local Codex executable is available.",
            CodexLocalConfigurationFailure.InvalidWorkingDirectory =>
                "The local Codex workspace configuration is invalid.",
            CodexLocalConfigurationFailure.InvalidTemporaryDirectory =>
                "The local Codex temporary-directory configuration is invalid.",
            CodexLocalConfigurationFailure.InvalidChildEnvironment =>
                "The local Codex child-process environment configuration is invalid.",
            CodexLocalConfigurationFailure.IncompleteSessionIsolation =>
                "The private Codex session configuration is incomplete.",
            CodexLocalConfigurationFailure.InvalidSessionIsolationDirectory =>
                "The private Codex session directory configuration is invalid.",
            CodexLocalConfigurationFailure.InvalidSessionIsolationToken =>
                "The private Codex session credential configuration is invalid.",
            CodexLocalConfigurationFailure.InvalidStartupTimeout =>
                "The local Codex startup-timeout configuration is invalid.",
            CodexLocalConfigurationFailure.InvalidShutdownTimeout =>
                "The local Codex shutdown-timeout configuration is invalid.",
            _ => "The local Codex configuration is invalid.",
        };
}

public sealed class CodexLocalConfigurationException : AppServerException
{
    public CodexLocalConfigurationException(CodexLocalConfigurationFailure failure)
        : base(CodexLocalConfigurationFailurePolicy.GetSafeMessage(failure))
    {
        Failure = CodexLocalConfigurationFailurePolicy.Normalize(failure);
    }

    public CodexLocalConfigurationFailure Failure { get; }

    public string ErrorCode => CodexLocalConfigurationFailurePolicy.GetErrorCode(Failure);
}

/// <summary>
/// Explicit inputs for resolving the local Codex installation. The request is also usable in
/// tests so discovery does not need to read global machine state.
/// </summary>
public sealed record CodexLocalAppServerConfigurationRequest
{
    public string? CommandLineExecutablePath { get; init; }

    public string? EnvironmentExecutablePath { get; init; }

    public string? ApplicationDataDirectory { get; init; }

    public string? PathValue { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;

    public string TemporaryDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Optional all-or-nothing isolation inputs. They are only accepted after AgentHost creates
    /// the private directories and reads a user-authorized Windows credential reference.
    /// </summary>
    public string? CodexHomeDirectory { get; init; }

    public string? CodexSqliteHomeDirectory { get; init; }

    public string? CodexAccessToken { get; init; }

    public TimeSpan StartupTimeout { get; init; } = CodexLocalAppServerConfiguration.DefaultStartupTimeout;

    public TimeSpan ShutdownTimeout { get; init; } = CodexLocalAppServerConfiguration.DefaultShutdownTimeout;

    /// <summary>
    /// Product-owned Codex compatibility window. This is intentionally not read from a user
    /// environment variable, because an unreviewed local override must not widen the protocol
    /// surface accepted by AgentHost.
    /// </summary>
    public CodexVersionCompatibility? VersionCompatibility { get; init; }
}

/// <summary>
/// Validated settings for an AgentHost-owned local <c>codex.exe</c> child. This is intentionally
/// narrower than <see cref="AppServerClientOptions"/>: generic transports remain testable, while
/// the real AgentHost fails closed instead of falling back to an unqualified PATH command.
/// </summary>
public sealed class CodexLocalAppServerConfiguration
{
    public static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(1);
    public const int MaximumSessionAccessTokenCharacters = 16 * 1024;

    private static readonly IReadOnlyList<string> DefaultMcpIsolationArguments =
        Array.AsReadOnly(new[]
        {
            "-c",
            "mcp_servers={}",
        });

    private readonly IReadOnlyDictionary<string, string?> versionPreflightEnvironment;

    internal CodexLocalAppServerConfiguration(
        string codexExecutablePath,
        CodexExecutableSource executableSource,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> childEnvironment,
        TimeSpan startupTimeout,
        TimeSpan shutdownTimeout,
        CodexVersionCompatibility versionCompatibility)
    {
        CodexExecutablePath = codexExecutablePath;
        ExecutableSource = executableSource;
        WorkingDirectory = workingDirectory;
        ChildEnvironment = childEnvironment;
        versionPreflightEnvironment = CreateVersionPreflightEnvironment(childEnvironment);
        StartupTimeout = startupTimeout;
        ShutdownTimeout = shutdownTimeout;
        VersionCompatibility = versionCompatibility ?? throw new ArgumentNullException(nameof(versionCompatibility));
    }

    public string CodexExecutablePath { get; }

    public CodexExecutableSource ExecutableSource { get; }

    public string WorkingDirectory { get; }

    internal IReadOnlyDictionary<string, string?> ChildEnvironment { get; }

    /// <summary>True when this configuration uses an AgentHost-owned Codex state root.</summary>
    public bool UsesSessionIsolation => ChildEnvironment.ContainsKey("CODEX_HOME");

    public TimeSpan StartupTimeout { get; }

    public TimeSpan ShutdownTimeout { get; }

    /// <summary>Frozen product compatibility window verified before App Server startup.</summary>
    public CodexVersionCompatibility VersionCompatibility { get; }

    public AppServerClientOptions CreateClientOptions()
        => CreateClientOptions(ChildEnvironment);

    /// <summary>
    /// Creates the constrained preflight process configuration. A version query never needs an
    /// access token, so the token is deliberately withheld until app-server startup.
    /// </summary>
    public AppServerClientOptions CreateVersionPreflightOptions()
        => CreateClientOptions(versionPreflightEnvironment);

    private AppServerClientOptions CreateClientOptions(
        IReadOnlyDictionary<string, string?> environment)
    {
        return new AppServerClientOptions
        {
            CodexExecutablePath = CodexExecutablePath,
            WorkingDirectory = WorkingDirectory,
            AdditionalArguments = DefaultMcpIsolationArguments,
            Environment = environment,
            InheritParentEnvironment = false,
            MaximumFrameBytes = 8 * 1024 * 1024,
            MaximumJsonDepth = 32,
            MaximumStandardErrorBytes = 16 * 1024,
            ShutdownTimeout = ShutdownTimeout,
        };
    }

    private static IReadOnlyDictionary<string, string?> CreateVersionPreflightEnvironment(
        IReadOnlyDictionary<string, string?> childEnvironment)
    {
        if (!childEnvironment.ContainsKey("CODEX_ACCESS_TOKEN"))
        {
            return childEnvironment;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in childEnvironment)
        {
            if (!string.Equals(pair.Key, "CODEX_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(pair.Key, pair.Value);
            }
        }

        return new ReadOnlyDictionary<string, string?>(values);
    }
}

public static class CodexLocalAppServerConfigurationResolver
{
    public static CodexLocalAppServerConfiguration ResolveForCurrentProcess(
        string? commandLineExecutablePath,
        string workingDirectory,
        string temporaryDirectory)
    {
        return Resolve(new CodexLocalAppServerConfigurationRequest
        {
            CommandLineExecutablePath = commandLineExecutablePath,
            EnvironmentExecutablePath = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE"),
            ApplicationDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            PathValue = Environment.GetEnvironmentVariable("PATH"),
            WorkingDirectory = workingDirectory,
            TemporaryDirectory = temporaryDirectory,
        });
    }

    public static CodexLocalAppServerConfiguration Resolve(
        CodexLocalAppServerConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows())
        {
            throw Failure(CodexLocalConfigurationFailure.UnsupportedPlatform);
        }

        var workingDirectory = ValidateWorkingDirectory(request.WorkingDirectory);
        var temporaryDirectory = ValidateTemporaryDirectory(request.TemporaryDirectory);
        var sessionIsolation = ValidateSessionIsolation(request);
        IReadOnlyDictionary<string, string?> childEnvironment;
        try
        {
            childEnvironment = CodexChildEnvironmentPolicy.CreateForCurrentProcess(
                temporaryDirectory,
                sessionIsolation);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or InvalidOperationException
                                          or NotSupportedException
                                          or UnauthorizedAccessException)
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidChildEnvironment);
        }
        var startupTimeout = ValidateTimeout(
            request.StartupTimeout,
            CodexLocalConfigurationFailure.InvalidStartupTimeout);
        var shutdownTimeout = ValidateTimeout(
            request.ShutdownTimeout,
            CodexLocalConfigurationFailure.InvalidShutdownTimeout);
        var versionCompatibility = request.VersionCompatibility ?? CodexVersionCompatibility.Default;

        var configuredCommandLine = NormalizeOptionalPath(request.CommandLineExecutablePath);
        if (configuredCommandLine is not null)
        {
            return Create(
                ValidateConfiguredExecutable(configuredCommandLine),
                CodexExecutableSource.CommandLine,
                workingDirectory,
                childEnvironment,
                startupTimeout,
                shutdownTimeout,
                versionCompatibility);
        }

        var configuredEnvironment = NormalizeOptionalPath(request.EnvironmentExecutablePath);
        if (configuredEnvironment is not null)
        {
            return Create(
                ValidateConfiguredExecutable(configuredEnvironment),
                CodexExecutableSource.Environment,
                workingDirectory,
                childEnvironment,
                startupTimeout,
                shutdownTimeout,
                versionCompatibility);
        }

        foreach (var candidate in EnumerateNpmCandidates(request.ApplicationDataDirectory))
        {
            if (TryValidateDiscoveredExecutable(candidate, out var executablePath))
            {
                return Create(
                    executablePath,
                    CodexExecutableSource.NpmPackage,
                    workingDirectory,
                    childEnvironment,
                    startupTimeout,
                    shutdownTimeout,
                    versionCompatibility);
            }
        }

        foreach (var candidate in EnumeratePathCandidates(request.PathValue))
        {
            if (TryValidateDiscoveredExecutable(candidate, out var executablePath))
            {
                return Create(
                    executablePath,
                    CodexExecutableSource.Path,
                    workingDirectory,
                    childEnvironment,
                    startupTimeout,
                    shutdownTimeout,
                    versionCompatibility);
            }
        }

        throw Failure(CodexLocalConfigurationFailure.CodexExecutableNotFound);
    }

    private static CodexLocalAppServerConfiguration Create(
        string executablePath,
        CodexExecutableSource source,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> childEnvironment,
        TimeSpan startupTimeout,
        TimeSpan shutdownTimeout,
        CodexVersionCompatibility versionCompatibility)
    {
        return new CodexLocalAppServerConfiguration(
            executablePath,
            source,
            workingDirectory,
            childEnvironment,
            startupTimeout,
            shutdownTimeout,
            versionCompatibility);
    }

    private static IEnumerable<string> EnumerateNpmCandidates(string? applicationDataDirectory)
    {
        var applicationData = NormalizeOptionalPath(applicationDataDirectory);
        if (applicationData is null)
        {
            yield break;
        }

        string[] candidates;
        try
        {
            var npmRoot = Path.Combine(applicationData, "npm", "node_modules", "@openai");
            candidates = new[]
            {
                Path.Combine(
                    npmRoot,
                    "codex",
                    "node_modules",
                    "@openai",
                    "codex-win32-x64",
                    "vendor",
                    "x86_64-pc-windows-msvc",
                    "bin",
                    "codex.exe"),
                Path.Combine(
                    npmRoot,
                    "codex-win32-x64",
                    "vendor",
                    "x86_64-pc-windows-msvc",
                    "bin",
                    "codex.exe"),
                Path.Combine(
                    npmRoot,
                    "codex",
                    "vendor",
                    "x86_64-pc-windows-msvc",
                    "bin",
                    "codex.exe"),
            };
        }
        catch (Exception exception) when (IsPathOrFileSystemException(exception))
        {
            yield break;
        }

        foreach (var candidate in candidates)
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> EnumeratePathCandidates(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (var rawDirectory in pathValue.Split(
                     new[] { Path.PathSeparator },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = NormalizeOptionalPath(rawDirectory);
            if (directory is null || !LooksLikeAbsoluteLocalWindowsPath(directory))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(directory, "codex.exe");
            }
            catch (Exception exception) when (IsPathOrFileSystemException(exception))
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static string ValidateConfiguredExecutable(string configuredPath)
    {
        try
        {
            if (TryValidateExecutable(configuredPath, out var executablePath))
            {
                return executablePath;
            }
        }
        catch (Exception exception) when (IsPathOrFileSystemException(exception))
        {
        }

        throw Failure(CodexLocalConfigurationFailure.InvalidConfiguredExecutable);
    }

    private static bool TryValidateDiscoveredExecutable(string candidate, out string executablePath)
    {
        try
        {
            return TryValidateExecutable(candidate, out executablePath);
        }
        catch (Exception exception) when (IsPathOrFileSystemException(exception))
        {
            executablePath = string.Empty;
            return false;
        }
    }

    private static bool TryValidateExecutable(string candidate, out string executablePath)
    {
        executablePath = string.Empty;
        var normalized = NormalizeOptionalPath(candidate);
        if (normalized is null || !LooksLikeAbsoluteLocalWindowsPath(normalized))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(normalized);
        if (!LooksLikeAbsoluteLocalWindowsPath(fullPath)
            || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath)
            || ContainsReparsePoint(fullPath)
            || !IsFixedLocalDrive(fullPath))
        {
            return false;
        }

        executablePath = fullPath;
        return true;
    }

    private static string ValidateWorkingDirectory(string value)
    {
        var normalized = NormalizeOptionalPath(value);
        if (normalized is null || !LooksLikeAbsoluteLocalWindowsPath(normalized))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidWorkingDirectory);
        }

        try
        {
            var fullPath = Path.GetFullPath(normalized);
            if (!LooksLikeAbsoluteLocalWindowsPath(fullPath)
                || !Directory.Exists(fullPath)
                || ContainsReparsePoint(fullPath)
                || !IsFixedLocalDrive(fullPath))
            {
                throw Failure(CodexLocalConfigurationFailure.InvalidWorkingDirectory);
            }

            return fullPath;
        }
        catch (CodexLocalConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (IsPathOrFileSystemException(exception))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidWorkingDirectory);
        }
    }

    private static string ValidateTemporaryDirectory(string value)
    {
        var normalized = NormalizeOptionalPath(value);
        if (normalized is null || !LooksLikeAbsoluteLocalWindowsPath(normalized))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidTemporaryDirectory);
        }

        try
        {
            var fullPath = Path.GetFullPath(normalized);
            if (!LooksLikeAbsoluteLocalWindowsPath(fullPath)
                || !Directory.Exists(fullPath)
                || ContainsReparsePoint(fullPath)
                || !IsFixedLocalDrive(fullPath))
            {
                throw Failure(CodexLocalConfigurationFailure.InvalidTemporaryDirectory);
            }

            return fullPath;
        }
        catch (CodexLocalConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (IsPathOrFileSystemException(exception))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidTemporaryDirectory);
        }
    }

    private static TimeSpan ValidateTimeout(
        TimeSpan timeout,
        CodexLocalConfigurationFailure failure)
    {
        if (timeout <= TimeSpan.Zero || timeout > CodexLocalAppServerConfiguration.MaximumTimeout)
        {
            throw Failure(failure);
        }

        return timeout;
    }

    private static CodexSessionIsolation? ValidateSessionIsolation(
        CodexLocalAppServerConfigurationRequest request)
    {
        var hasHome = !string.IsNullOrWhiteSpace(request.CodexHomeDirectory);
        var hasSqliteHome = !string.IsNullOrWhiteSpace(request.CodexSqliteHomeDirectory);
        var hasAccessToken = !string.IsNullOrWhiteSpace(request.CodexAccessToken);
        if (!hasHome && !hasSqliteHome && !hasAccessToken)
        {
            return null;
        }

        if (!hasHome || !hasSqliteHome || !hasAccessToken)
        {
            throw Failure(CodexLocalConfigurationFailure.IncompleteSessionIsolation);
        }

        var codexHome = ValidateSessionIsolationDirectory(request.CodexHomeDirectory);
        var codexSqliteHome = ValidateSessionIsolationDirectory(request.CodexSqliteHomeDirectory);
        if (string.Equals(codexHome, codexSqliteHome, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidSessionIsolationDirectory);
        }

        var accessToken = request.CodexAccessToken!;
        if (!IsValidSessionAccessToken(accessToken))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidSessionIsolationToken);
        }

        return new CodexSessionIsolation(codexHome, codexSqliteHome, accessToken);
    }

    private static string ValidateSessionIsolationDirectory(string? value)
    {
        var normalized = NormalizeOptionalPath(value);
        if (normalized is null || !LooksLikeAbsoluteLocalWindowsPath(normalized))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidSessionIsolationDirectory);
        }

        try
        {
            var fullPath = Path.GetFullPath(normalized);
            if (!LooksLikeAbsoluteLocalWindowsPath(fullPath)
                || !Directory.Exists(fullPath)
                || ContainsReparsePoint(fullPath)
                || !IsFixedLocalDrive(fullPath))
            {
                throw Failure(CodexLocalConfigurationFailure.InvalidSessionIsolationDirectory);
            }

            return fullPath;
        }
        catch (CodexLocalConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (IsPathOrFileSystemException(exception))
        {
            throw Failure(CodexLocalConfigurationFailure.InvalidSessionIsolationDirectory);
        }
    }

    private static bool IsValidSessionAccessToken(string value)
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

    private static bool LooksLikeAbsoluteLocalWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path)
            || path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = path.StartsWith("\\\\?\\", StringComparison.Ordinal)
            ? path.Substring(4)
            : path;
        return candidate.Length >= 3
            && char.IsLetter(candidate[0])
            && candidate[1] == ':'
            && (candidate[2] == '\\' || candidate[2] == '/');
    }

    private static bool IsFixedLocalDrive(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(root)
            && new DriveInfo(root).DriveType == DriveType.Fixed;
    }

    private static bool ContainsReparsePoint(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }

        var current = root;
        var relative = fullPath.Substring(root.Length);
        foreach (var segment in relative.Split(
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

    private static string? NormalizeOptionalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Trim('"');
    }

    private static bool IsPathException(Exception exception)
    {
        return exception is ArgumentException
            or NotSupportedException
            or PathTooLongException;
    }

    private static bool IsPathOrFileSystemException(Exception exception)
    {
        return IsPathException(exception)
            || exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException;
    }

    private static CodexLocalConfigurationException Failure(CodexLocalConfigurationFailure failure)
        => new(failure);
}

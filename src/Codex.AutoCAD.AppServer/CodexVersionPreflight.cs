using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Codex.AutoCAD.AppServer;

public enum CodexVersionPreflightFailure
{
    ProcessStartFailed,
    TimedOut,
    Cancelled,
    TerminationFailed,
    ProcessExitedWithError,
    VersionOutputTooLarge,
    InvalidVersionOutput,
    UnsupportedVersion,
    ExecutableIdentityUnavailable,
    ExecutableIdentityChanged,
}

public readonly record struct CodexSemanticVersion(int Major, int Minor, int Patch)
    : IComparable<CodexSemanticVersion>
{
    public int CompareTo(CodexSemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString()
        => Major.ToString(CultureInfo.InvariantCulture)
            + "."
            + Minor.ToString(CultureInfo.InvariantCulture)
            + "."
            + Patch.ToString(CultureInfo.InvariantCulture);
}

public sealed class CodexVersionCompatibility
{
    public static readonly CodexVersionCompatibility Default = new(
        new CodexSemanticVersion(0, 144, 4),
        new CodexSemanticVersion(0, 145, 0));

    public CodexVersionCompatibility(
        CodexSemanticVersion minimumInclusive,
        CodexSemanticVersion maximumExclusive)
    {
        if (minimumInclusive.CompareTo(maximumExclusive) >= 0)
        {
            throw new ArgumentException("Codex version compatibility range is invalid.");
        }

        MinimumInclusive = minimumInclusive;
        MaximumExclusive = maximumExclusive;
    }

    public CodexSemanticVersion MinimumInclusive { get; }

    public CodexSemanticVersion MaximumExclusive { get; }

    public bool IsSupported(CodexSemanticVersion version)
        => version.CompareTo(MinimumInclusive) >= 0
            && version.CompareTo(MaximumExclusive) < 0;

    public override string ToString()
        => ">=" + MinimumInclusive + " <" + MaximumExclusive;
}

public sealed record CodexVersionPreflightResult(
    CodexSemanticVersion Version,
    CodexVersionCompatibility Compatibility);

public sealed class CodexVersionPreflightException : AppServerException
{
    public CodexVersionPreflightException(
        CodexVersionPreflightFailure failure,
        string message)
        : base(message)
    {
        Failure = failure;
    }

    public CodexVersionPreflightFailure Failure { get; }
}

/// <summary>
/// Owns the executable identity lease proven by <c>codex --version</c>. Keep this object alive
/// until the associated App Server client or runtime has stopped.
/// </summary>
public sealed class CodexVerifiedLaunch : IDisposable
{
    private CodexExecutableLease? _lease;
    private readonly CodexLocalAppServerConfiguration _configuration;

    internal CodexVerifiedLaunch(
        CodexLocalAppServerConfiguration configuration,
        CodexExecutableLease lease,
        CodexVersionPreflightResult version)
    {
        _configuration = configuration;
        _lease = lease;
        Version = version;
    }

    public CodexVersionPreflightResult Version { get; }

    public AppServerClientOptions CreateClientOptions()
    {
        var lease = _lease
            ?? throw new ObjectDisposedException(nameof(CodexVerifiedLaunch));
        lease.ValidateCurrentPath(_configuration.CodexExecutablePath);
        return _configuration.CreateClientOptions(lease);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

public static class CodexVersionPreflight
{
    public const int MaximumVersionOutputBytes = 4 * 1024;
    public static readonly TimeSpan DefaultTerminationTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex VersionLine = new(
        @"^\s*codex(?:-cli)?\s+v?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:\+[0-9A-Za-z.-]+)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<CodexVerifiedLaunch> VerifyAsync(
        CodexLocalAppServerConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var lease = CodexExecutableLease.Acquire(configuration.CodexExecutablePath);
        try
        {
            var options = configuration.CreateClientOptions(lease);
            var version = await VerifyProcessAsync(
                    options,
                    configuration.VersionCompatibility,
                    configuration.StartupTimeout,
                    DefaultTerminationTimeout,
                    ProcessCodexVersionProcess.Start,
                    cancellationToken)
                .ConfigureAwait(false);
            return new CodexVerifiedLaunch(configuration, lease, version);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static async Task<CodexVersionPreflightResult> VerifyProcessAsync(
        AppServerClientOptions options,
        CodexVersionCompatibility compatibility,
        TimeSpan timeout,
        TimeSpan terminationTimeout,
        Func<ProcessStartInfo, ICodexVersionProcess> processFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(compatibility);
        ArgumentNullException.ThrowIfNull(processFactory);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTimeout(timeout, nameof(timeout));
        ValidateTimeout(terminationTimeout, nameof(terminationTimeout));
        options.ExecutableLease?.ValidateCurrentPath(options.CodexExecutablePath);

        ICodexVersionProcess process;
        try
        {
            process = processFactory(CreateStartInfo(options));
        }
        catch (CodexVersionPreflightException)
        {
            throw;
        }
        catch (Exception exception) when (IsProcessStartException(exception))
        {
            throw Failure(
                CodexVersionPreflightFailure.ProcessStartFailed,
                "The local Codex version preflight could not start.");
        }

        using (process)
        {
            try
            {
                process.CloseStandardInput();
            }
            catch (InvalidOperationException)
            {
            }

            var stdoutTask = CaptureStandardOutputAsync(process.StandardOutput);
            var stderrTask = AppServerStandardErrorCapture.DrainAsync(
                process.StandardError,
                options.MaximumStandardErrorBytes,
                CancellationToken.None);

            CodexVersionPreflightFailure? terminalFailure = null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                terminalFailure = cancellationToken.IsCancellationRequested
                    ? CodexVersionPreflightFailure.Cancelled
                    : CodexVersionPreflightFailure.TimedOut;
                if (!await TryTerminateAsync(process, terminationTimeout).ConfigureAwait(false))
                {
                    throw Failure(
                        CodexVersionPreflightFailure.TerminationFailed,
                        "The local Codex version preflight process could not be terminated safely.");
                }
            }

            VersionOutputCapture output;
            try
            {
                output = await stdoutTask.WaitAsync(terminationTimeout).ConfigureAwait(false);
                _ = await stderrTask.WaitAsync(terminationTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw Failure(
                    CodexVersionPreflightFailure.TerminationFailed,
                    "The local Codex version preflight streams did not close after termination.");
            }
            catch (Exception exception) when (exception is IOException
                                              or InvalidOperationException
                                              or ObjectDisposedException)
            {
                throw Failure(
                    CodexVersionPreflightFailure.InvalidVersionOutput,
                    "The local Codex version preflight returned unusable output.");
            }

            using (output)
            {
                if (terminalFailure is not null)
                {
                    throw Failure(
                        terminalFailure.Value,
                        terminalFailure == CodexVersionPreflightFailure.TimedOut
                            ? "The local Codex version preflight timed out."
                            : "The local Codex version preflight was cancelled.");
                }

                if (process.ExitCode != 0)
                {
                    throw Failure(
                        CodexVersionPreflightFailure.ProcessExitedWithError,
                        "The local Codex version preflight exited with an error.");
                }

                if (output.Truncated)
                {
                    throw Failure(
                        CodexVersionPreflightFailure.VersionOutputTooLarge,
                        "The local Codex version preflight output exceeded its limit.");
                }

                CodexSemanticVersion version;
                try
                {
                    if (!TryParseVersion(output.GetText(), out version))
                    {
                        throw Failure(
                            CodexVersionPreflightFailure.InvalidVersionOutput,
                            "The local Codex version preflight returned an unsupported version format.");
                    }
                }
                catch (DecoderFallbackException)
                {
                    throw Failure(
                        CodexVersionPreflightFailure.InvalidVersionOutput,
                        "The local Codex version preflight returned non-text version output.");
                }

                if (!compatibility.IsSupported(version))
                {
                    throw Failure(
                        CodexVersionPreflightFailure.UnsupportedVersion,
                        "The installed Codex version is outside this product's supported compatibility range.");
                }

                return new CodexVersionPreflightResult(version, compatibility);
            }
        }
    }

    internal static bool TryParseVersion(string value, out CodexSemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 1)
        {
            return false;
        }

        var match = VersionLine.Match(lines[0]);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new CodexSemanticVersion(major, minor, patch);
        return true;
    }

    private static ProcessStartInfo CreateStartInfo(AppServerClientOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.CodexExecutablePath,
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        startInfo.ArgumentList.Add("--version");
        if (!options.InheritParentEnvironment)
        {
            startInfo.Environment.Clear();
        }

        foreach (var (name, value) in options.Environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        return startInfo;
    }

    private static async Task<bool> TryTerminateAsync(
        ICodexVersionProcess process,
        TimeSpan terminationTimeout)
    {
        try
        {
            if (!process.HasExited)
            {
                process.KillProcessTree();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or NotSupportedException
                                          or Win32Exception)
        {
            return process.HasExited;
        }

        using var deadline = new CancellationTokenSource(terminationTimeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return process.HasExited;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return process.HasExited;
        }
    }

    private static async Task<VersionOutputCapture> CaptureStandardOutputAsync(Stream stream)
    {
        var retained = new MemoryStream(MaximumVersionOutputBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024);
        var truncated = false;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var remaining = MaximumVersionOutputBytes - (int)retained.Length;
                if (remaining > 0)
                {
                    retained.Write(buffer, 0, Math.Min(read, remaining));
                }

                if (read > remaining)
                {
                    truncated = true;
                }
            }

            return new VersionOutputCapture(retained.ToArray(), truncated);
        }
        finally
        {
            retained.Dispose();
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero || timeout > CodexLocalAppServerConfiguration.MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static bool IsProcessStartException(Exception exception)
        => exception is ArgumentException
            or InvalidOperationException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or Win32Exception;

    private static CodexVersionPreflightException Failure(
        CodexVersionPreflightFailure failure,
        string message)
        => new(failure, message);

    private sealed class VersionOutputCapture : IDisposable
    {
        private byte[]? _bytes;

        internal VersionOutputCapture(byte[] bytes, bool truncated)
        {
            _bytes = bytes;
            Truncated = truncated;
        }

        internal bool Truncated { get; }

        internal string GetText()
        {
            var bytes = _bytes ?? throw new ObjectDisposedException(nameof(VersionOutputCapture));
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        public void Dispose()
        {
            var bytes = Interlocked.Exchange(ref _bytes, null);
            if (bytes is not null)
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
    }
}

internal interface ICodexVersionProcess : IDisposable
{
    Stream StandardOutput { get; }

    Stream StandardError { get; }

    int ExitCode { get; }

    bool HasExited { get; }

    void CloseStandardInput();

    void KillProcessTree();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class ProcessCodexVersionProcess : ICodexVersionProcess
{
    private readonly Process _process;

    private ProcessCodexVersionProcess(Process process)
    {
        _process = process;
    }

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public Stream StandardError => _process.StandardError.BaseStream;

    public int ExitCode => _process.ExitCode;

    public bool HasExited => _process.HasExited;

    internal static ICodexVersionProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Codex version process did not start.");
            }

            return new ProcessCodexVersionProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public void CloseStandardInput()
    {
        _process.StandardInput.Close();
    }

    public void KillProcessTree()
    {
        _process.Kill(entireProcessTree: true);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return _process.WaitForExitAsync(cancellationToken);
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}

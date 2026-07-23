using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Codex.AutoCAD.AppServer;

/// <summary>Stable outcomes for the bounded local Codex version preflight.</summary>
public enum CodexVersionPreflightFailure
{
    ProcessStartFailed,
    TimedOut,
    ProcessExitedWithError,
    VersionOutputTooLarge,
    InvalidVersionOutput,
    UnsupportedVersion,
}

/// <summary>Three-part Codex CLI version used only for the supported local compatibility window.</summary>
public readonly record struct CodexSemanticVersion(int Major, int Minor, int Patch)
    : IComparable<CodexSemanticVersion>
{
    public int CompareTo(CodexSemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
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

/// <summary>
/// Immutable compatibility range intentionally pinned to the App Server protocol verified by this
/// product. A new Codex minor version requires an explicit protocol review and a new product range.
/// </summary>
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

/// <summary>Normalized, non-sensitive result of a successful Codex CLI version preflight.</summary>
public sealed record CodexVersionPreflightResult(
    CodexSemanticVersion Version,
    CodexVersionCompatibility Compatibility);

/// <summary>Path-free failure exposed by local Codex version preflight.</summary>
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
/// Runs the documented non-interactive <c>codex --version</c> command under the same constrained
/// environment as the App Server child. Its stdout is bounded and never exposed to callers or logs.
/// </summary>
public static class CodexVersionPreflight
{
    public const int MaximumVersionOutputBytes = 4 * 1024;

    private static readonly Regex VersionLine = new(
        @"^\s*codex(?:-cli)?\s+v?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:\+[0-9A-Za-z.-]+)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<CodexVersionPreflightResult> VerifyAsync(
        AppServerClientOptions options,
        CodexVersionCompatibility compatibility,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(compatibility);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero || timeout > CodexLocalAppServerConfiguration.MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(options),
        };

        try
        {
            if (!process.Start())
            {
                throw Failure(
                    CodexVersionPreflightFailure.ProcessStartFailed,
                    "The local Codex version preflight could not start.");
            }
        }
        catch (CodexVersionPreflightException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or IOException
                                          or NotSupportedException
                                          or UnauthorizedAccessException
                                          or System.ComponentModel.Win32Exception)
        {
            throw Failure(
                CodexVersionPreflightFailure.ProcessStartFailed,
                "The local Codex version preflight could not start.");
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
            // The child may already have exited after a successful version print.
        }

        var stdoutTask = CaptureStandardOutputAsync(process.StandardOutput.BaseStream);
        var stderrTask = AppServerStandardErrorCapture.DrainAsync(
            process.StandardError.BaseStream,
            options.MaximumStandardErrorBytes,
            CancellationToken.None);
        var timedOut = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            await TerminateAsync(process).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(process).ConfigureAwait(false);
            throw;
        }

        VersionOutputCapture output;
        try
        {
            output = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false);
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
            if (timedOut)
            {
                throw Failure(
                    CodexVersionPreflightFailure.TimedOut,
                    "The local Codex version preflight timed out.");
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

    private static async Task TerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the wait.
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

    private static CodexVersionPreflightException Failure(
        CodexVersionPreflightFailure failure,
        string message)
        => new(failure, message);

    private sealed class VersionOutputCapture : IDisposable
    {
        private byte[]? _bytes;

        public VersionOutputCapture(byte[] bytes, bool truncated)
        {
            _bytes = bytes;
            Truncated = truncated;
        }

        public bool Truncated { get; }

        public string GetText()
        {
            var bytes = _bytes ?? throw new ObjectDisposedException(nameof(VersionOutputCapture));
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
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

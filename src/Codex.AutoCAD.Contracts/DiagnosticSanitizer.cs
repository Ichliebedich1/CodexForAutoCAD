using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

namespace Codex.AutoCAD.Contracts;

public enum DiagnosticDataClassification
{
    General = 0,
    Exception = 1,
    StandardError = 2,
    Configuration = 3,
    Environment = 4,
    RemoteError = 5,
}

[Flags]
public enum DiagnosticRedactionKinds
{
    None = 0,
    Token = 1 << 0,
    Path = 1 << 1,
    Uri = 1 << 2,
    Identity = 1 << 3,
    ControlCharacter = 1 << 4,
    Truncated = 1 << 5,
    Fallback = 1 << 6,
}

public sealed class DiagnosticSanitizationResult
{
    internal DiagnosticSanitizationResult(
        DiagnosticDataClassification classification,
        string safeText,
        DiagnosticRedactionKinds redactions)
    {
        Classification = classification;
        SafeText = safeText;
        Redactions = redactions;
    }

    public DiagnosticDataClassification Classification { get; }

    public string SafeText { get; }

    public DiagnosticRedactionKinds Redactions { get; }

    public bool Truncated
        => (Redactions & DiagnosticRedactionKinds.Truncated) != 0;
}

/// <summary>
/// Removes common local identity and credential material from diagnostic text after the caller
/// classifies the source. The result is suitable for bounded user-visible diagnostics, not for
/// reconstructing or persisting the original value.
/// </summary>
public static class DiagnosticSanitizer
{
    public const int MaximumInputCharacters = 4096;
    public const int MaximumOutputCharacters = 512;
    public const int MaximumExceptionDepth = 8;
    public const int MaximumExceptionNodes = 16;

    private const string RedactedDiagnostic = "[redacted-diagnostic]";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex UriPattern = CreatePattern(
        @"\b[a-z][a-z0-9+\-.]{0,31}://[^\s<>{}\[\]\""']+");
    private static readonly Regex BearerPattern = CreatePattern(
        @"\bBearer[ \t]+[A-Za-z0-9._~+/=\-]{1,4096}");
    private static readonly Regex SecretAssignmentPattern = CreatePattern(
        @"(?<![A-Za-z0-9_])(?:[\""'])?(?:access[_-]?token|refresh[_-]?token|api[_-]?key|authorization|password|passwd|secret|credential|token|client[_-]?secret|private[_-]?key|connection[_-]?string|sas[_-]?token)(?:[\""'])?(?![A-Za-z0-9_])\s*[:=]\s*(?:\""[^\""\r\n]*\""|'[^'\r\n]*'|[^\s,;}\]]+)");
    private static readonly Regex EscapedJsonSecretAssignmentPattern = CreatePattern(
        @"(?<![A-Za-z0-9_])\\[\""'](?:access[_-]?token|refresh[_-]?token|api[_-]?key|authorization|password|passwd|secret|credential|token|client[_-]?secret|private[_-]?key|connection[_-]?string|sas[_-]?token)\\[\""']\s*[:=]\s*\\[\""'][^\""' \r\n]*\\[\""']");
    private static readonly Regex QuotedDevicePathPattern = CreatePattern(
        @"(?<quote>[\""'])(?:\\\\[?.]\\|\\\?\?\\)[^\r\n\t\""'<>|?*]+\k<quote>");
    private static readonly Regex DevicePathPattern = CreatePattern(
        @"(?:\\\\[?.]\\|\\\?\?\\)[^\s\r\n\t\""'<>|?*]+");
    private static readonly Regex QuotedWindowsPathPattern = CreatePattern(
        @"(?<quote>[\""'])(?:[A-Za-z]:[\\/]|\\\\)[^\r\n\t\""'<>|?*]+\k<quote>");
    private static readonly Regex WindowsPathPattern = CreatePattern(
        @"(?:[A-Za-z]:[\\/]|\\\\)[^\s\r\n\t\""'<>|?*]+");
    private static readonly Regex DomainIdentityPattern = CreatePattern(
        @"(?<![A-Za-z0-9._-])[A-Za-z0-9._-]{1,64}\\[A-Za-z0-9._-]{1,64}(?![A-Za-z0-9._-])");
    private static readonly Regex EmailIdentityPattern = CreatePattern(
        @"(?<![A-Z0-9._%+\-])[A-Z0-9._%+\-]{1,64}@[A-Z0-9.\-]{1,190}\.[A-Z]{2,63}(?![A-Z0-9.\-])");
    private static readonly Regex RepeatedWhitespacePattern = new(
        @"[ \t\r\n]+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    public static DiagnosticSanitizationResult SanitizeText(
        DiagnosticDataClassification classification,
        string? value)
    {
        if (!Enum.IsDefined(typeof(DiagnosticDataClassification), classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        if (string.IsNullOrEmpty(value))
        {
            return new DiagnosticSanitizationResult(
                classification,
                string.Empty,
                DiagnosticRedactionKinds.None);
        }

        var redactions = DiagnosticRedactionKinds.None;
        var bounded = value!;
        if (bounded.Length > MaximumInputCharacters)
        {
            bounded = bounded.Substring(0, MaximumInputCharacters);
            redactions |= DiagnosticRedactionKinds.Truncated;
        }

        try
        {
            var normalized = NormalizeCharacters(bounded, ref redactions);
            normalized = Replace(
                UriPattern,
                normalized,
                "[redacted-uri]",
                DiagnosticRedactionKinds.Uri,
                ref redactions);
            normalized = Replace(
                BearerPattern,
                normalized,
                "[redacted-token]",
                DiagnosticRedactionKinds.Token,
                ref redactions);
            normalized = Replace(
                SecretAssignmentPattern,
                normalized,
                "[redacted-token]",
                DiagnosticRedactionKinds.Token,
                ref redactions);
            normalized = Replace(
                EscapedJsonSecretAssignmentPattern,
                normalized,
                "[redacted-token]",
                DiagnosticRedactionKinds.Token,
                ref redactions);
            normalized = Replace(
                QuotedDevicePathPattern,
                normalized,
                "[redacted-path]",
                DiagnosticRedactionKinds.Path,
                ref redactions);
            normalized = Replace(
                DevicePathPattern,
                normalized,
                "[redacted-path]",
                DiagnosticRedactionKinds.Path,
                ref redactions);
            normalized = Replace(
                QuotedWindowsPathPattern,
                normalized,
                "[redacted-path]",
                DiagnosticRedactionKinds.Path,
                ref redactions);
            normalized = Replace(
                WindowsPathPattern,
                normalized,
                "[redacted-path]",
                DiagnosticRedactionKinds.Path,
                ref redactions);
            normalized = Replace(
                DomainIdentityPattern,
                normalized,
                "[redacted-identity]",
                DiagnosticRedactionKinds.Identity,
                ref redactions);
            normalized = Replace(
                EmailIdentityPattern,
                normalized,
                "[redacted-identity]",
                DiagnosticRedactionKinds.Identity,
                ref redactions);
            normalized = RepeatedWhitespacePattern.Replace(normalized, " ").Trim();
            if (normalized.Length > MaximumOutputCharacters)
            {
                normalized = normalized.Substring(0, MaximumOutputCharacters - 3) + "...";
                redactions |= DiagnosticRedactionKinds.Truncated;
            }

            return new DiagnosticSanitizationResult(
                classification,
                normalized,
                redactions);
        }
        catch (RegexMatchTimeoutException)
        {
            return new DiagnosticSanitizationResult(
                classification,
                RedactedDiagnostic,
                redactions | DiagnosticRedactionKinds.Fallback);
        }
    }

    /// <summary>
    /// Produces one bounded diagnostic from an exception graph without retaining exception objects,
    /// stack traces, or data dictionaries. Aggregate and inner exceptions are traversed by reference
    /// with fixed depth and node limits.
    /// </summary>
    public static DiagnosticSanitizationResult SanitizeException(
        DiagnosticDataClassification classification,
        Exception? exception)
    {
        if (!Enum.IsDefined(typeof(DiagnosticDataClassification), classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        if (exception is null)
        {
            return new DiagnosticSanitizationResult(
                classification,
                string.Empty,
                DiagnosticRedactionKinds.None);
        }

        var pendingExceptions = new Queue<Exception>();
        var pendingDepths = new Queue<int>();
        var visited = new HashSet<Exception>(ExceptionReferenceComparer.Instance);
        var safeText = new StringBuilder(MaximumOutputCharacters);
        var redactions = DiagnosticRedactionKinds.None;
        var processedNodes = 0;
        var graphWasTruncated = false;

        pendingExceptions.Enqueue(exception);
        pendingDepths.Enqueue(0);
        while (pendingExceptions.Count != 0)
        {
            var current = pendingExceptions.Dequeue();
            var depth = pendingDepths.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            if (depth > MaximumExceptionDepth || processedNodes >= MaximumExceptionNodes)
            {
                graphWasTruncated = true;
                continue;
            }

            processedNodes++;
            DiagnosticSanitizationResult currentResult;
            try
            {
                currentResult = SanitizeText(classification, current.Message);
            }
            catch (Exception)
            {
                currentResult = new DiagnosticSanitizationResult(
                    classification,
                    RedactedDiagnostic,
                    DiagnosticRedactionKinds.Fallback);
            }

            redactions |= currentResult.Redactions;
            AppendExceptionDiagnostic(safeText, currentResult.SafeText, ref redactions);

            if (depth == MaximumExceptionDepth)
            {
                if (HasInnerExceptions(current))
                {
                    graphWasTruncated = true;
                }

                continue;
            }

            if (current is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    if (processedNodes + pendingExceptions.Count >= MaximumExceptionNodes)
                    {
                        graphWasTruncated = true;
                        break;
                    }

                    pendingExceptions.Enqueue(innerException);
                    pendingDepths.Enqueue(depth + 1);
                }
            }
            else if (current.InnerException is not null)
            {
                if (processedNodes + pendingExceptions.Count >= MaximumExceptionNodes)
                {
                    graphWasTruncated = true;
                }
                else
                {
                    pendingExceptions.Enqueue(current.InnerException);
                    pendingDepths.Enqueue(depth + 1);
                }
            }
        }

        if (graphWasTruncated)
        {
            redactions |= DiagnosticRedactionKinds.Truncated;
        }

        return new DiagnosticSanitizationResult(
            classification,
            safeText.ToString(),
            redactions);
    }

    private static Regex CreatePattern(string pattern)
        => new(
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            RegexTimeout);

    private static string Replace(
        Regex pattern,
        string value,
        string replacement,
        DiagnosticRedactionKinds kind,
        ref DiagnosticRedactionKinds redactions)
    {
        if (!pattern.IsMatch(value))
        {
            return value;
        }

        redactions |= kind;
        return pattern.Replace(value, replacement);
    }

    private static string NormalizeCharacters(
        string value,
        ref DiagnosticRedactionKinds redactions)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\r' or '\n' or '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (char.IsControl(character) || IsDirectionalFormatting(character))
            {
                redactions |= DiagnosticRedactionKinds.ControlCharacter;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsDirectionalFormatting(char value)
        => value is '\u061c' or '\u200e' or '\u200f'
            or >= '\u202a' and <= '\u202e'
            or >= '\u2066' and <= '\u2069';

    private static void AppendExceptionDiagnostic(
        StringBuilder builder,
        string value,
        ref DiagnosticRedactionKinds redactions)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        const string separator = " | ";
        var separatorLength = builder.Length == 0 ? 0 : separator.Length;
        var remaining = MaximumOutputCharacters - builder.Length - separatorLength;
        if (remaining <= 0)
        {
            redactions |= DiagnosticRedactionKinds.Truncated;
            return;
        }

        if (separatorLength != 0)
        {
            builder.Append(separator);
        }

        if (value.Length <= remaining)
        {
            builder.Append(value);
            return;
        }

        if (remaining <= 3)
        {
            builder.Append(value, 0, remaining);
        }
        else
        {
            builder.Append(value, 0, remaining - 3);
            builder.Append("...");
        }

        redactions |= DiagnosticRedactionKinds.Truncated;
    }

    private static bool HasInnerExceptions(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Count != 0;
        }

        return exception.InnerException is not null;
    }

    private sealed class ExceptionReferenceComparer : IEqualityComparer<Exception>
    {
        public static readonly ExceptionReferenceComparer Instance = new();

        public bool Equals(Exception? left, Exception? right)
            => ReferenceEquals(left, right);

        public int GetHashCode(Exception value)
            => RuntimeHelpers.GetHashCode(value);
    }
}

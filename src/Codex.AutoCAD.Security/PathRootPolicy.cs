namespace Codex.AutoCAD.Security;

[Flags]
public enum PathAccessKind
{
    None = 0,
    Read = 1,
    Write = 2,
}

public sealed record PathRootRule(string RootPath, PathAccessKind AllowedAccess);

public enum PathPolicyFailureReason
{
    None,
    EmptyPath,
    InvalidPath,
    RelativePath,
    TraversalSegment,
    UncPath,
    DevicePath,
    AlternateDataStream,
    ReservedDeviceName,
    AmbiguousShortName,
    TrailingDotOrSpace,
    OutsideAllowedRoot,
    AccessNotAllowed,
    ProtectedCadFile,
    ReparsePoint,
    InspectionFailed,
}

public sealed record PathPolicyDecision(
    bool Allowed,
    PathPolicyFailureReason Failure,
    string? CanonicalPath,
    string ReasonCode,
    bool MustRevalidateImmediatelyBeforeUse);

/// <summary>
/// Canonical root allow-list for broker file operations. UNC/device paths, traversal, alternate
/// data streams, DOS aliases, reparse points and ordinary writes to CAD binaries fail closed.
/// </summary>
public sealed class PathRootPolicy
{
    private static readonly HashSet<string> ProtectedCadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dwg",
        ".dwt",
        ".dws",
        ".sv$",
        ".bak",
    };

    private readonly IReadOnlyList<NormalizedRootRule> _roots;

    public PathRootPolicy(IEnumerable<PathRootRule> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var normalized = new List<NormalizedRootRule>();
        foreach (var rule in roots)
        {
            ArgumentNullException.ThrowIfNull(rule);

            if (rule.AllowedAccess == PathAccessKind.None
                || (rule.AllowedAccess & ~(PathAccessKind.Read | PathAccessKind.Write)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(roots), "Every root must explicitly allow read and/or write access.");
            }

            var syntaxFailure = InspectRawPath(rule.RootPath);
            if (syntaxFailure != PathPolicyFailureReason.None)
            {
                throw new ArgumentException($"Unsafe root path: {syntaxFailure}.", nameof(roots));
            }

            string canonicalRoot;
            try
            {
                canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.RootPath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ArgumentException("Root path cannot be canonicalized.", nameof(roots), exception);
            }

            normalized.Add(new NormalizedRootRule(canonicalRoot, rule.AllowedAccess));
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException("At least one allowed root is required.", nameof(roots));
        }

        _roots = normalized
            .OrderByDescending(root => root.CanonicalRoot.Length)
            .ToArray();
    }

    public PathPolicyDecision Evaluate(string? candidatePath, PathAccessKind requestedAccess)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return Deny(PathPolicyFailureReason.EmptyPath, null, "PATH_EMPTY");
        }

        if (requestedAccess == PathAccessKind.None
            || (requestedAccess & ~(PathAccessKind.Read | PathAccessKind.Write)) != 0)
        {
            return Deny(PathPolicyFailureReason.AccessNotAllowed, null, "PATH_ACCESS_INVALID");
        }

        var syntaxFailure = InspectRawPath(candidatePath);
        if (syntaxFailure != PathPolicyFailureReason.None)
        {
            return Deny(syntaxFailure, null, $"PATH_{syntaxFailure.ToString().ToUpperInvariant()}");
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(candidatePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Deny(PathPolicyFailureReason.InvalidPath, null, "PATH_CANONICALIZATION_FAILED");
        }

        NormalizedRootRule? matchingRoot = null;
        var wasInsideAnyRoot = false;

        foreach (var root in _roots)
        {
            if (!IsWithinRoot(canonicalPath, root.CanonicalRoot))
            {
                continue;
            }

            wasInsideAnyRoot = true;
            if ((root.AllowedAccess & requestedAccess) == requestedAccess)
            {
                matchingRoot = root;
                break;
            }
        }

        if (matchingRoot is null)
        {
            return wasInsideAnyRoot
                ? Deny(PathPolicyFailureReason.AccessNotAllowed, canonicalPath, "PATH_ACCESS_NOT_ALLOWED")
                : Deny(PathPolicyFailureReason.OutsideAllowedRoot, canonicalPath, "PATH_OUTSIDE_ALLOWED_ROOT");
        }

        if ((requestedAccess & PathAccessKind.Write) != 0
            && ProtectedCadExtensions.Contains(Path.GetExtension(canonicalPath)))
        {
            return Deny(PathPolicyFailureReason.ProtectedCadFile, canonicalPath, "PATH_PROTECTED_CAD_WRITE");
        }

        var inspection = InspectExistingPathComponents(canonicalPath);
        if (inspection == ExistingPathInspection.ReparsePoint)
        {
            return Deny(PathPolicyFailureReason.ReparsePoint, canonicalPath, "PATH_REPARSE_POINT");
        }

        if (inspection == ExistingPathInspection.Failed)
        {
            return Deny(PathPolicyFailureReason.InspectionFailed, canonicalPath, "PATH_INSPECTION_FAILED");
        }

        return new PathPolicyDecision(
            Allowed: true,
            PathPolicyFailureReason.None,
            canonicalPath,
            "PATH_ALLOWED",
            MustRevalidateImmediatelyBeforeUse: true);
    }

    private static PathPolicyFailureReason InspectRawPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
        {
            return PathPolicyFailureReason.InvalidPath;
        }

        var normalizedSeparators = path.Replace('/', '\\');
        if (normalizedSeparators.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || normalizedSeparators.StartsWith("\\\\.\\", StringComparison.Ordinal)
            || normalizedSeparators.StartsWith("\\??\\", StringComparison.Ordinal))
        {
            return PathPolicyFailureReason.DevicePath;
        }

        if (normalizedSeparators.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return PathPolicyFailureReason.UncPath;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return PathPolicyFailureReason.RelativePath;
        }

        var pathRoot = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(pathRoot))
        {
            return PathPolicyFailureReason.InvalidPath;
        }

        var remainder = normalizedSeparators[pathRoot.Replace('/', '\\').Length..];
        if (remainder.IndexOf(':') >= 0)
        {
            return PathPolicyFailureReason.AlternateDataStream;
        }

        foreach (var segment in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                return PathPolicyFailureReason.TraversalSegment;
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                return PathPolicyFailureReason.TrailingDotOrSpace;
            }

            if (IsReservedDeviceName(segment))
            {
                return PathPolicyFailureReason.ReservedDeviceName;
            }

            if (ContainsDosShortNameMarker(segment))
            {
                return PathPolicyFailureReason.AmbiguousShortName;
            }
        }

        return PathPolicyFailureReason.None;
    }

    private static bool IsReservedDeviceName(string segment)
    {
        var stem = segment.Split('.')[0].TrimEnd(' ', '.');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9')
        {
            return true;
        }

        return false;
    }

    private static bool ContainsDosShortNameMarker(string segment)
    {
        for (var index = 0; index < segment.Length - 1; index++)
        {
            if (segment[index] == '~' && char.IsAsciiDigit(segment[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinRoot(string candidatePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, candidatePath);
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static ExistingPathInspection InspectExistingPathComponents(string canonicalPath)
    {
        try
        {
            var volumeRoot = Path.GetPathRoot(canonicalPath);
            if (string.IsNullOrEmpty(volumeRoot))
            {
                return ExistingPathInspection.Failed;
            }

            var relative = Path.GetRelativePath(volumeRoot, canonicalPath);
            var current = volumeRoot;

            foreach (var segment in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    break;
                }

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return ExistingPathInspection.ReparsePoint;
                }
            }

            return ExistingPathInspection.Safe;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or System.Security.SecurityException
            or ArgumentException
            or NotSupportedException)
        {
            return ExistingPathInspection.Failed;
        }
    }

    private static PathPolicyDecision Deny(
        PathPolicyFailureReason failure,
        string? canonicalPath,
        string reason) =>
        new(
            Allowed: false,
            failure,
            canonicalPath,
            reason,
            MustRevalidateImmediatelyBeforeUse: false);

    private sealed record NormalizedRootRule(string CanonicalRoot, PathAccessKind AllowedAccess);

    private enum ExistingPathInspection
    {
        Safe,
        ReparsePoint,
        Failed,
    }
}

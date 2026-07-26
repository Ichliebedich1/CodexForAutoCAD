using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AgentLauncher;

namespace Codex.AutoCAD.AgentHost;

internal enum AgentHostAuditCatalogStatus
{
    Complete,
    Incomplete,
    Corrupt,
    AnchorMismatch,
}

internal static class AgentHostAuditCatalogReasonCodes
{
    internal const string None = "none";
    internal const string MissingSegment = "missing_segment";
    internal const string MissingAnchor = "missing_anchor";
    internal const string TemporaryAnchor = "temporary_anchor";
    internal const string UnrecognizedArtifact = "unrecognized_artifact";
    internal const string AnchorInvalid = "anchor_invalid";
    internal const string SegmentReadFailed = "segment_read_failed";
    internal const string SegmentSequenceInvalid = "segment_sequence_invalid";
    internal const string ChainInvalid = "chain_invalid";
    internal const string AnchorMismatch = "anchor_mismatch";
    internal const string SessionNotTerminal = "session_not_terminal";
}

internal sealed class AgentHostAuditCatalogEntry
{
    internal string SystemSessionId { get; init; } = string.Empty;

    internal AgentHostAuditCatalogStatus Status { get; init; }

    internal string ReasonCode { get; init; } = AgentHostAuditCatalogReasonCodes.None;

    internal int SegmentCount { get; init; }

    internal long RecordCount { get; init; }

    internal string? AnchorSegmentId { get; init; }

    internal long? AnchorSequence { get; init; }
}

internal sealed class AgentHostAuditCatalogSnapshot
{
    internal IReadOnlyList<AgentHostAuditCatalogEntry> Entries { get; init; }
        = Array.Empty<AgentHostAuditCatalogEntry>();

    internal int IgnoredFileCount { get; init; }

    internal bool EnumerationComplete { get; init; }
}

internal sealed class AgentHostAuditCatalogSessionData
{
    internal string SystemSessionId { get; init; } = string.Empty;

    internal IReadOnlyList<ReadOnlyMemory<byte>> Segments { get; init; }
        = Array.Empty<ReadOnlyMemory<byte>>();

    internal AgentHostAuditAnchor Anchor { get; init; } = new();
}

internal sealed class AgentHostAuditCatalogException : Exception
{
    internal AgentHostAuditCatalogException(string message)
        : base(message)
    {
    }

    internal AgentHostAuditCatalogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Read-only catalog for the protected persistent audit store. It never repairs, deletes,
/// rewrites, or truncates an audit artifact. Ambiguous or invalid evidence is reported as a
/// conservative state for a later controlled recovery/export decision.
/// </summary>
internal static class AgentHostAuditCatalog
{
    private const int MaximumSessions = 4096;
    private const int MaximumFiles = 16384;
    private const long MaximumSegmentBytes = 64L * 1024 * 1024;
    private const long MaximumAnchorBytes = 64L * 1024;
    private const int SessionIdLength = 32;
    private const int SegmentNumberDigits = 6;
    private const int MaximumSegmentNumber = AgentHostAuditFileSegmentStore.AbsoluteMaximumSegments;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions AnchorSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    internal static AgentHostAuditCatalogSnapshot Read(
        string segmentDirectory,
        string anchorDirectory,
        int maximumSessions = MaximumSessions)
    {
        if (maximumSessions is < 1 or > MaximumSessions)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSessions));
        }

        ValidateDirectory(segmentDirectory, nameof(segmentDirectory));
        ValidateDirectory(anchorDirectory, nameof(anchorDirectory));

        var segmentRoot = Path.GetDirectoryName(segmentDirectory)!;
        var anchorRoot = Path.GetDirectoryName(anchorDirectory)!;
        if (!string.Equals(segmentRoot, anchorRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentHostAuditCatalogException(
                "AgentHost audit catalog segment and anchor roots must share one protected root.");
        }
        var currentUserSid = WindowsWorkspaceSecurity.GetCurrentUserSidString();
        WindowsWorkspaceSecurity.VerifyProtectedDirectory(segmentRoot, currentUserSid);
        WindowsWorkspaceSecurity.VerifyProtectedDirectory(segmentDirectory, currentUserSid);
        WindowsWorkspaceSecurity.VerifyProtectedDirectory(anchorDirectory, currentUserSid);

        var sessions = new Dictionary<string, SessionArtifacts>(StringComparer.Ordinal);
        var ignoredFileCount = 0;
        var fileCount = 0;
        EnumerateDirectory(segmentDirectory, isSegmentDirectory: true);
        EnumerateDirectory(anchorDirectory, isSegmentDirectory: false);

        var entries = new List<AgentHostAuditCatalogEntry>(sessions.Count);
        foreach (var pair in sessions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            entries.Add(Classify(pair.Key, pair.Value));
        }

        return new AgentHostAuditCatalogSnapshot
        {
            Entries = entries,
            IgnoredFileCount = ignoredFileCount,
            EnumerationComplete = true,
        };

        void EnumerateDirectory(string directory, bool isSegmentDirectory)
        {
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                throw new AgentHostAuditCatalogException(
                    "AgentHost audit catalog enumeration failed.",
                    exception);
            }

            foreach (var path in paths)
            {
                if (++fileCount > MaximumFiles)
                {
                    throw new AgentHostAuditCatalogException(
                        "AgentHost audit catalog file limit was exceeded.");
                }

                var fileName = Path.GetFileName(path);
                if (TryParseSegmentFileName(fileName, out var sessionId, out var segmentNumber))
                {
                    if (!isSegmentDirectory)
                    {
                        ignoredFileCount++;
                        continue;
                    }

                    var artifacts = GetArtifacts(sessionId);
                    if (!artifacts.Segments.TryAdd(segmentNumber, path))
                    {
                        artifacts.HasUnrecognizedArtifact = true;
                    }

                    continue;
                }

                if (TryParseAnchorFileName(fileName, out sessionId))
                {
                    if (isSegmentDirectory)
                    {
                        ignoredFileCount++;
                        continue;
                    }

                    var artifacts = GetArtifacts(sessionId);
                    if (artifacts.AnchorPath is not null)
                    {
                        artifacts.HasUnrecognizedArtifact = true;
                    }
                    else
                    {
                        artifacts.AnchorPath = path;
                    }

                    continue;
                }

                if (TryParseSessionPrefix(fileName, out sessionId))
                {
                    var artifacts = GetArtifacts(sessionId);
                    if (!isSegmentDirectory
                        && fileName.StartsWith(sessionId + ".anchor.json.tmp-", StringComparison.Ordinal))
                    {
                        artifacts.HasTemporaryAnchor = true;
                    }
                    else
                    {
                        artifacts.HasUnrecognizedArtifact = true;
                    }
                }
                else
                {
                    ignoredFileCount++;
                }
            }
        }

        SessionArtifacts GetArtifacts(string sessionId)
        {
            if (!sessions.TryGetValue(sessionId, out var artifacts))
            {
                if (sessions.Count >= maximumSessions)
                {
                    throw new AgentHostAuditCatalogException(
                        "AgentHost audit catalog session limit was exceeded.");
                }

                artifacts = new SessionArtifacts();
                sessions.Add(sessionId, artifacts);
            }

            return artifacts;
        }
    }

    internal static AgentHostAuditCatalogSessionData ReadCompleteSession(
        string segmentDirectory,
        string anchorDirectory,
        string systemSessionId)
    {
        if (!IsSessionId(systemSessionId))
        {
            throw new AgentHostAuditCatalogException("Audit session id is invalid.");
        }

        var snapshot = Read(segmentDirectory, anchorDirectory);
        var entry = snapshot.Entries.SingleOrDefault(candidate =>
            string.Equals(candidate.SystemSessionId, systemSessionId, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new AgentHostAuditCatalogException("Audit session was not found.");
        }

        if (entry.Status != AgentHostAuditCatalogStatus.Complete)
        {
            throw new AgentHostAuditCatalogException(
                "Audit session is not complete: " + entry.Status.ToString().ToLowerInvariant() + ".");
        }

        var segmentPaths = Directory.EnumerateFiles(
                segmentDirectory,
                systemSessionId + ".segment-*.jsonl",
                SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var segments = new List<ReadOnlyMemory<byte>>(segmentPaths.Length);
        foreach (var path in segmentPaths)
        {
            var info = new FileInfo(path);
            if (info.Length is < 1 or > MaximumSegmentBytes)
            {
                throw new AgentHostAuditCatalogException(
                    "Audit segment size is outside the diagnostic bound.");
            }

            segments.Add(File.ReadAllBytes(path));
        }

        var anchorPath = Path.Combine(anchorDirectory, systemSessionId + ".anchor.json");
        var anchor = ReadAnchor(anchorPath);

        // M4.13：锚点 MAC 校验。使用只加载不创建的入口——在只读分类路径生成密钥会把
        // "该存储从未启用 MAC"悄悄变成"已启用"，反而掩盖既有锚点缺少 MAC 的事实。
        // 密钥存在时，缺失或不匹配的 .mac 一律 fail-closed，删除 sidecar 无法降级。
        using (var chainKey = AgentHostAuditChainKey.TryLoad(
                   Path.GetDirectoryName(anchorDirectory)!))
        {
            AgentHostAuditAnchorMac.Verify(anchorPath, chainKey);
        }

        var verification = AgentHostAuditIntegrity.Verify(segments, anchor);
        if (!string.Equals(verification.FinalSystemSessionId, systemSessionId, StringComparison.Ordinal))
        {
            throw new AgentHostAuditCatalogException(
                "Audit session identity does not match the chain.");
        }

        if (!IsTerminalEvent(verification.FinalEventType))
        {
            throw new AgentHostAuditCatalogException(
                "Audit session does not contain a terminal record.");
        }

        return new AgentHostAuditCatalogSessionData
        {
            SystemSessionId = systemSessionId,
            Segments = segments,
            Anchor = anchor,
        };
    }

    private static AgentHostAuditCatalogEntry Classify(
        string sessionId,
        SessionArtifacts artifacts)
    {
        var segmentCount = artifacts.Segments.Count;
        if (artifacts.HasTemporaryAnchor)
        {
            return Incomplete(sessionId, segmentCount, AgentHostAuditCatalogReasonCodes.TemporaryAnchor);
        }

        if (artifacts.HasUnrecognizedArtifact)
        {
            return Corrupt(sessionId, segmentCount, AgentHostAuditCatalogReasonCodes.UnrecognizedArtifact);
        }

        if (segmentCount == 0)
        {
            return Incomplete(sessionId, 0, AgentHostAuditCatalogReasonCodes.MissingSegment);
        }

        if (artifacts.AnchorPath is null)
        {
            return Incomplete(sessionId, segmentCount, AgentHostAuditCatalogReasonCodes.MissingAnchor);
        }

        AgentHostAuditAnchor anchor;
        try
        {
            anchor = ReadAnchor(artifacts.AnchorPath);
        }
        catch (Exception exception) when (exception is AgentHostAuditCatalogException
            or AgentHostAuditIntegrityException
            or IOException
            or UnauthorizedAccessException
            or JsonException
            or DecoderFallbackException)
        {
            return Corrupt(sessionId, segmentCount, AgentHostAuditCatalogReasonCodes.AnchorInvalid);
        }

        var segmentPaths = new List<ReadOnlyMemory<byte>>(segmentCount);
        foreach (var segment in artifacts.Segments.OrderBy(static pair => pair.Key))
        {
            if (segment.Key != segmentPaths.Count + 1)
            {
                return Corrupt(sessionId, segmentCount, AgentHostAuditCatalogReasonCodes.SegmentSequenceInvalid);
            }

            try
            {
                segmentPaths.Add(File.ReadAllBytes(segment.Value));
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return Corrupt(sessionId, segmentCount, AgentHostAuditCatalogReasonCodes.SegmentReadFailed);
            }
        }

        AgentHostAuditVerificationResult chain;
        try
        {
            chain = AgentHostAuditIntegrity.VerifyChain(segmentPaths);
        }
        catch (AgentHostAuditIntegrityException)
        {
            return Corrupt(sessionId, segmentCount, AgentHostAuditCatalogReasonCodes.ChainInvalid);
        }

        if (!string.Equals(anchor.SystemSessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(anchor.SystemSessionId, chain.FinalSystemSessionId, StringComparison.Ordinal)
            || !string.Equals(anchor.SegmentId, chain.FinalSegmentId, StringComparison.Ordinal)
            || anchor.Sequence != chain.FinalSequence
            || !string.Equals(anchor.RecordHash, chain.FinalRecordHash, StringComparison.Ordinal))
        {
            return new AgentHostAuditCatalogEntry
            {
                SystemSessionId = sessionId,
                Status = AgentHostAuditCatalogStatus.AnchorMismatch,
                ReasonCode = AgentHostAuditCatalogReasonCodes.AnchorMismatch,
                SegmentCount = segmentCount,
                RecordCount = chain.RecordCount,
                AnchorSegmentId = anchor.SegmentId,
                AnchorSequence = anchor.Sequence,
            };
        }

        if (!IsTerminalEvent(chain.FinalEventType))
        {
            return Incomplete(
                sessionId,
                segmentCount,
                AgentHostAuditCatalogReasonCodes.SessionNotTerminal,
                chain.RecordCount,
                anchor);
        }

        return new AgentHostAuditCatalogEntry
        {
            SystemSessionId = sessionId,
            Status = AgentHostAuditCatalogStatus.Complete,
            ReasonCode = AgentHostAuditCatalogReasonCodes.None,
            SegmentCount = segmentCount,
            RecordCount = chain.RecordCount,
            AnchorSegmentId = anchor.SegmentId,
            AnchorSequence = anchor.Sequence,
        };
    }

    private static bool IsTerminalEvent(string eventType)
        => eventType is AgentHostAuditEventTypes.SessionStopped
            or AgentHostAuditEventTypes.SessionFailed;

    private static AgentHostAuditAnchor ReadAnchor(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = StrictUtf8.GetString(bytes);
        using var document = JsonDocument.Parse(text);
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!propertyNames.Add(property.Name))
            {
                throw new AgentHostAuditCatalogException("Audit anchor contains a duplicate property.");
            }
        }

        var anchor = JsonSerializer.Deserialize<AgentHostAuditAnchor>(text, AnchorSerializerOptions)
            ?? throw new AgentHostAuditCatalogException("Audit anchor is empty.");
        if (!string.Equals(anchor.Schema, AgentHostAuditAnchor.SchemaValue, StringComparison.Ordinal)
            || !IsSessionId(anchor.SystemSessionId)
            || !IsSegmentId(anchor.SegmentId)
            || anchor.Sequence < 1)
        {
            throw new AgentHostAuditCatalogException("Audit anchor identity is invalid.");
        }

        AgentHostAuditIntegrity.ValidateHash(anchor.RecordHash, "anchor record hash");
        return anchor;
    }

    private static AgentHostAuditCatalogEntry Incomplete(
        string sessionId,
        int segmentCount,
        string reasonCode,
        long recordCount = 0,
        AgentHostAuditAnchor? anchor = null)
        => new()
        {
            SystemSessionId = sessionId,
            Status = AgentHostAuditCatalogStatus.Incomplete,
            ReasonCode = reasonCode,
            SegmentCount = segmentCount,
            RecordCount = recordCount,
            AnchorSegmentId = anchor?.SegmentId,
            AnchorSequence = anchor?.Sequence,
        };

    private static AgentHostAuditCatalogEntry Corrupt(
        string sessionId,
        int segmentCount,
        string reasonCode)
        => new()
        {
            SystemSessionId = sessionId,
            Status = AgentHostAuditCatalogStatus.Corrupt,
            ReasonCode = reasonCode,
            SegmentCount = segmentCount,
        };

    private static bool TryParseSegmentFileName(
        string fileName,
        out string sessionId,
        out int segmentNumber)
    {
        sessionId = string.Empty;
        segmentNumber = 0;
        const string separator = ".segment-";
        const string suffix = ".jsonl";
        if (fileName.Length != SessionIdLength + separator.Length + SegmentNumberDigits + suffix.Length
            || !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var separatorIndex = SessionIdLength;
        if (!fileName.AsSpan(separatorIndex, separator.Length).SequenceEqual(separator))
        {
            return false;
        }

        sessionId = fileName[..SessionIdLength];
        if (!IsSessionId(sessionId)
            || !int.TryParse(
                fileName.AsSpan(separatorIndex + separator.Length, SegmentNumberDigits),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out segmentNumber)
            || segmentNumber is < 1 or > MaximumSegmentNumber)
        {
            sessionId = string.Empty;
            segmentNumber = 0;
            return false;
        }

        return true;
    }

    private static bool TryParseAnchorFileName(string fileName, out string sessionId)
    {
        const string suffix = ".anchor.json";
        sessionId = string.Empty;
        if (fileName.Length != SessionIdLength + suffix.Length
            || !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        sessionId = fileName[..SessionIdLength];
        if (!IsSessionId(sessionId))
        {
            sessionId = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryParseSessionPrefix(string fileName, out string sessionId)
    {
        sessionId = string.Empty;
        if (fileName.Length < SessionIdLength + 1)
        {
            return false;
        }

        var candidate = fileName[..SessionIdLength];
        if (!IsSessionId(candidate) || fileName[SessionIdLength] != '.')
        {
            return false;
        }

        sessionId = candidate;
        return true;
    }

    private static bool IsSessionId(string value)
        => value.Length == SessionIdLength
            && value.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static bool IsSegmentId(string value)
        => value.Length == "segment-000001".Length
            && value.StartsWith("segment-", StringComparison.Ordinal)
            && int.TryParse(
                value.AsSpan("segment-".Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number)
            && number is >= 1 and <= MaximumSegmentNumber;

    private static void ValidateDirectory(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !Path.IsPathFullyQualified(path)
            || path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new AgentHostAuditCatalogException(
                "AgentHost audit catalog directory is not a safe local path: " + parameterName + ".");
        }

        try
        {
            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                throw new AgentHostAuditCatalogException(
                    "AgentHost audit catalog directory does not exist: " + parameterName + ".");
            }

            for (var current = directory; current is not null; current = current.Parent)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AgentHostAuditCatalogException(
                        "AgentHost audit catalog directory cannot traverse a reparse point.");
                }

                if (current.Parent is null)
                {
                    break;
                }
            }

            var root = Path.GetPathRoot(directory.FullName);
            if (string.IsNullOrWhiteSpace(root)
                || !new DriveInfo(root).DriveType.Equals(DriveType.Fixed))
            {
                throw new AgentHostAuditCatalogException(
                    "AgentHost audit catalog directory must use a fixed drive.");
            }
        }
        catch (AgentHostAuditCatalogException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw new AgentHostAuditCatalogException(
                "AgentHost audit catalog directory could not be validated.",
                exception);
        }
    }

    private static bool IsFileSystemException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;

    private sealed class SessionArtifacts
    {
        internal Dictionary<int, string> Segments { get; } = new();

        internal string? AnchorPath { get; set; }

        internal bool HasTemporaryAnchor { get; set; }

        internal bool HasUnrecognizedArtifact { get; set; }
    }
}

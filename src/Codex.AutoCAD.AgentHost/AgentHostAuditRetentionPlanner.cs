using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Codex.AutoCAD.AgentHost;

internal static class AgentHostAuditRetentionActionCodes
{
    internal const string RetainPolicy = "retain_policy";
    internal const string RetainMinimum = "retain_minimum";
    internal const string RetainManualReview = "retain_manual_review";
    internal const string EligibleAge = "eligible_age";
    internal const string EligibleCapacity = "eligible_capacity";
}

internal sealed class AgentHostAuditRetentionPolicy
{
    internal const int MaximumRetentionDays = 3650;
    internal const long MaximumStoreBytesLimit = 1024L * 1024 * 1024 * 1024;
    internal const int MaximumRetainedCompleteSessions = 4096;

    public int OlderThanDays { get; init; }

    public long MaximumStoreBytes { get; init; }

    public int MinimumCompleteSessionsToRetain { get; init; }

    internal void Validate()
    {
        if (OlderThanDays is < 1 or > MaximumRetentionDays)
        {
            throw new ArgumentOutOfRangeException(nameof(OlderThanDays));
        }

        if (MaximumStoreBytes is < 1 or > MaximumStoreBytesLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumStoreBytes));
        }

        if (MinimumCompleteSessionsToRetain is < 0 or > MaximumRetainedCompleteSessions)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumCompleteSessionsToRetain));
        }
    }
}

internal sealed class AgentHostAuditRetentionEntry
{
    public string SystemSessionId { get; init; } = string.Empty;

    public string CatalogStatus { get; init; } = string.Empty;

    public string CatalogReasonCode { get; init; } = string.Empty;

    public long TotalBytes { get; init; }

    public string LastWriteUtc { get; init; } = string.Empty;

    public string Action { get; set; } = AgentHostAuditRetentionActionCodes.RetainPolicy;
}

internal sealed class AgentHostAuditRetentionPlan
{
    internal const string SchemaValue = "codex.autocad.agenthost.audit-retention-plan/1";

    public string Schema { get; init; } = SchemaValue;

    public string PlanId { get; init; } = string.Empty;

    public string GeneratedAtUtc { get; init; } = string.Empty;

    public AgentHostAuditRetentionPolicy Policy { get; init; } = new();

    public long CurrentStoreBytes { get; init; }

    public long CandidateBytes { get; init; }

    public long ProjectedStoreBytes { get; init; }

    public bool CapacitySatisfied { get; init; }

    public int IgnoredFileCount { get; init; }

    public AgentHostAuditRetentionControlStatus ControlStatus { get; init; } = new();

    public IReadOnlyList<AgentHostAuditRetentionEntry> Entries { get; init; }
        = Array.Empty<AgentHostAuditRetentionEntry>();
}

/// <summary>
/// Produces a bounded, read-only cleanup plan for the protected persistent audit store. It never
/// deletes, moves, rewrites, repairs, or opens an audit artifact for write access.
/// </summary>
internal static class AgentHostAuditRetentionPlanner
{
    private const int MaximumFiles = 16384;
    private const long MaximumArtifactBytes = 64L * 1024 * 1024;
    private const int SessionIdLength = 32;

    internal static AgentHostAuditRetentionPlan CreateCurrentUserPlan(
        AgentHostAuditRetentionPolicy policy,
        DateTimeOffset utcNow)
    {
        using var store = AgentLauncher.AgentPersistentAuditStoreLease.CreateForCurrentUser();
        var controlStatus = AgentHostAuditRetentionExecutor.InspectControlDirectory(
            store.ControlDirectory);
        return Create(
            store.SegmentDirectory,
            store.AnchorDirectory,
            policy,
            utcNow,
            controlStatus);
    }

    internal static AgentHostAuditRetentionPlan Create(
        string segmentDirectory,
        string anchorDirectory,
        AgentHostAuditRetentionPolicy policy,
        DateTimeOffset utcNow,
        AgentHostAuditRetentionControlStatus? controlStatus = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit retention time must be UTC.", nameof(utcNow));
        }

        var catalog = AgentHostAuditCatalog.Read(segmentDirectory, anchorDirectory);
        var artifacts = ReadArtifactMetadata(segmentDirectory, anchorDirectory);
        var entries = new List<AgentHostAuditRetentionEntry>(catalog.Entries.Count);
        foreach (var catalogEntry in catalog.Entries)
        {
            artifacts.BySession.TryGetValue(catalogEntry.SystemSessionId, out var metadata);
            entries.Add(new AgentHostAuditRetentionEntry
            {
                SystemSessionId = catalogEntry.SystemSessionId,
                CatalogStatus = catalogEntry.Status.ToString().ToLowerInvariant(),
                CatalogReasonCode = catalogEntry.ReasonCode,
                TotalBytes = metadata?.TotalBytes ?? 0,
                LastWriteUtc = FormatUtc(metadata?.LastWriteUtc ?? DateTimeOffset.UnixEpoch),
                Action = catalogEntry.Status == AgentHostAuditCatalogStatus.Complete
                    ? AgentHostAuditRetentionActionCodes.RetainPolicy
                    : AgentHostAuditRetentionActionCodes.RetainManualReview,
            });
        }

        var completeNewestFirst = entries
            .Where(static entry => string.Equals(
                entry.CatalogStatus,
                "complete",
                StringComparison.Ordinal))
            .OrderByDescending(static entry => entry.LastWriteUtc, StringComparer.Ordinal)
            .ThenBy(static entry => entry.SystemSessionId, StringComparer.Ordinal)
            .ToArray();
        foreach (var entry in completeNewestFirst.Take(policy.MinimumCompleteSessionsToRetain))
        {
            entry.Action = AgentHostAuditRetentionActionCodes.RetainMinimum;
        }

        var cutoff = utcNow.AddDays(-policy.OlderThanDays);
        foreach (var entry in completeNewestFirst
                     .Where(static entry => string.Equals(
                         entry.Action,
                         AgentHostAuditRetentionActionCodes.RetainPolicy,
                         StringComparison.Ordinal)))
        {
            var lastWrite = DateTimeOffset.ParseExact(
                entry.LastWriteUtc,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            if (lastWrite <= cutoff)
            {
                entry.Action = AgentHostAuditRetentionActionCodes.EligibleAge;
            }
        }

        var candidateBytes = entries
            .Where(IsEligible)
            .Aggregate(0L, static (total, entry) => checked(total + entry.TotalBytes));
        var projectedBytes = checked(artifacts.TotalBytes - candidateBytes);
        if (projectedBytes > policy.MaximumStoreBytes)
        {
            foreach (var entry in entries
                         .Where(static entry => string.Equals(
                             entry.Action,
                             AgentHostAuditRetentionActionCodes.RetainPolicy,
                             StringComparison.Ordinal))
                         .OrderBy(static entry => entry.LastWriteUtc, StringComparer.Ordinal)
                         .ThenBy(static entry => entry.SystemSessionId, StringComparer.Ordinal))
            {
                entry.Action = AgentHostAuditRetentionActionCodes.EligibleCapacity;
                candidateBytes = checked(candidateBytes + entry.TotalBytes);
                projectedBytes = checked(projectedBytes - entry.TotalBytes);
                if (projectedBytes <= policy.MaximumStoreBytes)
                {
                    break;
                }
            }
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(
            left.SystemSessionId,
            right.SystemSessionId));
        var planId = ComputePlanId(
            policy,
            artifacts.TotalBytes,
            candidateBytes,
            projectedBytes,
            projectedBytes <= policy.MaximumStoreBytes,
            catalog.IgnoredFileCount,
            entries);
        return new AgentHostAuditRetentionPlan
        {
            PlanId = planId,
            GeneratedAtUtc = FormatUtc(utcNow),
            Policy = policy,
            CurrentStoreBytes = artifacts.TotalBytes,
            CandidateBytes = candidateBytes,
            ProjectedStoreBytes = projectedBytes,
            CapacitySatisfied = projectedBytes <= policy.MaximumStoreBytes,
            IgnoredFileCount = catalog.IgnoredFileCount,
            ControlStatus = controlStatus ?? new AgentHostAuditRetentionControlStatus(),
            Entries = entries,
        };
    }

    private static ArtifactInventory ReadArtifactMetadata(
        string segmentDirectory,
        string anchorDirectory)
    {
        var bySession = new Dictionary<string, SessionArtifactMetadata>(StringComparer.Ordinal);
        long totalBytes = 0;
        var fileCount = 0;
        ReadDirectory(segmentDirectory);
        ReadDirectory(anchorDirectory);
        return new ArtifactInventory(totalBytes, bySession);

        void ReadDirectory(string directory)
        {
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                throw new AgentHostAuditCatalogException(
                    "Audit retention inventory enumeration failed.",
                    exception);
            }

            foreach (var path in paths)
            {
                if (++fileCount > MaximumFiles)
                {
                    throw new AgentHostAuditCatalogException(
                        "Audit retention inventory file limit was exceeded.");
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                        || info.Length is < 0 or > MaximumArtifactBytes)
                    {
                        throw new AgentHostAuditCatalogException(
                            "Audit retention inventory contains an unsafe artifact.");
                    }
                }
                catch (AgentHostAuditCatalogException)
                {
                    throw;
                }
                catch (Exception exception) when (IsFileSystemException(exception))
                {
                    throw new AgentHostAuditCatalogException(
                        "Audit retention inventory metadata read failed.",
                        exception);
                }

                totalBytes = checked(totalBytes + info.Length);
                if (!TryGetSessionId(info.Name, out var sessionId))
                {
                    continue;
                }

                if (!bySession.TryGetValue(sessionId, out var metadata))
                {
                    metadata = new SessionArtifactMetadata();
                    bySession.Add(sessionId, metadata);
                }

                metadata.TotalBytes = checked(metadata.TotalBytes + info.Length);
                var writeUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                if (writeUtc > metadata.LastWriteUtc)
                {
                    metadata.LastWriteUtc = writeUtc;
                }
            }
        }
    }

    private static bool TryGetSessionId(string fileName, out string sessionId)
    {
        sessionId = string.Empty;
        if (fileName.Length <= SessionIdLength || fileName[SessionIdLength] != '.')
        {
            return false;
        }

        var candidate = fileName[..SessionIdLength];
        if (!candidate.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f'))
        {
            return false;
        }

        sessionId = candidate;
        return true;
    }

    private static bool IsEligible(AgentHostAuditRetentionEntry entry)
        => entry.Action is AgentHostAuditRetentionActionCodes.EligibleAge
            or AgentHostAuditRetentionActionCodes.EligibleCapacity;

    private static string ComputePlanId(
        AgentHostAuditRetentionPolicy policy,
        long currentStoreBytes,
        long candidateBytes,
        long projectedStoreBytes,
        bool capacitySatisfied,
        int ignoredFileCount,
        IReadOnlyList<AgentHostAuditRetentionEntry> entries)
    {
        var builder = new StringBuilder(checked(512 + entries.Count * 192));
        Append(AgentHostAuditRetentionPlan.SchemaValue);
        Append(policy.OlderThanDays.ToString(CultureInfo.InvariantCulture));
        Append(policy.MaximumStoreBytes.ToString(CultureInfo.InvariantCulture));
        Append(policy.MinimumCompleteSessionsToRetain.ToString(CultureInfo.InvariantCulture));
        Append(currentStoreBytes.ToString(CultureInfo.InvariantCulture));
        Append(candidateBytes.ToString(CultureInfo.InvariantCulture));
        Append(projectedStoreBytes.ToString(CultureInfo.InvariantCulture));
        Append(capacitySatisfied ? "1" : "0");
        Append(ignoredFileCount.ToString(CultureInfo.InvariantCulture));
        foreach (var entry in entries)
        {
            Append(entry.SystemSessionId);
            Append(entry.CatalogStatus);
            Append(entry.CatalogReasonCode);
            Append(entry.TotalBytes.ToString(CultureInfo.InvariantCulture));
            Append(entry.LastWriteUtc);
            Append(entry.Action);
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }

        void Append(string value)
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append(';');
        }
    }

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);

    private static bool IsFileSystemException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;

    private sealed record ArtifactInventory(
        long TotalBytes,
        IReadOnlyDictionary<string, SessionArtifactMetadata> BySession);

    private sealed class SessionArtifactMetadata
    {
        internal long TotalBytes { get; set; }

        internal DateTimeOffset LastWriteUtc { get; set; } = DateTimeOffset.UnixEpoch;
    }
}

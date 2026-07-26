using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AgentLauncher;

namespace Codex.AutoCAD.AgentHost;

internal static class AgentHostAuditRetentionApplyStatuses
{
    internal const string Applied = "applied";
    internal const string Recovered = "recovered";
    internal const string AlreadyApplied = "already_applied";
    internal const string NoCandidates = "no_candidates";
}

internal static class AgentHostAuditRetentionExecutionReasonCodes
{
    internal const string PlanChanged = "plan_changed";
    internal const string CleanupBusy = "cleanup_busy";
    internal const string JournalConflict = "journal_conflict";
    internal const string JournalInvalid = "journal_invalid";
    internal const string ArtifactChanged = "artifact_changed";
    internal const string ManualReviewRequired = "manual_review_required";
    internal const string CleanupFailed = "cleanup_failed";
}

internal static class AgentHostAuditRetentionControlStatuses
{
    internal const string NotInspected = "not_inspected";
    internal const string Ready = "ready";
    internal const string RecoveryRequired = "recovery_required";
    internal const string ManualReviewRequired = "manual_review_required";
}

internal static class AgentHostAuditRetentionControlReasonCodes
{
    internal const string PendingRecovery = "pending_recovery";
    internal const string UnknownArtifact = "unknown_artifact";
    internal const string InvalidArtifact = "invalid_artifact";
    internal const string UnsafeArtifact = "unsafe_artifact";
    internal const string InventoryIncomplete = "inventory_incomplete";
}

internal sealed class AgentHostAuditRetentionControlStatus
{
    internal const string SchemaValue
        = "codex.autocad.agenthost.audit-retention-control-status/1";

    public string Schema { get; init; } = SchemaValue;

    public string Status { get; init; } = AgentHostAuditRetentionControlStatuses.NotInspected;

    public bool InspectionComplete { get; init; }

    public bool RecoveryRequired { get; init; }

    public bool ManualReviewRequired { get; init; }

    public int ArtifactCount { get; init; }

    public int KnownArtifactCount { get; init; }

    public int RecoveryArtifactCount { get; init; }

    public int ManualReviewArtifactCount { get; init; }

    public int UnsafeArtifactCount { get; init; }

    public int InvalidArtifactCount { get; init; }

    public IReadOnlyList<string> RecoveryPlanIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
}

internal sealed class AgentHostAuditRetentionExecutionException : Exception
{
    internal AgentHostAuditRetentionExecutionException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    internal AgentHostAuditRetentionExecutionException(
        string reasonCode,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        ReasonCode = reasonCode;
    }

    internal string ReasonCode { get; }
}

internal sealed class AgentHostAuditRetentionApplyResult
{
    internal const string SchemaValue = "codex.autocad.agenthost.audit-retention-apply/1";

    public string Schema { get; init; } = SchemaValue;

    public string PlanId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int DeletedSessionCount { get; init; }

    public int DeletedArtifactCount { get; init; }

    public long DeletedBytes { get; init; }
}

internal enum AgentHostAuditRetentionPersistenceStage
{
    JournalPrepared,
    ReceiptPrepared,
    ReceiptCheckpointPrepared,
}

internal interface IAgentHostAuditRetentionFaultInjector
{
    void OnControlFilePrepared(AgentHostAuditRetentionPersistenceStage stage)
    {
    }

    void OnJournalCommitted();

    void OnArtifactDeleted(int deletedArtifactCount);

    void OnReceiptCheckpointCommitted()
    {
    }
}

/// <summary>
/// Applies a previously reviewed retention plan only after re-planning the protected store and
/// matching its cryptographic plan id. A durable whole-plan journal is committed before the first
/// deletion. Interrupted work resumes only from that validated journal and every remaining
/// artifact is re-hashed before deletion.
/// </summary>
internal static class AgentHostAuditRetentionExecutor
{
    private const int MaximumArtifacts = 16384;
    private const int MaximumRootControlFiles = 4096;
    private const int MaximumControlFileBytes = 4 * 1024 * 1024;
    private const int MaximumRetainedReceipts = 256;
    private const long MaximumArtifactBytes = 64L * 1024 * 1024;
    private const string JournalSchema = "codex.autocad.agenthost.audit-retention-journal/1";
    private const string ReceiptSchema = "codex.autocad.agenthost.audit-retention-receipt/1";
    private const string ReceiptCheckpointSchema
        = "codex.autocad.agenthost.audit-retention-receipt-checkpoint/1";
    private const string LockFileName = ".audit-retention.lock";
    private const string ControlPrefix = ".audit-retention-";
    private const string JournalSuffix = ".journal.json";
    private const string ReceiptSuffix = ".receipt.json";
    private const string ReceiptCheckpointFileName
        = ".audit-retention-receipts.checkpoint.json";
    private const string ReceiptCheckpointTemporaryFileName
        = ReceiptCheckpointFileName + ".tmp";
    private const string TemporarySuffix = ".tmp";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    internal static AgentHostAuditRetentionApplyResult ApplyCurrentUserPlan(
        AgentHostAuditRetentionPolicy policy,
        string expectedPlanId,
        DateTimeOffset utcNow)
    {
        using var store = AgentPersistentAuditStoreLease.CreateForCurrentUser();
        return Apply(
            store.Root,
            store.ControlDirectory,
            store.SegmentDirectory,
            store.AnchorDirectory,
            policy,
            expectedPlanId,
            utcNow);
    }

    internal static AgentHostAuditRetentionApplyResult Apply(
        string auditRoot,
        string controlDirectory,
        string segmentDirectory,
        string anchorDirectory,
        AgentHostAuditRetentionPolicy policy,
        string expectedPlanId,
        DateTimeOffset utcNow,
        IAgentHostAuditRetentionFaultInjector? faultInjector = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        ValidatePlanId(expectedPlanId);
        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit retention apply time must be UTC.", nameof(utcNow));
        }

        ValidateDirectories(
            auditRoot,
            controlDirectory,
            segmentDirectory,
            anchorDirectory);
        using var cleanupLock = OpenCleanupLock(controlDirectory);
        var controlStatus = InspectControlDirectory(controlDirectory);
        if (controlStatus.ReasonCodes.Any(static reasonCode => reasonCode is
                AgentHostAuditRetentionControlReasonCodes.UnknownArtifact
                or AgentHostAuditRetentionControlReasonCodes.UnsafeArtifact
                or AgentHostAuditRetentionControlReasonCodes.InventoryIncomplete))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.ManualReviewRequired,
                "The audit retention control store requires manual review.");
        }

        var paths = GetControlPaths(controlDirectory, expectedPlanId);
        var inventory = ReadControlInventory(controlDirectory);
        RejectConflictingJournals(
            inventory,
            paths.JournalPath,
            paths.JournalTemporaryPath);
        ReconcileReceiptTemporaryFiles(
            controlDirectory,
            inventory,
            paths.ReceiptTemporaryPath);
        ConvergeReceipts(controlDirectory, inventory, faultInjector);
        DeleteSafeTemporaryFile(paths.JournalTemporaryPath);
        DeleteSafeTemporaryFile(paths.ReceiptTemporaryPath);

        if (File.Exists(paths.ReceiptPath))
        {
            var receipt = ReadReceipt(paths.ReceiptPath, expectedPlanId);
            if (File.Exists(paths.JournalPath))
            {
                DeleteControlFile(paths.JournalPath);
            }

            return ToResult(receipt, AgentHostAuditRetentionApplyStatuses.AlreadyApplied);
        }

        if (File.Exists(paths.JournalPath))
        {
            var journal = ReadJournal(paths.JournalPath, expectedPlanId);
            ExecuteJournal(journal, segmentDirectory, anchorDirectory, faultInjector);
            var receipt = CreateReceipt(journal, utcNow);
            WriteControlFileAtomically(
                paths.ReceiptTemporaryPath,
                paths.ReceiptPath,
                receipt,
                AgentHostAuditRetentionPersistenceStage.ReceiptPrepared,
                faultInjector);
            DeleteControlFile(paths.JournalPath);
            ConvergeReceipts(
                controlDirectory,
                ReadControlInventory(controlDirectory),
                faultInjector);
            return ToResult(receipt, AgentHostAuditRetentionApplyStatuses.Recovered);
        }

        var plan = AgentHostAuditRetentionPlanner.Create(
            segmentDirectory,
            anchorDirectory,
            policy,
            utcNow);
        if (!string.Equals(plan.PlanId, expectedPlanId, StringComparison.Ordinal))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.PlanChanged,
                "The audit retention plan changed before apply.");
        }

        var eligibleSessionIds = plan.Entries
            .Where(static entry => entry.Action is AgentHostAuditRetentionActionCodes.EligibleAge
                or AgentHostAuditRetentionActionCodes.EligibleCapacity)
            .Select(static entry => entry.SystemSessionId)
            .OrderBy(static sessionId => sessionId, StringComparer.Ordinal)
            .ToArray();
        if (eligibleSessionIds.Length == 0)
        {
            var emptyReceipt = new CleanupReceipt
            {
                PlanId = expectedPlanId,
                CompletedAtUtc = FormatUtc(utcNow),
                DeletedSessionCount = 0,
                DeletedArtifactCount = 0,
                DeletedBytes = 0,
            };
            WriteControlFileAtomically(
                paths.ReceiptTemporaryPath,
                paths.ReceiptPath,
                emptyReceipt,
                AgentHostAuditRetentionPersistenceStage.ReceiptPrepared,
                faultInjector);
            ConvergeReceipts(
                controlDirectory,
                ReadControlInventory(controlDirectory),
                faultInjector);
            return ToResult(emptyReceipt, AgentHostAuditRetentionApplyStatuses.NoCandidates);
        }

        var journalToCommit = CreateJournal(
            expectedPlanId,
            eligibleSessionIds,
            segmentDirectory,
            anchorDirectory,
            utcNow);
        WriteControlFileAtomically(
            paths.JournalTemporaryPath,
            paths.JournalPath,
            journalToCommit,
            AgentHostAuditRetentionPersistenceStage.JournalPrepared,
            faultInjector);
        InvokeFaultInjector(
            faultInjector is null ? null : faultInjector.OnJournalCommitted,
            "The audit retention journal commit fault fixture interrupted cleanup.");
        ExecuteJournal(journalToCommit, segmentDirectory, anchorDirectory, faultInjector);
        var completedReceipt = CreateReceipt(journalToCommit, utcNow);
        WriteControlFileAtomically(
            paths.ReceiptTemporaryPath,
            paths.ReceiptPath,
            completedReceipt,
            AgentHostAuditRetentionPersistenceStage.ReceiptPrepared,
            faultInjector);
        DeleteControlFile(paths.JournalPath);
        ConvergeReceipts(
            controlDirectory,
            ReadControlInventory(controlDirectory),
            faultInjector);
        return ToResult(completedReceipt, AgentHostAuditRetentionApplyStatuses.Applied);
    }

    internal static AgentHostAuditRetentionControlStatus InspectControlDirectory(
        string controlDirectory)
    {
        if (string.IsNullOrWhiteSpace(controlDirectory)
            || !Path.IsPathFullyQualified(controlDirectory)
            || !string.Equals(
                Path.GetFileName(controlDirectory),
                "retention-control",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Audit retention control directory is invalid.",
                nameof(controlDirectory));
        }

        var artifactCount = 0;
        var knownArtifactCount = 0;
        var recoveryArtifactCount = 0;
        var manualReviewArtifactCount = 0;
        var unsafeArtifactCount = 0;
        var invalidArtifactCount = 0;
        var inspectionComplete = true;
        var recoveryPlanIds = new HashSet<string>(StringComparer.Ordinal);
        var reasonCodes = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(
                         controlDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (artifactCount >= MaximumRootControlFiles)
                {
                    inspectionComplete = false;
                    manualReviewArtifactCount++;
                    reasonCodes.Add(
                        AgentHostAuditRetentionControlReasonCodes.InventoryIncomplete);
                    break;
                }

                artifactCount++;
                if (!TryReadSafeControlFileMetadata(path, out var name))
                {
                    manualReviewArtifactCount++;
                    unsafeArtifactCount++;
                    reasonCodes.Add(
                        AgentHostAuditRetentionControlReasonCodes.UnsafeArtifact);
                    continue;
                }

                if (string.Equals(name, LockFileName, StringComparison.Ordinal))
                {
                    knownArtifactCount++;
                    continue;
                }

                if (string.Equals(
                        name,
                        ReceiptCheckpointTemporaryFileName,
                        StringComparison.Ordinal))
                {
                    recoveryArtifactCount++;
                    reasonCodes.Add(
                        AgentHostAuditRetentionControlReasonCodes.PendingRecovery);
                    continue;
                }

                if (string.Equals(
                        name,
                        ReceiptCheckpointFileName,
                        StringComparison.Ordinal))
                {
                    if (TryValidateControlArtifact(
                            () => ReadReceiptCheckpoint(path)))
                    {
                        knownArtifactCount++;
                    }
                    else
                    {
                        manualReviewArtifactCount++;
                        invalidArtifactCount++;
                        reasonCodes.Add(
                            AgentHostAuditRetentionControlReasonCodes.InvalidArtifact);
                    }

                    continue;
                }

                if (TryGetPlanId(name, JournalSuffix + TemporarySuffix, out var planId)
                    || TryGetPlanId(name, ReceiptSuffix + TemporarySuffix, out planId))
                {
                    recoveryArtifactCount++;
                    recoveryPlanIds.Add(planId);
                    reasonCodes.Add(
                        AgentHostAuditRetentionControlReasonCodes.PendingRecovery);
                    continue;
                }

                if (TryGetPlanId(name, JournalSuffix, out planId))
                {
                    if (TryValidateControlArtifact(
                            () => ReadJournal(path, planId)))
                    {
                        recoveryArtifactCount++;
                        recoveryPlanIds.Add(planId);
                        reasonCodes.Add(
                            AgentHostAuditRetentionControlReasonCodes.PendingRecovery);
                    }
                    else
                    {
                        manualReviewArtifactCount++;
                        invalidArtifactCount++;
                        reasonCodes.Add(
                            AgentHostAuditRetentionControlReasonCodes.InvalidArtifact);
                    }

                    continue;
                }

                if (TryGetPlanId(name, ReceiptSuffix, out planId))
                {
                    if (TryValidateControlArtifact(
                            () => ReadReceipt(path, planId)))
                    {
                        knownArtifactCount++;
                    }
                    else
                    {
                        manualReviewArtifactCount++;
                        invalidArtifactCount++;
                        reasonCodes.Add(
                            AgentHostAuditRetentionControlReasonCodes.InvalidArtifact);
                    }

                    continue;
                }

                manualReviewArtifactCount++;
                reasonCodes.Add(
                    AgentHostAuditRetentionControlReasonCodes.UnknownArtifact);
            }
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            inspectionComplete = false;
            manualReviewArtifactCount++;
            reasonCodes.Add(
                AgentHostAuditRetentionControlReasonCodes.InventoryIncomplete);
        }

        var manualReviewRequired = manualReviewArtifactCount > 0 || !inspectionComplete;
        var recoveryRequired = recoveryArtifactCount > 0;
        return new AgentHostAuditRetentionControlStatus
        {
            Status = manualReviewRequired
                ? AgentHostAuditRetentionControlStatuses.ManualReviewRequired
                : recoveryRequired
                    ? AgentHostAuditRetentionControlStatuses.RecoveryRequired
                    : AgentHostAuditRetentionControlStatuses.Ready,
            InspectionComplete = inspectionComplete,
            RecoveryRequired = recoveryRequired,
            ManualReviewRequired = manualReviewRequired,
            ArtifactCount = artifactCount,
            KnownArtifactCount = knownArtifactCount,
            RecoveryArtifactCount = recoveryArtifactCount,
            ManualReviewArtifactCount = manualReviewArtifactCount,
            UnsafeArtifactCount = unsafeArtifactCount,
            InvalidArtifactCount = invalidArtifactCount,
            RecoveryPlanIds = recoveryPlanIds
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray(),
            ReasonCodes = reasonCodes
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static bool TryReadSafeControlFileMetadata(string path, out string name)
    {
        name = Path.GetFileName(path);
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return false;
            }

            var info = new FileInfo(path);
            info.Refresh();
            return info.Exists && info.Length is >= 0 and <= MaximumControlFileBytes;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return false;
        }
    }

    private static bool TryValidateControlArtifact(Action validate)
    {
        try
        {
            validate();
            return true;
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            return false;
        }
    }

    private static bool TryGetPlanId(string name, string suffix, out string planId)
    {
        if (!name.StartsWith(ControlPrefix, StringComparison.Ordinal)
            || !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            planId = string.Empty;
            return false;
        }

        planId = name.Substring(
            ControlPrefix.Length,
            name.Length - ControlPrefix.Length - suffix.Length);
        return IsLowerHex(planId, 64);
    }

    private static void ConvergeReceipts(
        string controlDirectory,
        IReadOnlyList<string> controlFiles,
        IAgentHostAuditRetentionFaultInjector? faultInjector)
    {
        var receipts = new List<ReceiptInventoryEntry>();
        foreach (var path in controlFiles)
        {
            if (!TryGetReceiptPlanId(path, out var planId))
            {
                continue;
            }

            var receipt = ReadReceipt(path, planId);
            receipts.Add(new ReceiptInventoryEntry(
                path,
                receipt,
                ParseControlUtc(
                    receipt.CompletedAtUtc,
                    "The audit retention receipt completion time is invalid.")));
        }

        if (receipts.Count <= MaximumRetainedReceipts)
        {
            return;
        }

        var orderedReceipts = receipts
            .OrderBy(static entry => entry.CompletedAtUtc)
            .ThenBy(static entry => entry.Receipt.PlanId, StringComparer.Ordinal)
            .ToList();
        var checkpointPath = Path.Combine(
            controlDirectory,
            ReceiptCheckpointFileName);
        var checkpointTemporaryPath = Path.Combine(
            controlDirectory,
            ReceiptCheckpointTemporaryFileName);
        var checkpoint = File.Exists(checkpointPath)
            ? ReadReceiptCheckpoint(checkpointPath)
            : ReceiptCheckpoint.CreateEmpty();
        while (orderedReceipts.Count > MaximumRetainedReceipts)
        {
            var entry = orderedReceipts[0];
            orderedReceipts.RemoveAt(0);
            var cursorComparison = checkpoint.CompareCursor(
                entry.Receipt.PlanId,
                entry.CompletedAtUtc);
            if (cursorComparison > 0)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "An audit retention receipt predates the durable checkpoint.");
            }

            if (cursorComparison == 0)
            {
                if (!checkpoint.MatchesLastReceipt(entry.Receipt))
                {
                    throw Rejected(
                        AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                        "The pending audit retention receipt does not match the durable checkpoint.");
                }

                DeleteControlFile(entry.Path);
                continue;
            }

            checkpoint = checkpoint.Append(entry.Receipt, entry.CompletedAtUtc);
            WriteReceiptCheckpointAtomically(
                checkpointTemporaryPath,
                checkpointPath,
                checkpoint,
                faultInjector);
            InvokeFaultInjector(
                faultInjector is null ? null : faultInjector.OnReceiptCheckpointCommitted,
                "The audit retention receipt checkpoint fault fixture interrupted cleanup.");
            DeleteControlFile(entry.Path);
        }
    }

    private static bool TryGetReceiptPlanId(string path, out string planId)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(ControlPrefix, StringComparison.Ordinal)
            || !name.EndsWith(ReceiptSuffix, StringComparison.Ordinal))
        {
            planId = string.Empty;
            return false;
        }

        planId = name.Substring(
            ControlPrefix.Length,
            name.Length - ControlPrefix.Length - ReceiptSuffix.Length);
        return IsLowerHex(planId, 64);
    }

    private static void ReconcileReceiptTemporaryFiles(
        string controlDirectory,
        IReadOnlyList<string> controlFiles,
        string expectedReceiptTemporaryPath)
    {
        var receiptTemporarySuffix = ReceiptSuffix + TemporarySuffix;
        foreach (var path in controlFiles)
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(receiptTemporarySuffix, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(
                    path,
                    expectedReceiptTemporaryPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!name.StartsWith(ControlPrefix, StringComparison.Ordinal))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt temporary file name is invalid.");
            }

            var planId = name.Substring(
                ControlPrefix.Length,
                name.Length - ControlPrefix.Length - receiptTemporarySuffix.Length);
            if (!IsLowerHex(planId, 64))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt temporary file name is invalid.");
            }

            var finalPath = Path.Combine(
                controlDirectory,
                ControlPrefix + planId + ReceiptSuffix);
            if (!File.Exists(finalPath))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalConflict,
                    "An audit retention receipt commit requires recovery.");
            }

            ReadReceipt(finalPath, planId);
            DeleteSafeTemporaryFile(path);
        }
    }

    private static ReceiptCheckpoint ReadReceiptCheckpoint(string path)
    {
        try
        {
            var checkpoint = JsonSerializer.Deserialize<ReceiptCheckpoint>(
                    ReadBoundedControlFile(path),
                    SerializerOptions)
                ?? throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt checkpoint is empty.");
            checkpoint.Validate();
            return checkpoint;
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or ArgumentException
            or OverflowException)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention receipt checkpoint is invalid.",
                exception);
        }
    }

    private static void WriteReceiptCheckpointAtomically(
        string temporaryPath,
        string finalPath,
        ReceiptCheckpoint checkpoint,
        IAgentHostAuditRetentionFaultInjector? faultInjector)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, SerializerOptions);
        if (bytes.Length is < 1 or > MaximumControlFileBytes)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention receipt checkpoint is too large.");
        }

        try
        {
            DeleteSafeTemporaryFile(temporaryPath);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            InvokeFaultInjector(
                faultInjector is null
                    ? null
                    : () => faultInjector.OnControlFilePrepared(
                        AgentHostAuditRetentionPersistenceStage.ReceiptCheckpointPrepared),
                "The audit retention receipt checkpoint fault fixture interrupted commit.");

            if (File.Exists(finalPath))
            {
                if ((File.GetAttributes(finalPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw Rejected(
                        AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                        "The audit retention receipt checkpoint is unsafe.");
                }

                File.Replace(temporaryPath, finalPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, finalPath);
            }
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "The audit retention receipt checkpoint could not be committed.",
                exception);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static CleanupJournal CreateJournal(
        string planId,
        IReadOnlyList<string> eligibleSessionIds,
        string segmentDirectory,
        string anchorDirectory,
        DateTimeOffset utcNow)
    {
        var catalog = AgentHostAuditCatalog.Read(segmentDirectory, anchorDirectory);
        var catalogBySession = catalog.Entries.ToDictionary(
            static entry => entry.SystemSessionId,
            StringComparer.Ordinal);
        var sessions = new List<CleanupSession>(eligibleSessionIds.Count);
        var artifacts = new List<CleanupArtifact>();
        foreach (var sessionId in eligibleSessionIds)
        {
            if (!catalogBySession.TryGetValue(sessionId, out var catalogEntry)
                || catalogEntry.Status != AgentHostAuditCatalogStatus.Complete
                || catalogEntry.SegmentCount < 1)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.PlanChanged,
                    "An eligible audit session is no longer complete.");
            }

            sessions.Add(new CleanupSession
            {
                SystemSessionId = sessionId,
                SegmentCount = catalogEntry.SegmentCount,
            });
            artifacts.Add(ReadArtifact(
                sessionId,
                "anchor",
                sessionId + ".anchor.json",
                anchorDirectory));
            for (var segmentNumber = 1;
                 segmentNumber <= catalogEntry.SegmentCount;
                 segmentNumber++)
            {
                artifacts.Add(ReadArtifact(
                    sessionId,
                    "segment",
                    sessionId
                    + ".segment-"
                    + segmentNumber.ToString("D6", CultureInfo.InvariantCulture)
                    + ".jsonl",
                    segmentDirectory));
            }

            if (artifacts.Count > MaximumArtifacts)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention journal artifact limit was exceeded.");
            }
        }

        return new CleanupJournal
        {
            PlanId = planId,
            CreatedAtUtc = FormatUtc(utcNow),
            Sessions = sessions,
            Artifacts = artifacts,
        };
    }

    private static CleanupArtifact ReadArtifact(
        string sessionId,
        string kind,
        string fileName,
        string directory)
    {
        var path = Path.Combine(directory, fileName);
        try
        {
            var before = ReadSafeFileInfo(path);
            string sha256;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }

            var after = ReadSafeFileInfo(path);
            if (before.Length != after.Length
                || before.LastWriteTimeUtc.Ticks != after.LastWriteTimeUtc.Ticks)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.ArtifactChanged,
                    "An audit artifact changed while the cleanup journal was created.");
            }

            return new CleanupArtifact
            {
                SystemSessionId = sessionId,
                Kind = kind,
                FileName = fileName,
                Length = after.Length,
                LastWriteUtcTicks = after.LastWriteTimeUtc.Ticks,
                Sha256 = sha256,
            };
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "An audit artifact could not be captured for cleanup.",
                exception);
        }
    }

    private static void ExecuteJournal(
        CleanupJournal journal,
        string segmentDirectory,
        string anchorDirectory,
        IAgentHostAuditRetentionFaultInjector? faultInjector)
    {
        ValidateJournal(journal, journal.PlanId);
        var deleted = 0;
        foreach (var artifact in journal.Artifacts)
        {
            var directory = string.Equals(artifact.Kind, "anchor", StringComparison.Ordinal)
                ? anchorDirectory
                : segmentDirectory;
            var path = Path.Combine(directory, artifact.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            VerifyArtifactForDeletion(path, artifact);
            try
            {
                File.Delete(path);
                if (File.Exists(path))
                {
                    throw new IOException("Audit artifact deletion was not durable.");
                }
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                    "An audit artifact could not be deleted safely.",
                    exception);
            }

            deleted++;
            InvokeFaultInjector(
                faultInjector is null
                    ? null
                    : () => faultInjector.OnArtifactDeleted(deleted),
                "The audit retention artifact deletion fault fixture interrupted cleanup.");
        }
    }

    private static void VerifyArtifactForDeletion(string path, CleanupArtifact expected)
    {
        try
        {
            var before = ReadSafeFileInfo(path);
            if (before.Length != expected.Length
                || before.LastWriteTimeUtc.Ticks != expected.LastWriteUtcTicks)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.ArtifactChanged,
                    "An audit artifact changed after cleanup approval.");
            }

            string observedHash;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                observedHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }

            var after = ReadSafeFileInfo(path);
            if (before.Length != after.Length
                || before.LastWriteTimeUtc.Ticks != after.LastWriteTimeUtc.Ticks
                || !string.Equals(observedHash, expected.Sha256, StringComparison.Ordinal))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.ArtifactChanged,
                    "An audit artifact changed after cleanup approval.");
            }
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "An audit artifact could not be verified for deletion.",
                exception);
        }
    }

    private static FileInfo ReadSafeFileInfo(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists
            || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length is < 0 or > MaximumArtifactBytes)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.ArtifactChanged,
                "An audit artifact is missing or unsafe.");
        }

        return info;
    }

    private static CleanupJournal ReadJournal(string path, string expectedPlanId)
    {
        try
        {
            var journal = JsonSerializer.Deserialize<CleanupJournal>(
                    ReadBoundedControlFile(path),
                    SerializerOptions)
                ?? throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention journal is empty.");
            ValidateJournal(journal, expectedPlanId);
            return journal;
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or ArgumentException)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention journal is invalid.",
                exception);
        }
    }

    private static CleanupReceipt ReadReceipt(string path, string expectedPlanId)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<CleanupReceipt>(
                    ReadBoundedControlFile(path),
                    SerializerOptions)
                ?? throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt is empty.");
            if (!string.Equals(receipt.Schema, ReceiptSchema, StringComparison.Ordinal)
                || !string.Equals(receipt.PlanId, expectedPlanId, StringComparison.Ordinal)
                || receipt.DeletedSessionCount < 0
                || receipt.DeletedArtifactCount < 0
                || receipt.DeletedBytes < 0
                || string.IsNullOrWhiteSpace(receipt.CompletedAtUtc))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt is invalid.");
            }

            ParseControlUtc(
                receipt.CompletedAtUtc,
                "The audit retention receipt completion time is invalid.");

            return receipt;
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or ArgumentException)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention receipt is invalid.",
                exception);
        }
    }

    private static void ValidateJournal(CleanupJournal journal, string expectedPlanId)
    {
        if (!string.Equals(journal.Schema, JournalSchema, StringComparison.Ordinal)
            || !string.Equals(journal.PlanId, expectedPlanId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(journal.CreatedAtUtc)
            || journal.Sessions.Count is < 1 or > 4096
            || journal.Artifacts.Count is < 2 or > MaximumArtifacts)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention journal header is invalid.");
        }

        var sessions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var session in journal.Sessions)
        {
            ValidateSessionId(session.SystemSessionId);
            if (session.SegmentCount is < 1
                or > AgentHostAuditFileSegmentStore.AbsoluteMaximumSegments
                || !sessions.TryAdd(session.SystemSessionId, session.SegmentCount))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention journal contains an invalid session.");
            }
        }

        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessionsWithAnchors = new HashSet<string>(StringComparer.Ordinal);
        var segmentNumbersBySession = sessions.Keys.ToDictionary(
            static sessionId => sessionId,
            static _ => new HashSet<int>(),
            StringComparer.Ordinal);
        foreach (var artifact in journal.Artifacts)
        {
            ValidateSessionId(artifact.SystemSessionId);
            if (!sessions.ContainsKey(artifact.SystemSessionId)
                || artifact.Length is < 0 or > MaximumArtifactBytes
                || artifact.LastWriteUtcTicks <= 0
                || !IsLowerHex(artifact.Sha256, 64)
                || !fileNames.Add(artifact.Kind + ":" + artifact.FileName))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention journal contains an invalid artifact.");
            }

            if (string.Equals(artifact.Kind, "anchor", StringComparison.Ordinal))
            {
                if (!string.Equals(
                        artifact.FileName,
                        artifact.SystemSessionId + ".anchor.json",
                        StringComparison.Ordinal)
                    || !sessionsWithAnchors.Add(artifact.SystemSessionId))
                {
                    throw Rejected(
                        AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                        "The audit retention journal anchor is invalid.");
                }
            }
            else if (string.Equals(artifact.Kind, "segment", StringComparison.Ordinal))
            {
                var prefix = artifact.SystemSessionId + ".segment-";
                const string suffix = ".jsonl";
                var numberText = artifact.FileName.Length == prefix.Length + 6 + suffix.Length
                    ? artifact.FileName.Substring(prefix.Length, 6)
                    : string.Empty;
                if (!artifact.FileName.StartsWith(prefix, StringComparison.Ordinal)
                    || !artifact.FileName.EndsWith(suffix, StringComparison.Ordinal)
                    || numberText.Length != 6
                    || !numberText.All(static character => character is >= '0' and <= '9')
                    || !int.TryParse(
                        numberText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var segmentNumber)
                    || segmentNumber < 1
                    || !segmentNumbersBySession[artifact.SystemSessionId].Add(segmentNumber))
                {
                    throw Rejected(
                        AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                        "The audit retention journal segment is invalid.");
                }
            }
            else
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention journal artifact kind is invalid.");
            }
        }

        if (!sessionsWithAnchors.SetEquals(sessions.Keys))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention journal is missing an anchor.");
        }

        foreach (var session in sessions)
        {
            var observed = segmentNumbersBySession[session.Key];
            if (observed.Count != session.Value
                || Enumerable.Range(1, session.Value).Any(number => !observed.Contains(number)))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention journal segment sequence is incomplete.");
            }
        }
    }

    private static CleanupReceipt CreateReceipt(CleanupJournal journal, DateTimeOffset utcNow)
        => new()
        {
            PlanId = journal.PlanId,
            CompletedAtUtc = FormatUtc(utcNow),
            DeletedSessionCount = journal.Sessions.Count,
            DeletedArtifactCount = journal.Artifacts.Count,
            DeletedBytes = journal.Artifacts.Aggregate(
                0L,
                static (total, artifact) => checked(total + artifact.Length)),
        };

    private static AgentHostAuditRetentionApplyResult ToResult(
        CleanupReceipt receipt,
        string status)
        => new()
        {
            PlanId = receipt.PlanId,
            Status = status,
            DeletedSessionCount = receipt.DeletedSessionCount,
            DeletedArtifactCount = receipt.DeletedArtifactCount,
            DeletedBytes = receipt.DeletedBytes,
        };

    private static byte[] ReadBoundedControlFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            info.Refresh();
            if (!info.Exists
                || (info.Attributes & FileAttributes.ReparsePoint) != 0
                || info.Length is < 1 or > MaximumControlFileBytes)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention control file is unsafe.");
            }

            return File.ReadAllBytes(path);
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention control file could not be read.",
                exception);
        }
    }

    private static void WriteControlFileAtomically<T>(
        string temporaryPath,
        string finalPath,
        T value,
        AgentHostAuditRetentionPersistenceStage stage,
        IAgentHostAuditRetentionFaultInjector? faultInjector)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        if (bytes.Length is < 1 or > MaximumControlFileBytes)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention control file is too large.");
        }

        try
        {
            DeleteSafeTemporaryFile(temporaryPath);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            InvokeFaultInjector(
                faultInjector is null
                    ? null
                    : () => faultInjector.OnControlFilePrepared(stage),
                "The audit retention control commit fault fixture interrupted cleanup.");

            File.Move(temporaryPath, finalPath);
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "The audit retention control file could not be committed.",
                exception);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static void InvokeFaultInjector(Action? action, string message)
    {
        if (action is null)
        {
            return;
        }

        try
        {
            action();
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception)
            || exception is TimeoutException)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                message,
                exception);
        }
    }

    private static void DeleteSafeTemporaryFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention temporary control file is unsafe.");
            }

            File.Delete(path);
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "The audit retention temporary control file could not be removed.",
                exception);
        }
    }

    private static void DeleteControlFile(string path)
    {
        try
        {
            if (File.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention control file is unsafe.");
            }

            File.Delete(path);
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "The audit retention control file could not be removed.",
                exception);
        }
    }

    private static FileStream OpenCleanupLock(string auditRoot)
    {
        var path = Path.Combine(auditRoot, LockFileName);
        try
        {
            if (File.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalConflict,
                    "The audit retention lock file is unsafe.");
            }

            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupBusy,
                "Another audit retention cleanup is active.",
                exception);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "The audit retention cleanup lock could not be opened.",
                exception);
        }
    }

    private static IReadOnlyList<string> ReadControlInventory(string auditRoot)
    {
        try
        {
            var result = new List<string>();
            foreach (var path in Directory.EnumerateFiles(
                         auditRoot,
                         ControlPrefix + "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (result.Count >= MaximumRootControlFiles)
                {
                    throw Rejected(
                        AgentHostAuditRetentionExecutionReasonCodes.JournalConflict,
                        "The audit retention control file limit was exceeded.");
                }

                result.Add(path);
            }

            return result;
        }
        catch (AgentHostAuditRetentionExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.CleanupFailed,
                "The audit retention control inventory could not be read.",
                exception);
        }
    }

    private static void RejectConflictingJournals(
        IReadOnlyList<string> controlFiles,
        string expectedJournalPath,
        string expectedJournalTemporaryPath)
    {
        foreach (var path in controlFiles)
        {
            var name = Path.GetFileName(path);
            var isJournal = name.EndsWith(JournalSuffix, StringComparison.Ordinal);
            var isJournalTemporary = name.EndsWith(
                JournalSuffix + TemporarySuffix,
                StringComparison.Ordinal);
            if ((isJournal
                    && !string.Equals(
                        path,
                        expectedJournalPath,
                        StringComparison.OrdinalIgnoreCase))
                || (isJournalTemporary
                    && !string.Equals(
                        path,
                        expectedJournalTemporaryPath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalConflict,
                    "A different audit retention cleanup requires recovery.");
            }
        }
    }

    private static ControlPaths GetControlPaths(string auditRoot, string planId)
    {
        var stem = ControlPrefix + planId;
        var journalPath = Path.Combine(auditRoot, stem + JournalSuffix);
        var receiptPath = Path.Combine(auditRoot, stem + ReceiptSuffix);
        return new ControlPaths(
            journalPath,
            journalPath + TemporarySuffix,
            receiptPath,
            receiptPath + TemporarySuffix);
    }

    private static void ValidateDirectories(
        string auditRoot,
        string controlDirectory,
        string segmentDirectory,
        string anchorDirectory)
    {
        if (string.IsNullOrWhiteSpace(auditRoot)
            || string.IsNullOrWhiteSpace(controlDirectory)
            || string.IsNullOrWhiteSpace(segmentDirectory)
            || string.IsNullOrWhiteSpace(anchorDirectory)
            || !Path.IsPathFullyQualified(auditRoot)
            || !string.Equals(
                Path.GetDirectoryName(controlDirectory),
                auditRoot,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetDirectoryName(segmentDirectory),
                auditRoot,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetDirectoryName(anchorDirectory),
                auditRoot,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(controlDirectory),
                "retention-control",
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(segmentDirectory),
                "segments",
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(anchorDirectory),
                "anchors",
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Audit retention directories are invalid.");
        }
    }

    private static void ValidatePlanId(string value)
    {
        if (!IsLowerHex(value, 64))
        {
            throw new ArgumentException("Audit retention plan id is invalid.", nameof(value));
        }
    }

    private static void ValidateSessionId(string value)
    {
        if (!IsLowerHex(value, 32))
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                "The audit retention journal session id is invalid.");
        }
    }

    private static bool IsLowerHex(string? value, int length)
        => value != null
            && value.Length == length
            && value.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseControlUtc(string value, string errorMessage)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
            || parsed.Offset != TimeSpan.Zero)
        {
            throw Rejected(
                AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                errorMessage);
        }

        return parsed;
    }

    private static bool IsFileSystemException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;

    private static AgentHostAuditRetentionExecutionException Rejected(
        string reasonCode,
        string message,
        Exception? innerException = null)
        => innerException == null
            ? new AgentHostAuditRetentionExecutionException(reasonCode, message)
            : new AgentHostAuditRetentionExecutionException(reasonCode, message, innerException);

    private sealed class CleanupJournal
    {
        public string Schema { get; init; } = JournalSchema;

        public string PlanId { get; init; } = string.Empty;

        public string CreatedAtUtc { get; init; } = string.Empty;

        public IReadOnlyList<CleanupSession> Sessions { get; init; }
            = Array.Empty<CleanupSession>();

        public IReadOnlyList<CleanupArtifact> Artifacts { get; init; }
            = Array.Empty<CleanupArtifact>();
    }

    private sealed class CleanupSession
    {
        public string SystemSessionId { get; init; } = string.Empty;

        public int SegmentCount { get; init; }
    }

    private sealed class CleanupArtifact
    {
        public string SystemSessionId { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public long Length { get; init; }

        public long LastWriteUtcTicks { get; init; }

        public string Sha256 { get; init; } = string.Empty;
    }

    private sealed class CleanupReceipt
    {
        public string Schema { get; init; } = ReceiptSchema;

        public string PlanId { get; init; } = string.Empty;

        public string CompletedAtUtc { get; init; } = string.Empty;

        public int DeletedSessionCount { get; init; }

        public int DeletedArtifactCount { get; init; }

        public long DeletedBytes { get; init; }
    }

    private sealed class ReceiptCheckpoint
    {
        private static readonly string EmptyChainSha256 = new('0', 64);

        public string Schema { get; init; } = ReceiptCheckpointSchema;

        public long CompactedReceiptCount { get; init; }

        public long DeletedSessionCount { get; init; }

        public long DeletedArtifactCount { get; init; }

        public long DeletedBytes { get; init; }

        public string CompactedThroughCompletedAtUtc { get; init; } = string.Empty;

        public string CompactedThroughPlanId { get; init; } = string.Empty;

        public string ReceiptChainSha256 { get; init; } = EmptyChainSha256;

        public string LastReceiptSha256 { get; init; } = string.Empty;

        internal static ReceiptCheckpoint CreateEmpty() => new();

        internal void Validate()
        {
            if (!string.Equals(Schema, ReceiptCheckpointSchema, StringComparison.Ordinal)
                || CompactedReceiptCount < 0
                || DeletedSessionCount < 0
                || DeletedArtifactCount < 0
                || DeletedBytes < 0
                || !IsLowerHex(ReceiptChainSha256, 64))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt checkpoint is invalid.");
            }

            if (CompactedReceiptCount == 0)
            {
                if (DeletedSessionCount != 0
                    || DeletedArtifactCount != 0
                    || DeletedBytes != 0
                    || CompactedThroughCompletedAtUtc.Length != 0
                    || CompactedThroughPlanId.Length != 0
                    || LastReceiptSha256.Length != 0
                    || !string.Equals(
                        ReceiptChainSha256,
                        EmptyChainSha256,
                        StringComparison.Ordinal))
                {
                    throw Rejected(
                        AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                        "The empty audit retention receipt checkpoint is invalid.");
                }

                return;
            }

            ParseControlUtc(
                CompactedThroughCompletedAtUtc,
                "The audit retention receipt checkpoint time is invalid.");
            if (!IsLowerHex(CompactedThroughPlanId, 64))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt checkpoint plan id is invalid.");
            }

            if (!IsLowerHex(LastReceiptSha256, 64))
            {
                throw Rejected(
                    AgentHostAuditRetentionExecutionReasonCodes.JournalInvalid,
                    "The audit retention receipt checkpoint hash is invalid.");
            }
        }

        internal int CompareCursor(string planId, DateTimeOffset completedAtUtc)
        {
            if (CompactedReceiptCount == 0)
            {
                return -1;
            }

            var timeComparison = ParseControlUtc(
                    CompactedThroughCompletedAtUtc,
                    "The audit retention receipt checkpoint time is invalid.")
                .CompareTo(completedAtUtc);
            return timeComparison != 0
                ? timeComparison
                : string.Compare(
                    CompactedThroughPlanId,
                    planId,
                    StringComparison.Ordinal);
        }

        internal bool MatchesLastReceipt(CleanupReceipt receipt)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, SerializerOptions);
            try
            {
                return string.Equals(
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    LastReceiptSha256,
                    StringComparison.Ordinal);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        internal ReceiptCheckpoint Append(
            CleanupReceipt receipt,
            DateTimeOffset completedAtUtc)
        {
            var previousHash = Convert.FromHexString(ReceiptChainSha256);
            var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
                receipt,
                SerializerOptions);
            try
            {
                var receiptSha256 = Convert.ToHexString(
                    SHA256.HashData(receiptBytes)).ToLowerInvariant();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(previousHash);
                hash.AppendData(receiptBytes);
                return new ReceiptCheckpoint
                {
                    CompactedReceiptCount = checked(CompactedReceiptCount + 1),
                    DeletedSessionCount = checked(
                        DeletedSessionCount + receipt.DeletedSessionCount),
                    DeletedArtifactCount = checked(
                        DeletedArtifactCount + receipt.DeletedArtifactCount),
                    DeletedBytes = checked(DeletedBytes + receipt.DeletedBytes),
                    CompactedThroughCompletedAtUtc = FormatUtc(completedAtUtc),
                    CompactedThroughPlanId = receipt.PlanId,
                    ReceiptChainSha256 = Convert.ToHexString(
                        hash.GetHashAndReset()).ToLowerInvariant(),
                    LastReceiptSha256 = receiptSha256,
                };
            }
            finally
            {
                Array.Clear(previousHash, 0, previousHash.Length);
                Array.Clear(receiptBytes, 0, receiptBytes.Length);
            }
        }
    }

    private sealed record ReceiptInventoryEntry(
        string Path,
        CleanupReceipt Receipt,
        DateTimeOffset CompletedAtUtc);

    private sealed record ControlPaths(
        string JournalPath,
        string JournalTemporaryPath,
        string ReceiptPath,
        string ReceiptTemporaryPath);
}

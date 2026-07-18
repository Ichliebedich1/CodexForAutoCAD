using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Security;

public static class SecurityHash
{
    public static string ComputeSha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

/// <summary>
/// Immutable facts that an approval is bound to. Hashes are canonical lowercase SHA-256 values.
/// </summary>
public sealed record CadApprovalBinding
{
    public CadApprovalBinding(
        string threadId,
        string turnId,
        string normalizedPlanHash,
        string drawingFingerprint,
        long drawingRevision,
        string selectionSnapshotHash)
    {
        ThreadId = RequireIdentifier(threadId, nameof(threadId));
        TurnId = RequireIdentifier(turnId, nameof(turnId));
        NormalizedPlanHash = RequireSha256(normalizedPlanHash, nameof(normalizedPlanHash));
        DrawingFingerprint = RequireSha256(drawingFingerprint, nameof(drawingFingerprint));
        SelectionSnapshotHash = RequireSha256(selectionSnapshotHash, nameof(selectionSnapshotHash));

        if (drawingRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(drawingRevision));
        }

        DrawingRevision = drawingRevision;
    }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string NormalizedPlanHash { get; }

    public string DrawingFingerprint { get; }

    public long DrawingRevision { get; }

    public string SelectionSnapshotHash { get; }

    public CadApprovalBinding WithDrawingRevision(long drawingRevision) =>
        new(
            ThreadId,
            TurnId,
            NormalizedPlanHash,
            DrawingFingerprint,
            drawingRevision,
            SelectionSnapshotHash);

    /// <summary>
    /// Recomputes all approval facts from a strongly typed plan. The approval gate uses the same
    /// derivation internally; callers should use the plan overload of ValidateAndConsume for CAD.
    /// </summary>
    public static CadApprovalBinding FromPlan(CadOperationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return new CadApprovalBinding(
            batch.ThreadId,
            batch.TurnId,
            CadPlanHash.Compute(batch),
            batch.Document.DrawingFingerprint,
            batch.Document.Revision,
            batch.SelectionSnapshotHash);
    }

    private static string RequireIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
        {
            throw new ArgumentException("Identifier must contain 1 to 256 non-whitespace characters.", parameterName);
        }

        return value.Trim();
    }

    private static string RequireSha256(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Value must be a 64-character SHA-256 hexadecimal digest.", parameterName);
        }

        return value.ToLowerInvariant();
    }
}

public enum CadApprovalState
{
    Proposed,
    SchemaValidated,
    PolicyValidated,
    SideDatabaseSimulated,
    PreviewReady,
    CheckpointRecorded,
    AwaitingUser,
    ApprovedOnce,
    Declined,
    Expired,
    DocumentLocked,
    RevisionRevalidated,
    Executing,
    Committed,
    RolledBack,
    ResultUncertain,
}

public enum ApprovalFailureReason
{
    None,
    UnknownRequest,
    InvalidState,
    PolicyDenied,
    SessionScopeForbidden,
    TokenExpired,
    TokenMismatch,
    BindingMismatch,
    ReplayDetected,
    CheckpointRequired,
    CheckpointMismatch,
}

/// <summary>
/// Immutable checkpoint identity supplied by the trusted checkpoint writer. The gate seals this
/// evidence to the request and plan with a per-gate HMAC before it can authorize an R4 request.
/// </summary>
public sealed record CadCheckpointEvidence
{
    public CadCheckpointEvidence(string checkpointId, string checkpointDigest)
    {
        if (string.IsNullOrWhiteSpace(checkpointId)
            || checkpointId.Length > 256
            || checkpointId.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "Checkpoint identifier must contain 1 to 256 non-whitespace characters.",
                nameof(checkpointId));
        }

        if (checkpointDigest is null
            || checkpointDigest.Length != 64
            || checkpointDigest.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Checkpoint digest must be a 64-character SHA-256 hexadecimal digest.",
                nameof(checkpointDigest));
        }

        CheckpointId = checkpointId.Trim();
        CheckpointDigest = checkpointDigest.ToLowerInvariant();
    }

    public string CheckpointId { get; }

    public string CheckpointDigest { get; }
}

/// <summary>
/// Frozen, trusted facts derived from a schema-valid operation batch.
/// </summary>
public sealed record CadApprovalPlanSnapshot(
    string BatchId,
    string NormalizedPlanHash,
    CadActionKind EffectiveAction,
    int OperationCount,
    int CreatedEntityCount,
    int ModifiedEntityCount,
    int DeletedEntityCount,
    int TargetEntityCount,
    bool RequiresSelectionRevalidation);

/// <summary>
/// Auditable checkpoint proof. Attestation is a per-gate HMAC over the checkpoint, approval
/// binding, effective risk, and frozen plan summary; it cannot be recomputed by an untrusted caller.
/// </summary>
public sealed record CadCheckpointAuditSnapshot(
    CadCheckpointEvidence Evidence,
    string Attestation,
    DateTimeOffset RecordedAt);

public sealed record ApprovalOperationResult(
    bool Success,
    ApprovalFailureReason Failure,
    CadApprovalState? State,
    string ReasonCode);

public sealed record ApprovalIssueResult(
    bool Success,
    ApprovalFailureReason Failure,
    CadApprovalState? State,
    string ReasonCode,
    CadApprovalToken? Token);

public sealed record ApprovalConsumptionResult(
    bool Success,
    ApprovalFailureReason Failure,
    CadApprovalState? State,
    string ReasonCode);

public sealed record CadApprovalRequestSnapshot(
    Guid RequestId,
    CadApprovalBinding Binding,
    RiskAssessment Risk,
    CadApprovalPlanSnapshot? Plan,
    CadCheckpointAuditSnapshot? Checkpoint,
    CadApprovalState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Opaque capability returned only to the trusted broker. Its secret is never exposed as a string
/// and is wiped when consumed or disposed.
/// </summary>
public sealed class CadApprovalToken : IDisposable
{
    private byte[]? _secret;

    internal CadApprovalToken(Guid requestId, DateTimeOffset expiresAt, byte[] secret)
    {
        RequestId = requestId;
        ExpiresAt = expiresAt;
        _secret = secret;
    }

    public Guid RequestId { get; }

    public DateTimeOffset ExpiresAt { get; }

    public bool IsDisposed => _secret is null;

    internal ReadOnlySpan<byte> Secret => _secret is null ? ReadOnlySpan<byte>.Empty : _secret;

    public void Dispose()
    {
        if (_secret is not null)
        {
            CryptographicOperations.ZeroMemory(_secret);
            _secret = null;
        }

        GC.SuppressFinalize(this);
    }

    ~CadApprovalToken() => Dispose();
}

/// <summary>
/// Thread-safe, fail-closed approval state machine. It never accepts session grants and issues
/// only a single 60-second capability bound to the approved plan and current drawing state.
/// </summary>
public sealed class CadApprovalGate : IDisposable
{
    private const int SecretSizeBytes = 32;
    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromSeconds(60);

    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly ResourceQuotaPolicy _resourceQuotas;
    private readonly byte[] _integrityKey;
    private bool _disposed;

    public CadApprovalGate(TimeProvider? timeProvider = null, ResourceQuotaPolicy? resourceQuotas = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resourceQuotas = resourceQuotas ?? new ResourceQuotaPolicy();
        _integrityKey = RandomNumberGenerator.GetBytes(SecretSizeBytes);
    }

    public static TimeSpan TokenLifetime => ApprovalLifetime;

    /// <summary>
    /// Trusted CAD-plan entry point. The supplied mutable contract is cloned first, then schema
    /// validated, hashed, classified, quota assessed, and reduced to immutable audit facts.
    /// </summary>
    public Guid Propose(CadOperationBatch batch)
    {
        var derived = FreezeAndDerivePlan(batch);
        return ProposeCore(derived.Binding, derived.Action, derived.Plan);
    }

    /// <summary>
    /// Compatibility entry point for non-plan actions such as context egress. CAD writes and any
    /// descriptor carrying operation/entity counts are rejected; CAD plans must use Propose(batch).
    /// </summary>
    public Guid Propose(CadApprovalBinding binding, CadActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(action);

        var risk = CadRiskClassifier.Assess(action);
        if (risk.IsCadWrite
            || action.OperationCount != 0
            || action.AffectedEntityCount != 0
            || action.DeletedEntityCount != 0
            || action.TargetEntityCount != 0)
        {
            throw new InvalidOperationException(
                "CAD operation plans must enter the approval gate as a validated CadOperationBatch.");
        }

        return ProposeCore(binding, action, plan: null);
    }

    private Guid ProposeCore(
        CadApprovalBinding binding,
        CadActionDescriptor action,
        CadApprovalPlanSnapshot? plan)
    {
        // Classification happens inside the trusted gate so callers cannot inject RiskAssessment.
        var risk = CadRiskClassifier.Assess(action);
        var quota = _resourceQuotas.AssessPlan(
            action.OperationCount,
            action.DeletedEntityCount,
            action.TargetEntityCount);
        if (quota.Disposition == QuotaDisposition.Reject)
        {
            throw new InvalidOperationException($"Action cannot enter the approval gate: {quota.ReasonCode}.");
        }

        if (quota.Disposition == QuotaDisposition.EscalateToHighRisk
            && risk.IsCadWrite
            && risk.Level < CadRiskLevel.R4DestructiveOrBulk)
        {
            risk = risk with
            {
                Level = CadRiskLevel.R4DestructiveOrBulk,
                RequiresCheckpoint = true,
                ReasonCode = "POLICY_RESOURCE_ESCALATED",
            };
        }

        if (risk.Decision != PolicyDecision.RequireApproval)
        {
            throw new InvalidOperationException("Only actions explicitly classified as requiring approval may enter the CAD approval gate.");
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            var requestId = Guid.NewGuid();
            _entries.Add(requestId, new Entry(
                requestId,
                binding,
                risk,
                plan,
                _timeProvider.GetUtcNow()));
            return requestId;
        }
    }

    public ApprovalOperationResult RecordSchemaValidated(Guid requestId) =>
        Transition(requestId, CadApprovalState.Proposed, CadApprovalState.SchemaValidated, "APPROVAL_SCHEMA_VALIDATED");

    public ApprovalOperationResult RecordPolicyValidated(Guid requestId) =>
        Transition(requestId, CadApprovalState.SchemaValidated, CadApprovalState.PolicyValidated, "APPROVAL_POLICY_VALIDATED");

    public ApprovalOperationResult RecordSideDatabaseSimulated(Guid requestId) =>
        Transition(requestId, CadApprovalState.PolicyValidated, CadApprovalState.SideDatabaseSimulated, "APPROVAL_SIMULATED");

    public ApprovalOperationResult RecordPreviewReady(Guid requestId) =>
        Transition(requestId, CadApprovalState.SideDatabaseSimulated, CadApprovalState.PreviewReady, "APPROVAL_PREVIEW_READY");

    public ApprovalOperationResult RecordCheckpoint(
        Guid requestId,
        CadCheckpointEvidence checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        // Defensive copy prevents a future implementation change from accidentally retaining a
        // caller-owned mutable subtype or representation.
        var frozen = new CadCheckpointEvidence(
            checkpoint.CheckpointId,
            checkpoint.CheckpointDigest);

        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(
                    ApprovalFailureReason.UnknownRequest,
                    null,
                    "APPROVAL_UNKNOWN_REQUEST");
            }

            if (!entry.Risk.RequiresCheckpoint)
            {
                return OperationFailure(
                    ApprovalFailureReason.InvalidState,
                    entry.State,
                    "APPROVAL_CHECKPOINT_NOT_REQUIRED");
            }

            if (entry.State != CadApprovalState.PreviewReady)
            {
                return OperationFailure(
                    ApprovalFailureReason.InvalidState,
                    entry.State,
                    "APPROVAL_INVALID_STATE");
            }

            entry.Checkpoint = frozen;
            entry.CheckpointRecordedAt = _timeProvider.GetUtcNow();
            entry.CheckpointSeal = ComputeCheckpointSeal(entry, frozen);
            entry.State = CadApprovalState.CheckpointRecorded;
            return OperationSuccess(entry.State, "APPROVAL_CHECKPOINT_RECORDED");
        }
    }

    public ApprovalOperationResult AwaitUserDecision(Guid requestId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(
                    ApprovalFailureReason.UnknownRequest,
                    null,
                    "APPROVAL_UNKNOWN_REQUEST");
            }

            if (entry.Risk.RequiresCheckpoint)
            {
                if (entry.State == CadApprovalState.PreviewReady)
                {
                    return OperationFailure(
                        ApprovalFailureReason.CheckpointRequired,
                        entry.State,
                        "APPROVAL_CHECKPOINT_REQUIRED");
                }

                if (entry.State != CadApprovalState.CheckpointRecorded)
                {
                    return OperationFailure(
                        ApprovalFailureReason.InvalidState,
                        entry.State,
                        "APPROVAL_INVALID_STATE");
                }

                if (!HasValidCheckpoint(entry))
                {
                    return OperationFailure(
                        ApprovalFailureReason.CheckpointMismatch,
                        entry.State,
                        "APPROVAL_CHECKPOINT_ATTESTATION_INVALID");
                }
            }
            else if (entry.State != CadApprovalState.PreviewReady)
            {
                return OperationFailure(
                    ApprovalFailureReason.InvalidState,
                    entry.State,
                    "APPROVAL_INVALID_STATE");
            }

            entry.State = CadApprovalState.AwaitingUser;
            return OperationSuccess(entry.State, "APPROVAL_AWAITING_USER");
        }
    }

    public ApprovalIssueResult Approve(Guid requestId, ApprovalScope scope)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return IssueFailure(ApprovalFailureReason.UnknownRequest, null, "APPROVAL_UNKNOWN_REQUEST");
            }

            if (scope != ApprovalScope.Once)
            {
                return IssueFailure(
                    ApprovalFailureReason.SessionScopeForbidden,
                    entry.State,
                    "APPROVAL_CAD_SESSION_SCOPE_FORBIDDEN");
            }

            if (entry.State != CadApprovalState.AwaitingUser)
            {
                return IssueFailure(ApprovalFailureReason.InvalidState, entry.State, "APPROVAL_INVALID_STATE");
            }

            if (entry.Risk.Decision != PolicyDecision.RequireApproval)
            {
                return IssueFailure(ApprovalFailureReason.PolicyDenied, entry.State, "APPROVAL_POLICY_DENIED");
            }

            if (entry.Risk.RequiresCheckpoint && !HasValidCheckpoint(entry))
            {
                var failure = entry.Checkpoint is null
                    ? ApprovalFailureReason.CheckpointRequired
                    : ApprovalFailureReason.CheckpointMismatch;
                var reason = entry.Checkpoint is null
                    ? "APPROVAL_CHECKPOINT_REQUIRED"
                    : "APPROVAL_CHECKPOINT_ATTESTATION_INVALID";
                return IssueFailure(failure, entry.State, reason);
            }

            var secret = RandomNumberGenerator.GetBytes(SecretSizeBytes);
            entry.ApprovedAt = _timeProvider.GetUtcNow();
            entry.ApprovedTimestamp = _timeProvider.GetTimestamp();
            entry.ExpiresAt = entry.ApprovedAt + ApprovalLifetime;
            entry.TokenDigest = ComputeTokenDigest(secret, entry);
            entry.State = CadApprovalState.ApprovedOnce;

            return new ApprovalIssueResult(
                Success: true,
                ApprovalFailureReason.None,
                entry.State,
                "APPROVAL_TOKEN_ISSUED",
                new CadApprovalToken(requestId, entry.ExpiresAt.Value, secret));
        }
    }

    public ApprovalOperationResult Decline(Guid requestId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(ApprovalFailureReason.UnknownRequest, null, "APPROVAL_UNKNOWN_REQUEST");
            }

            if (entry.State != CadApprovalState.AwaitingUser)
            {
                return OperationFailure(ApprovalFailureReason.InvalidState, entry.State, "APPROVAL_INVALID_STATE");
            }

            ClearToken(entry);
            entry.State = CadApprovalState.Declined;
            return OperationSuccess(entry.State, "APPROVAL_DECLINED");
        }
    }

    public ApprovalOperationResult MarkDocumentLocked(Guid requestId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(ApprovalFailureReason.UnknownRequest, null, "APPROVAL_UNKNOWN_REQUEST");
            }

            if (entry.State != CadApprovalState.ApprovedOnce)
            {
                return OperationFailure(ApprovalFailureReason.InvalidState, entry.State, "APPROVAL_INVALID_STATE");
            }

            if (IsExpired(entry))
            {
                ExpireEntry(entry);
                return OperationFailure(ApprovalFailureReason.TokenExpired, entry.State, "APPROVAL_TOKEN_EXPIRED");
            }

            if (entry.Risk.RequiresCheckpoint && !HasValidCheckpoint(entry))
            {
                ExpireEntry(entry);
                return OperationFailure(
                    ApprovalFailureReason.CheckpointMismatch,
                    entry.State,
                    "APPROVAL_CHECKPOINT_ATTESTATION_INVALID");
            }

            entry.State = CadApprovalState.DocumentLocked;
            return OperationSuccess(entry.State, "APPROVAL_DOCUMENT_LOCKED");
        }
    }

    /// <summary>
    /// CAD-plan consumption path. The current plan is cloned and independently re-derived inside
    /// the gate so a caller cannot present a stale hand-built binding while executing other ops.
    /// </summary>
    public ApprovalConsumptionResult ValidateAndConsume(
        CadApprovalToken? token,
        CadOperationBatch currentPlan,
        CadCheckpointEvidence? currentCheckpoint = null)
    {
        DerivedPlan derived;
        try
        {
            derived = FreezeAndDerivePlan(currentPlan);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            return RejectInvalidCurrentPlan(token);
        }

        return ValidateAndConsumeCore(
            token,
            derived.Binding,
            derived.Plan,
            currentCheckpoint,
            requirePlan: true);
    }

    private ApprovalConsumptionResult RejectInvalidCurrentPlan(CadApprovalToken? token)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (token is null)
            {
                return ConsumptionFailure(
                    ApprovalFailureReason.TokenMismatch,
                    null,
                    "APPROVAL_TOKEN_MISMATCH");
            }

            if (!_entries.TryGetValue(token.RequestId, out var entry))
            {
                token.Dispose();
                return ConsumptionFailure(
                    ApprovalFailureReason.TokenMismatch,
                    null,
                    "APPROVAL_TOKEN_MISMATCH");
            }

            ExpireEntry(entry);
            token.Dispose();
            return ConsumptionFailure(
                ApprovalFailureReason.PolicyDenied,
                entry.State,
                "APPROVAL_CURRENT_PLAN_INVALID");
        }
    }

    /// <summary>
    /// Non-plan compatibility path. It deliberately refuses entries created from a CAD batch.
    /// </summary>
    public ApprovalConsumptionResult ValidateAndConsume(
        CadApprovalToken? token,
        CadApprovalBinding currentBinding)
    {
        ArgumentNullException.ThrowIfNull(currentBinding);
        return ValidateAndConsumeCore(
            token,
            currentBinding,
            currentPlan: null,
            currentCheckpoint: null,
            requirePlan: false);
    }

    private ApprovalConsumptionResult ValidateAndConsumeCore(
        CadApprovalToken? token,
        CadApprovalBinding currentBinding,
        CadApprovalPlanSnapshot? currentPlan,
        CadCheckpointEvidence? currentCheckpoint,
        bool requirePlan)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (token is null || !_entries.TryGetValue(token.RequestId, out var entry))
            {
                return ConsumptionFailure(ApprovalFailureReason.TokenMismatch, null, "APPROVAL_TOKEN_MISMATCH");
            }

            if (entry.State is CadApprovalState.RevisionRevalidated
                or CadApprovalState.Executing
                or CadApprovalState.Committed
                or CadApprovalState.RolledBack
                or CadApprovalState.ResultUncertain)
            {
                return ConsumptionFailure(ApprovalFailureReason.ReplayDetected, entry.State, "APPROVAL_TOKEN_REPLAYED");
            }

            if (entry.State != CadApprovalState.DocumentLocked)
            {
                return ConsumptionFailure(ApprovalFailureReason.InvalidState, entry.State, "APPROVAL_INVALID_STATE");
            }

            if (requirePlan != (entry.Plan is not null))
            {
                ExpireEntry(entry);
                token.Dispose();
                return ConsumptionFailure(
                    ApprovalFailureReason.PolicyDenied,
                    entry.State,
                    entry.Plan is null
                        ? "APPROVAL_NON_PLAN_REVALIDATION_REQUIRED"
                        : "APPROVAL_PLAN_REVALIDATION_REQUIRED");
            }

            if (IsExpired(entry))
            {
                ExpireEntry(entry);
                token.Dispose();
                return ConsumptionFailure(ApprovalFailureReason.TokenExpired, entry.State, "APPROVAL_TOKEN_EXPIRED");
            }

            if (entry.Risk.RequiresCheckpoint)
            {
                if (!HasValidCheckpoint(entry) || currentCheckpoint is null)
                {
                    ExpireEntry(entry);
                    token.Dispose();
                    return ConsumptionFailure(
                        currentCheckpoint is null
                            ? ApprovalFailureReason.CheckpointRequired
                            : ApprovalFailureReason.CheckpointMismatch,
                        entry.State,
                        currentCheckpoint is null
                            ? "APPROVAL_CHECKPOINT_REQUIRED"
                            : "APPROVAL_CHECKPOINT_ATTESTATION_INVALID");
                }

                var frozenCurrentCheckpoint = new CadCheckpointEvidence(
                    currentCheckpoint.CheckpointId,
                    currentCheckpoint.CheckpointDigest);
                if (entry.Checkpoint != frozenCurrentCheckpoint)
                {
                    ExpireEntry(entry);
                    token.Dispose();
                    return ConsumptionFailure(
                        ApprovalFailureReason.CheckpointMismatch,
                        entry.State,
                        "APPROVAL_CHECKPOINT_CHANGED");
                }
            }

            if (!MatchesStoredDigest(token, entry))
            {
                return ConsumptionFailure(ApprovalFailureReason.TokenMismatch, entry.State, "APPROVAL_TOKEN_MISMATCH");
            }

            if (entry.Binding != currentBinding || entry.Plan != currentPlan)
            {
                ExpireEntry(entry);
                token.Dispose();
                return ConsumptionFailure(ApprovalFailureReason.BindingMismatch, entry.State, "APPROVAL_BINDING_CHANGED");
            }

            ClearToken(entry);
            token.Dispose();
            entry.State = CadApprovalState.RevisionRevalidated;
            return new ApprovalConsumptionResult(
                Success: true,
                ApprovalFailureReason.None,
                entry.State,
                "APPROVAL_TOKEN_CONSUMED");
        }
    }

    public ApprovalOperationResult BeginExecution(Guid requestId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(
                    ApprovalFailureReason.UnknownRequest,
                    null,
                    "APPROVAL_UNKNOWN_REQUEST");
            }

            if (entry.State != CadApprovalState.RevisionRevalidated)
            {
                return OperationFailure(
                    ApprovalFailureReason.InvalidState,
                    entry.State,
                    "APPROVAL_INVALID_STATE");
            }

            if (entry.Risk.RequiresCheckpoint && !HasValidCheckpoint(entry))
            {
                return OperationFailure(
                    ApprovalFailureReason.CheckpointMismatch,
                    entry.State,
                    "APPROVAL_CHECKPOINT_ATTESTATION_INVALID");
            }

            entry.State = CadApprovalState.Executing;
            return OperationSuccess(entry.State, "APPROVAL_EXECUTING");
        }
    }

    public ApprovalOperationResult Commit(Guid requestId) =>
        Transition(requestId, CadApprovalState.Executing, CadApprovalState.Committed, "APPROVAL_COMMITTED");

    public ApprovalOperationResult MarkResultUncertain(Guid requestId) =>
        Transition(requestId, CadApprovalState.Executing, CadApprovalState.ResultUncertain, "APPROVAL_RESULT_UNCERTAIN");

    public ApprovalOperationResult RollBack(Guid requestId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(ApprovalFailureReason.UnknownRequest, null, "APPROVAL_UNKNOWN_REQUEST");
            }

            if (entry.State is not (CadApprovalState.DocumentLocked
                or CadApprovalState.RevisionRevalidated
                or CadApprovalState.Executing))
            {
                return OperationFailure(ApprovalFailureReason.InvalidState, entry.State, "APPROVAL_INVALID_STATE");
            }

            ClearToken(entry);
            entry.State = CadApprovalState.RolledBack;
            return OperationSuccess(entry.State, "APPROVAL_ROLLED_BACK");
        }
    }

    public ApprovalOperationResult Expire(Guid requestId)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(ApprovalFailureReason.UnknownRequest, null, "APPROVAL_UNKNOWN_REQUEST");
            }

            if (entry.State is not (CadApprovalState.AwaitingUser
                or CadApprovalState.ApprovedOnce
                or CadApprovalState.DocumentLocked))
            {
                return OperationFailure(ApprovalFailureReason.InvalidState, entry.State, "APPROVAL_INVALID_STATE");
            }

            ExpireEntry(entry);
            return OperationSuccess(entry.State, "APPROVAL_EXPIRED");
        }
    }

    public bool TryGetSnapshot(Guid requestId, out CadApprovalRequestSnapshot? snapshot)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(requestId, out var entry))
            {
                snapshot = null;
                return false;
            }

            snapshot = entry.ToSnapshot();
            return true;
        }
    }

    public int SweepExpired()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var count = 0;

            foreach (var entry in _entries.Values)
            {
                if (entry.State is (CadApprovalState.ApprovedOnce or CadApprovalState.DocumentLocked)
                    && IsExpired(entry))
                {
                    ExpireEntry(entry);
                    count++;
                }
            }

            return count;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var entry in _entries.Values)
            {
                ClearToken(entry);
                ClearCheckpointSeal(entry);
            }

            _entries.Clear();
            CryptographicOperations.ZeroMemory(_integrityKey);
            _disposed = true;
        }
    }

    private ApprovalOperationResult Transition(
        Guid requestId,
        CadApprovalState expected,
        CadApprovalState next,
        string reason)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_entries.TryGetValue(requestId, out var entry))
            {
                return OperationFailure(ApprovalFailureReason.UnknownRequest, null, "APPROVAL_UNKNOWN_REQUEST");
            }

            if (entry.State != expected)
            {
                return OperationFailure(ApprovalFailureReason.InvalidState, entry.State, "APPROVAL_INVALID_STATE");
            }

            entry.State = next;
            return OperationSuccess(next, reason);
        }
    }

    private bool IsExpired(Entry entry)
    {
        if (entry.ExpiresAt is null || entry.ApprovedTimestamp is null)
        {
            return true;
        }

        var wallClockExpired = _timeProvider.GetUtcNow() >= entry.ExpiresAt.Value;
        var monotonicExpired = _timeProvider.GetElapsedTime(entry.ApprovedTimestamp.Value) >= ApprovalLifetime;
        return wallClockExpired || monotonicExpired;
    }

    private bool MatchesStoredDigest(CadApprovalToken token, Entry entry)
    {
        if (entry.TokenDigest is null || token.Secret.Length != SecretSizeBytes)
        {
            return false;
        }

        var candidateDigest = ComputeTokenDigest(token.Secret, entry);
        try
        {
            return CryptographicOperations.FixedTimeEquals(candidateDigest, entry.TokenDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateDigest);
        }
    }

    private bool HasValidCheckpoint(Entry entry)
    {
        if (!entry.Risk.RequiresCheckpoint)
        {
            return true;
        }

        if (entry.Checkpoint is null
            || entry.CheckpointRecordedAt is null
            || entry.CheckpointSeal is null
            || entry.CheckpointSeal.Length != 32)
        {
            return false;
        }

        var candidate = ComputeCheckpointSeal(entry, entry.Checkpoint);
        try
        {
            return CryptographicOperations.FixedTimeEquals(candidate, entry.CheckpointSeal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    private byte[] ComputeCheckpointSeal(Entry entry, CadCheckpointEvidence checkpoint)
    {
        var payload = BuildApprovalPayload(entry, checkpoint, checkpointSeal: null);
        try
        {
            return HMACSHA256.HashData(_integrityKey, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static byte[] ComputeTokenDigest(ReadOnlySpan<byte> secret, Entry entry)
    {
        var payload = BuildApprovalPayload(entry, entry.Checkpoint, entry.CheckpointSeal);
        try
        {
            return HMACSHA256.HashData(secret, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static byte[] BuildApprovalPayload(
        Entry entry,
        CadCheckpointEvidence? checkpoint,
        byte[]? checkpointSeal)
    {
        var canonical = new StringBuilder();
        AppendAuditField(canonical, entry.RequestId.ToString("D"));
        AppendAuditField(canonical, entry.Binding.ThreadId);
        AppendAuditField(canonical, entry.Binding.TurnId);
        AppendAuditField(canonical, entry.Binding.NormalizedPlanHash);
        AppendAuditField(canonical, entry.Binding.DrawingFingerprint);
        AppendAuditField(canonical, entry.Binding.DrawingRevision.ToString(CultureInfo.InvariantCulture));
        AppendAuditField(canonical, entry.Binding.SelectionSnapshotHash);
        AppendAuditField(canonical, ((int)entry.Risk.Action).ToString(CultureInfo.InvariantCulture));
        AppendAuditField(canonical, ((int)entry.Risk.Level).ToString(CultureInfo.InvariantCulture));
        AppendAuditField(canonical, ((int)entry.Risk.Decision).ToString(CultureInfo.InvariantCulture));
        AppendAuditField(canonical, entry.Risk.IsCadWrite ? "1" : "0");
        AppendAuditField(canonical, entry.Risk.IsExternalEffect ? "1" : "0");
        AppendAuditField(canonical, entry.Risk.RequiresCheckpoint ? "1" : "0");
        AppendAuditField(canonical, entry.Risk.ReasonCode);

        if (entry.Plan is null)
        {
            AppendAuditField(canonical, string.Empty);
        }
        else
        {
            AppendAuditField(canonical, entry.Plan.BatchId);
            AppendAuditField(canonical, entry.Plan.NormalizedPlanHash);
            AppendAuditField(canonical, ((int)entry.Plan.EffectiveAction).ToString(CultureInfo.InvariantCulture));
            AppendAuditField(canonical, entry.Plan.OperationCount.ToString(CultureInfo.InvariantCulture));
            AppendAuditField(canonical, entry.Plan.CreatedEntityCount.ToString(CultureInfo.InvariantCulture));
            AppendAuditField(canonical, entry.Plan.ModifiedEntityCount.ToString(CultureInfo.InvariantCulture));
            AppendAuditField(canonical, entry.Plan.DeletedEntityCount.ToString(CultureInfo.InvariantCulture));
            AppendAuditField(canonical, entry.Plan.TargetEntityCount.ToString(CultureInfo.InvariantCulture));
            AppendAuditField(canonical, entry.Plan.RequiresSelectionRevalidation ? "1" : "0");
        }

        AppendAuditField(canonical, checkpoint?.CheckpointId);
        AppendAuditField(canonical, checkpoint?.CheckpointDigest);
        AppendAuditField(
            canonical,
            entry.CheckpointRecordedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        AppendAuditField(
            canonical,
            checkpointSeal is null ? string.Empty : Convert.ToHexString(checkpointSeal));
        return Encoding.UTF8.GetBytes(canonical.ToString());
    }

    private static void AppendAuditField(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }

    private static DerivedPlan FreezeAndDerivePlan(CadOperationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var frozen = CloneBatch(batch);
        var failures = CadContractValidator.Validate(frozen);
        if (failures.Length != 0)
        {
            throw new InvalidOperationException(
                "CAD plan failed trusted schema validation: "
                + string.Join(", ", failures.Select(static failure => failure.Code)));
        }

        var normalizedPlanHash = CadPlanHash.Compute(frozen);
        var operationCount = frozen.Operations.Length;
        var createdEntityCount = 0;
        var modifiedEntityCount = 0;
        var deletedEntityCount = 0;
        var targetEntityCount = 0;
        var hasTransform = false;
        var hasErase = false;

        checked
        {
            foreach (var operation in frozen.Operations)
            {
                switch (operation)
                {
                    case CreateLineOperation:
                        createdEntityCount++;
                        break;
                    case TransformEntitiesOperation transform:
                        hasTransform = true;
                        modifiedEntityCount += transform.Handles.Length;
                        targetEntityCount += transform.Handles.Length;
                        break;
                    case EraseEntitiesOperation erase:
                        hasErase = true;
                        deletedEntityCount += erase.Handles.Length;
                        targetEntityCount += erase.Handles.Length;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "CAD plan contains an operation outside the trusted whitelist.");
                }
            }
        }

        var effectiveAction = hasErase
            ? CadActionKind.DeleteEntity
            : hasTransform
                ? CadActionKind.TransformEntity
                : CadActionKind.CreateEntity;
        var affectedEntityCount = checked(
            createdEntityCount + modifiedEntityCount + deletedEntityCount);
        var binding = new CadApprovalBinding(
            frozen.ThreadId,
            frozen.TurnId,
            normalizedPlanHash,
            frozen.Document.DrawingFingerprint,
            frozen.Document.Revision,
            frozen.SelectionSnapshotHash);
        var plan = new CadApprovalPlanSnapshot(
            frozen.BatchId,
            normalizedPlanHash,
            effectiveAction,
            operationCount,
            createdEntityCount,
            modifiedEntityCount,
            deletedEntityCount,
            targetEntityCount,
            frozen.RequiresSelectionRevalidation);
        var action = new CadActionDescriptor(effectiveAction)
        {
            OperationCount = operationCount,
            AffectedEntityCount = affectedEntityCount,
            DeletedEntityCount = deletedEntityCount,
            TargetEntityCount = targetEntityCount,
        };

        return new DerivedPlan(binding, plan, action);
    }

    private static CadOperationBatch CloneBatch(CadOperationBatch source)
    {
        var document = source.Document
            ?? throw new InvalidOperationException("CAD plan document is required.");
        var operations = source.Operations
            ?? throw new InvalidOperationException("CAD plan operations are required.");
        var clonedOperations = new CadOperation[operations.Length];
        for (var index = 0; index < operations.Length; index++)
        {
            clonedOperations[index] = CloneOperation(operations[index]);
        }

        return new CadOperationBatch
        {
            ProtocolVersion = source.ProtocolVersion,
            BatchId = source.BatchId,
            ThreadId = source.ThreadId,
            TurnId = source.TurnId,
            Document = new CadDocumentRef
            {
                DocumentId = document.DocumentId,
                DisplayName = document.DisplayName,
                PathHash = document.PathHash,
                DrawingFingerprint = document.DrawingFingerprint,
                Revision = document.Revision,
                CurrentSpace = document.CurrentSpace,
                DrawingVersion = document.DrawingVersion,
            },
            SelectionSnapshotHash = source.SelectionSnapshotHash,
            RequiresSelectionRevalidation = source.RequiresSelectionRevalidation,
            DeclaredRisk = source.DeclaredRisk,
            Operations = clonedOperations,
        };
    }

    private static CadOperation CloneOperation(CadOperation? operation)
    {
        return operation switch
        {
            CreateLineOperation line => new CreateLineOperation
            {
                OperationId = line.OperationId,
                Start = ClonePoint(line.Start, "start"),
                End = ClonePoint(line.End, "end"),
                Layer = line.Layer,
                LayerHandle = line.LayerHandle,
                OwnerSpaceHandle = line.OwnerSpaceHandle,
            },
            EraseEntitiesOperation erase => new EraseEntitiesOperation
            {
                OperationId = erase.OperationId,
                Handles = erase.Handles?.ToArray()
                    ?? throw new InvalidOperationException("Erase handles are required."),
            },
            TransformEntitiesOperation transform => new TransformEntitiesOperation
            {
                OperationId = transform.OperationId,
                Handles = transform.Handles?.ToArray()
                    ?? throw new InvalidOperationException("Transform handles are required."),
                Translation = ClonePoint(transform.Translation, "translation"),
                RotationRadians = transform.RotationRadians,
                UniformScale = transform.UniformScale,
            },
            null => throw new InvalidOperationException("CAD plan operation cannot be null."),
            _ => throw new InvalidOperationException(
                "CAD plan contains an operation outside the trusted whitelist."),
        };
    }

    private static CadPoint3 ClonePoint(CadPoint3? point, string field)
    {
        if (point is null)
        {
            throw new InvalidOperationException("CAD plan " + field + " point is required.");
        }

        return new CadPoint3(point.X, point.Y, point.Z);
    }

    private static void ExpireEntry(Entry entry)
    {
        ClearToken(entry);
        entry.State = CadApprovalState.Expired;
    }

    private static void ClearToken(Entry entry)
    {
        if (entry.TokenDigest is not null)
        {
            CryptographicOperations.ZeroMemory(entry.TokenDigest);
            entry.TokenDigest = null;
        }
    }

    private static void ClearCheckpointSeal(Entry entry)
    {
        if (entry.CheckpointSeal is not null)
        {
            CryptographicOperations.ZeroMemory(entry.CheckpointSeal);
            entry.CheckpointSeal = null;
        }
    }

    private static ApprovalOperationResult OperationSuccess(CadApprovalState state, string reason) =>
        new(true, ApprovalFailureReason.None, state, reason);

    private static ApprovalOperationResult OperationFailure(
        ApprovalFailureReason failure,
        CadApprovalState? state,
        string reason) => new(false, failure, state, reason);

    private static ApprovalIssueResult IssueFailure(
        ApprovalFailureReason failure,
        CadApprovalState? state,
        string reason) => new(false, failure, state, reason, null);

    private static ApprovalConsumptionResult ConsumptionFailure(
        ApprovalFailureReason failure,
        CadApprovalState? state,
        string reason) => new(false, failure, state, reason);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class Entry
    {
        public Entry(
            Guid requestId,
            CadApprovalBinding binding,
            RiskAssessment risk,
            CadApprovalPlanSnapshot? plan,
            DateTimeOffset createdAt)
        {
            RequestId = requestId;
            Binding = binding;
            Risk = risk;
            Plan = plan;
            CreatedAt = createdAt;
        }

        public Guid RequestId { get; }

        public CadApprovalBinding Binding { get; }

        public RiskAssessment Risk { get; }

        public CadApprovalPlanSnapshot? Plan { get; }

        public DateTimeOffset CreatedAt { get; }

        public CadApprovalState State { get; set; } = CadApprovalState.Proposed;

        public DateTimeOffset? ApprovedAt { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public long? ApprovedTimestamp { get; set; }

        public byte[]? TokenDigest { get; set; }

        public CadCheckpointEvidence? Checkpoint { get; set; }

        public DateTimeOffset? CheckpointRecordedAt { get; set; }

        public byte[]? CheckpointSeal { get; set; }

        public CadApprovalRequestSnapshot ToSnapshot() =>
            new(
                RequestId,
                Binding,
                Risk,
                Plan,
                Checkpoint is null
                    ? null
                    : new CadCheckpointAuditSnapshot(
                        Checkpoint,
                        CheckpointSeal is null
                            ? string.Empty
                            : Convert.ToHexString(CheckpointSeal).ToLowerInvariant(),
                        CheckpointRecordedAt ?? DateTimeOffset.MinValue),
                State,
                CreatedAt,
                ApprovedAt,
                ExpiresAt);
    }

    private sealed record DerivedPlan(
        CadApprovalBinding Binding,
        CadApprovalPlanSnapshot Plan,
        CadActionDescriptor Action);
}

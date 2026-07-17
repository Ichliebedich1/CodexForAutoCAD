using System.Security.Cryptography;
using System.Text;

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
}

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
    private bool _disposed;

    public CadApprovalGate(TimeProvider? timeProvider = null, ResourceQuotaPolicy? resourceQuotas = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resourceQuotas = resourceQuotas ?? new ResourceQuotaPolicy();
    }

    public static TimeSpan TokenLifetime => ApprovalLifetime;

    public Guid Propose(CadApprovalBinding binding, CadActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(action);

        // Classification happens inside the trusted gate so a caller cannot forge a permissive
        // RiskAssessment record and bypass default-deny policy.
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
            _entries.Add(requestId, new Entry(requestId, binding, risk, _timeProvider.GetUtcNow()));
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

    public ApprovalOperationResult AwaitUserDecision(Guid requestId) =>
        Transition(requestId, CadApprovalState.PreviewReady, CadApprovalState.AwaitingUser, "APPROVAL_AWAITING_USER");

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

            var secret = RandomNumberGenerator.GetBytes(SecretSizeBytes);
            entry.TokenDigest = SHA256.HashData(secret);
            entry.ApprovedAt = _timeProvider.GetUtcNow();
            entry.ApprovedTimestamp = _timeProvider.GetTimestamp();
            entry.ExpiresAt = entry.ApprovedAt + ApprovalLifetime;
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

            entry.State = CadApprovalState.DocumentLocked;
            return OperationSuccess(entry.State, "APPROVAL_DOCUMENT_LOCKED");
        }
    }

    public ApprovalConsumptionResult ValidateAndConsume(
        CadApprovalToken? token,
        CadApprovalBinding currentBinding)
    {
        ArgumentNullException.ThrowIfNull(currentBinding);

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

            if (IsExpired(entry))
            {
                ExpireEntry(entry);
                token.Dispose();
                return ConsumptionFailure(ApprovalFailureReason.TokenExpired, entry.State, "APPROVAL_TOKEN_EXPIRED");
            }

            if (!MatchesStoredDigest(token, entry))
            {
                return ConsumptionFailure(ApprovalFailureReason.TokenMismatch, entry.State, "APPROVAL_TOKEN_MISMATCH");
            }

            if (entry.Binding != currentBinding)
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

    public ApprovalOperationResult BeginExecution(Guid requestId) =>
        Transition(requestId, CadApprovalState.RevisionRevalidated, CadApprovalState.Executing, "APPROVAL_EXECUTING");

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
            }

            _entries.Clear();
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

    private static bool MatchesStoredDigest(CadApprovalToken token, Entry entry)
    {
        if (entry.TokenDigest is null || token.Secret.Length != SecretSizeBytes)
        {
            return false;
        }

        Span<byte> candidateDigest = stackalloc byte[32];
        SHA256.HashData(token.Secret, candidateDigest);
        var matches = CryptographicOperations.FixedTimeEquals(candidateDigest, entry.TokenDigest);
        CryptographicOperations.ZeroMemory(candidateDigest);
        return matches;
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
            DateTimeOffset createdAt)
        {
            RequestId = requestId;
            Binding = binding;
            Risk = risk;
            CreatedAt = createdAt;
        }

        public Guid RequestId { get; }

        public CadApprovalBinding Binding { get; }

        public RiskAssessment Risk { get; }

        public DateTimeOffset CreatedAt { get; }

        public CadApprovalState State { get; set; } = CadApprovalState.Proposed;

        public DateTimeOffset? ApprovedAt { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public long? ApprovedTimestamp { get; set; }

        public byte[]? TokenDigest { get; set; }

        public CadApprovalRequestSnapshot ToSnapshot() =>
            new(RequestId, Binding, Risk, State, CreatedAt, ApprovedAt, ExpiresAt);
    }
}

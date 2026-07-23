using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codex.AutoCAD.AppServer;

namespace Codex.AutoCAD.AgentHost;

public static class AgentHostAuditEventTypes
{
    public const string SessionStarted = "session_started";
    public const string SessionStopped = "session_stopped";
    public const string SessionFailed = "session_failed";
    public const string BridgeConnected = "bridge_connected";
    public const string BridgeDisconnected = "bridge_disconnected";
    public const string RequestReceived = "request_received";
    public const string RequestCompleted = "request_completed";
    public const string RequestFailed = "request_failed";
    public const string ThreadStarted = "thread_started";
    public const string TurnStarted = "turn_started";
    public const string CancelRequested = "cancel_requested";
    public const string CancelDispatched = "cancel_dispatched";
    public const string ApprovalRequested = "approval_requested";
    public const string TurnCompleted = "turn_completed";
    public const string TurnCancelled = "turn_cancelled";
    public const string TurnFailed = "turn_failed";

    internal static bool IsKnown(string value)
        => value is SessionStarted
            or SessionStopped
            or SessionFailed
            or BridgeConnected
            or BridgeDisconnected
            or RequestReceived
            or RequestCompleted
            or RequestFailed
            or ThreadStarted
            or TurnStarted
            or CancelRequested
            or CancelDispatched
            or ApprovalRequested
            or TurnCompleted
            or TurnCancelled
            or TurnFailed;
}

public static class AgentHostAuditOutcomeCodes
{
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Connected = "connected";
    public const string Disconnected = "disconnected";
    public const string Dispatched = "dispatched";
    public const string Failed = "failed";
}

public static class AgentHostAuditErrorCodes
{
    public const string AccessDenied = "access_denied";
    public const string AuditUnavailable = "audit_unavailable";
    public const string CodexVersionInvalidOutput = "codex_version_invalid_output";
    public const string CodexVersionProcessFailed = "codex_version_process_failed";
    public const string CodexVersionTimedOut = "codex_version_timeout";
    public const string CodexVersionUnsupported = "codex_version_unsupported";
    public const string CodexCredentialReferenceInvalid = "codex_credential_reference_invalid";
    public const string CodexCredentialUnavailable = "codex_credential_unavailable";
    public const string CodexCredentialRejected = "codex_credential_rejected";
    public const string CodexSessionWorkspaceUnavailable = "codex_session_workspace_unavailable";
    public const string InvalidRequest = "invalid_request";
    public const string InvalidState = "invalid_state";
    public const string IoFailure = "io_failure";
    public const string RequestCancelled = "request_cancelled";
    public const string SessionAbandoned = "session_abandoned";
    public const string Timeout = "timeout";
    public const string UnexpectedFailure = "unexpected_failure";

    public static string FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            AgentHostAuditException => AuditUnavailable,
            CodexVersionPreflightException
            {
                Failure: CodexVersionPreflightFailure.UnsupportedVersion
            } => CodexVersionUnsupported,
            CodexVersionPreflightException
            {
                Failure: CodexVersionPreflightFailure.TimedOut
            } => CodexVersionTimedOut,
            CodexVersionPreflightException
            {
                Failure: CodexVersionPreflightFailure.InvalidVersionOutput
                    or CodexVersionPreflightFailure.VersionOutputTooLarge
            } => CodexVersionInvalidOutput,
            CodexVersionPreflightException => CodexVersionProcessFailed,
            AgentHostCodexSessionIsolationException
            {
                Failure: AgentHostCodexSessionIsolationFailure.InvalidCredentialReference
            } => CodexCredentialReferenceInvalid,
            AgentHostCodexSessionIsolationException
            {
                Failure: AgentHostCodexSessionIsolationFailure.CredentialUnavailable
            } => CodexCredentialUnavailable,
            AgentHostCodexSessionIsolationException
            {
                Failure: AgentHostCodexSessionIsolationFailure.CredentialRejected
            } => CodexCredentialRejected,
            AgentHostCodexSessionIsolationException => CodexSessionWorkspaceUnavailable,
            OperationCanceledException => RequestCancelled,
            TimeoutException => Timeout,
            InvalidDataException or JsonException or ArgumentException => InvalidRequest,
            UnauthorizedAccessException => AccessDenied,
            IOException => IoFailure,
            InvalidOperationException => InvalidState,
            _ => UnexpectedFailure,
        };
    }
}

public sealed class AgentHostAuditEvent
{
    public string EventType { get; init; } = string.Empty;

    public string? SystemConversationId { get; init; }

    public string? SystemRequestId { get; init; }

    public string? BridgeRequestId { get; init; }

    public string? ProviderThreadId { get; init; }

    public string? ProviderTurnId { get; init; }

    public string? Method { get; init; }

    public string? ApprovalKind { get; init; }

    public string? Resolution { get; init; }

    public string? OutcomeCode { get; init; }

    public string? ErrorCode { get; init; }
}

public sealed class AgentHostAuditException : Exception
{
    public AgentHostAuditException(string message)
        : base(message)
    {
    }

    public AgentHostAuditException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal enum AgentHostAuditIntegrityFailure
{
    None,
    EmptyLog,
    TooLarge,
    TooManyRecords,
    InvalidUtf8,
    InvalidJson,
    UnexpectedField,
    DuplicateField,
    MissingField,
    InvalidField,
    SchemaMismatch,
    SessionMismatch,
    SequenceMismatch,
    PreviousHashMismatch,
    RecordHashMismatch,
    NonCanonicalRecord,
    InitialRecordInvalid,
    TerminalRecordMissing,
    TerminalRecordNotLast,
}

internal sealed class AgentHostAuditIntegrityResult
{
    internal AgentHostAuditIntegrityResult(
        bool isValid,
        AgentHostAuditIntegrityFailure failure,
        long recordCount,
        string? terminalRecordHash)
    {
        IsValid = isValid;
        Failure = failure;
        RecordCount = recordCount;
        TerminalRecordHash = terminalRecordHash;
    }

    internal bool IsValid { get; }

    internal AgentHostAuditIntegrityFailure Failure { get; }

    internal long RecordCount { get; }

    internal string? TerminalRecordHash { get; }
}

public sealed class AgentHostAuditLog : IDisposable, IAsyncDisposable
{
    public const string Schema = "codex.autocad.agenthost.audit/2";
    public const int DefaultMaximumRecords = 10_000;
    public const long DefaultMaximumBytes = 4L * 1024 * 1024;
    public const int DefaultMaximumRetainedFiles = 512;
    public static readonly TimeSpan DefaultRetentionAge = TimeSpan.FromDays(30);

    private const int MaximumIdentifierCharacters = 256;
    private const int MaximumCodeCharacters = 128;
    private const int AbsoluteMaximumRecords = 1_000_000;
    private const long AbsoluteMaximumBytes = 64L * 1024 * 1024;
    private const int AbsoluteMaximumRetainedFiles = 4096;
    private const int RecordHashCharacters = 64;
    private const int MaximumAuditJsonDepth = 16;
    private static readonly string InitialPreviousRecordHash = new('0', RecordHashCharacters);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonDocumentOptions IntegrityJsonDocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumAuditJsonDepth,
    };

    private readonly Stream _destination;
    private readonly bool _leaveOpen;
    private readonly string _sessionId;
    private readonly int _maximumRecords;
    private readonly long _maximumBytes;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _sync = new();
    private long _sequence;
    private long _bytesWritten;
    private string _previousRecordHash = InitialPreviousRecordHash;
    private int _terminal;
    private int _faulted;
    private int _disposed;

    public AgentHostAuditLog(
        Stream destination,
        string sessionId,
        bool leaveOpen = false,
        int maximumRecords = DefaultMaximumRecords,
        long maximumBytes = DefaultMaximumBytes,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("AgentHost audit destination must be writable.", nameof(destination));
        }

        ValidateIdentifier(sessionId, nameof(sessionId));
        if (maximumRecords is < 2 or > AbsoluteMaximumRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        }

        if (maximumBytes is < 1024 or > AbsoluteMaximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        _destination = destination;
        _sessionId = sessionId;
        _leaveOpen = leaveOpen;
        _maximumRecords = maximumRecords;
        _maximumBytes = maximumBytes;
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
        WriteCore(new AgentHostAuditEvent
        {
            EventType = AgentHostAuditEventTypes.SessionStarted,
        });
    }

    public static AgentHostAuditLog CreateForCurrentUser(string sessionId)
    {
        ValidateBootstrapSessionId(sessionId);
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new AgentHostAuditException("Local application data is unavailable.");
        }

        var auditDirectory = Path.Combine(
            localApplicationData,
            "OpenAI",
            "CodexForAutoCAD",
            "audit",
            "agenthost");
        return CreateForDirectory(auditDirectory, sessionId);
    }

    internal static AgentHostAuditLog CreateForDirectory(
        string auditDirectory,
        string sessionId,
        TimeSpan? retentionAge = null,
        int maximumRetainedFiles = DefaultMaximumRetainedFiles,
        DateTime? utcNow = null)
    {
        ValidateBootstrapSessionId(sessionId);
        var maximumAge = retentionAge ?? DefaultRetentionAge;
        if (maximumAge < TimeSpan.Zero || maximumAge > TimeSpan.FromDays(3650))
        {
            throw new ArgumentOutOfRangeException(nameof(retentionAge));
        }

        if (maximumRetainedFiles is < 2 or > AbsoluteMaximumRetainedFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedFiles));
        }

        try
        {
            var safeAuditDirectory = AgentHostPrivateStorage.PreparePrivateDirectory(
                auditDirectory);
            PruneAuditFiles(
                safeAuditDirectory,
                maximumAge,
                maximumRetainedFiles,
                utcNow ?? DateTime.UtcNow);
            var auditPath = Path.Combine(safeAuditDirectory, sessionId + ".jsonl");
            try
            {
                using (new FileStream(
                           auditPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           1,
                           FileOptions.WriteThrough))
                {
                }
                AgentHostPrivateStorage.ApplyPrivateFileAcl(auditPath);
                var stream = new FileStream(
                    auditPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);
                try
                {
                    return new AgentHostAuditLog(stream, sessionId);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch
            {
                TryDeleteFailedAuditFile(auditPath);
                throw;
            }
        }
        catch (AgentHostAuditException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or AgentHostPrivateStorageException
            or System.Security.SecurityException)
        {
            throw new AgentHostAuditException(
                "AgentHost audit file could not be created safely.",
                exception);
        }
    }

    public void Record(AgentHostAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (_terminal != 0)
            {
                throw new AgentHostAuditException("AgentHost audit session is already terminal.");
            }

            WriteCore(auditEvent);
        }
    }

    public void Complete()
    {
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (_terminal != 0)
            {
                return;
            }

            WriteCore(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.SessionStopped,
                OutcomeCode = AgentHostAuditOutcomeCodes.Completed,
            });
            _terminal = 1;
        }
    }

    public void Fail(string errorCode)
    {
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (_terminal != 0)
            {
                return;
            }

            ValidateCode(errorCode, nameof(errorCode));
            WriteCore(new AgentHostAuditEvent
            {
                EventType = AgentHostAuditEventTypes.SessionFailed,
                OutcomeCode = AgentHostAuditOutcomeCodes.Failed,
                ErrorCode = errorCode,
            });
            _terminal = 1;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed != 0)
            {
                return;
            }

            try
            {
                if (_faulted == 0 && _terminal == 0)
                {
                    WriteCore(new AgentHostAuditEvent
                    {
                        EventType = AgentHostAuditEventTypes.SessionFailed,
                        OutcomeCode = AgentHostAuditOutcomeCodes.Failed,
                        ErrorCode = AgentHostAuditErrorCodes.SessionAbandoned,
                    });
                    _terminal = 1;
                }
            }
            finally
            {
                _disposed = 1;
                if (!_leaveOpen)
                {
                    _destination.Dispose();
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Verifies the bounded, canonical SHA-256 record chain emitted by this process.
    /// This detects accidental corruption and simple tampering; it is not a substitute for
    /// externally protected, signed, or append-only storage.
    /// </summary>
    internal static AgentHostAuditIntegrityResult VerifyIntegrity(
        Stream source,
        int maximumRecords = DefaultMaximumRecords,
        long maximumBytes = DefaultMaximumBytes,
        string? expectedSessionId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("AgentHost audit source must be readable.", nameof(source));
        }

        ValidateIntegrityLimits(maximumRecords, maximumBytes);
        if (expectedSessionId is not null)
        {
            ValidateIdentifier(expectedSessionId, nameof(expectedSessionId));
        }

        var bytes = ReadBounded(source, maximumBytes);
        if (bytes is null)
        {
            return IntegrityFailure(AgentHostAuditIntegrityFailure.TooLarge, 0);
        }

        if (bytes.Length == 0)
        {
            return IntegrityFailure(AgentHostAuditIntegrityFailure.EmptyLog, 0);
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return IntegrityFailure(AgentHostAuditIntegrityFailure.InvalidUtf8, 0);
        }

        if (!text.EndsWith('\n'))
        {
            return IntegrityFailure(AgentHostAuditIntegrityFailure.InvalidJson, 0);
        }

        var lines = text[..^1].Split('\n');
        if (lines.Length > maximumRecords)
        {
            return IntegrityFailure(AgentHostAuditIntegrityFailure.TooManyRecords, 0);
        }

        var expectedSequence = 1L;
        var expectedPreviousRecordHash = InitialPreviousRecordHash;
        var observedSessionId = expectedSessionId;
        var terminalSeen = false;
        string? terminalRecordHash = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var recordCount = index + 1L;
            if (lines[index].Length == 0)
            {
                return IntegrityFailure(AgentHostAuditIntegrityFailure.InvalidJson, recordCount);
            }

            var lineBytes = StrictUtf8.GetBytes(lines[index]);
            if (!TryParseEnvelope(lineBytes, out var envelope, out var parseFailure))
            {
                return IntegrityFailure(parseFailure, recordCount);
            }

            if (terminalSeen)
            {
                return IntegrityFailure(
                    AgentHostAuditIntegrityFailure.TerminalRecordNotLast,
                    recordCount);
            }

            if (index == 0 && envelope.EventType != AgentHostAuditEventTypes.SessionStarted)
            {
                return IntegrityFailure(
                    AgentHostAuditIntegrityFailure.InitialRecordInvalid,
                    recordCount);
            }

            if (envelope.Sequence != expectedSequence)
            {
                return IntegrityFailure(AgentHostAuditIntegrityFailure.SequenceMismatch, recordCount);
            }

            if (observedSessionId is null)
            {
                observedSessionId = envelope.SessionId;
            }
            else if (!string.Equals(
                         observedSessionId,
                         envelope.SessionId,
                         StringComparison.Ordinal))
            {
                return IntegrityFailure(AgentHostAuditIntegrityFailure.SessionMismatch, recordCount);
            }

            if (!string.Equals(
                    expectedPreviousRecordHash,
                    envelope.PreviousRecordHash,
                    StringComparison.Ordinal))
            {
                return IntegrityFailure(
                    AgentHostAuditIntegrityFailure.PreviousHashMismatch,
                    recordCount);
            }

            var expectedRecordHash = ComputeRecordHash(envelope);
            if (!string.Equals(expectedRecordHash, envelope.RecordHash, StringComparison.Ordinal))
            {
                return IntegrityFailure(
                    AgentHostAuditIntegrityFailure.RecordHashMismatch,
                    recordCount);
            }

            var canonicalBytes = SerializeCanonicalEnvelope(envelope, includeRecordHash: true);
            if (!lineBytes.AsSpan().SequenceEqual(canonicalBytes))
            {
                return IntegrityFailure(
                    AgentHostAuditIntegrityFailure.NonCanonicalRecord,
                    recordCount);
            }

            expectedSequence = checked(expectedSequence + 1);
            expectedPreviousRecordHash = envelope.RecordHash;
            if (IsTerminalEvent(envelope.EventType))
            {
                terminalSeen = true;
                terminalRecordHash = envelope.RecordHash;
            }
        }

        return terminalSeen
            ? new AgentHostAuditIntegrityResult(
                isValid: true,
                AgentHostAuditIntegrityFailure.None,
                lines.Length,
                terminalRecordHash)
            : IntegrityFailure(AgentHostAuditIntegrityFailure.TerminalRecordMissing, lines.Length);
    }

    private void WriteCore(AgentHostAuditEvent auditEvent)
    {
        ValidateEvent(auditEvent);
        var nextSequence = checked(_sequence + 1);
        var envelope = new AgentHostAuditEnvelope
        {
            Schema = Schema,
            Sequence = nextSequence,
            TimestampUtc = _utcNow().ToUniversalTime().ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture),
            SessionId = _sessionId,
            EventType = auditEvent.EventType,
            SystemConversationId = auditEvent.SystemConversationId,
            SystemRequestId = auditEvent.SystemRequestId,
            BridgeRequestId = auditEvent.BridgeRequestId,
            ProviderThreadId = auditEvent.ProviderThreadId,
            ProviderTurnId = auditEvent.ProviderTurnId,
            Method = auditEvent.Method,
            ApprovalKind = auditEvent.ApprovalKind,
            Resolution = auditEvent.Resolution,
            OutcomeCode = auditEvent.OutcomeCode,
            ErrorCode = auditEvent.ErrorCode,
            PreviousRecordHash = _previousRecordHash,
        };
        envelope.RecordHash = ComputeRecordHash(envelope);
        var recordBytes = SerializeCanonicalEnvelope(envelope, includeRecordHash: true);
        var bytes = new byte[recordBytes.Length + 1];
        Buffer.BlockCopy(recordBytes, 0, bytes, 0, recordBytes.Length);
        bytes[^1] = (byte)'\n';
        if (nextSequence > _maximumRecords
            || _bytesWritten > _maximumBytes - bytes.Length)
        {
            _faulted = 1;
            throw new AgentHostAuditException("AgentHost audit capacity is exhausted.");
        }

        try
        {
            _destination.Write(bytes, 0, bytes.Length);
            FlushDurably();
            _sequence = nextSequence;
            _bytesWritten += bytes.Length;
            _previousRecordHash = envelope.RecordHash;
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            _faulted = 1;
            throw new AgentHostAuditException("AgentHost audit write failed.", exception);
        }
    }

    private void FlushDurably()
    {
        if (_destination is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
        else
        {
            _destination.Flush();
        }
    }

    private void ThrowIfUnavailable()
    {
        if (_disposed != 0)
        {
            throw new ObjectDisposedException(nameof(AgentHostAuditLog));
        }

        if (_faulted != 0)
        {
            throw new AgentHostAuditException("AgentHost audit is unavailable after a prior failure.");
        }
    }

    private static void ValidateEvent(AgentHostAuditEvent auditEvent)
    {
        if (!AgentHostAuditEventTypes.IsKnown(auditEvent.EventType))
        {
            throw new ArgumentException("Unknown AgentHost audit event type.", nameof(auditEvent));
        }

        ValidateOptionalIdentifier(auditEvent.SystemConversationId, "systemConversationId");
        ValidateOptionalIdentifier(auditEvent.SystemRequestId, "systemRequestId");
        ValidateOptionalIdentifier(auditEvent.BridgeRequestId, "bridgeRequestId");
        ValidateOptionalIdentifier(auditEvent.ProviderThreadId, "providerThreadId");
        ValidateOptionalIdentifier(auditEvent.ProviderTurnId, "providerTurnId");
        ValidateOptionalCode(auditEvent.Method, "method");
        ValidateOptionalCode(auditEvent.ApprovalKind, "approvalKind");
        ValidateOptionalCode(auditEvent.Resolution, "resolution");
        ValidateOptionalCode(auditEvent.OutcomeCode, "outcomeCode");
        ValidateOptionalCode(auditEvent.ErrorCode, "errorCode");
    }

    private static void ValidateBootstrapSessionId(string sessionId)
    {
        if (sessionId.Length != 32
            || !sessionId.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f'))
        {
            throw new AgentHostAuditException("AgentHost audit session id is invalid.");
        }
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (!IsValidIdentifier(value))
        {
            throw new ArgumentException("Audit identifier is invalid.", parameterName);
        }
    }

    private static void ValidateOptionalIdentifier(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateIdentifier(value, parameterName);
        }
    }

    private static void ValidateCode(string value, string parameterName)
    {
        if (!IsValidCode(value))
        {
            throw new ArgumentException("Audit code is invalid.", parameterName);
        }
    }

    private static void ValidateOptionalCode(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateCode(value, parameterName);
        }
    }

    private static bool IsSafeIdentifierCharacter(char character)
        => character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-' or '_' or '.' or ':';

    private static bool IsValidIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaximumIdentifierCharacters
            && value.All(IsSafeIdentifierCharacter);

    private static bool IsValidCode(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaximumCodeCharacters
            && value.All(IsSafeIdentifierCharacter);

    private static bool IsValidRecordHash(string? value)
        => value is { Length: RecordHashCharacters }
            && value.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static bool IsTerminalEvent(string eventType)
        => eventType is AgentHostAuditEventTypes.SessionStopped
            or AgentHostAuditEventTypes.SessionFailed;

    private static AgentHostAuditIntegrityResult IntegrityFailure(
        AgentHostAuditIntegrityFailure failure,
        long recordCount)
        => new(isValid: false, failure, recordCount, terminalRecordHash: null);

    private static void ValidateIntegrityLimits(int maximumRecords, long maximumBytes)
    {
        if (maximumRecords is < 2 or > AbsoluteMaximumRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        }

        if (maximumBytes is < 1024 or > AbsoluteMaximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
    }

    private static byte[]? ReadBounded(Stream source, long maximumBytes)
    {
        using var buffer = new MemoryStream();
        var readBuffer = new byte[8192];
        while (true)
        {
            var read = source.Read(readBuffer, 0, readBuffer.Length);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length > maximumBytes - read)
            {
                return null;
            }

            buffer.Write(readBuffer, 0, read);
        }
    }

    private static bool TryParseEnvelope(
        byte[] lineBytes,
        out AgentHostAuditEnvelope envelope,
        out AgentHostAuditIntegrityFailure failure)
    {
        envelope = new AgentHostAuditEnvelope();
        failure = AgentHostAuditIntegrityFailure.InvalidJson;
        try
        {
            using var document = JsonDocument.Parse(lineBytes, IntegrityJsonDocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? schema = null;
            long? sequence = null;
            string? timestampUtc = null;
            string? sessionId = null;
            string? eventType = null;
            string? systemConversationId = null;
            string? systemRequestId = null;
            string? bridgeRequestId = null;
            string? providerThreadId = null;
            string? providerTurnId = null;
            string? method = null;
            string? approvalKind = null;
            string? resolution = null;
            string? outcomeCode = null;
            string? errorCode = null;
            string? previousRecordHash = null;
            string? recordHash = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    failure = AgentHostAuditIntegrityFailure.DuplicateField;
                    return false;
                }

                switch (property.Name)
                {
                    case "schema":
                        if (!TryGetRequiredString(property.Value, out schema))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "sequence":
                        if (property.Value.ValueKind != JsonValueKind.Number
                            || !property.Value.TryGetInt64(out var parsedSequence))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        sequence = parsedSequence;
                        break;
                    case "timestampUtc":
                        if (!TryGetRequiredString(property.Value, out timestampUtc))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "sessionId":
                        if (!TryGetRequiredString(property.Value, out sessionId))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "eventType":
                        if (!TryGetRequiredString(property.Value, out eventType))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "systemConversationId":
                        if (!TryGetOptionalString(property.Value, out systemConversationId))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "systemRequestId":
                        if (!TryGetOptionalString(property.Value, out systemRequestId))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "bridgeRequestId":
                        if (!TryGetOptionalString(property.Value, out bridgeRequestId))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "providerThreadId":
                        if (!TryGetOptionalString(property.Value, out providerThreadId))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "providerTurnId":
                        if (!TryGetOptionalString(property.Value, out providerTurnId))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "method":
                        if (!TryGetOptionalString(property.Value, out method))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "approvalKind":
                        if (!TryGetOptionalString(property.Value, out approvalKind))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "resolution":
                        if (!TryGetOptionalString(property.Value, out resolution))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "outcomeCode":
                        if (!TryGetOptionalString(property.Value, out outcomeCode))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "errorCode":
                        if (!TryGetOptionalString(property.Value, out errorCode))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "previousRecordHash":
                        if (!TryGetRequiredString(property.Value, out previousRecordHash))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    case "recordHash":
                        if (!TryGetRequiredString(property.Value, out recordHash))
                        {
                            failure = AgentHostAuditIntegrityFailure.InvalidField;
                            return false;
                        }

                        break;
                    default:
                        failure = AgentHostAuditIntegrityFailure.UnexpectedField;
                        return false;
                }
            }

            if (schema is null
                || sequence is null
                || timestampUtc is null
                || sessionId is null
                || eventType is null
                || previousRecordHash is null
                || recordHash is null)
            {
                failure = AgentHostAuditIntegrityFailure.MissingField;
                return false;
            }

            envelope = new AgentHostAuditEnvelope
            {
                Schema = schema,
                Sequence = sequence.Value,
                TimestampUtc = timestampUtc,
                SessionId = sessionId,
                EventType = eventType,
                SystemConversationId = systemConversationId,
                SystemRequestId = systemRequestId,
                BridgeRequestId = bridgeRequestId,
                ProviderThreadId = providerThreadId,
                ProviderTurnId = providerTurnId,
                Method = method,
                ApprovalKind = approvalKind,
                Resolution = resolution,
                OutcomeCode = outcomeCode,
                ErrorCode = errorCode,
                PreviousRecordHash = previousRecordHash,
                RecordHash = recordHash,
            };
        }
        catch (JsonException)
        {
            failure = AgentHostAuditIntegrityFailure.InvalidJson;
            return false;
        }

        return TryValidateEnvelope(envelope, out failure);
    }

    private static bool TryValidateEnvelope(
        AgentHostAuditEnvelope envelope,
        out AgentHostAuditIntegrityFailure failure)
    {
        if (!string.Equals(envelope.Schema, Schema, StringComparison.Ordinal))
        {
            failure = AgentHostAuditIntegrityFailure.SchemaMismatch;
            return false;
        }

        if (envelope.Sequence <= 0
            || !IsCanonicalUtcTimestamp(envelope.TimestampUtc)
            || !IsValidIdentifier(envelope.SessionId)
            || !AgentHostAuditEventTypes.IsKnown(envelope.EventType)
            || !IsValidOptionalIdentifier(envelope.SystemConversationId)
            || !IsValidOptionalIdentifier(envelope.SystemRequestId)
            || !IsValidOptionalIdentifier(envelope.BridgeRequestId)
            || !IsValidOptionalIdentifier(envelope.ProviderThreadId)
            || !IsValidOptionalIdentifier(envelope.ProviderTurnId)
            || !IsValidOptionalCode(envelope.Method)
            || !IsValidOptionalCode(envelope.ApprovalKind)
            || !IsValidOptionalCode(envelope.Resolution)
            || !IsValidOptionalCode(envelope.OutcomeCode)
            || !IsValidOptionalCode(envelope.ErrorCode)
            || !IsValidRecordHash(envelope.PreviousRecordHash)
            || !IsValidRecordHash(envelope.RecordHash))
        {
            failure = AgentHostAuditIntegrityFailure.InvalidField;
            return false;
        }

        failure = AgentHostAuditIntegrityFailure.None;
        return true;
    }

    private static bool TryGetRequiredString(JsonElement element, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    private static bool TryGetOptionalString(JsonElement element, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    private static bool IsCanonicalUtcTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return false;
        }

        return string.Equals(
            value,
            timestamp.ToUniversalTime().ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static bool IsValidOptionalIdentifier(string? value)
        => value is null || IsValidIdentifier(value);

    private static bool IsValidOptionalCode(string? value)
        => value is null || IsValidCode(value);

    private static string ComputeRecordHash(AgentHostAuditEnvelope envelope)
    {
        var canonicalBytes = SerializeCanonicalEnvelope(envelope, includeRecordHash: false);
        return Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
    }

    private static byte[] SerializeCanonicalEnvelope(
        AgentHostAuditEnvelope envelope,
        bool includeRecordHash)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", envelope.Schema);
            writer.WriteNumber("sequence", envelope.Sequence);
            writer.WriteString("timestampUtc", envelope.TimestampUtc);
            writer.WriteString("sessionId", envelope.SessionId);
            writer.WriteString("eventType", envelope.EventType);
            WriteOptionalString(writer, "systemConversationId", envelope.SystemConversationId);
            WriteOptionalString(writer, "systemRequestId", envelope.SystemRequestId);
            WriteOptionalString(writer, "bridgeRequestId", envelope.BridgeRequestId);
            WriteOptionalString(writer, "providerThreadId", envelope.ProviderThreadId);
            WriteOptionalString(writer, "providerTurnId", envelope.ProviderTurnId);
            WriteOptionalString(writer, "method", envelope.Method);
            WriteOptionalString(writer, "approvalKind", envelope.ApprovalKind);
            WriteOptionalString(writer, "resolution", envelope.Resolution);
            WriteOptionalString(writer, "outcomeCode", envelope.OutcomeCode);
            WriteOptionalString(writer, "errorCode", envelope.ErrorCode);
            writer.WriteString("previousRecordHash", envelope.PreviousRecordHash);
            if (includeRecordHash)
            {
                writer.WriteString("recordHash", envelope.RecordHash);
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.ToArray();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void PruneAuditFiles(
        string auditDirectory,
        TimeSpan retentionAge,
        int maximumRetainedFiles,
        DateTime utcNow)
    {
        var discovered = Directory.EnumerateFileSystemEntries(auditDirectory)
            .Take(AbsoluteMaximumRetainedFiles + 1)
            .ToList();
        if (discovered.Count > AbsoluteMaximumRetainedFiles)
        {
            throw new AgentHostAuditException(
                "AgentHost audit retention exceeded its scan limit.");
        }

        var files = discovered
            .Select(path => new AuditFile(path))
            .Where(file => file.IsManaged)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var file in files)
        {
            if (utcNow - file.LastWriteTimeUtc >= retentionAge)
            {
                file.TryDelete();
            }
        }

        files = files.Where(file => File.Exists(file.Path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var remainingCount = files.Count;
        foreach (var file in files)
        {
            if (remainingCount < maximumRetainedFiles)
            {
                break;
            }

            if (file.TryDelete())
            {
                remainingCount--;
            }
        }

        if (remainingCount >= maximumRetainedFiles)
        {
            throw new AgentHostAuditException(
                "AgentHost audit retention is at capacity.");
        }
    }

    private static void TryDeleteFailedAuditFile(string auditPath)
    {
        try
        {
            File.Delete(auditPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class AuditFile
    {
        internal AuditFile(string path)
        {
            Path = path;
            var name = System.IO.Path.GetFileName(path);
            IsManaged = name.EndsWith(".jsonl", StringComparison.Ordinal)
                && AgentHostPrivateStorage.IsLowerHexIdentifier(name[..^6]);
            if (!IsManaged)
            {
                return;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new AgentHostAuditException(
                    "AgentHost audit retention refused a non-file entry.");
            }

            LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        }

        internal string Path { get; }

        internal bool IsManaged { get; }

        internal DateTime LastWriteTimeUtc { get; }

        internal bool TryDelete()
        {
            try
            {
                File.Delete(Path);
                return true;
            }
            catch (IOException exception) when (
                AgentHostPrivateStorage.IsSharingViolation(exception))
            {
                return false;
            }
        }
    }

    private sealed class AgentHostAuditEnvelope
    {
        public string Schema { get; init; } = string.Empty;

        public long Sequence { get; init; }

        public string TimestampUtc { get; init; } = string.Empty;

        public string SessionId { get; init; } = string.Empty;

        public string EventType { get; init; } = string.Empty;

        public string? SystemConversationId { get; init; }

        public string? SystemRequestId { get; init; }

        public string? BridgeRequestId { get; init; }

        public string? ProviderThreadId { get; init; }

        public string? ProviderTurnId { get; init; }

        public string? Method { get; init; }

        public string? ApprovalKind { get; init; }

        public string? Resolution { get; init; }

        public string? OutcomeCode { get; init; }

        public string? ErrorCode { get; init; }

        public string PreviousRecordHash { get; init; } = string.Empty;

        public string RecordHash { get; set; } = string.Empty;
    }
}

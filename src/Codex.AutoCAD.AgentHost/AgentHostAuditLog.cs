using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.AutoCAD.AgentLauncher;
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
    public const string CodexAppServerHandshakeFailed = "codex_appserver_handshake_failed";
    public const string CodexAppServerHandshakeTimedOut = "codex_appserver_handshake_timeout";
    public const string CodexConfigurationInvalid = "codex_configuration_invalid";
    public const string CodexExecutableIdentityFailed = "codex_executable_identity_failed";
    public const string CodexHomeConfigurationInvalid = "codex_home_configuration_invalid";
    public const string CodexSessionHomeCleanupFailed = "codex_session_home_cleanup_failed";
    public const string CodexSessionHomeInitializationFailed = "codex_session_home_initialization_failed";
    public const string CodexSessionHomeInvalid = "codex_session_home_invalid";
    public const string CodexSessionHomeInUse = "codex_session_home_in_use";
    public const string CodexVersionCancelled = "codex_version_cancelled";
    public const string CodexVersionInvalidOutput = "codex_version_invalid_output";
    public const string CodexVersionProcessFailed = "codex_version_process_failed";
    public const string CodexVersionTerminationFailed = "codex_version_termination_failed";
    public const string CodexVersionTimedOut = "codex_version_timeout";
    public const string CodexVersionUnsupported = "codex_version_unsupported";
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
            AgentHostCodexHealthException
            {
                Failure: AgentHostCodexHealthFailure.AppServerHandshakeTimedOut
            } => CodexAppServerHandshakeTimedOut,
            AgentHostCodexHealthException => CodexAppServerHandshakeFailed,
            CodexSessionHomeException
            {
                Failure: CodexSessionHomeFailure.InvalidSessionId
                    or CodexSessionHomeFailure.InvalidRoot
            } => CodexSessionHomeInvalid,
            CodexSessionHomeException
            {
                Failure: CodexSessionHomeFailure.AlreadyExists
            } => CodexSessionHomeInUse,
            CodexSessionHomeException
            {
                Failure: CodexSessionHomeFailure.CleanupFailed
            } => CodexSessionHomeCleanupFailed,
            CodexSessionHomeException => CodexSessionHomeInitializationFailed,
            CodexLocalConfigurationException
            {
                Failure: CodexLocalConfigurationFailure.InvalidCodexHomeDirectory
            } => CodexHomeConfigurationInvalid,
            CodexLocalConfigurationException => CodexConfigurationInvalid,
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
                Failure: CodexVersionPreflightFailure.Cancelled
            } => CodexVersionCancelled,
            CodexVersionPreflightException
            {
                Failure: CodexVersionPreflightFailure.TerminationFailed
            } => CodexVersionTerminationFailed,
            CodexVersionPreflightException
            {
                Failure: CodexVersionPreflightFailure.ExecutableIdentityUnavailable
                    or CodexVersionPreflightFailure.ExecutableIdentityChanged
            } => CodexExecutableIdentityFailed,
            CodexVersionPreflightException
            {
                Failure: CodexVersionPreflightFailure.InvalidVersionOutput
                    or CodexVersionPreflightFailure.VersionOutputTooLarge
            } => CodexVersionInvalidOutput,
            CodexVersionPreflightException => CodexVersionProcessFailed,
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

public enum AgentHostCodexHealthFailure
{
    AppServerHandshakeFailed,
    AppServerHandshakeTimedOut,
}

public sealed class AgentHostCodexHealthException : Exception
{
    public AgentHostCodexHealthException(
        AgentHostCodexHealthFailure failure,
        string message)
        : base(message)
    {
        Failure = failure;
    }

    public AgentHostCodexHealthFailure Failure { get; }
}

public sealed class AgentHostAuditEvent
{
    public string EventType { get; init; } = string.Empty;

    public string? SystemConversationId { get; init; }

    public string? SystemTurnId { get; init; }

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

public sealed class AgentHostAuditLog : IDisposable, IAsyncDisposable
{
    public const string Schema = "codex.autocad.agenthost.audit/2";
    public const int DefaultMaximumRecords = 10_000;
    public const long DefaultMaximumBytes = 4L * 1024 * 1024;

    private const int MaximumIdentifierCharacters = 256;
    private const int MaximumCodeCharacters = 128;
    private const int AbsoluteMaximumRecords = 1_000_000;
    private const long AbsoluteMaximumBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private Stream _destination;
    private readonly bool _leaveOpen;
    private readonly string _sessionId;
    private string _segmentId;
    private readonly IAgentHostAuditAnchorSink _anchorSink;
    private readonly IDisposable? _ownedStore;
    private readonly IAgentHostAuditSegmentStore? _segmentStore;
    private readonly int _maximumRecords;
    private readonly long _maximumBytes;
    private readonly object _sync = new();
    private int _segmentNumber = 1;
    private long _sequence;
    private long _bytesWritten;
    private string _previousRecordHash;
    private int _terminal;
    private int _faulted;
    private int _disposed;

    public AgentHostAuditLog(
        Stream destination,
        string sessionId,
        bool leaveOpen = false,
        int maximumRecords = DefaultMaximumRecords,
        long maximumBytes = DefaultMaximumBytes)
        : this(
            destination,
            sessionId,
            "segment-000001",
            AgentHostAuditIntegrity.GenesisHash,
            AgentHostAuditNullAnchorSink.Instance,
            leaveOpen,
            maximumRecords,
            maximumBytes)
    {
    }

    internal AgentHostAuditLog(
        Stream destination,
        string sessionId,
        string segmentId,
        string previousRecordHash,
        IAgentHostAuditAnchorSink anchorSink,
        bool leaveOpen = false,
        int maximumRecords = DefaultMaximumRecords,
        long maximumBytes = DefaultMaximumBytes,
        IDisposable? ownedStore = null,
        IAgentHostAuditSegmentStore? segmentStore = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(anchorSink);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("AgentHost audit destination must be writable.", nameof(destination));
        }

        ValidateIdentifier(sessionId, nameof(sessionId));
        ValidateIdentifier(segmentId, nameof(segmentId));
        AgentHostAuditIntegrity.ValidateHash(previousRecordHash, nameof(previousRecordHash));
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
        _segmentId = segmentId;
        _previousRecordHash = previousRecordHash;
        _anchorSink = anchorSink;
        _ownedStore = ownedStore;
        _segmentStore = segmentStore;
        _leaveOpen = leaveOpen;
        _maximumRecords = maximumRecords;
        _maximumBytes = maximumBytes;
        WriteCore(new AgentHostAuditEvent
        {
            EventType = AgentHostAuditEventTypes.SessionStarted,
        });
    }

    public static AgentHostAuditLog CreateForCurrentUser(string sessionId)
    {
        ValidateBootstrapSessionId(sessionId);
        AgentPersistentAuditStoreLease? store = null;
        try
        {
            store = AgentPersistentAuditStoreLease.CreateForCurrentUser();
            var audit = CreateRotatingInProtectedDirectories(
                sessionId,
                store.SegmentDirectory,
                store.AnchorDirectory,
                store);
            store = null;
            return audit;
        }
        catch (AgentHostAuditException)
        {
            throw;
        }
        catch (Exception exception) when (exception is AgentBootstrapLaunchException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            throw new AgentHostAuditException(
                "AgentHost persistent audit store could not be opened safely.",
                exception);
        }
        finally
        {
            store?.Dispose();
        }
    }

    internal static AgentHostAuditLog CreateInSessionDirectory(
        string sessionId,
        string auditDirectory)
        => CreateInProtectedDirectories(
            sessionId,
            auditDirectory,
            auditDirectory);

    internal static AgentHostAuditLog CreateInProtectedDirectories(
        string sessionId,
        string segmentDirectory,
        string anchorDirectory,
        IDisposable? ownedStore = null)
    {
        ValidateBootstrapSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(segmentDirectory)
            || string.IsNullOrWhiteSpace(anchorDirectory))
        {
            throw new AgentHostAuditException(
                "AgentHost persistent audit directories are unavailable.");
        }

        var auditPath = Path.Combine(segmentDirectory, sessionId + ".jsonl");
        try
        {
            EnsureSafeLocalDirectory(segmentDirectory);
            EnsureSafeLocalDirectory(anchorDirectory);
            var stream = new FileStream(
                auditPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan | FileOptions.WriteThrough);
            IAgentHostAuditAnchorSink? anchorSink = null;
            try
            {
                anchorSink = new AgentHostAuditFileAnchorSink(
                    Path.Combine(anchorDirectory, sessionId + ".anchor.json"));
                EnsureSafeLocalDirectory(segmentDirectory);
                EnsureSafeLocalDirectory(anchorDirectory);
                return new AgentHostAuditLog(
                    stream,
                    sessionId,
                    "segment-000001",
                    AgentHostAuditIntegrity.GenesisHash,
                    anchorSink,
                    ownedStore: ownedStore);
            }
            catch
            {
                anchorSink?.Dispose();
                stream.Dispose();
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
            or System.Security.SecurityException)
        {
            throw new AgentHostAuditException(
                "AgentHost audit file could not be created safely.",
                exception);
        }
    }

    internal static AgentHostAuditLog CreateRotatingInProtectedDirectories(
        string sessionId,
        string segmentDirectory,
        string anchorDirectory,
        IDisposable? ownedStore = null,
        int maximumRecords = DefaultMaximumRecords,
        long maximumBytes = DefaultMaximumBytes,
        int maximumSegments = AgentHostAuditFileSegmentStore.DefaultMaximumSegments)
    {
        ValidateBootstrapSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(segmentDirectory)
            || string.IsNullOrWhiteSpace(anchorDirectory))
        {
            throw new AgentHostAuditException(
                "AgentHost persistent audit directories are unavailable.");
        }

        AgentHostAuditFileSegmentStore? segmentStore = null;
        Stream? stream = null;
        IAgentHostAuditAnchorSink? anchorSink = null;
        try
        {
            EnsureSafeLocalDirectory(segmentDirectory);
            EnsureSafeLocalDirectory(anchorDirectory);
            segmentStore = new AgentHostAuditFileSegmentStore(
                segmentDirectory,
                sessionId,
                maximumSegments);
            stream = segmentStore.OpenSegment(FormatSegmentId(1));
            anchorSink = new AgentHostAuditFileAnchorSink(
                Path.Combine(anchorDirectory, sessionId + ".anchor.json"));
            EnsureSafeLocalDirectory(segmentDirectory);
            EnsureSafeLocalDirectory(anchorDirectory);
            var audit = new AgentHostAuditLog(
                stream,
                sessionId,
                FormatSegmentId(1),
                AgentHostAuditIntegrity.GenesisHash,
                anchorSink,
                maximumRecords: maximumRecords,
                maximumBytes: maximumBytes,
                ownedStore: ownedStore,
                segmentStore: segmentStore);
            stream = null;
            anchorSink = null;
            segmentStore = null;
            return audit;
        }
        catch (AgentHostAuditException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            throw new AgentHostAuditException(
                "AgentHost rotating audit could not be created safely.",
                exception);
        }
        finally
        {
            anchorSink?.Dispose();
            stream?.Dispose();
            segmentStore?.Dispose();
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
                try
                {
                    if (!_leaveOpen)
                    {
                        _destination.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        _anchorSink.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            _segmentStore?.Dispose();
                        }
                        finally
                        {
                            _ownedStore?.Dispose();
                        }
                    }
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void WriteCore(AgentHostAuditEvent auditEvent)
    {
        ValidateEvent(auditEvent);
        var nextSequence = checked(_sequence + 1);
        var envelope = CreateEnvelope(auditEvent, nextSequence);
        var bytes = SerializeEnvelope(envelope);
        if (WouldExceedCurrentSegment(nextSequence, bytes.Length))
        {
            if (_segmentStore is null || _sequence == 0)
            {
                _faulted = 1;
                throw new AgentHostAuditException("AgentHost audit capacity is exhausted.");
            }

            try
            {
                RotateSegment();
            }
            catch (AgentHostAuditException)
            {
                _faulted = 1;
                throw;
            }

            nextSequence = 1;
            envelope = CreateEnvelope(auditEvent, nextSequence);
            bytes = SerializeEnvelope(envelope);
            if (WouldExceedCurrentSegment(nextSequence, bytes.Length))
            {
                _faulted = 1;
                throw new AgentHostAuditException("AgentHost audit record exceeds segment capacity.");
            }
        }

        try
        {
            _destination.Write(bytes, 0, bytes.Length);
            FlushDurably();
            _anchorSink.Write(new AgentHostAuditAnchor
            {
                SystemSessionId = _sessionId,
                SegmentId = _segmentId,
                Sequence = nextSequence,
                RecordHash = envelope.RecordHash,
            });
            _sequence = nextSequence;
            _bytesWritten += bytes.Length;
            _previousRecordHash = envelope.RecordHash;
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException
            or NotSupportedException
            or UnauthorizedAccessException
            or ArgumentException
            or System.Security.SecurityException)
        {
            _faulted = 1;
            throw new AgentHostAuditException("AgentHost audit write failed.", exception);
        }
    }

    private AgentHostAuditEnvelope CreateEnvelope(
        AgentHostAuditEvent auditEvent,
        long sequence)
    {
        var envelope = new AgentHostAuditEnvelope
        {
            Schema = Schema,
            Sequence = sequence,
            TimestampUtc = DateTimeOffset.UtcNow.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture),
            SystemSessionId = _sessionId,
            SegmentId = _segmentId,
            PreviousRecordHash = _previousRecordHash,
            EventType = auditEvent.EventType,
            SystemConversationId = auditEvent.SystemConversationId,
            SystemTurnId = auditEvent.SystemTurnId,
            SystemRequestId = auditEvent.SystemRequestId,
            BridgeRequestId = auditEvent.BridgeRequestId,
            ProviderThreadId = auditEvent.ProviderThreadId,
            ProviderTurnId = auditEvent.ProviderTurnId,
            Method = auditEvent.Method,
            ApprovalKind = auditEvent.ApprovalKind,
            Resolution = auditEvent.Resolution,
            OutcomeCode = auditEvent.OutcomeCode,
            ErrorCode = auditEvent.ErrorCode,
        };
        envelope.RecordHash = AgentHostAuditIntegrity.ComputeRecordHash(envelope);
        return envelope;
    }

    private static byte[] SerializeEnvelope(AgentHostAuditEnvelope envelope)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, SerializerOptions) + "\n");

    private bool WouldExceedCurrentSegment(long nextSequence, int nextBytes)
        => nextSequence > _maximumRecords
            || _bytesWritten > _maximumBytes - nextBytes;

    private void RotateSegment()
    {
        var nextSegmentNumber = checked(_segmentNumber + 1);
        var nextSegmentId = FormatSegmentId(nextSegmentNumber);
        var nextDestination = _segmentStore!.OpenSegment(nextSegmentId);
        if (!_leaveOpen)
        {
            _destination.Dispose();
        }

        _destination = nextDestination;
        _segmentNumber = nextSegmentNumber;
        _segmentId = nextSegmentId;
        _sequence = 0;
        _bytesWritten = 0;
    }

    internal static string FormatSegmentId(int segmentNumber)
    {
        if (segmentNumber is < 1 or > AgentHostAuditFileSegmentStore.AbsoluteMaximumSegments)
        {
            throw new AgentHostAuditException("AgentHost audit segment number is invalid.");
        }

        return "segment-" + segmentNumber.ToString("D6", CultureInfo.InvariantCulture);
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
        ValidateOptionalIdentifier(auditEvent.SystemTurnId, "systemTurnId");
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
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumIdentifierCharacters
            || !value.All(IsSafeIdentifierCharacter))
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
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumCodeCharacters
            || !value.All(IsSafeIdentifierCharacter))
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

    private static void EnsureSafeLocalDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        if (!Path.IsPathFullyQualified(fullPath)
            || fullPath.StartsWith("\\\\", StringComparison.Ordinal)
            || fullPath.StartsWith("\\\\?\\", StringComparison.Ordinal)
            || fullPath.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            throw new AgentHostAuditException("AgentHost audit directory must be local.");
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)
            || new DriveInfo(root).DriveType != DriveType.Fixed)
        {
            throw new AgentHostAuditException("AgentHost audit directory must use a fixed drive.");
        }

        Directory.CreateDirectory(fullPath);
        for (var current = new DirectoryInfo(fullPath); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new AgentHostAuditException(
                    "AgentHost audit directory cannot traverse a reparse point.");
            }

            if (string.Equals(current.FullName, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
    }

    internal sealed class AgentHostAuditEnvelope
    {
        public string Schema { get; init; } = string.Empty;

        public long Sequence { get; init; }

        public string TimestampUtc { get; init; } = string.Empty;

        public string SystemSessionId { get; init; } = string.Empty;

        public string SegmentId { get; init; } = string.Empty;

        public string PreviousRecordHash { get; init; } = string.Empty;

        public string RecordHash { get; set; } = string.Empty;

        public string EventType { get; init; } = string.Empty;

        public string? SystemConversationId { get; init; }

        public string? SystemTurnId { get; init; }

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
}

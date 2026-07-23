using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public const string Schema = "codex.autocad.agenthost.audit/1";
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

    private readonly Stream _destination;
    private readonly bool _leaveOpen;
    private readonly string _sessionId;
    private readonly int _maximumRecords;
    private readonly long _maximumBytes;
    private readonly object _sync = new();
    private long _sequence;
    private long _bytesWritten;
    private int _terminal;
    private int _faulted;
    private int _disposed;

    public AgentHostAuditLog(
        Stream destination,
        string sessionId,
        bool leaveOpen = false,
        int maximumRecords = DefaultMaximumRecords,
        long maximumBytes = DefaultMaximumBytes)
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
        var auditPath = Path.Combine(auditDirectory, sessionId + ".jsonl");
        try
        {
            EnsureSafeLocalDirectory(auditDirectory);
            var stream = new FileStream(
                auditPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan | FileOptions.WriteThrough);
            try
            {
                EnsureSafeLocalDirectory(auditDirectory);
                return new AgentHostAuditLog(stream, sessionId);
            }
            catch
            {
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

    private void WriteCore(AgentHostAuditEvent auditEvent)
    {
        ValidateEvent(auditEvent);
        var nextSequence = checked(_sequence + 1);
        var envelope = new AgentHostAuditEnvelope
        {
            Schema = Schema,
            Sequence = nextSequence,
            TimestampUtc = DateTimeOffset.UtcNow.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture),
            SystemSessionId = _sessionId,
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
        var json = JsonSerializer.Serialize(envelope, SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
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

    private sealed class AgentHostAuditEnvelope
    {
        public string Schema { get; init; } = string.Empty;

        public long Sequence { get; init; }

        public string TimestampUtc { get; init; } = string.Empty;

        public string SystemSessionId { get; init; } = string.Empty;

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

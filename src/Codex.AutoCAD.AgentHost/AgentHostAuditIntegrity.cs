using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.AutoCAD.AgentHost;

internal interface IAgentHostAuditAnchorSink : IDisposable
{
    void Write(AgentHostAuditAnchor anchor);
}

internal sealed class AgentHostAuditAnchor
{
    internal const string SchemaValue = "codex.autocad.agenthost.audit-anchor/1";

    public string Schema { get; init; } = SchemaValue;

    public string SystemSessionId { get; init; } = string.Empty;

    public string SegmentId { get; init; } = string.Empty;

    public long Sequence { get; init; }

    public string RecordHash { get; init; } = string.Empty;
}

internal sealed class AgentHostAuditVerificationResult
{
    public int SegmentCount { get; init; }

    public long RecordCount { get; init; }

    public string FinalRecordHash { get; init; } = string.Empty;

    public string FinalSystemSessionId { get; init; } = string.Empty;

    public string FinalSegmentId { get; init; } = string.Empty;

    public long FinalSequence { get; init; }

    public string FinalEventType { get; init; } = string.Empty;
}

internal sealed class AgentHostAuditIntegrityException : Exception
{
    public AgentHostAuditIntegrityException(string message)
        : base(message)
    {
    }

    public AgentHostAuditIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class AgentHostAuditIntegrity
{
    internal const string GenesisHash =
        "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    internal static string ComputeRecordHash(AgentHostAuditLog.AgentHostAuditEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var builder = new StringBuilder(1024);
        Append(builder, envelope.Schema);
        Append(builder, envelope.Sequence.ToString(CultureInfo.InvariantCulture));
        Append(builder, envelope.TimestampUtc);
        Append(builder, envelope.SystemSessionId);
        Append(builder, envelope.SegmentId);
        Append(builder, envelope.PreviousRecordHash);
        Append(builder, envelope.EventType);
        Append(builder, envelope.SystemConversationId);
        Append(builder, envelope.SystemTurnId);
        Append(builder, envelope.SystemRequestId);
        Append(builder, envelope.BridgeRequestId);
        Append(builder, envelope.ProviderThreadId);
        Append(builder, envelope.ProviderTurnId);
        Append(builder, envelope.Method);
        Append(builder, envelope.ApprovalKind);
        Append(builder, envelope.Resolution);
        Append(builder, envelope.OutcomeCode);
        Append(builder, envelope.ErrorCode);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    internal static AgentHostAuditVerificationResult Verify(
        IReadOnlyList<ReadOnlyMemory<byte>> segments,
        AgentHostAuditAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(anchor);
        if (segments.Count == 0)
        {
            throw new AgentHostAuditIntegrityException("Audit verification requires a segment.");
        }

        ValidateHash(anchor.RecordHash, "anchor record hash");
        if (!string.Equals(anchor.Schema, AgentHostAuditAnchor.SchemaValue, StringComparison.Ordinal))
        {
            throw new AgentHostAuditIntegrityException("Audit anchor schema is invalid.");
        }

        var chain = VerifyChain(segments);
        if (!string.Equals(chain.FinalSystemSessionId, anchor.SystemSessionId, StringComparison.Ordinal)
            || !string.Equals(chain.FinalSegmentId, anchor.SegmentId, StringComparison.Ordinal)
            || chain.FinalSequence != anchor.Sequence
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(chain.FinalRecordHash),
                Convert.FromHexString(anchor.RecordHash)))
        {
            throw new AgentHostAuditIntegrityException(
                "Audit anchor does not match the final chain record.");
        }

        return chain;
    }

    internal static AgentHostAuditVerificationResult VerifyChain(
        IReadOnlyList<ReadOnlyMemory<byte>> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new AgentHostAuditIntegrityException("Audit verification requires a segment.");
        }

        var expectedPreviousHash = GenesisHash;
        string? expectedSessionId = null;
        var observedSegments = new HashSet<string>(StringComparer.Ordinal);
        long totalRecords = 0;
        AgentHostAuditLog.AgentHostAuditEnvelope? lastRecord = null;
        foreach (var segmentBytes in segments)
        {
            var bytes = segmentBytes.Span;
            if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit segment is empty or has a truncated tail.");
            }

            string text;
            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit segment is not strict UTF-8.",
                    exception);
            }

            var lines = text.Split('\n', StringSplitOptions.None);
            var expectedSequence = 1L;
            string? segmentId = null;
            for (var index = 0; index < lines.Length - 1; index++)
            {
                var line = lines[index].TrimEnd('\r');
                if (line.Length == 0)
                {
                    throw new AgentHostAuditIntegrityException(
                        "Audit segment contains an empty record.");
                }

                AgentHostAuditLog.AgentHostAuditEnvelope record;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!names.Add(property.Name))
                        {
                            throw new AgentHostAuditIntegrityException(
                                "Audit record contains a duplicate property.");
                        }
                    }

                    record = JsonSerializer.Deserialize<AgentHostAuditLog.AgentHostAuditEnvelope>(
                            line,
                            SerializerOptions)
                        ?? throw new AgentHostAuditIntegrityException(
                            "Audit record deserialized to null.");
                }
                catch (AgentHostAuditIntegrityException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is JsonException
                    or NotSupportedException)
                {
                    throw new AgentHostAuditIntegrityException(
                        "Audit record JSON is invalid.",
                        exception);
                }

                if (!string.Equals(record.Schema, AgentHostAuditLog.Schema, StringComparison.Ordinal)
                    || record.Sequence != expectedSequence)
                {
                    throw new AgentHostAuditIntegrityException(
                        "Audit record identity or sequence is invalid.");
                }

                if (expectedSessionId is null)
                {
                    expectedSessionId = record.SystemSessionId;
                    if (string.IsNullOrWhiteSpace(expectedSessionId))
                    {
                        throw new AgentHostAuditIntegrityException(
                            "Audit session identity is invalid.");
                    }
                }
                else if (!string.Equals(record.SystemSessionId, expectedSessionId, StringComparison.Ordinal))
                {
                    throw new AgentHostAuditIntegrityException(
                        "Audit record session identity changed within the chain.");
                }

                if (segmentId is null)
                {
                    segmentId = record.SegmentId;
                    if (string.IsNullOrWhiteSpace(segmentId)
                        || !observedSegments.Add(segmentId))
                    {
                        throw new AgentHostAuditIntegrityException(
                            "Audit segment identity is invalid or repeated.");
                    }
                }
                else if (!string.Equals(segmentId, record.SegmentId, StringComparison.Ordinal))
                {
                    throw new AgentHostAuditIntegrityException(
                        "Audit segment identity changed within a segment.");
                }

                ValidateHash(record.PreviousRecordHash, "previous record hash");
                ValidateHash(record.RecordHash, "record hash");
                if (!string.Equals(
                        record.PreviousRecordHash,
                        expectedPreviousHash,
                        StringComparison.Ordinal))
                {
                    throw new AgentHostAuditIntegrityException(
                        "Audit previous-record hash does not match the chain head.");
                }

                var computedHash = ComputeRecordHash(record);
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(computedHash),
                        Convert.FromHexString(record.RecordHash)))
                {
                    throw new AgentHostAuditIntegrityException(
                        "Audit record hash verification failed.");
                }

                expectedPreviousHash = record.RecordHash;
                expectedSequence++;
                totalRecords++;
                lastRecord = record;
            }

            if (segmentId is null)
            {
                throw new AgentHostAuditIntegrityException(
                    "Audit segment contains no records.");
            }
        }

        if (lastRecord is null)
        {
            throw new AgentHostAuditIntegrityException(
                "Audit chain contains no records.");
        }

        return new AgentHostAuditVerificationResult
        {
            SegmentCount = segments.Count,
            RecordCount = totalRecords,
            FinalRecordHash = lastRecord.RecordHash,
            FinalSystemSessionId = lastRecord.SystemSessionId,
            FinalSegmentId = lastRecord.SegmentId,
            FinalSequence = lastRecord.Sequence,
            FinalEventType = lastRecord.EventType,
        };
    }

    internal static void ValidateHash(string value, string name)
    {
        if (value.Length != 64
            || !value.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f'))
        {
            throw new AgentHostAuditIntegrityException(name + " is invalid.");
        }
    }

    private static void Append(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:");
            return;
        }

        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}

internal sealed class AgentHostAuditNullAnchorSink : IAgentHostAuditAnchorSink
{
    internal static readonly AgentHostAuditNullAnchorSink Instance = new();

    public void Write(AgentHostAuditAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
    }

    public void Dispose()
    {
    }
}

internal sealed class AgentHostAuditFileAnchorSink : IAgentHostAuditAnchorSink
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly string _path;
    private int _hasWritten;
    private int _disposed;

    internal AgentHostAuditFileAnchorSink(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Audit anchor path is invalid.", nameof(path));
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new AgentHostAuditException(
                "AgentHost audit anchor already exists.");
        }

        _path = path;
    }

    public void Write(AgentHostAuditAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(anchor, SerializerOptions);
        var temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }

            if (_hasWritten == 0)
            {
                File.Move(temporaryPath, _path);
                _hasWritten = 1;
            }
            else
            {
                File.Move(temporaryPath, _path, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Dispose()
    {
        _disposed = 1;
    }
}

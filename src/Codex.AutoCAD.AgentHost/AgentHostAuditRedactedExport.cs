using System.Text.Json;

namespace Codex.AutoCAD.AgentHost;

internal static class AgentHostAuditRedactedExport
{
    internal const string Schema = "codex.autocad.agenthost.audit-export/1";

    internal static AgentHostAuditVerificationResult WriteVerified(
        Stream destination,
        IReadOnlyList<ReadOnlyMemory<byte>> segments,
        AgentHostAuditAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(anchor);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("Audit export destination must be writable.", nameof(destination));
        }

        var verification = AgentHostAuditIntegrity.Verify(segments, anchor);
        using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        });
        writer.WriteStartObject();
        writer.WriteString("schema", Schema);
        writer.WriteString("systemSessionId", anchor.SystemSessionId);
        writer.WriteNumber("segmentCount", verification.SegmentCount);
        writer.WriteNumber("recordCount", verification.RecordCount);
        writer.WriteString("finalRecordHash", verification.FinalRecordHash);
        writer.WriteStartArray("omittedFields");
        writer.WriteStringValue("providerThreadId");
        writer.WriteStringValue("providerTurnId");
        writer.WriteStringValue("payload");
        writer.WriteStringValue("path");
        writer.WriteEndArray();
        writer.WriteStartArray("records");
        foreach (var segment in segments)
        {
            var text = System.Text.Encoding.UTF8.GetString(segment.Span);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line.TrimEnd('\r'));
                var source = document.RootElement;
                writer.WriteStartObject();
                CopyRequiredNumber(source, writer, "sequence");
                CopyRequiredString(source, writer, "timestampUtc");
                CopyRequiredString(source, writer, "segmentId");
                CopyRequiredString(source, writer, "previousRecordHash");
                CopyRequiredString(source, writer, "recordHash");
                CopyRequiredString(source, writer, "eventType");
                CopyOptionalString(source, writer, "systemConversationId");
                CopyOptionalString(source, writer, "systemTurnId");
                CopyOptionalString(source, writer, "systemRequestId");
                CopyOptionalString(source, writer, "bridgeRequestId");
                CopyOptionalString(source, writer, "method");
                CopyOptionalString(source, writer, "approvalKind");
                CopyOptionalString(source, writer, "resolution");
                CopyOptionalString(source, writer, "outcomeCode");
                CopyOptionalString(source, writer, "errorCode");
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        if (destination is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
        else
        {
            destination.Flush();
        }

        return verification;
    }

    private static void CopyRequiredString(
        JsonElement source,
        Utf8JsonWriter writer,
        string propertyName)
    {
        var value = source.GetProperty(propertyName).GetString()
            ?? throw new AgentHostAuditIntegrityException(
                "Audit export source contains a null required string.");
        writer.WriteString(propertyName, value);
    }

    private static void CopyOptionalString(
        JsonElement source,
        Utf8JsonWriter writer,
        string propertyName)
    {
        if (source.TryGetProperty(propertyName, out var property))
        {
            var value = property.GetString();
            if (value is not null)
            {
                writer.WriteString(propertyName, value);
            }
        }
    }

    private static void CopyRequiredNumber(
        JsonElement source,
        Utf8JsonWriter writer,
        string propertyName)
        => writer.WriteNumber(propertyName, source.GetProperty(propertyName).GetInt64());
}

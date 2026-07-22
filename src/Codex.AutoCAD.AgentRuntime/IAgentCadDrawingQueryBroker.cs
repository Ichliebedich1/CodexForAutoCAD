using Codex.AutoCAD.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.AutoCAD.AgentRuntime;

/// <summary>
/// Host-unbound read-only drawing query emitted by the Agent runtime. The model can supply only
/// filters and pagination; the trusted AutoCAD host owns index, document and revision identity.
/// </summary>
public sealed class AgentCadDrawingQuery
{
    private readonly CadQueryFilter _filter;

    public AgentCadDrawingQuery(
        string requestId,
        string queryId,
        string callId,
        string threadId,
        string turnId,
        CadQueryFilter filter,
        int pageSize,
        string cursor)
    {
        ArgumentNullException.ThrowIfNull(filter);
        RequestId = requestId;
        QueryId = queryId;
        CallId = callId;
        ThreadId = threadId;
        TurnId = turnId;
        _filter = CadDrawingQueryCloner.CloneFilter(filter);
        PageSize = pageSize;
        Cursor = cursor;
    }

    public string QueryId { get; }

    public string RequestId { get; }

    public string CallId { get; }

    public string ThreadId { get; }

    public string TurnId { get; }

    public CadQueryFilter Filter => CadDrawingQueryCloner.CloneFilter(_filter);

    public int PageSize { get; }

    public string Cursor { get; }

    internal AgentCadDrawingQuery DeepClone()
        => new(RequestId, QueryId, CallId, ThreadId, TurnId, _filter, PageSize, Cursor);
}

public sealed class AgentCadDrawingQueryResult
{
    private readonly CadQueryResponse _response;

    public AgentCadDrawingQueryResult(
        string threadId,
        string turnId,
        string callId,
        string queryId,
        CadQueryResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ThreadId = threadId;
        TurnId = turnId;
        CallId = callId;
        QueryId = queryId;
        _response = CadDrawingQueryCloner.CloneResponse(response);
    }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string CallId { get; }

    public string QueryId { get; }

    public CadQueryResponse Response => CadDrawingQueryCloner.CloneResponse(_response);

    public static AgentCadDrawingQueryResult ForQuery(
        AgentCadDrawingQuery query,
        CadQueryResponse response)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new AgentCadDrawingQueryResult(
            query.ThreadId,
            query.TurnId,
            query.CallId,
            query.QueryId,
            response);
    }
}

public interface IAgentCadDrawingQueryBroker
{
    ValueTask<AgentCadDrawingQueryResult> ExecuteAsync(
        AgentCadDrawingQuery query,
        CancellationToken cancellationToken);
}

internal static class CadDrawingQueryCloner
{
    internal static CadQueryFilter CloneFilter(CadQueryFilter value)
        => new()
        {
            EntityTypes = CloneStrings(value.EntityTypes),
            Layers = CloneStrings(value.Layers),
            Spaces = CloneStrings(value.Spaces),
            BlockNames = CloneStrings(value.BlockNames),
            ObjectIds = CloneStrings(value.ObjectIds),
            TextContains = value.TextContains ?? string.Empty,
            IncludeUnsupported = value.IncludeUnsupported,
            Bounds = value.Bounds is null
                ? null
                : new CadQueryBounds
                {
                    Minimum = ClonePoint(value.Bounds.Minimum),
                    Maximum = ClonePoint(value.Bounds.Maximum),
                },
        };

    internal static CadQueryResponse CloneResponse(CadQueryResponse value)
        => new()
        {
            Schema = value.Schema ?? string.Empty,
            SchemaVersion = value.SchemaVersion,
            IndexId = value.IndexId ?? string.Empty,
            DocumentId = value.DocumentId ?? string.Empty,
            DocumentRevision = value.DocumentRevision,
            QueryId = value.QueryId ?? string.Empty,
            Status = value.Status ?? string.Empty,
            Complete = value.Complete,
            TotalMatches = value.TotalMatches,
            ReturnedCount = value.ReturnedCount,
            Entities = (value.Entities ?? Array.Empty<CadQueryEntity>())
                .Select(CloneEntity)
                .ToArray(),
            NextCursor = value.NextCursor ?? string.Empty,
            Message = value.Message ?? string.Empty,
        };

    private static CadQueryEntity CloneEntity(CadQueryEntity value)
        => new()
        {
            ObjectId = value.ObjectId ?? string.Empty,
            EntityType = value.EntityType ?? string.Empty,
            ActualType = value.ActualType ?? string.Empty,
            Layer = value.Layer ?? string.Empty,
            Space = value.Space ?? string.Empty,
            BlockName = value.BlockName ?? string.Empty,
            TextExcerpt = value.TextExcerpt ?? string.Empty,
            Bounds = value.Bounds is null
                ? null
                : new CadExtents3
                {
                    Minimum = ClonePoint(value.Bounds.Minimum),
                    Maximum = ClonePoint(value.Bounds.Maximum),
                },
            Unsupported = value.Unsupported,
            ReadStatus = value.ReadStatus ?? string.Empty,
        };

    private static CadPoint3 ClonePoint(CadPoint3 value)
        => new(value.X, value.Y, value.Z);

    private static string[] CloneStrings(string[]? values)
        => values is null || values.Length == 0
            ? Array.Empty<string>()
            : (string[])values.Clone();
}

internal static class CadDrawingQueryToolResultCodec
{
    internal static string Serialize(CadQueryResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var entities = (response.Entities ?? Array.Empty<CadQueryEntity>())
            .Select(value => new CadDrawingQueryToolEntityWire(
                value.ObjectId ?? string.Empty,
                value.EntityType ?? string.Empty,
                value.ActualType ?? string.Empty,
                value.Layer ?? string.Empty,
                value.Space ?? string.Empty,
                value.BlockName ?? string.Empty,
                value.TextExcerpt ?? string.Empty,
                value.Bounds is null
                    ? null
                    : new CadDrawingQueryToolBoundsWire(
                        new CadDrawingQueryToolPointWire(
                            value.Bounds.Minimum.X,
                            value.Bounds.Minimum.Y,
                            value.Bounds.Minimum.Z),
                        new CadDrawingQueryToolPointWire(
                            value.Bounds.Maximum.X,
                            value.Bounds.Maximum.Y,
                            value.Bounds.Maximum.Z)),
                value.Unsupported,
                value.ReadStatus ?? string.Empty))
            .ToArray();
        return JsonSerializer.Serialize(new CadDrawingQueryToolResponseWire(
            response.Schema ?? DrawingIndexContractConstants.QuerySchema,
            response.SchemaVersion,
            DrawingIndexContractConstants.ContextEgressRisk,
            "untrusted-cad-data",
            response.Status ?? CadQueryStatuses.Failed,
            response.Complete,
            response.TotalMatches,
            response.ReturnedCount,
            entities,
            response.NextCursor ?? string.Empty,
            response.Message ?? string.Empty));
    }
}

internal sealed record CadDrawingQueryToolResponseWire(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("egressRisk")] string EgressRisk,
    [property: JsonPropertyName("trust")] string Trust,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("complete")] bool Complete,
    [property: JsonPropertyName("totalMatches")] int TotalMatches,
    [property: JsonPropertyName("returnedCount")] int ReturnedCount,
    [property: JsonPropertyName("entities")] IReadOnlyList<CadDrawingQueryToolEntityWire> Entities,
    [property: JsonPropertyName("nextCursor")] string NextCursor,
    [property: JsonPropertyName("message")] string Message);

internal sealed record CadDrawingQueryToolEntityWire(
    [property: JsonPropertyName("objectId")] string ObjectId,
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("actualType")] string ActualType,
    [property: JsonPropertyName("layer")] string Layer,
    [property: JsonPropertyName("space")] string Space,
    [property: JsonPropertyName("blockName")] string BlockName,
    [property: JsonPropertyName("textExcerpt")] string TextExcerpt,
    [property: JsonPropertyName("bounds")] CadDrawingQueryToolBoundsWire? Bounds,
    [property: JsonPropertyName("unsupported")] bool Unsupported,
    [property: JsonPropertyName("readStatus")] string ReadStatus);

internal sealed record CadDrawingQueryToolBoundsWire(
    [property: JsonPropertyName("minimum")] CadDrawingQueryToolPointWire Minimum,
    [property: JsonPropertyName("maximum")] CadDrawingQueryToolPointWire Maximum);

internal sealed record CadDrawingQueryToolPointWire(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Z);

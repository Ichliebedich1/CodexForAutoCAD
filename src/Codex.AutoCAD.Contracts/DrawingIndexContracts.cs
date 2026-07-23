using System.Globalization;

namespace Codex.AutoCAD.Contracts;

/// <summary>
/// Frozen v1 contract for a lightweight, read-only drawing index. The index is intentionally
/// separate from CadContextJson v2: CadContext remains a bounded selection snapshot while this
/// contract describes large drawings through summaries and cursor-based queries.
/// </summary>
public static class DrawingIndexContractConstants
{
    public const string Schema = "codex.autocad.drawing-index/1";
    public const int SchemaVersion = 1;
    public const string QuerySchema = "codex.autocad.cad-query/1";
    public const int QuerySchemaVersion = 1;
    public const string ContextEgressRisk = "context-egress";

    public const int MaximumIndexedEntities = 100_000;
    public const int MaximumReportedEntities = 2_000_000;
    public const int MaximumCountBuckets = 4_096;
    public const int MaximumPageSize = 200;
    public const int DefaultPageSize = 50;
    public const int MaximumFilterValues = 64;
    public const int MaximumNameCharacters = 255;
    public const int MaximumTypeCharacters = 128;
    public const int MaximumTextQueryCharacters = 512;
    public const int MaximumTextExcerptCharacters = 256;
    public const int MaximumBlockAttributes = 8;
    public const int MaximumBlockAttributeTagCharacters = 48;
    public const int MaximumBlockAttributeValueCharacters = 48;
    public const int MaximumDynamicBlockProperties = 8;
    public const int MaximumDynamicBlockPropertyNameCharacters = 48;
    public const int MaximumDynamicBlockPropertyValueCharacters = 48;
    public const int MaximumNestedBlockReferences = 64;
    public const int MaximumNestedBlockDepth = 4;
    public const int MaximumNestedBlockDefinitionEntities = 512;
    public const int MaximumCursorCharacters = 512;
    public const int MaximumIdentifierCharacters = 128;
    public const double MaximumCoordinateMagnitude = 1_000_000_000d;
}

public static class DrawingIndexScopes
{
    public const string Selection = "selection";
    public const string CurrentSpace = "current_space";
    public const string ModelSpace = "model_space";
    public const string Layouts = "layouts";
    public const string Drawing = "drawing";
}

public static class DrawingIndexStatuses
{
    public const string NotBuilt = "not_built";
    public const string Preparing = "preparing";
    public const string Scanning = "scanning";
    public const string Ready = "ready";
    public const string Partial = "partial";
    public const string Limited = "limited";
    public const string Cancelled = "cancelled";
    public const string Stale = "stale";
    public const string Failed = "failed";
}

public static class CadQueryStatuses
{
    public const string Ok = "ok";
    public const string Partial = "partial";
    public const string Limited = "limited";
    public const string Stale = "stale";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public static class CadQueryReadStatuses
{
    public const string Parsed = "parsed";
    public const string Unsupported = "unsupported";
    public const string DataLimited = "data_limited";
    public const string ReadFailed = "read_failed";
}

public static class CadQueryBlockDetailStatuses
{
    public const string Complete = "complete";
    public const string Limited = "limited";
}

public static class CadQueryLayoutKinds
{
    public const string None = "none";
    public const string Model = "model";
    public const string Paper = "paper";
    public const string Unavailable = "unavailable";
}

public static class CadQueryDynamicValueKinds
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Point = "point";
    public const string Enum = "enum";
    public const string Unavailable = "unavailable";
}

public sealed class DrawingIndexCountBucket
{
    public string Key { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class DrawingIndexDescriptor
{
    public string Schema { get; set; } = DrawingIndexContractConstants.Schema;

    public int SchemaVersion { get; set; } = DrawingIndexContractConstants.SchemaVersion;

    public string EgressRisk { get; set; } = DrawingIndexContractConstants.ContextEgressRisk;

    public string IndexId { get; set; } = string.Empty;

    public string DocumentId { get; set; } = string.Empty;

    public string DrawingFingerprint { get; set; } = string.Empty;

    public long DocumentRevision { get; set; }

    public string Scope { get; set; } = DrawingIndexScopes.Drawing;

    public string Status { get; set; } = DrawingIndexStatuses.NotBuilt;

    public bool Complete { get; set; }

    public bool Limited { get; set; }

    public int EntityCount { get; set; }

    public int IndexedEntityCount { get; set; }

    public int UnsupportedEntityCount { get; set; }

    public int FailedEntityCount { get; set; }

    public int ProgressPercent { get; set; }

    public long EstimatedManagedBytes { get; set; }

    public string StartedAtUtc { get; set; } = string.Empty;

    public string CompletedAtUtc { get; set; } = string.Empty;

    public string LimitReason { get; set; } = string.Empty;

    public DrawingIndexCountBucket[] TypeCounts { get; set; } = new DrawingIndexCountBucket[0];

    public DrawingIndexCountBucket[] LayerCounts { get; set; } = new DrawingIndexCountBucket[0];

    public DrawingIndexCountBucket[] SpaceCounts { get; set; } = new DrawingIndexCountBucket[0];

    public DrawingIndexCountBucket[] BlockCounts { get; set; } = new DrawingIndexCountBucket[0];
}

public sealed class CadQueryBounds
{
    public CadPoint3 Minimum { get; set; } = new();

    public CadPoint3 Maximum { get; set; } = new();
}

public sealed class CadQueryFilter
{
    public string[] EntityTypes { get; set; } = new string[0];

    public string[] Layers { get; set; } = new string[0];

    public string[] Spaces { get; set; } = new string[0];

    public string[] BlockNames { get; set; } = new string[0];

    public string[] ObjectIds { get; set; } = new string[0];

    public string TextContains { get; set; } = string.Empty;

    public CadQueryBounds? Bounds { get; set; }

    public bool IncludeUnsupported { get; set; } = true;
}

public sealed class CadQueryRequest
{
    public string Schema { get; set; } = DrawingIndexContractConstants.QuerySchema;

    public int SchemaVersion { get; set; } = DrawingIndexContractConstants.QuerySchemaVersion;

    public string IndexId { get; set; } = string.Empty;

    public string DocumentId { get; set; } = string.Empty;

    public long DocumentRevision { get; set; }

    public string QueryId { get; set; } = string.Empty;

    public CadQueryFilter Filter { get; set; } = new();

    public int PageSize { get; set; } = DrawingIndexContractConstants.DefaultPageSize;

    public string Cursor { get; set; } = string.Empty;
}

public sealed class CadQueryEntity
{
    /// <summary>Opaque, document-local entity token. It is not a DWG path or executable API id.</summary>
    public string ObjectId { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string ActualType { get; set; } = string.Empty;

    public string Layer { get; set; } = string.Empty;

    public string Space { get; set; } = string.Empty;

    public string BlockName { get; set; } = string.Empty;

    /// <summary>
    /// Optional bounded block-specific detail. Existing non-block entity records leave this null.
    /// No Xref source path is represented by this contract.
    /// </summary>
    public CadQueryBlockDetails? BlockDetails { get; set; }

    public string TextExcerpt { get; set; } = string.Empty;

    public CadExtents3? Bounds { get; set; }

    public bool Unsupported { get; set; }

    public string ReadStatus { get; set; } = CadQueryReadStatuses.Parsed;
}

public sealed class CadQueryBlockDetails
{
    public string DetailStatus { get; set; } = CadQueryBlockDetailStatuses.Complete;

    public bool IsDynamic { get; set; }

    public bool IsExternalReference { get; set; }

    public bool IsOverlayReference { get; set; }

    public bool IsAnonymousDefinition { get; set; }

    public bool IsLayoutDefinition { get; set; }

    public bool HasAttributeDefinitions { get; set; }

    public string LayoutName { get; set; } = string.Empty;

    public string LayoutKind { get; set; } = CadQueryLayoutKinds.None;

    public int AttributeCount { get; set; }

    public CadQueryBlockAttribute[] Attributes { get; set; } = new CadQueryBlockAttribute[0];

    public int DynamicPropertyCount { get; set; }

    public CadQueryDynamicBlockProperty[] DynamicProperties { get; set; } =
        new CadQueryDynamicBlockProperty[0];

    public int NestedBlockReferenceCount { get; set; }

    public int MaximumNestedBlockDepth { get; set; }
}

public sealed class CadQueryBlockAttribute
{
    public string Tag { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool IsInvisible { get; set; }

    public bool IsMText { get; set; }
}

public sealed class CadQueryDynamicBlockProperty
{
    public string Name { get; set; } = string.Empty;

    public string ValueKind { get; set; } = CadQueryDynamicValueKinds.Unavailable;

    public string Value { get; set; } = string.Empty;

    public bool IsReadOnly { get; set; }

    public bool IsVisible { get; set; }
}

public static class CadQueryBlockDetailsCloner
{
    public static CadQueryBlockDetails? Clone(CadQueryBlockDetails? value)
    {
        if (value is null)
        {
            return null;
        }

        var attributes = value.Attributes ?? new CadQueryBlockAttribute[0];
        var properties = value.DynamicProperties ?? new CadQueryDynamicBlockProperty[0];
        var attributeCopies = new CadQueryBlockAttribute[attributes.Length];
        var propertyCopies = new CadQueryDynamicBlockProperty[properties.Length];

        for (var index = 0; index < attributes.Length; index++)
        {
            var attribute = attributes[index] ?? new CadQueryBlockAttribute();
            attributeCopies[index] = new CadQueryBlockAttribute
            {
                Tag = attribute.Tag ?? string.Empty,
                Value = attribute.Value ?? string.Empty,
                IsInvisible = attribute.IsInvisible,
                IsMText = attribute.IsMText,
            };
        }

        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index] ?? new CadQueryDynamicBlockProperty();
            propertyCopies[index] = new CadQueryDynamicBlockProperty
            {
                Name = property.Name ?? string.Empty,
                ValueKind = property.ValueKind ?? string.Empty,
                Value = property.Value ?? string.Empty,
                IsReadOnly = property.IsReadOnly,
                IsVisible = property.IsVisible,
            };
        }

        return new CadQueryBlockDetails
        {
            DetailStatus = value.DetailStatus ?? string.Empty,
            IsDynamic = value.IsDynamic,
            IsExternalReference = value.IsExternalReference,
            IsOverlayReference = value.IsOverlayReference,
            IsAnonymousDefinition = value.IsAnonymousDefinition,
            IsLayoutDefinition = value.IsLayoutDefinition,
            HasAttributeDefinitions = value.HasAttributeDefinitions,
            LayoutName = value.LayoutName ?? string.Empty,
            LayoutKind = value.LayoutKind ?? string.Empty,
            AttributeCount = value.AttributeCount,
            Attributes = attributeCopies,
            DynamicPropertyCount = value.DynamicPropertyCount,
            DynamicProperties = propertyCopies,
            NestedBlockReferenceCount = value.NestedBlockReferenceCount,
            MaximumNestedBlockDepth = value.MaximumNestedBlockDepth,
        };
    }
}

public sealed class CadQueryResponse
{
    public string Schema { get; set; } = DrawingIndexContractConstants.QuerySchema;

    public int SchemaVersion { get; set; } = DrawingIndexContractConstants.QuerySchemaVersion;

    public string IndexId { get; set; } = string.Empty;

    public string DocumentId { get; set; } = string.Empty;

    public long DocumentRevision { get; set; }

    public string QueryId { get; set; } = string.Empty;

    public string Status { get; set; } = CadQueryStatuses.Ok;

    public bool Complete { get; set; }

    public int TotalMatches { get; set; }

    public int ReturnedCount { get; set; }

    public CadQueryEntity[] Entities { get; set; } = new CadQueryEntity[0];

    public string NextCursor { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public static class DrawingIndexContractValidator
{
    public static CadValidationFailure[] Validate(DrawingIndexDescriptor? descriptor)
    {
        var failures = new List<CadValidationFailure>();
        if (descriptor is null)
        {
            return [new CadValidationFailure("drawing_index_required", "$", "DrawingIndex不能为空。")];
        }

        Require(descriptor.Schema == DrawingIndexContractConstants.Schema, failures,
            "drawing_index_schema", "$.schema", "DrawingIndex schema不受支持。");
        Require(descriptor.SchemaVersion == DrawingIndexContractConstants.SchemaVersion, failures,
            "drawing_index_schema_version", "$.schemaVersion", "DrawingIndex schema版本不受支持。");
        Require(descriptor.EgressRisk == DrawingIndexContractConstants.ContextEgressRisk, failures,
            "drawing_index_egress", "$.egressRisk", "DrawingIndex必须标记为上下文外发数据。");
        ValidateIdentifier(descriptor.IndexId, "drawing_index_id", "$.indexId", failures);
        ValidateIdentifier(descriptor.DocumentId, "drawing_document_id", "$.documentId", failures);
        Require(IsSha256Hex(descriptor.DrawingFingerprint), failures,
            "drawing_fingerprint", "$.drawingFingerprint", "图纸指纹必须为64位十六进制SHA-256。");
        Require(descriptor.DocumentRevision >= 0, failures,
            "drawing_revision", "$.documentRevision", "图纸修订号不能为负数。");
        Require(IsKnownScope(descriptor.Scope), failures,
            "drawing_index_scope", "$.scope", "DrawingIndex扫描范围不受支持。");
        Require(IsKnownIndexStatus(descriptor.Status), failures,
            "drawing_index_status", "$.status", "DrawingIndex状态不受支持。");
        Require(descriptor.EntityCount >= 0
                && descriptor.EntityCount <= DrawingIndexContractConstants.MaximumReportedEntities,
            failures, "drawing_entity_count", "$.entityCount", "图元总数超出DrawingIndex硬限额。");
        Require(descriptor.IndexedEntityCount >= 0
                && descriptor.IndexedEntityCount <= descriptor.EntityCount
                && descriptor.IndexedEntityCount <= DrawingIndexContractConstants.MaximumIndexedEntities,
            failures, "drawing_indexed_count", "$.indexedEntityCount", "已索引图元数不合法。");
        Require(descriptor.UnsupportedEntityCount >= 0
                && descriptor.UnsupportedEntityCount <= descriptor.IndexedEntityCount,
            failures, "drawing_unsupported_count", "$.unsupportedEntityCount", "不支持图元数不合法。");
        Require(descriptor.FailedEntityCount >= 0
                && descriptor.FailedEntityCount <= descriptor.IndexedEntityCount,
            failures, "drawing_failed_count", "$.failedEntityCount", "读取失败图元数不合法。");
        Require(descriptor.ProgressPercent is >= 0 and <= 100, failures,
            "drawing_progress", "$.progressPercent", "索引进度必须在0到100之间。");
        Require(descriptor.EstimatedManagedBytes >= 0, failures,
            "drawing_memory", "$.estimatedManagedBytes", "估算内存不能为负数。");
        ValidateTimestamp(descriptor.StartedAtUtc, false, "$.startedAtUtc", failures);
        ValidateTimestamp(descriptor.CompletedAtUtc, true, "$.completedAtUtc", failures);
        ValidateSafeOptionalString(descriptor.LimitReason, 512, "drawing_limit_reason",
            "$.limitReason", failures);
        ValidateBuckets(descriptor.TypeCounts, "$.typeCounts", failures);
        ValidateBuckets(descriptor.LayerCounts, "$.layerCounts", failures);
        ValidateBuckets(descriptor.SpaceCounts, "$.spaceCounts", failures);
        ValidateBuckets(descriptor.BlockCounts, "$.blockCounts", failures);

        if (descriptor.Status == DrawingIndexStatuses.Ready)
        {
            Require(descriptor.Complete && !descriptor.Limited, failures,
                "drawing_ready_flags", "$", "ready索引必须完整且不受限。");
            Require(descriptor.IndexedEntityCount == descriptor.EntityCount, failures,
                "drawing_ready_count", "$.indexedEntityCount", "ready索引必须覆盖全部图元。");
            Require(descriptor.ProgressPercent == 100, failures,
                "drawing_ready_progress", "$.progressPercent", "ready索引进度必须为100。");
        }

        if (descriptor.Complete)
        {
            Require(descriptor.IndexedEntityCount == descriptor.EntityCount, failures,
                "drawing_complete_count", "$.complete", "完整索引必须覆盖全部图元。");
        }

        if (descriptor.Limited)
        {
            Require(descriptor.Status == DrawingIndexStatuses.Limited, failures,
                "drawing_limited_status", "$.status", "受限索引必须使用limited状态。");
            Require(!string.IsNullOrWhiteSpace(descriptor.LimitReason), failures,
                "drawing_limit_reason_required", "$.limitReason", "受限索引必须说明限制原因。");
        }

        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(CadQueryRequest? request)
    {
        var failures = new List<CadValidationFailure>();
        if (request is null)
        {
            return [new CadValidationFailure("cad_query_required", "$", "CadQuery请求不能为空。")];
        }

        Require(request.Schema == DrawingIndexContractConstants.QuerySchema, failures,
            "cad_query_schema", "$.schema", "CadQuery schema不受支持。");
        Require(request.SchemaVersion == DrawingIndexContractConstants.QuerySchemaVersion, failures,
            "cad_query_schema_version", "$.schemaVersion", "CadQuery schema版本不受支持。");
        ValidateIdentifier(request.IndexId, "cad_query_index_id", "$.indexId", failures);
        ValidateIdentifier(request.DocumentId, "cad_query_document_id", "$.documentId", failures);
        Require(request.DocumentRevision >= 0, failures,
            "cad_query_revision", "$.documentRevision", "CadQuery图纸修订号不能为负数。");
        ValidateIdentifier(request.QueryId, "cad_query_id", "$.queryId", failures);
        Require(request.PageSize is >= 1 and <= DrawingIndexContractConstants.MaximumPageSize,
            failures, "cad_query_page_size", "$.pageSize", "CadQuery页大小超出硬限额。");
        ValidateSafeOptionalString(request.Cursor, DrawingIndexContractConstants.MaximumCursorCharacters,
            "cad_query_cursor", "$.cursor", failures);
        ValidateFilter(request.Filter, failures);
        return failures.ToArray();
    }

    public static CadValidationFailure[] Validate(CadQueryResponse? response)
    {
        var failures = new List<CadValidationFailure>();
        if (response is null)
        {
            return [new CadValidationFailure("cad_query_response_required", "$", "CadQuery响应不能为空。")];
        }

        Require(response.Schema == DrawingIndexContractConstants.QuerySchema, failures,
            "cad_query_schema", "$.schema", "CadQuery schema不受支持。");
        Require(response.SchemaVersion == DrawingIndexContractConstants.QuerySchemaVersion, failures,
            "cad_query_schema_version", "$.schemaVersion", "CadQuery schema版本不受支持。");
        ValidateIdentifier(response.IndexId, "cad_query_index_id", "$.indexId", failures);
        ValidateIdentifier(response.DocumentId, "cad_query_document_id", "$.documentId", failures);
        Require(response.DocumentRevision >= 0, failures,
            "cad_query_revision", "$.documentRevision", "CadQuery图纸修订号不能为负数。");
        ValidateIdentifier(response.QueryId, "cad_query_id", "$.queryId", failures);
        Require(IsKnownQueryStatus(response.Status), failures,
            "cad_query_status", "$.status", "CadQuery状态不受支持。");
        Require(response.TotalMatches >= 0, failures,
            "cad_query_total", "$.totalMatches", "匹配总数不能为负数。");
        var entities = response.Entities ?? new CadQueryEntity[0];
        Require(response.ReturnedCount == entities.Length, failures,
            "cad_query_returned_count", "$.returnedCount", "返回计数与实体数组不一致。");
        Require(entities.Length <= DrawingIndexContractConstants.MaximumPageSize, failures,
            "cad_query_entities_limit", "$.entities", "CadQuery单页实体数超限。");
        Require(response.ReturnedCount <= response.TotalMatches, failures,
            "cad_query_match_count", "$.returnedCount", "返回计数不能超过匹配总数。");
        ValidateSafeOptionalString(response.NextCursor,
            DrawingIndexContractConstants.MaximumCursorCharacters, "cad_query_next_cursor",
            "$.nextCursor", failures);
        ValidateSafeOptionalString(response.Message, 512, "cad_query_message", "$.message", failures);
        var seenObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entities.Length; index++)
        {
            ValidateEntity(entities[index], "$.entities[" + index.ToString(CultureInfo.InvariantCulture) + "]", failures);
            if (entities[index] is not null && !string.IsNullOrWhiteSpace(entities[index].ObjectId))
            {
                Require(seenObjectIds.Add(entities[index].ObjectId), failures,
                    "cad_query_object_id_duplicate",
                    "$.entities[" + index.ToString(CultureInfo.InvariantCulture) + "].objectId",
                    "单页查询实体令牌不能重复。");
            }
        }

        if (response.Complete)
        {
            Require(string.IsNullOrEmpty(response.NextCursor), failures,
                "cad_query_complete_cursor", "$.nextCursor", "完整查询页不能继续返回游标。");
        }

        return failures.ToArray();
    }

    private static void ValidateFilter(CadQueryFilter? filter, List<CadValidationFailure> failures)
    {
        if (filter is null)
        {
            failures.Add(new CadValidationFailure("cad_query_filter_required", "$.filter", "查询过滤器不能为空。"));
            return;
        }

        ValidateValues(filter.EntityTypes, DrawingIndexContractConstants.MaximumTypeCharacters,
            "$.filter.entityTypes", failures);
        ValidateValues(filter.Layers, DrawingIndexContractConstants.MaximumNameCharacters,
            "$.filter.layers", failures);
        ValidateValues(filter.Spaces, DrawingIndexContractConstants.MaximumNameCharacters,
            "$.filter.spaces", failures);
        ValidateValues(filter.BlockNames, DrawingIndexContractConstants.MaximumNameCharacters,
            "$.filter.blockNames", failures);
        ValidateValues(filter.ObjectIds, 32, "$.filter.objectIds", failures);
        ValidateSafeOptionalString(filter.TextContains,
            DrawingIndexContractConstants.MaximumTextQueryCharacters, "cad_query_text",
            "$.filter.textContains", failures);
        if (filter.Bounds is not null)
        {
            ValidateBounds(filter.Bounds.Minimum, filter.Bounds.Maximum, "$.filter.bounds", failures);
        }
    }

    private static void ValidateEntity(
        CadQueryEntity? entity,
        string path,
        List<CadValidationFailure> failures)
    {
        if (entity is null)
        {
            failures.Add(new CadValidationFailure("cad_query_entity_required", path, "查询实体不能为空。"));
            return;
        }

        ValidateIdentifier(entity.ObjectId, "cad_query_object_id", path + ".objectId", failures, 32);
        ValidateSafeRequiredString(entity.EntityType, DrawingIndexContractConstants.MaximumTypeCharacters,
            "cad_query_entity_type", path + ".entityType", failures);
        ValidateSafeRequiredString(entity.ActualType, DrawingIndexContractConstants.MaximumTypeCharacters,
            "cad_query_actual_type", path + ".actualType", failures);
        ValidateSafeRequiredString(entity.Layer, DrawingIndexContractConstants.MaximumNameCharacters,
            "cad_query_layer", path + ".layer", failures);
        ValidateSafeRequiredString(entity.Space, DrawingIndexContractConstants.MaximumNameCharacters,
            "cad_query_space", path + ".space", failures);
        ValidateSafeOptionalString(entity.BlockName, DrawingIndexContractConstants.MaximumNameCharacters,
            "cad_query_block", path + ".blockName", failures);
        if (entity.BlockDetails is not null)
        {
            Require(
                entity.EntityType == CadContextEntityTypesV2.BlockReference,
                failures,
                "cad_query_block_details_type",
                path + ".blockDetails",
                "块详情只能附加在块参照实体上。");
            ValidateBlockDetails(entity.BlockDetails, path + ".blockDetails", failures);
        }
        ValidateSafeOptionalString(entity.TextExcerpt,
            DrawingIndexContractConstants.MaximumTextExcerptCharacters,
            "cad_query_text_excerpt", path + ".textExcerpt", failures);
        Require(IsKnownReadStatus(entity.ReadStatus), failures,
            "cad_query_read_status", path + ".readStatus", "查询实体读取状态不受支持。");
        Require(entity.Unsupported == (entity.ReadStatus != CadQueryReadStatuses.Parsed), failures,
            "cad_query_unsupported_flag", path + ".unsupported", "unsupported标志与读取状态不一致。");
        if (entity.Bounds is not null)
        {
            ValidateBounds(entity.Bounds.Minimum, entity.Bounds.Maximum, path + ".bounds", failures);
        }
    }

    private static void ValidateBlockDetails(
        CadQueryBlockDetails details,
        string path,
        List<CadValidationFailure> failures)
    {
        Require(IsKnownBlockDetailStatus(details.DetailStatus), failures,
            "cad_query_block_detail_status", path + ".detailStatus", "块详情状态不受支持。");
        Require(details.AttributeCount >= 0
                && details.AttributeCount <= DrawingIndexContractConstants.MaximumReportedEntities,
            failures, "cad_query_block_attribute_count", path + ".attributeCount",
            "块属性总数必须处于安全范围内。");
        Require(details.DynamicPropertyCount >= 0
                && details.DynamicPropertyCount <= DrawingIndexContractConstants.MaximumReportedEntities,
            failures, "cad_query_block_dynamic_property_count", path + ".dynamicPropertyCount",
            "动态属性总数必须处于安全范围内。");
        Require(details.NestedBlockReferenceCount >= 0
                && details.NestedBlockReferenceCount
                <= DrawingIndexContractConstants.MaximumNestedBlockReferences,
            failures, "cad_query_block_nested_count", path + ".nestedBlockReferenceCount",
            "嵌套块参照数量超出安全范围。");
        Require(details.MaximumNestedBlockDepth >= 0
                && details.MaximumNestedBlockDepth
                <= DrawingIndexContractConstants.MaximumNestedBlockDepth,
            failures, "cad_query_block_nested_depth", path + ".maximumNestedBlockDepth",
            "嵌套块深度超出安全范围。");

        var attributes = details.Attributes ?? new CadQueryBlockAttribute[0];
        var properties = details.DynamicProperties ?? new CadQueryDynamicBlockProperty[0];
        Require(attributes.Length <= DrawingIndexContractConstants.MaximumBlockAttributes,
            failures, "cad_query_block_attributes_limit", path + ".attributes",
            "块属性摘要数量超出安全上限。");
        Require(properties.Length <= DrawingIndexContractConstants.MaximumDynamicBlockProperties,
            failures, "cad_query_block_dynamic_properties_limit", path + ".dynamicProperties",
            "动态块属性摘要数量超出安全上限。");
        Require(details.AttributeCount >= attributes.Length,
            failures, "cad_query_block_attribute_count_consistency", path + ".attributeCount",
            "块属性总数不能小于已返回的属性数量。");
        Require(details.DynamicPropertyCount >= properties.Length,
            failures, "cad_query_block_dynamic_property_count_consistency", path + ".dynamicPropertyCount",
            "动态属性总数不能小于已返回的属性数量。");

        if (details.DetailStatus == CadQueryBlockDetailStatuses.Complete)
        {
            Require(details.AttributeCount == attributes.Length,
                failures, "cad_query_block_attributes_complete", path + ".attributes",
                "完整块详情必须返回全部受限属性。");
            Require(details.DynamicPropertyCount == properties.Length,
                failures, "cad_query_block_dynamic_properties_complete", path + ".dynamicProperties",
                "完整块详情必须返回全部受限动态属性。");
        }

        ValidateLayoutDetails(details, path, failures);
        for (var index = 0; index < attributes.Length; index++)
        {
            var attribute = attributes[index];
            var itemPath = path + ".attributes[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (attribute is null)
            {
                failures.Add(new CadValidationFailure(
                    "cad_query_block_attribute_required", itemPath, "块属性摘要不能为空。"));
                continue;
            }
            ValidateSafeRequiredString(attribute.Tag,
                DrawingIndexContractConstants.MaximumBlockAttributeTagCharacters,
                "cad_query_block_attribute_tag", itemPath + ".tag", failures);
            ValidateSafeOptionalString(attribute.Value,
                DrawingIndexContractConstants.MaximumBlockAttributeValueCharacters,
                "cad_query_block_attribute_value", itemPath + ".value", failures);
        }

        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            var itemPath = path + ".dynamicProperties["
                + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (property is null)
            {
                failures.Add(new CadValidationFailure(
                    "cad_query_block_dynamic_property_required", itemPath,
                    "动态块属性摘要不能为空。"));
                continue;
            }
            ValidateSafeRequiredString(property.Name,
                DrawingIndexContractConstants.MaximumDynamicBlockPropertyNameCharacters,
                "cad_query_block_dynamic_property_name", itemPath + ".name", failures);
            Require(IsKnownDynamicValueKind(property.ValueKind), failures,
                "cad_query_block_dynamic_property_value_kind", itemPath + ".valueKind",
                "动态块属性值类型不受支持。");
            ValidateSafeOptionalString(property.Value,
                DrawingIndexContractConstants.MaximumDynamicBlockPropertyValueCharacters,
                "cad_query_block_dynamic_property_value", itemPath + ".value", failures);
            if (property.ValueKind == CadQueryDynamicValueKinds.Unavailable)
            {
                Require(string.IsNullOrEmpty(property.Value), failures,
                    "cad_query_block_dynamic_property_unavailable_value", itemPath + ".value",
                    "不可用动态属性不得携带未分类值。");
            }
        }
    }

    private static void ValidateLayoutDetails(
        CadQueryBlockDetails details,
        string path,
        List<CadValidationFailure> failures)
    {
        Require(IsKnownLayoutKind(details.LayoutKind), failures,
            "cad_query_block_layout_kind", path + ".layoutKind", "布局类型不受支持。");
        ValidateSafeOptionalString(details.LayoutName,
            DrawingIndexContractConstants.MaximumNameCharacters,
            "cad_query_block_layout_name", path + ".layoutName", failures);

        if (!details.IsLayoutDefinition)
        {
            Require(details.LayoutKind == CadQueryLayoutKinds.None,
                failures, "cad_query_block_layout_non_layout_kind", path + ".layoutKind",
                "非布局块定义只能使用none布局类型。");
            Require(string.IsNullOrEmpty(details.LayoutName),
                failures, "cad_query_block_layout_non_layout_name", path + ".layoutName",
                "非布局块定义不能携带布局名称。");
            return;
        }

        Require(details.LayoutKind != CadQueryLayoutKinds.None,
            failures, "cad_query_block_layout_kind_required", path + ".layoutKind",
            "布局块定义必须标记模型、图纸或不可用布局类型。");
        if (details.LayoutKind != CadQueryLayoutKinds.Unavailable)
        {
            ValidateSafeRequiredString(details.LayoutName,
                DrawingIndexContractConstants.MaximumNameCharacters,
                "cad_query_block_layout_name_required", path + ".layoutName", failures);
        }
    }

    private static void ValidateBuckets(
        DrawingIndexCountBucket[]? buckets,
        string path,
        List<CadValidationFailure> failures)
    {
        var values = buckets ?? new DrawingIndexCountBucket[0];
        Require(values.Length <= DrawingIndexContractConstants.MaximumCountBuckets, failures,
            "drawing_count_buckets_limit", path, "统计桶数量超出硬限额。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < values.Length; index++)
        {
            var bucket = values[index];
            var itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (bucket is null)
            {
                failures.Add(new CadValidationFailure("drawing_count_bucket_required", itemPath, "统计桶不能为空。"));
                continue;
            }

            ValidateSafeRequiredString(bucket.Key, DrawingIndexContractConstants.MaximumNameCharacters,
                "drawing_count_bucket_key", itemPath + ".key", failures);
            Require(bucket.Count >= 0, failures,
                "drawing_count_bucket_value", itemPath + ".count", "统计值不能为负数。");
            if (!string.IsNullOrWhiteSpace(bucket.Key))
            {
                Require(seen.Add(bucket.Key), failures,
                    "drawing_count_bucket_duplicate", itemPath + ".key", "统计桶键不能重复。");
            }
        }
    }

    private static void ValidateValues(
        string[]? values,
        int maximumCharacters,
        string path,
        List<CadValidationFailure> failures)
    {
        var array = values ?? new string[0];
        Require(array.Length <= DrawingIndexContractConstants.MaximumFilterValues, failures,
            "cad_query_filter_limit", path, "过滤值数量超出硬限额。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < array.Length; index++)
        {
            var itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            ValidateSafeRequiredString(array[index], maximumCharacters,
                "cad_query_filter_value", itemPath, failures);
            if (!string.IsNullOrWhiteSpace(array[index]))
            {
                Require(seen.Add(array[index]), failures,
                    "cad_query_filter_duplicate", itemPath, "过滤值不能重复。");
            }
        }
    }

    private static void ValidateBounds(
        CadPoint3? minimum,
        CadPoint3? maximum,
        string path,
        List<CadValidationFailure> failures)
    {
        if (minimum is null || maximum is null)
        {
            failures.Add(new CadValidationFailure("cad_query_bounds_required", path, "范围最小点和最大点均不能为空。"));
            return;
        }

        Require(IsSafePoint(minimum) && IsSafePoint(maximum), failures,
            "cad_query_bounds_finite", path, "范围坐标必须为有限且受限的数值。");
        Require(minimum.X <= maximum.X && minimum.Y <= maximum.Y && minimum.Z <= maximum.Z,
            failures, "cad_query_bounds_order", path, "范围最小点不能大于最大点。");
    }

    private static void ValidateIdentifier(
        string? value,
        string code,
        string path,
        List<CadValidationFailure> failures,
        int maximum = DrawingIndexContractConstants.MaximumIdentifierCharacters)
    {
        ValidateSafeRequiredString(value, maximum, code, path, failures);
        if (value is null || value.Length == 0)
        {
            return;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            Require((character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character is '-' or '_' or ':' or '.', failures,
                code + "_format", path, "标识符只能包含ASCII字母、数字、点、冒号、下划线或连字符。");
        }
    }

    private static void ValidateSafeRequiredString(
        string? value,
        int maximum,
        string code,
        string path,
        List<CadValidationFailure> failures)
    {
        Require(!string.IsNullOrWhiteSpace(value), failures, code, path, "字符串不能为空。");
        if (value is not null && value.Length != 0)
        {
            Require(value.Length <= maximum && IsSafeString(value), failures,
                code + "_format", path, "字符串超长或包含不安全字符。");
        }
    }

    private static void ValidateSafeOptionalString(
        string? value,
        int maximum,
        string code,
        string path,
        List<CadValidationFailure> failures)
    {
        if (value is null || value.Length == 0)
        {
            return;
        }

        Require(value.Length <= maximum && IsSafeString(value), failures,
            code, path, "字符串超长或包含不安全字符。");
    }

    private static void ValidateTimestamp(
        string? value,
        bool optional,
        string path,
        List<CadValidationFailure> failures)
    {
        if (optional && string.IsNullOrEmpty(value))
        {
            return;
        }

        DateTime parsed;
        Require(!string.IsNullOrWhiteSpace(value)
                && DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed),
            failures, "drawing_timestamp", path, "时间戳必须是可解析的UTC时间。");
    }

    private static bool IsSafePoint(CadPoint3 point)
        => point.IsFinite
           && Math.Abs(point.X) <= DrawingIndexContractConstants.MaximumCoordinateMagnitude
           && Math.Abs(point.Y) <= DrawingIndexContractConstants.MaximumCoordinateMagnitude
           && Math.Abs(point.Z) <= DrawingIndexContractConstants.MaximumCoordinateMagnitude;

    private static bool IsSafeString(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0' || char.IsControl(character))
            {
                return false;
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
                index++;
                continue;
            }
            if (char.IsLowSurrogate(character))
            {
                return false;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            if (category is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9')
                  || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownScope(string value)
        => value == DrawingIndexScopes.Selection
           || value == DrawingIndexScopes.CurrentSpace
           || value == DrawingIndexScopes.ModelSpace
           || value == DrawingIndexScopes.Layouts
           || value == DrawingIndexScopes.Drawing;

    private static bool IsKnownIndexStatus(string value)
        => value == DrawingIndexStatuses.NotBuilt
           || value == DrawingIndexStatuses.Preparing
           || value == DrawingIndexStatuses.Scanning
           || value == DrawingIndexStatuses.Ready
           || value == DrawingIndexStatuses.Partial
           || value == DrawingIndexStatuses.Limited
           || value == DrawingIndexStatuses.Cancelled
           || value == DrawingIndexStatuses.Stale
           || value == DrawingIndexStatuses.Failed;

    private static bool IsKnownQueryStatus(string value)
        => value == CadQueryStatuses.Ok
           || value == CadQueryStatuses.Partial
           || value == CadQueryStatuses.Limited
           || value == CadQueryStatuses.Stale
           || value == CadQueryStatuses.Cancelled
           || value == CadQueryStatuses.Failed;

    private static bool IsKnownReadStatus(string value)
        => value == CadQueryReadStatuses.Parsed
           || value == CadQueryReadStatuses.Unsupported
           || value == CadQueryReadStatuses.DataLimited
           || value == CadQueryReadStatuses.ReadFailed;

    private static bool IsKnownBlockDetailStatus(string value)
        => value == CadQueryBlockDetailStatuses.Complete
           || value == CadQueryBlockDetailStatuses.Limited;

    private static bool IsKnownLayoutKind(string value)
        => value == CadQueryLayoutKinds.None
           || value == CadQueryLayoutKinds.Model
           || value == CadQueryLayoutKinds.Paper
           || value == CadQueryLayoutKinds.Unavailable;

    private static bool IsKnownDynamicValueKind(string value)
        => value == CadQueryDynamicValueKinds.Text
           || value == CadQueryDynamicValueKinds.Number
           || value == CadQueryDynamicValueKinds.Boolean
           || value == CadQueryDynamicValueKinds.Point
           || value == CadQueryDynamicValueKinds.Enum
           || value == CadQueryDynamicValueKinds.Unavailable;

    private static void Require(
        bool condition,
        List<CadValidationFailure> failures,
        string code,
        string path,
        string message)
    {
        if (!condition)
        {
            failures.Add(new CadValidationFailure(code, path, message));
        }
    }
}

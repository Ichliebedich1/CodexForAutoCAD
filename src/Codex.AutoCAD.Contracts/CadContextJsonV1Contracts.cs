using System.Globalization;
using System.Text;

namespace Codex.AutoCAD.Contracts;

/// <summary>
/// CadContextJson wire-schema constants. This schema version is intentionally independent from
/// <see cref="ProtocolConstants.CurrentVersion"/>, which versions the authenticated IPC envelope.
/// </summary>
public static class CadContextJsonV1Constants
{
    public const string Schema = "codex.autocad.cad-context";
    public const int SchemaVersion = 1;
    public const string ReadOnlySelectionSource = "autocad.readonly-selection";
    public const string ContextEgressRisk = "context-egress";
    public const string ModelSpace = "model";
    public const string PaperSpace = "paper";
    public const int MaximumEntities = 64;
    public const int MaximumPolylineVertices = 256;
    public const int MaximumTextCharacters = 2_048;
    public const int MaximumNameCharacters = 255;
    public const int MaximumCanonicalJsonBytes = 256 * 1024;
    public const double MaximumCoordinateMagnitude = 1_000_000_000d;
}

public static class CadContextEntityTypes
{
    public const string Line = "line";
    public const string Circle = "circle";
    public const string Polyline = "polyline";
    public const string DbText = "dbText";
    public const string MText = "mText";
    public const string BlockReference = "blockReference";
}

public sealed class CadContextJsonV1
{
    public string Schema { get; set; } = CadContextJsonV1Constants.Schema;

    public int SchemaVersion { get; set; } = CadContextJsonV1Constants.SchemaVersion;

    public string CapturedAtUtc { get; set; } = string.Empty;

    public string Source { get; set; } = CadContextJsonV1Constants.ReadOnlySelectionSource;

    public string EgressRisk { get; set; } = CadContextJsonV1Constants.ContextEgressRisk;

    public CadContextDocumentV1 Document { get; set; } = new();

    public CadContextSelectionV1 Selection { get; set; } = new();
}

public sealed class CadContextDocumentV1
{
    /// <summary>Opaque per-open-document identity. It must not contain a file name or path.</summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Lower-case SHA-256 drawing identity produced by the trusted Host.</summary>
    public string DrawingFingerprint { get; set; } = string.Empty;

    public long Revision { get; set; }

    public string CurrentSpace { get; set; } = string.Empty;

    public string DrawingVersion { get; set; } = string.Empty;

    public string Units { get; set; } = string.Empty;
}

public sealed class CadContextSelectionV1
{
    /// <summary>
    /// Lower-case SHA-256 of the trusted Host selection snapshot. For the 2016 read-only source,
    /// this remains the already verified binary-v1 selection hash rather than a hash invented by UI.
    /// </summary>
    public string SnapshotHash { get; set; } = string.Empty;

    public int EntityCount { get; set; }

    public CadContextEntityV1[] Entities { get; set; } = new CadContextEntityV1[0];
}

public sealed class CadContextEntityV1
{
    public string Handle { get; set; } = string.Empty;

    public string OwnerSpaceHandle { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string StateHash { get; set; } = string.Empty;

    public string Layer { get; set; } = string.Empty;

    public CadContextLineV1? Line { get; set; }

    public CadContextCircleV1? Circle { get; set; }

    public CadContextPolylineV1? Polyline { get; set; }

    public CadContextDbTextV1? DbText { get; set; }

    public CadContextMTextV1? MText { get; set; }

    public CadContextBlockReferenceV1? BlockReference { get; set; }
}

public sealed class CadContextLineV1
{
    public CadPoint3 Start { get; set; } = new();

    public CadPoint3 End { get; set; } = new();
}

public sealed class CadContextCircleV1
{
    public CadPoint3 Center { get; set; } = new();

    public double Radius { get; set; }

    public CadPoint3 Normal { get; set; } = new();
}

public sealed class CadContextPolylineVertexV1
{
    public CadPoint2 Position { get; set; } = new();

    public double Bulge { get; set; }
}

public sealed class CadContextPolylineV1
{
    public bool Closed { get; set; }

    public double Elevation { get; set; }

    public CadPoint3 Normal { get; set; } = new();

    public CadContextPolylineVertexV1[] Vertices { get; set; } = new CadContextPolylineVertexV1[0];
}

public sealed class CadContextDbTextV1
{
    public string Text { get; set; } = string.Empty;

    public CadPoint3 Position { get; set; } = new();

    public double Height { get; set; }

    public double Rotation { get; set; }
}

public sealed class CadContextMTextV1
{
    public string Text { get; set; } = string.Empty;

    public CadPoint3 Location { get; set; } = new();

    public double TextHeight { get; set; }

    public double Rotation { get; set; }
}

public sealed class CadContextBlockReferenceV1
{
    public CadPoint3 Position { get; set; } = new();

    public double Rotation { get; set; }

    public CadPoint3 Scale { get; set; } = new(1d, 1d, 1d);

    public string EffectiveName { get; set; } = string.Empty;

    public bool IsDynamic { get; set; }

    public bool IsExternalReference { get; set; }
}

public sealed class CadPoint2
{
    public CadPoint2()
    {
    }

    public CadPoint2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; set; }

    public double Y { get; set; }
}

public static class CadContextJsonV1Validator
{
    private const int MaximumIdentifierCharacters = 128;
    private const int MaximumMetadataCharacters = 64;
    private const int MaximumNameBytes = 1_024;
    private const int MaximumTextBytes = 8_192;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static CadValidationFailure[] Validate(CadContextJsonV1? context)
    {
        var failures = new List<CadValidationFailure>();
        if (context is null)
        {
            return [new CadValidationFailure("context_required", "$", "CAD上下文不能为空。")];
        }

        Require(string.Equals(context.Schema, CadContextJsonV1Constants.Schema, StringComparison.Ordinal),
            failures, "context_schema", "$.schema", "CAD上下文schema不受支持。");
        Require(context.SchemaVersion == CadContextJsonV1Constants.SchemaVersion,
            failures, "context_schema_version", "$.schemaVersion", "CAD上下文schema版本不受支持。");
        ValidateCapturedAtUtc(context.CapturedAtUtc, failures);
        Require(string.Equals(context.Source, CadContextJsonV1Constants.ReadOnlySelectionSource,
                StringComparison.Ordinal),
            failures, "context_source", "$.source", "CAD上下文来源不受支持。");
        Require(string.Equals(context.EgressRisk, CadContextJsonV1Constants.ContextEgressRisk,
                StringComparison.Ordinal),
            failures, "context_egress_risk", "$.egressRisk", "CAD上下文外发风险标记不受支持。");

        ValidateDocument(context.Document, failures);
        ValidateSelection(context.Selection, failures);

        if (failures.Count == 0)
        {
            try
            {
                var json = CadContextJsonV1Codec.SerializeCanonicalUnchecked(context);
                var bytes = StrictUtf8.GetByteCount(json);
                Require(bytes <= CadContextJsonV1Constants.MaximumCanonicalJsonBytes,
                    failures, "context_json_bytes_limit", "$",
                    "CAD上下文规范JSON超过安全字节上限。");
            }
            catch (EncoderFallbackException)
            {
                failures.Add(new CadValidationFailure(
                    "context_json_unicode", "$", "CAD上下文不能编码为严格UTF-8。"));
            }
            catch (OverflowException)
            {
                failures.Add(new CadValidationFailure(
                    "context_json_bytes_limit", "$", "CAD上下文规范JSON超过安全字节上限。"));
            }
        }

        return failures.ToArray();
    }

    private static void ValidateCapturedAtUtc(
        string? value,
        ICollection<CadValidationFailure> failures)
    {
        DateTimeOffset parsed;
        var valid = value is not null
            && DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed)
            && parsed.Offset == TimeSpan.Zero;
        Require(valid, failures, "context_captured_at", "$.capturedAtUtc",
            "捕获时间必须是带三位毫秒的UTC RFC3339时间。" );
    }

    private static void ValidateDocument(
        CadContextDocumentV1? document,
        ICollection<CadValidationFailure> failures)
    {
        if (document is null)
        {
            failures.Add(new CadValidationFailure(
                "context_document_required", "$.document", "CAD文档上下文不能为空。"));
            return;
        }

        ValidateString(document.DocumentId, MaximumIdentifierCharacters, MaximumNameBytes,
            false, true, "context_document_id", "$.document.documentId", failures);
        Require(IsOpaqueIdentifier(document.DocumentId), failures,
            "context_document_id", "$.document.documentId",
            "文档标识必须是受限ASCII不透明ID，不能包含文件名或路径分隔符。" );
        Require(IsLowerSha256(document.DrawingFingerprint), failures,
            "context_drawing_fingerprint", "$.document.drawingFingerprint",
            "图纸指纹必须是64位小写ASCII十六进制SHA-256。" );
        Require(document.Revision >= 0, failures,
            "context_revision", "$.document.revision", "图纸修订号不能为负数。" );
        Require(string.Equals(document.CurrentSpace, CadContextJsonV1Constants.ModelSpace,
                    StringComparison.Ordinal)
                || string.Equals(document.CurrentSpace, CadContextJsonV1Constants.PaperSpace,
                    StringComparison.Ordinal),
            failures, "context_current_space", "$.document.currentSpace",
            "当前空间必须是model或paper。" );
        ValidateString(document.DrawingVersion, MaximumMetadataCharacters, MaximumNameBytes,
            false, true, "context_drawing_version", "$.document.drawingVersion", failures);
        ValidateString(document.Units, MaximumMetadataCharacters, MaximumNameBytes,
            false, true, "context_units", "$.document.units", failures);
    }

    private static void ValidateSelection(
        CadContextSelectionV1? selection,
        ICollection<CadValidationFailure> failures)
    {
        if (selection is null)
        {
            failures.Add(new CadValidationFailure(
                "context_selection_required", "$.selection", "选择上下文不能为空。"));
            return;
        }

        Require(IsLowerSha256(selection.SnapshotHash), failures,
            "context_snapshot_hash", "$.selection.snapshotHash",
            "选择快照必须是64位小写ASCII十六进制SHA-256。" );
        var entities = selection.Entities ?? new CadContextEntityV1[0];
        Require(selection.EntityCount == entities.Length, failures,
            "context_entity_count", "$.selection.entityCount",
            "选择图元计数必须与entities长度完全一致。" );
        Require(entities.Length > 0, failures,
            "context_entities_required", "$.selection.entities", "选择上下文至少包含一个图元。" );
        Require(entities.Length <= CadContextJsonV1Constants.MaximumEntities, failures,
            "context_entity_limit", "$.selection.entities", "选择图元数量超过安全上限。" );
        if (entities.Length == 0 || entities.Length > CadContextJsonV1Constants.MaximumEntities)
        {
            return;
        }

        var handles = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entities.Length; index++)
        {
            var entity = entities[index];
            var path = "$.selection.entities[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (entity is null)
            {
                failures.Add(new CadValidationFailure(
                    "context_entity_required", path, "选择图元不能为空。"));
                continue;
            }

            ValidateEntity(entity, path, failures);
            if (IsUpperHandle(entity.Handle))
            {
                Require(handles.Add(entity.Handle), failures,
                    "context_handle_duplicate", path + ".handle", "图元Handle不能重复。" );
            }
        }
    }

    private static void ValidateEntity(
        CadContextEntityV1 entity,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsUpperHandle(entity.Handle), failures,
            "context_handle", path + ".handle",
            "图元Handle必须是1到16位大写ASCII十六进制值。" );
        Require(IsUpperHandle(entity.OwnerSpaceHandle), failures,
            "context_owner_space_handle", path + ".ownerSpaceHandle",
            "所有者空间Handle必须是1到16位大写ASCII十六进制值。" );
        Require(IsLowerSha256(entity.StateHash), failures,
            "context_state_hash", path + ".stateHash",
            "图元状态哈希必须是64位小写ASCII十六进制SHA-256。" );
        ValidateString(entity.Layer, CadContextJsonV1Constants.MaximumNameCharacters,
            MaximumNameBytes, false, true, "context_layer", path + ".layer", failures);

        var shapeCount = 0;
        shapeCount += entity.Line is null ? 0 : 1;
        shapeCount += entity.Circle is null ? 0 : 1;
        shapeCount += entity.Polyline is null ? 0 : 1;
        shapeCount += entity.DbText is null ? 0 : 1;
        shapeCount += entity.MText is null ? 0 : 1;
        shapeCount += entity.BlockReference is null ? 0 : 1;
        Require(shapeCount == 1, failures,
            "context_shape_count", path, "每个图元必须且只能包含一个强类型payload。" );

        switch (entity.EntityType)
        {
            case CadContextEntityTypes.Line:
                Require(entity.Line is not null, failures,
                    "context_shape_mismatch", path + ".line", "line图元必须包含line payload。" );
                if (entity.Line is not null)
                {
                    ValidatePoint3(entity.Line.Start, path + ".line.start", failures);
                    ValidatePoint3(entity.Line.End, path + ".line.end", failures);
                }
                break;
            case CadContextEntityTypes.Circle:
                Require(entity.Circle is not null, failures,
                    "context_shape_mismatch", path + ".circle", "circle图元必须包含circle payload。" );
                if (entity.Circle is not null)
                {
                    ValidatePoint3(entity.Circle.Center, path + ".circle.center", failures);
                    Require(IsBoundedFinite(entity.Circle.Radius) && entity.Circle.Radius > 0d,
                        failures, "context_radius", path + ".circle.radius",
                        "圆半径必须是安全范围内的正有限数。" );
                    ValidateNormal(entity.Circle.Normal, path + ".circle.normal", failures);
                }
                break;
            case CadContextEntityTypes.Polyline:
                Require(entity.Polyline is not null, failures,
                    "context_shape_mismatch", path + ".polyline",
                    "polyline图元必须包含polyline payload。" );
                if (entity.Polyline is not null)
                {
                    ValidatePolyline(entity.Polyline, path + ".polyline", failures);
                }
                break;
            case CadContextEntityTypes.DbText:
                Require(entity.DbText is not null, failures,
                    "context_shape_mismatch", path + ".dbText", "dbText图元必须包含dbText payload。" );
                if (entity.DbText is not null)
                {
                    ValidateText(entity.DbText.Text, path + ".dbText.text", failures);
                    ValidatePoint3(entity.DbText.Position, path + ".dbText.position", failures);
                    ValidatePositive(entity.DbText.Height, path + ".dbText.height",
                        "context_text_height", failures);
                    ValidateScalar(entity.DbText.Rotation, path + ".dbText.rotation",
                        "context_rotation", failures);
                }
                break;
            case CadContextEntityTypes.MText:
                Require(entity.MText is not null, failures,
                    "context_shape_mismatch", path + ".mText", "mText图元必须包含mText payload。" );
                if (entity.MText is not null)
                {
                    ValidateText(entity.MText.Text, path + ".mText.text", failures);
                    ValidatePoint3(entity.MText.Location, path + ".mText.location", failures);
                    ValidatePositive(entity.MText.TextHeight, path + ".mText.textHeight",
                        "context_text_height", failures);
                    ValidateScalar(entity.MText.Rotation, path + ".mText.rotation",
                        "context_rotation", failures);
                }
                break;
            case CadContextEntityTypes.BlockReference:
                Require(entity.BlockReference is not null, failures,
                    "context_shape_mismatch", path + ".blockReference",
                    "blockReference图元必须包含blockReference payload。" );
                if (entity.BlockReference is not null)
                {
                    ValidateBlock(entity.BlockReference, path + ".blockReference", failures);
                }
                break;
            default:
                failures.Add(new CadValidationFailure(
                    "context_entity_type", path + ".entityType", "图元类型不在v1白名单中。"));
                break;
        }
    }

    private static void ValidatePolyline(
        CadContextPolylineV1 polyline,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateScalar(polyline.Elevation, path + ".elevation", "context_elevation", failures);
        ValidateNormal(polyline.Normal, path + ".normal", failures);
        var vertices = polyline.Vertices ?? new CadContextPolylineVertexV1[0];
        Require(vertices.Length > 0, failures,
            "context_polyline_vertices_required", path + ".vertices", "多段线必须至少包含一个顶点。" );
        Require(vertices.Length <= CadContextJsonV1Constants.MaximumPolylineVertices, failures,
            "context_polyline_vertex_limit", path + ".vertices", "多段线顶点数量超过安全上限。" );
        if (vertices.Length == 0
            || vertices.Length > CadContextJsonV1Constants.MaximumPolylineVertices)
        {
            return;
        }

        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = vertices[index];
            var vertexPath = path + ".vertices[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (vertex is null)
            {
                failures.Add(new CadValidationFailure(
                    "context_polyline_vertex_required", vertexPath, "多段线顶点不能为空。"));
                continue;
            }

            ValidatePoint2(vertex.Position, vertexPath + ".position", failures);
            ValidateScalar(vertex.Bulge, vertexPath + ".bulge", "context_bulge", failures);
        }
    }

    private static void ValidateBlock(
        CadContextBlockReferenceV1 block,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidatePoint3(block.Position, path + ".position", failures);
        ValidateScalar(block.Rotation, path + ".rotation", "context_rotation", failures);
        ValidatePoint3(block.Scale, path + ".scale", failures);
        if (block.Scale is not null)
        {
            Require(Math.Abs(block.Scale.X) > 1e-20d
                    && Math.Abs(block.Scale.Y) > 1e-20d
                    && Math.Abs(block.Scale.Z) > 1e-20d,
                failures, "context_block_scale", path + ".scale",
                "块缩放的每个分量都必须非零。" );
        }
        ValidateString(block.EffectiveName, CadContextJsonV1Constants.MaximumNameCharacters,
            MaximumNameBytes, false, true, "context_block_name", path + ".effectiveName", failures);
    }

    private static void ValidateText(
        string? value,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateString(value, CadContextJsonV1Constants.MaximumTextCharacters,
            MaximumTextBytes, true, false, "context_text", path, failures);
    }

    private static void ValidatePoint2(
        CadPoint2? point,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(point is not null && IsBoundedFinite(point.X) && IsBoundedFinite(point.Y),
            failures, "context_point2", path, "二维坐标必须是安全范围内的有限数。" );
    }

    private static void ValidatePoint3(
        CadPoint3? point,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(point is not null
                && IsBoundedFinite(point.X)
                && IsBoundedFinite(point.Y)
                && IsBoundedFinite(point.Z),
            failures, "context_point3", path, "三维坐标必须是安全范围内的有限数。" );
    }

    private static void ValidateNormal(
        CadPoint3? normal,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidatePoint3(normal, path, failures);
        if (normal is not null
            && IsBoundedFinite(normal.X)
            && IsBoundedFinite(normal.Y)
            && IsBoundedFinite(normal.Z))
        {
            var lengthSquared = (normal.X * normal.X)
                + (normal.Y * normal.Y)
                + (normal.Z * normal.Z);
            Require(lengthSquared > 1e-20d, failures,
                "context_normal_zero", path, "法向量不能为零向量。" );
        }
    }

    private static void ValidatePositive(
        double value,
        string path,
        string code,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsBoundedFinite(value) && value > 0d,
            failures, code, path, "数值必须是安全范围内的正有限数。" );
    }

    private static void ValidateScalar(
        double value,
        string path,
        string code,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsBoundedFinite(value), failures, code, path,
            "数值必须处于安全范围且为有限数。" );
    }

    private static void ValidateString(
        string? value,
        int maximumCharacters,
        int maximumBytes,
        bool allowTextControls,
        bool requireNonEmpty,
        string code,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        if (value is null || (requireNonEmpty && string.IsNullOrWhiteSpace(value)))
        {
            failures.Add(new CadValidationFailure(code, path, "字符串不能为空。"));
            return;
        }

        if (value.Length > maximumCharacters)
        {
            failures.Add(new CadValidationFailure(code + "_characters", path,
                "字符串字符数超过安全上限。"));
            return;
        }

        if (!IsSafeUnicode(value, allowTextControls))
        {
            failures.Add(new CadValidationFailure(code + "_unicode", path,
                "字符串包含无效代理项、NUL、危险格式或不允许的控制字符。"));
            return;
        }

        try
        {
            Require(StrictUtf8.GetByteCount(value) <= maximumBytes, failures,
                code + "_bytes", path, "字符串UTF-8字节数超过安全上限。" );
        }
        catch (EncoderFallbackException)
        {
            failures.Add(new CadValidationFailure(code + "_unicode", path,
                "字符串不能编码为严格UTF-8。"));
        }
    }

    private static bool IsSafeUnicode(string value, bool allowTextControls)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0')
            {
                return false;
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                if (index == 0 || !char.IsHighSurrogate(value[index - 1]))
                {
                    return false;
                }

                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            if (category is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }

            if (category == UnicodeCategory.Control)
            {
                var allowed = allowTextControls
                    && (character == '\r' || character == '\n' || character == '\t');
                if (!allowed)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsUpperHandle(string? value)
    {
        return value is { Length: >= 1 and <= 16 }
            && value.All(static character =>
                character is >= '0' and <= '9'
                or >= 'A' and <= 'F');
    }

    private static bool IsLowerSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(static character =>
                character is >= '0' and <= '9'
                or >= 'a' and <= 'f');
    }

    private static bool IsOpaqueIdentifier(string? value)
    {
        return value is { Length: >= 1 and <= MaximumIdentifierCharacters }
            && value.All(static character =>
                character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_' or '.' or ':');
    }

    private static bool IsBoundedFinite(double value)
    {
        return !double.IsNaN(value)
            && !double.IsInfinity(value)
            && Math.Abs(value) <= CadContextJsonV1Constants.MaximumCoordinateMagnitude;
    }

    private static void Require(
        bool condition,
        ICollection<CadValidationFailure> failures,
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

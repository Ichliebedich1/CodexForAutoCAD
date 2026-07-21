using System.Globalization;
using System.Text;

namespace Codex.AutoCAD.Contracts;

public static class CadContextJsonV2Constants
{
    public const string Schema = "codex.autocad.cad-context";
    public const int SchemaVersion = 2;
    public const string ReadOnlySelectionSource = "autocad.readonly-selection";
    public const string ContextEgressRisk = "context-egress";
    public const string ModelSpace = "model";
    public const string PaperSpace = "paper";
    public const int MaximumEntities = 64;
    public const int MaximumPolylineVertices = 256;
    public const int MaximumSplinePoints = 256;
    public const int MaximumHatchLoops = 128;
    public const int MaximumLeaderVertices = 256;
    public const int MaximumMLeaderLines = 64;
    public const int MaximumMLeaderVertices = 256;
    public const int MaximumTableCells = 64;
    public const int MaximumTextCharacters = 2_048;
    public const int MaximumNameCharacters = 255;
    public const int MaximumTokenCharacters = 64;
    public const int MaximumCanonicalJsonBytes = 256 * 1024;
    public const double MaximumCoordinateMagnitude = 1_000_000_000d;
}

public static class CadContextEntityTypesV2
{
    public const string Line = "line";
    public const string Circle = "circle";
    public const string Polyline = "polyline";
    public const string DbText = "dbText";
    public const string MText = "mText";
    public const string BlockReference = "blockReference";
    public const string Arc = "arc";
    public const string Ellipse = "ellipse";
    public const string Spline = "spline";
    public const string Point = "point";
    public const string Ray = "ray";
    public const string Xline = "xline";
    public const string Polyline2d = "polyline2d";
    public const string Polyline3d = "polyline3d";
    public const string Dimension = "dimension";
    public const string Hatch = "hatch";
    public const string Leader = "leader";
    public const string MLeader = "mLeader";
    public const string Table = "table";
    public const string Unsupported = "unsupported";
}

public static class CadContextUnsupportedReasonsV2
{
    public const string UnknownEntityType = "unknown-entity-type";
    public const string EntityReadFailed = "entity-read-failed";
    public const string EntityDataLimit = "entity-data-limit";
}

public sealed class CadContextJsonV2
{
    public string Schema { get; set; } = CadContextJsonV2Constants.Schema;
    public int SchemaVersion { get; set; } = CadContextJsonV2Constants.SchemaVersion;
    public string CapturedAtUtc { get; set; } = string.Empty;
    public string Source { get; set; } = CadContextJsonV2Constants.ReadOnlySelectionSource;
    public string EgressRisk { get; set; } = CadContextJsonV2Constants.ContextEgressRisk;
    public CadContextDocumentV2 Document { get; set; } = new();
    public CadContextSelectionV2 Selection { get; set; } = new();
}

public sealed class CadContextDocumentV2
{
    public string DocumentId { get; set; } = string.Empty;
    public string DrawingFingerprint { get; set; } = string.Empty;
    public long Revision { get; set; }
    public string CurrentSpace { get; set; } = string.Empty;
    public string DrawingVersion { get; set; } = string.Empty;
    public string Units { get; set; } = string.Empty;
}

public sealed class CadContextSelectionV2
{
    public string SnapshotHash { get; set; } = string.Empty;
    public int EntityCount { get; set; }
    public int ParsedEntityCount { get; set; }
    public int UnsupportedEntityCount { get; set; }
    public bool Complete { get; set; }
    public CadContextEntityV2[] Entities { get; set; } = new CadContextEntityV2[0];
}

public sealed class CadContextEntityV2
{
    public string Handle { get; set; } = string.Empty;
    public string OwnerSpaceHandle { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string StateHash { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public CadContextLineV2? Line { get; set; }
    public CadContextCircleV2? Circle { get; set; }
    public CadContextPolylineV2? Polyline { get; set; }
    public CadContextDbTextV2? DbText { get; set; }
    public CadContextMTextV2? MText { get; set; }
    public CadContextBlockReferenceV2? BlockReference { get; set; }
    public CadContextArcV2? Arc { get; set; }
    public CadContextEllipseV2? Ellipse { get; set; }
    public CadContextSplineV2? Spline { get; set; }
    public CadContextPointV2? Point { get; set; }
    public CadContextRayV2? Ray { get; set; }
    public CadContextXlineV2? Xline { get; set; }
    public CadContextPolyline2dV2? Polyline2d { get; set; }
    public CadContextPolyline3dV2? Polyline3d { get; set; }
    public CadContextDimensionV2? Dimension { get; set; }
    public CadContextHatchV2? Hatch { get; set; }
    public CadContextLeaderV2? Leader { get; set; }
    public CadContextMLeaderV2? MLeader { get; set; }
    public CadContextTableV2? Table { get; set; }
    public CadContextUnsupportedV2? Unsupported { get; set; }
}

public sealed class CadContextLineV2
{
    public CadPoint3 Start { get; set; } = new();
    public CadPoint3 End { get; set; } = new();
}

public sealed class CadContextCircleV2
{
    public CadPoint3 Center { get; set; } = new();
    public double Radius { get; set; }
    public CadPoint3 Normal { get; set; } = new();
}

public sealed class CadContextPolylineVertexV2
{
    public CadPoint2 Position { get; set; } = new();
    public double Bulge { get; set; }
}

public sealed class CadContextPolylineV2
{
    public bool Closed { get; set; }
    public double Elevation { get; set; }
    public CadPoint3 Normal { get; set; } = new();
    public CadContextPolylineVertexV2[] Vertices { get; set; } =
        new CadContextPolylineVertexV2[0];
}

public sealed class CadContextDbTextV2
{
    public string Text { get; set; } = string.Empty;
    public CadPoint3 Position { get; set; } = new();
    public double Height { get; set; }
    public double Rotation { get; set; }
}

public sealed class CadContextMTextV2
{
    public string Text { get; set; } = string.Empty;
    public CadPoint3 Location { get; set; } = new();
    public double TextHeight { get; set; }
    public double Rotation { get; set; }
}

public sealed class CadContextBlockReferenceV2
{
    public CadPoint3 Position { get; set; } = new();
    public double Rotation { get; set; }
    public CadPoint3 Scale { get; set; } = new(1d, 1d, 1d);
    public string EffectiveName { get; set; } = string.Empty;
    public bool IsDynamic { get; set; }
    public bool IsExternalReference { get; set; }
}

public sealed class CadContextArcV2
{
    public CadPoint3 Center { get; set; } = new();
    public double Radius { get; set; }
    public double StartAngle { get; set; }
    public double EndAngle { get; set; }
    public CadPoint3 Normal { get; set; } = new();
}

public sealed class CadContextEllipseV2
{
    public CadPoint3 Center { get; set; } = new();
    public CadPoint3 MajorAxis { get; set; } = new();
    public double RadiusRatio { get; set; }
    public double StartParameter { get; set; }
    public double EndParameter { get; set; }
    public CadPoint3 Normal { get; set; } = new();
}

public sealed class CadContextSplineV2
{
    public int Degree { get; set; }
    public bool IsRational { get; set; }
    public bool HasFitData { get; set; }
    public CadPoint3[] ControlPoints { get; set; } = new CadPoint3[0];
    public CadPoint3[] FitPoints { get; set; } = new CadPoint3[0];
}

public sealed class CadContextPointV2
{
    public CadPoint3 Position { get; set; } = new();
    public CadPoint3 Normal { get; set; } = new();
    public double EcsRotation { get; set; }
}

public sealed class CadContextRayV2
{
    public CadPoint3 BasePoint { get; set; } = new();
    public CadPoint3 SecondPoint { get; set; } = new();
}

public sealed class CadContextXlineV2
{
    public CadPoint3 BasePoint { get; set; } = new();
    public CadPoint3 SecondPoint { get; set; } = new();
}

public sealed class CadContextPolyline2dVertexV2
{
    public CadPoint3 Position { get; set; } = new();
    public double Bulge { get; set; }
    public double StartWidth { get; set; }
    public double EndWidth { get; set; }
}

public sealed class CadContextPolyline2dV2
{
    public bool Closed { get; set; }
    public double Elevation { get; set; }
    public CadPoint3 Normal { get; set; } = new();
    public CadContextPolyline2dVertexV2[] Vertices { get; set; } =
        new CadContextPolyline2dVertexV2[0];
}

public sealed class CadContextPolyline3dV2
{
    public bool Closed { get; set; }
    public CadPoint3[] Vertices { get; set; } = new CadPoint3[0];
}

public sealed class CadContextDimensionV2
{
    public string DimensionType { get; set; } = string.Empty;
    public double Measurement { get; set; }
    public string DimensionText { get; set; } = string.Empty;
    public CadPoint3 TextPosition { get; set; } = new();
    public double TextRotation { get; set; }
    public CadPoint3 Normal { get; set; } = new();
    public string StyleName { get; set; } = string.Empty;
}

public sealed class CadContextHatchV2
{
    public bool Associative { get; set; }
    public bool IsGradient { get; set; }
    public bool IsSolidFill { get; set; }
    public string PatternName { get; set; } = string.Empty;
    public double PatternAngle { get; set; }
    public double PatternScale { get; set; }
    public double Elevation { get; set; }
    public CadPoint3 Normal { get; set; } = new();
    public string[] LoopTypes { get; set; } = new string[0];
}

public sealed class CadContextLeaderV2
{
    public bool IsSplined { get; set; }
    public bool HasArrowHead { get; set; }
    public string AnnotationType { get; set; } = string.Empty;
    public CadPoint3 Normal { get; set; } = new();
    public CadPoint3[] Vertices { get; set; } = new CadPoint3[0];
}

public sealed class CadContextMLeaderLineV2
{
    public CadPoint3[] Vertices { get; set; } = new CadPoint3[0];
}

public sealed class CadContextMLeaderV2
{
    public string ContentType { get; set; } = string.Empty;
    public CadPoint3 Normal { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public CadContextMLeaderLineV2[] LeaderLines { get; set; } =
        new CadContextMLeaderLineV2[0];
}

public sealed class CadContextTableCellV2
{
    public int Row { get; set; }
    public int Column { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class CadContextTableV2
{
    public CadPoint3 Position { get; set; } = new();
    public CadPoint3 Direction { get; set; } = new();
    public int Rows { get; set; }
    public int Columns { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string StyleName { get; set; } = string.Empty;
    public CadContextTableCellV2[] Cells { get; set; } = new CadContextTableCellV2[0];
}

public sealed class CadContextUnsupportedV2
{
    public string DxfName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public static class CadContextJsonV2Validator
{
    private const int MaximumIdentifierCharacters = 128;
    private const int MaximumMetadataCharacters = 64;
    private const int MaximumNameBytes = 1_024;
    private const int MaximumTextBytes = 8_192;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static CadValidationFailure[] Validate(CadContextJsonV2? context)
    {
        var failures = new List<CadValidationFailure>();
        if (context is null)
        {
            return [new CadValidationFailure("context_v2_required", "$", "CAD上下文v2不能为空。")];
        }

        Require(string.Equals(context.Schema, CadContextJsonV2Constants.Schema, StringComparison.Ordinal),
            failures, "context_v2_schema", "$.schema", "CAD上下文v2 schema不受支持。");
        Require(context.SchemaVersion == CadContextJsonV2Constants.SchemaVersion,
            failures, "context_v2_schema_version", "$.schemaVersion",
            "CAD上下文v2 schema版本不受支持。");
        ValidateCapturedAtUtc(context.CapturedAtUtc, failures);
        Require(string.Equals(context.Source, CadContextJsonV2Constants.ReadOnlySelectionSource,
                StringComparison.Ordinal),
            failures, "context_v2_source", "$.source", "CAD上下文v2来源不受支持。");
        Require(string.Equals(context.EgressRisk, CadContextJsonV2Constants.ContextEgressRisk,
                StringComparison.Ordinal),
            failures, "context_v2_egress_risk", "$.egressRisk",
            "CAD上下文v2外发风险标记不受支持。");

        ValidateDocument(context.Document, failures);
        ValidateSelection(context.Selection, failures);

        if (failures.Count == 0)
        {
            try
            {
                var json = CadContextJsonV2Codec.SerializeCanonicalUnchecked(context);
                Require(StrictUtf8.GetByteCount(json)
                        <= CadContextJsonV2Constants.MaximumCanonicalJsonBytes,
                    failures, "context_v2_json_bytes_limit", "$",
                    "CAD上下文v2规范JSON超过安全字节上限。");
            }
            catch (EncoderFallbackException)
            {
                failures.Add(new CadValidationFailure(
                    "context_v2_json_unicode", "$", "CAD上下文v2不能编码为严格UTF-8。"));
            }
            catch (OverflowException)
            {
                failures.Add(new CadValidationFailure(
                    "context_v2_json_bytes_limit", "$", "CAD上下文v2规范JSON超过安全字节上限。"));
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
        Require(valid, failures, "context_v2_captured_at", "$.capturedAtUtc",
            "捕获时间必须是带三位毫秒的UTC RFC3339时间。");
    }

    private static void ValidateDocument(
        CadContextDocumentV2? document,
        ICollection<CadValidationFailure> failures)
    {
        if (document is null)
        {
            failures.Add(new CadValidationFailure(
                "context_v2_document_required", "$.document", "CAD文档上下文v2不能为空。"));
            return;
        }

        ValidateString(document.DocumentId, MaximumIdentifierCharacters, MaximumNameBytes,
            false, true, "context_v2_document_id", "$.document.documentId", failures);
        Require(IsOpaqueIdentifier(document.DocumentId), failures,
            "context_v2_document_id", "$.document.documentId",
            "文档标识必须是受限ASCII不透明ID。");
        Require(IsLowerSha256(document.DrawingFingerprint), failures,
            "context_v2_drawing_fingerprint", "$.document.drawingFingerprint",
            "图纸指纹必须是64位小写ASCII十六进制SHA-256。");
        Require(document.Revision >= 0, failures,
            "context_v2_revision", "$.document.revision", "图纸修订号不能为负数。");
        Require(string.Equals(document.CurrentSpace, CadContextJsonV2Constants.ModelSpace,
                    StringComparison.Ordinal)
                || string.Equals(document.CurrentSpace, CadContextJsonV2Constants.PaperSpace,
                    StringComparison.Ordinal),
            failures, "context_v2_current_space", "$.document.currentSpace",
            "当前空间必须是model或paper。");
        ValidateString(document.DrawingVersion, MaximumMetadataCharacters, MaximumNameBytes,
            false, true, "context_v2_drawing_version", "$.document.drawingVersion", failures);
        ValidateString(document.Units, MaximumMetadataCharacters, MaximumNameBytes,
            false, true, "context_v2_units", "$.document.units", failures);
    }

    private static void ValidateSelection(
        CadContextSelectionV2? selection,
        ICollection<CadValidationFailure> failures)
    {
        if (selection is null)
        {
            failures.Add(new CadValidationFailure(
                "context_v2_selection_required", "$.selection", "选择上下文v2不能为空。"));
            return;
        }

        Require(IsLowerSha256(selection.SnapshotHash), failures,
            "context_v2_snapshot_hash", "$.selection.snapshotHash",
            "选择快照必须是64位小写ASCII十六进制SHA-256。");
        var entities = selection.Entities ?? new CadContextEntityV2[0];
        Require(selection.EntityCount == entities.Length, failures,
            "context_v2_entity_count", "$.selection.entityCount",
            "选择图元计数必须与entities长度完全一致。");
        Require(entities.Length > 0, failures,
            "context_v2_entities_required", "$.selection.entities",
            "选择上下文v2至少包含一个图元。");
        Require(entities.Length <= CadContextJsonV2Constants.MaximumEntities, failures,
            "context_v2_entity_limit", "$.selection.entities",
            "选择图元数量超过安全上限。");
        if (entities.Length == 0 || entities.Length > CadContextJsonV2Constants.MaximumEntities)
        {
            return;
        }

        var unsupportedCount = 0;
        var handles = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entities.Length; index++)
        {
            var entity = entities[index];
            var path = "$.selection.entities[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (entity is null)
            {
                failures.Add(new CadValidationFailure(
                    "context_v2_entity_required", path, "选择图元不能为空。"));
                continue;
            }

            ValidateEntity(entity, path, failures);
            if (string.Equals(entity.EntityType, CadContextEntityTypesV2.Unsupported,
                    StringComparison.Ordinal))
            {
                unsupportedCount++;
            }
            if (IsUpperHandle(entity.Handle))
            {
                Require(handles.Add(entity.Handle), failures,
                    "context_v2_handle_duplicate", path + ".handle", "图元Handle不能重复。");
            }
        }

        var parsedCount = entities.Length - unsupportedCount;
        Require(selection.ParsedEntityCount == parsedCount, failures,
            "context_v2_parsed_count", "$.selection.parsedEntityCount",
            "成功解析图元计数与entities不一致。");
        Require(selection.UnsupportedEntityCount == unsupportedCount, failures,
            "context_v2_unsupported_count", "$.selection.unsupportedEntityCount",
            "未解析图元计数与entities不一致。");
        Require(selection.ParsedEntityCount + selection.UnsupportedEntityCount
                == selection.EntityCount,
            failures, "context_v2_count_sum", "$.selection",
            "成功解析数与未解析数之和必须等于选择数。");
        Require(selection.Complete == (unsupportedCount == 0), failures,
            "context_v2_complete", "$.selection.complete",
            "complete必须准确反映是否存在未解析图元。");
    }

    private static void ValidateEntity(
        CadContextEntityV2 entity,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsUpperHandle(entity.Handle), failures,
            "context_v2_handle", path + ".handle",
            "图元Handle必须是1到16位大写ASCII十六进制值。");
        Require(IsUpperHandle(entity.OwnerSpaceHandle), failures,
            "context_v2_owner_space_handle", path + ".ownerSpaceHandle",
            "所有者空间Handle必须是1到16位大写ASCII十六进制值。");
        Require(IsLowerSha256(entity.StateHash), failures,
            "context_v2_state_hash", path + ".stateHash",
            "图元状态哈希必须是64位小写ASCII十六进制SHA-256。");
        ValidateString(entity.Layer, CadContextJsonV2Constants.MaximumNameCharacters,
            MaximumNameBytes, false, true, "context_v2_layer", path + ".layer", failures);

        var shapeCount = CountPayloads(entity);
        Require(shapeCount == 1, failures,
            "context_v2_shape_count", path, "每个图元必须且只能包含一个强类型payload。");

        switch (entity.EntityType)
        {
            case CadContextEntityTypesV2.Line:
                RequirePayload(entity.Line, path + ".line", failures);
                if (entity.Line is not null)
                {
                    ValidatePoint3(entity.Line.Start, path + ".line.start", failures);
                    ValidatePoint3(entity.Line.End, path + ".line.end", failures);
                    ValidateDistinct(entity.Line.Start, entity.Line.End, path + ".line",
                        "context_v2_line_zero_length", failures);
                }
                break;
            case CadContextEntityTypesV2.Circle:
                RequirePayload(entity.Circle, path + ".circle", failures);
                if (entity.Circle is not null)
                {
                    ValidatePoint3(entity.Circle.Center, path + ".circle.center", failures);
                    ValidatePositive(entity.Circle.Radius, path + ".circle.radius",
                        "context_v2_radius", failures);
                    ValidateNormal(entity.Circle.Normal, path + ".circle.normal", failures);
                }
                break;
            case CadContextEntityTypesV2.Polyline:
                RequirePayload(entity.Polyline, path + ".polyline", failures);
                if (entity.Polyline is not null)
                {
                    ValidateScalar(entity.Polyline.Elevation, path + ".polyline.elevation",
                        "context_v2_elevation", failures);
                    ValidateNormal(entity.Polyline.Normal, path + ".polyline.normal", failures);
                    ValidatePolylineVertices(entity.Polyline.Vertices,
                        path + ".polyline.vertices", failures);
                }
                break;
            case CadContextEntityTypesV2.DbText:
                RequirePayload(entity.DbText, path + ".dbText", failures);
                if (entity.DbText is not null)
                {
                    ValidateText(entity.DbText.Text, path + ".dbText.text", failures);
                    ValidatePoint3(entity.DbText.Position, path + ".dbText.position", failures);
                    ValidatePositive(entity.DbText.Height, path + ".dbText.height",
                        "context_v2_text_height", failures);
                    ValidateScalar(entity.DbText.Rotation, path + ".dbText.rotation",
                        "context_v2_rotation", failures);
                }
                break;
            case CadContextEntityTypesV2.MText:
                RequirePayload(entity.MText, path + ".mText", failures);
                if (entity.MText is not null)
                {
                    ValidateText(entity.MText.Text, path + ".mText.text", failures);
                    ValidatePoint3(entity.MText.Location, path + ".mText.location", failures);
                    ValidatePositive(entity.MText.TextHeight, path + ".mText.textHeight",
                        "context_v2_text_height", failures);
                    ValidateScalar(entity.MText.Rotation, path + ".mText.rotation",
                        "context_v2_rotation", failures);
                }
                break;
            case CadContextEntityTypesV2.BlockReference:
                RequirePayload(entity.BlockReference, path + ".blockReference", failures);
                if (entity.BlockReference is not null)
                {
                    ValidateBlock(entity.BlockReference, path + ".blockReference", failures);
                }
                break;
            case CadContextEntityTypesV2.Arc:
                RequirePayload(entity.Arc, path + ".arc", failures);
                if (entity.Arc is not null)
                {
                    ValidatePoint3(entity.Arc.Center, path + ".arc.center", failures);
                    ValidatePositive(entity.Arc.Radius, path + ".arc.radius",
                        "context_v2_radius", failures);
                    ValidateScalar(entity.Arc.StartAngle, path + ".arc.startAngle",
                        "context_v2_angle", failures);
                    ValidateScalar(entity.Arc.EndAngle, path + ".arc.endAngle",
                        "context_v2_angle", failures);
                    ValidateNormal(entity.Arc.Normal, path + ".arc.normal", failures);
                }
                break;
            case CadContextEntityTypesV2.Ellipse:
                RequirePayload(entity.Ellipse, path + ".ellipse", failures);
                if (entity.Ellipse is not null)
                {
                    ValidatePoint3(entity.Ellipse.Center, path + ".ellipse.center", failures);
                    ValidateVector(entity.Ellipse.MajorAxis, path + ".ellipse.majorAxis", failures);
                    Require(IsBoundedFinite(entity.Ellipse.RadiusRatio)
                            && entity.Ellipse.RadiusRatio > 0d
                            && entity.Ellipse.RadiusRatio <= 1d,
                        failures, "context_v2_ellipse_ratio", path + ".ellipse.radiusRatio",
                        "椭圆半径比必须处于(0,1]。");
                    ValidateScalar(entity.Ellipse.StartParameter,
                        path + ".ellipse.startParameter", "context_v2_parameter", failures);
                    ValidateScalar(entity.Ellipse.EndParameter,
                        path + ".ellipse.endParameter", "context_v2_parameter", failures);
                    ValidateNormal(entity.Ellipse.Normal, path + ".ellipse.normal", failures);
                }
                break;
            case CadContextEntityTypesV2.Spline:
                RequirePayload(entity.Spline, path + ".spline", failures);
                if (entity.Spline is not null)
                {
                    ValidateSpline(entity.Spline, path + ".spline", failures);
                }
                break;
            case CadContextEntityTypesV2.Point:
                RequirePayload(entity.Point, path + ".point", failures);
                if (entity.Point is not null)
                {
                    ValidatePoint3(entity.Point.Position, path + ".point.position", failures);
                    ValidateNormal(entity.Point.Normal, path + ".point.normal", failures);
                    ValidateScalar(entity.Point.EcsRotation, path + ".point.ecsRotation",
                        "context_v2_rotation", failures);
                }
                break;
            case CadContextEntityTypesV2.Ray:
                RequirePayload(entity.Ray, path + ".ray", failures);
                if (entity.Ray is not null)
                {
                    ValidateInfiniteLine(entity.Ray.BasePoint, entity.Ray.SecondPoint,
                        path + ".ray", failures);
                }
                break;
            case CadContextEntityTypesV2.Xline:
                RequirePayload(entity.Xline, path + ".xline", failures);
                if (entity.Xline is not null)
                {
                    ValidateInfiniteLine(entity.Xline.BasePoint, entity.Xline.SecondPoint,
                        path + ".xline", failures);
                }
                break;
            case CadContextEntityTypesV2.Polyline2d:
                RequirePayload(entity.Polyline2d, path + ".polyline2d", failures);
                if (entity.Polyline2d is not null)
                {
                    ValidatePolyline2d(entity.Polyline2d, path + ".polyline2d", failures);
                }
                break;
            case CadContextEntityTypesV2.Polyline3d:
                RequirePayload(entity.Polyline3d, path + ".polyline3d", failures);
                if (entity.Polyline3d is not null)
                {
                    ValidatePoint3Array(entity.Polyline3d.Vertices,
                        CadContextJsonV2Constants.MaximumPolylineVertices, true,
                        path + ".polyline3d.vertices", "context_v2_polyline3d_vertices", failures);
                }
                break;
            case CadContextEntityTypesV2.Dimension:
                RequirePayload(entity.Dimension, path + ".dimension", failures);
                if (entity.Dimension is not null)
                {
                    ValidateDimension(entity.Dimension, path + ".dimension", failures);
                }
                break;
            case CadContextEntityTypesV2.Hatch:
                RequirePayload(entity.Hatch, path + ".hatch", failures);
                if (entity.Hatch is not null)
                {
                    ValidateHatch(entity.Hatch, path + ".hatch", failures);
                }
                break;
            case CadContextEntityTypesV2.Leader:
                RequirePayload(entity.Leader, path + ".leader", failures);
                if (entity.Leader is not null)
                {
                    ValidateLeader(entity.Leader, path + ".leader", failures);
                }
                break;
            case CadContextEntityTypesV2.MLeader:
                RequirePayload(entity.MLeader, path + ".mLeader", failures);
                if (entity.MLeader is not null)
                {
                    ValidateMLeader(entity.MLeader, path + ".mLeader", failures);
                }
                break;
            case CadContextEntityTypesV2.Table:
                RequirePayload(entity.Table, path + ".table", failures);
                if (entity.Table is not null)
                {
                    ValidateTable(entity.Table, path + ".table", failures);
                }
                break;
            case CadContextEntityTypesV2.Unsupported:
                RequirePayload(entity.Unsupported, path + ".unsupported", failures);
                if (entity.Unsupported is not null)
                {
                    ValidateUnsupported(entity.Unsupported, path + ".unsupported", failures);
                }
                break;
            default:
                failures.Add(new CadValidationFailure(
                    "context_v2_entity_type", path + ".entityType",
                    "图元类型不在v2白名单中。"));
                break;
        }
    }

    private static int CountPayloads(CadContextEntityV2 entity)
    {
        var count = 0;
        count += entity.Line is null ? 0 : 1;
        count += entity.Circle is null ? 0 : 1;
        count += entity.Polyline is null ? 0 : 1;
        count += entity.DbText is null ? 0 : 1;
        count += entity.MText is null ? 0 : 1;
        count += entity.BlockReference is null ? 0 : 1;
        count += entity.Arc is null ? 0 : 1;
        count += entity.Ellipse is null ? 0 : 1;
        count += entity.Spline is null ? 0 : 1;
        count += entity.Point is null ? 0 : 1;
        count += entity.Ray is null ? 0 : 1;
        count += entity.Xline is null ? 0 : 1;
        count += entity.Polyline2d is null ? 0 : 1;
        count += entity.Polyline3d is null ? 0 : 1;
        count += entity.Dimension is null ? 0 : 1;
        count += entity.Hatch is null ? 0 : 1;
        count += entity.Leader is null ? 0 : 1;
        count += entity.MLeader is null ? 0 : 1;
        count += entity.Table is null ? 0 : 1;
        count += entity.Unsupported is null ? 0 : 1;
        return count;
    }

    private static void ValidatePolylineVertices(
        CadContextPolylineVertexV2[]? vertices,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        var values = vertices ?? new CadContextPolylineVertexV2[0];
        Require(values.Length > 0, failures,
            "context_v2_polyline_vertices_required", path,
            "多段线必须至少包含一个顶点。");
        Require(values.Length <= CadContextJsonV2Constants.MaximumPolylineVertices, failures,
            "context_v2_polyline_vertex_limit", path, "多段线顶点数量超过安全上限。");
        if (values.Length == 0
            || values.Length > CadContextJsonV2Constants.MaximumPolylineVertices)
        {
            return;
        }
        for (var index = 0; index < values.Length; index++)
        {
            var vertex = values[index];
            var itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (vertex is null)
            {
                failures.Add(new CadValidationFailure(
                    "context_v2_polyline_vertex_required", itemPath, "多段线顶点不能为空。"));
                continue;
            }
            ValidatePoint2(vertex.Position, itemPath + ".position", failures);
            ValidateScalar(vertex.Bulge, itemPath + ".bulge", "context_v2_bulge", failures);
        }
    }

    private static void ValidateBlock(
        CadContextBlockReferenceV2 block,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidatePoint3(block.Position, path + ".position", failures);
        ValidateScalar(block.Rotation, path + ".rotation", "context_v2_rotation", failures);
        ValidatePoint3(block.Scale, path + ".scale", failures);
        if (block.Scale is not null)
        {
            Require(Math.Abs(block.Scale.X) > 1e-20d
                    && Math.Abs(block.Scale.Y) > 1e-20d
                    && Math.Abs(block.Scale.Z) > 1e-20d,
                failures, "context_v2_block_scale", path + ".scale",
                "块缩放的每个分量都必须非零。");
        }
        ValidateString(block.EffectiveName, CadContextJsonV2Constants.MaximumNameCharacters,
            MaximumNameBytes, false, true, "context_v2_block_name",
            path + ".effectiveName", failures);
    }

    private static void ValidateSpline(
        CadContextSplineV2 spline,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(spline.Degree >= 1 && spline.Degree <= 25, failures,
            "context_v2_spline_degree", path + ".degree",
            "样条曲线次数必须处于1到25。");
        var control = spline.ControlPoints ?? new CadPoint3[0];
        var fit = spline.FitPoints ?? new CadPoint3[0];
        Require(control.Length > 0, failures,
            "context_v2_spline_control_required", path + ".controlPoints",
            "样条曲线必须包含控制点。");
        Require((long)control.Length + fit.Length
                <= CadContextJsonV2Constants.MaximumSplinePoints,
            failures, "context_v2_spline_point_limit", path,
            "样条曲线控制点与拟合点总数超过安全上限。");
        Require(spline.HasFitData == (fit.Length > 0), failures,
            "context_v2_spline_fit_consistency", path + ".hasFitData",
            "hasFitData必须与拟合点数组一致。");
        if ((long)control.Length + fit.Length
            > CadContextJsonV2Constants.MaximumSplinePoints)
        {
            return;
        }
        ValidatePoint3Array(control, CadContextJsonV2Constants.MaximumSplinePoints, true,
            path + ".controlPoints", "context_v2_spline_control", failures);
        ValidatePoint3Array(fit, CadContextJsonV2Constants.MaximumSplinePoints, false,
            path + ".fitPoints", "context_v2_spline_fit", failures);
    }

    private static void ValidateInfiniteLine(
        CadPoint3 basePoint,
        CadPoint3 secondPoint,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidatePoint3(basePoint, path + ".basePoint", failures);
        ValidatePoint3(secondPoint, path + ".secondPoint", failures);
        ValidateDistinct(basePoint, secondPoint, path,
            "context_v2_infinite_line_direction", failures);
    }

    private static void ValidatePolyline2d(
        CadContextPolyline2dV2 polyline,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateScalar(polyline.Elevation, path + ".elevation", "context_v2_elevation", failures);
        ValidateNormal(polyline.Normal, path + ".normal", failures);
        var vertices = polyline.Vertices ?? new CadContextPolyline2dVertexV2[0];
        Require(vertices.Length > 0, failures,
            "context_v2_polyline2d_vertices_required", path + ".vertices",
            "旧式二维多段线必须至少包含一个顶点。");
        Require(vertices.Length <= CadContextJsonV2Constants.MaximumPolylineVertices, failures,
            "context_v2_polyline2d_vertex_limit", path + ".vertices",
            "旧式二维多段线顶点数量超过安全上限。");
        if (vertices.Length == 0
            || vertices.Length > CadContextJsonV2Constants.MaximumPolylineVertices)
        {
            return;
        }
        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = vertices[index];
            var itemPath = path + ".vertices[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (vertex is null)
            {
                failures.Add(new CadValidationFailure(
                    "context_v2_polyline2d_vertex_required", itemPath, "顶点不能为空。"));
                continue;
            }
            ValidatePoint3(vertex.Position, itemPath + ".position", failures);
            ValidateScalar(vertex.Bulge, itemPath + ".bulge", "context_v2_bulge", failures);
            ValidateNonNegative(vertex.StartWidth, itemPath + ".startWidth",
                "context_v2_width", failures);
            ValidateNonNegative(vertex.EndWidth, itemPath + ".endWidth",
                "context_v2_width", failures);
        }
    }

    private static void ValidateDimension(
        CadContextDimensionV2 dimension,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateToken(dimension.DimensionType, true,
            "context_v2_dimension_type", path + ".dimensionType", failures);
        ValidateNonNegative(dimension.Measurement, path + ".measurement",
            "context_v2_measurement", failures);
        ValidateText(dimension.DimensionText, path + ".dimensionText", failures);
        ValidatePoint3(dimension.TextPosition, path + ".textPosition", failures);
        ValidateScalar(dimension.TextRotation, path + ".textRotation",
            "context_v2_rotation", failures);
        ValidateNormal(dimension.Normal, path + ".normal", failures);
        ValidateString(dimension.StyleName, CadContextJsonV2Constants.MaximumNameCharacters,
            MaximumNameBytes, false, true, "context_v2_dimension_style",
            path + ".styleName", failures);
    }

    private static void ValidateHatch(
        CadContextHatchV2 hatch,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateString(hatch.PatternName, CadContextJsonV2Constants.MaximumNameCharacters,
            MaximumNameBytes, false, false, "context_v2_hatch_pattern",
            path + ".patternName", failures);
        ValidateScalar(hatch.PatternAngle, path + ".patternAngle",
            "context_v2_angle", failures);
        ValidatePositive(hatch.PatternScale, path + ".patternScale",
            "context_v2_hatch_scale", failures);
        ValidateScalar(hatch.Elevation, path + ".elevation", "context_v2_elevation", failures);
        ValidateNormal(hatch.Normal, path + ".normal", failures);
        var loops = hatch.LoopTypes ?? new string[0];
        Require(loops.Length <= CadContextJsonV2Constants.MaximumHatchLoops, failures,
            "context_v2_hatch_loop_limit", path + ".loopTypes",
            "填充边界环数量超过安全上限。");
        if (loops.Length > CadContextJsonV2Constants.MaximumHatchLoops)
        {
            return;
        }
        for (var index = 0; index < loops.Length; index++)
        {
            ValidateToken(loops[index], true, "context_v2_hatch_loop_type",
                path + ".loopTypes[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                failures);
        }
    }

    private static void ValidateLeader(
        CadContextLeaderV2 leader,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateToken(leader.AnnotationType, true, "context_v2_leader_annotation_type",
            path + ".annotationType", failures);
        ValidateNormal(leader.Normal, path + ".normal", failures);
        var vertices = leader.Vertices ?? new CadPoint3[0];
        Require(vertices.Length >= 2, failures,
            "context_v2_leader_vertices_required", path + ".vertices",
            "引线必须至少包含两个顶点。");
        ValidatePoint3Array(vertices, CadContextJsonV2Constants.MaximumLeaderVertices, true,
            path + ".vertices", "context_v2_leader_vertices", failures);
    }

    private static void ValidateMLeader(
        CadContextMLeaderV2 leader,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateToken(leader.ContentType, true, "context_v2_mleader_content_type",
            path + ".contentType", failures);
        ValidateNormal(leader.Normal, path + ".normal", failures);
        ValidateText(leader.Text, path + ".text", failures);
        var lines = leader.LeaderLines ?? new CadContextMLeaderLineV2[0];
        Require(lines.Length <= CadContextJsonV2Constants.MaximumMLeaderLines, failures,
            "context_v2_mleader_line_limit", path + ".leaderLines",
            "多重引线数量超过安全上限。");
        if (lines.Length > CadContextJsonV2Constants.MaximumMLeaderLines)
        {
            return;
        }
        var totalVertices = 0L;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var itemPath = path + ".leaderLines[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (line is null)
            {
                failures.Add(new CadValidationFailure(
                    "context_v2_mleader_line_required", itemPath, "多重引线不能为空。"));
                continue;
            }
            var vertices = line.Vertices ?? new CadPoint3[0];
            totalVertices += vertices.Length;
            Require(vertices.Length >= 2, failures,
                "context_v2_mleader_vertices_required", itemPath + ".vertices",
                "每条多重引线必须至少包含两个顶点。");
            ValidatePoint3Array(vertices, CadContextJsonV2Constants.MaximumMLeaderVertices,
                true, itemPath + ".vertices", "context_v2_mleader_vertices", failures);
        }
        Require(totalVertices <= CadContextJsonV2Constants.MaximumMLeaderVertices, failures,
            "context_v2_mleader_vertex_limit", path + ".leaderLines",
            "多重引线顶点总数超过安全上限。");
    }

    private static void ValidateTable(
        CadContextTableV2 table,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidatePoint3(table.Position, path + ".position", failures);
        ValidateVector(table.Direction, path + ".direction", failures);
        Require(table.Rows > 0, failures, "context_v2_table_rows", path + ".rows",
            "表格行数必须为正数。");
        Require(table.Columns > 0, failures, "context_v2_table_columns", path + ".columns",
            "表格列数必须为正数。");
        ValidatePositive(table.Width, path + ".width", "context_v2_table_width", failures);
        ValidatePositive(table.Height, path + ".height", "context_v2_table_height", failures);
        ValidateString(table.StyleName, CadContextJsonV2Constants.MaximumNameCharacters,
            MaximumNameBytes, false, true, "context_v2_table_style",
            path + ".styleName", failures);

        var expectedCells = (long)table.Rows * table.Columns;
        var cells = table.Cells ?? new CadContextTableCellV2[0];
        Require(expectedCells <= CadContextJsonV2Constants.MaximumTableCells, failures,
            "context_v2_table_cell_limit", path,
            "表格单元格数量超过安全上限。");
        Require(expectedCells == cells.Length, failures,
            "context_v2_table_cell_count", path + ".cells",
            "表格单元格数组必须完整且与行列数一致。");
        if (expectedCells <= 0
            || expectedCells > CadContextJsonV2Constants.MaximumTableCells
            || expectedCells != cells.Length)
        {
            return;
        }

        var coordinates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            var itemPath = path + ".cells[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (cell is null)
            {
                failures.Add(new CadValidationFailure(
                    "context_v2_table_cell_required", itemPath, "表格单元格不能为空。"));
                continue;
            }
            Require(cell.Row >= 0 && cell.Row < table.Rows, failures,
                "context_v2_table_cell_row", itemPath + ".row", "表格单元格行索引越界。");
            Require(cell.Column >= 0 && cell.Column < table.Columns, failures,
                "context_v2_table_cell_column", itemPath + ".column", "表格单元格列索引越界。");
            if (cell.Row >= 0 && cell.Row < table.Rows
                && cell.Column >= 0 && cell.Column < table.Columns)
            {
                var key = cell.Row.ToString(CultureInfo.InvariantCulture)
                    + ":"
                    + cell.Column.ToString(CultureInfo.InvariantCulture);
                Require(coordinates.Add(key), failures,
                    "context_v2_table_cell_duplicate", itemPath,
                    "表格单元格坐标不能重复。");
            }
            ValidateText(cell.Text, itemPath + ".text", failures);
        }
    }

    private static void ValidateUnsupported(
        CadContextUnsupportedV2 unsupported,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsDxfName(unsupported.DxfName), failures,
            "context_v2_unsupported_dxf", path + ".dxfName",
            "DXF名称必须是受限ASCII令牌。");
        Require(string.Equals(unsupported.Reason,
                    CadContextUnsupportedReasonsV2.UnknownEntityType, StringComparison.Ordinal)
                || string.Equals(unsupported.Reason,
                    CadContextUnsupportedReasonsV2.EntityReadFailed, StringComparison.Ordinal)
                || string.Equals(unsupported.Reason,
                    CadContextUnsupportedReasonsV2.EntityDataLimit, StringComparison.Ordinal),
            failures, "context_v2_unsupported_reason", path + ".reason",
            "未解析原因不在闭集中。");
    }

    private static void ValidatePoint3Array(
        CadPoint3[]? values,
        int maximum,
        bool requireNonEmpty,
        string path,
        string code,
        ICollection<CadValidationFailure> failures)
    {
        var points = values ?? new CadPoint3[0];
        if (requireNonEmpty)
        {
            Require(points.Length > 0, failures, code + "_required", path,
                "坐标数组不能为空。");
        }
        Require(points.Length <= maximum, failures, code + "_limit", path,
            "坐标数组超过安全上限。");
        if (points.Length > maximum)
        {
            return;
        }
        for (var index = 0; index < points.Length; index++)
        {
            ValidatePoint3(points[index],
                path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", failures);
        }
    }

    private static void ValidateText(
        string? value,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateString(value, CadContextJsonV2Constants.MaximumTextCharacters,
            MaximumTextBytes, true, false, "context_v2_text", path, failures);
    }

    private static void ValidateToken(
        string? value,
        bool requireNonEmpty,
        string code,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(value is not null
                && (!requireNonEmpty || value.Length > 0)
                && value.Length <= CadContextJsonV2Constants.MaximumTokenCharacters
                && IsSafeToken(value),
            failures, code, path, "令牌必须是受限ASCII值。");
    }

    private static void ValidatePoint2(
        CadPoint2? point,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(point is not null && IsBoundedFinite(point.X) && IsBoundedFinite(point.Y),
            failures, "context_v2_point2", path, "二维坐标必须是安全范围内的有限数。");
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
            failures, "context_v2_point3", path, "三维坐标必须是安全范围内的有限数。");
    }

    private static void ValidateNormal(
        CadPoint3? normal,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidateVector(normal, path, failures);
    }

    private static void ValidateVector(
        CadPoint3? vector,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        ValidatePoint3(vector, path, failures);
        if (vector is not null
            && IsBoundedFinite(vector.X)
            && IsBoundedFinite(vector.Y)
            && IsBoundedFinite(vector.Z))
        {
            var lengthSquared = (vector.X * vector.X)
                + (vector.Y * vector.Y)
                + (vector.Z * vector.Z);
            Require(lengthSquared > 1e-20d, failures,
                "context_v2_vector_zero", path, "向量不能为零向量。");
        }
    }

    private static void ValidateDistinct(
        CadPoint3? first,
        CadPoint3? second,
        string path,
        string code,
        ICollection<CadValidationFailure> failures)
    {
        if (first is null || second is null
            || !IsBoundedFinite(first.X) || !IsBoundedFinite(first.Y)
            || !IsBoundedFinite(first.Z) || !IsBoundedFinite(second.X)
            || !IsBoundedFinite(second.Y) || !IsBoundedFinite(second.Z))
        {
            return;
        }
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        var dz = first.Z - second.Z;
        Require((dx * dx) + (dy * dy) + (dz * dz) > 1e-20d,
            failures, code, path, "两个方向定义点不能重合。");
    }

    private static void ValidatePositive(
        double value,
        string path,
        string code,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsBoundedFinite(value) && value > 0d,
            failures, code, path, "数值必须是安全范围内的正有限数。");
    }

    private static void ValidateNonNegative(
        double value,
        string path,
        string code,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsBoundedFinite(value) && value >= 0d,
            failures, code, path, "数值必须是安全范围内的非负有限数。");
    }

    private static void ValidateScalar(
        double value,
        string path,
        string code,
        ICollection<CadValidationFailure> failures)
    {
        Require(IsBoundedFinite(value), failures, code, path,
            "数值必须处于安全范围且为有限数。");
    }

    private static void RequirePayload(
        object? payload,
        string path,
        ICollection<CadValidationFailure> failures)
    {
        Require(payload is not null, failures,
            "context_v2_shape_mismatch", path, "图元类型与payload不匹配。");
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
            failures.Add(new CadValidationFailure(
                code + "_characters", path, "字符串字符数超过安全上限。"));
            return;
        }
        if (!IsSafeUnicode(value, allowTextControls))
        {
            failures.Add(new CadValidationFailure(
                code + "_unicode", path, "字符串包含不允许的Unicode字符。"));
            return;
        }
        try
        {
            Require(StrictUtf8.GetByteCount(value) <= maximumBytes, failures,
                code + "_bytes", path, "字符串UTF-8字节数超过安全上限。");
        }
        catch (EncoderFallbackException)
        {
            failures.Add(new CadValidationFailure(
                code + "_unicode", path, "字符串不能编码为严格UTF-8。"));
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

    private static bool IsSafeToken(string value)
    {
        return value.All(static character =>
            character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_' or '-' or ',' or ' ' or '.');
    }

    private static bool IsDxfName(string? value)
    {
        return value is { Length: >= 1 and <= CadContextJsonV2Constants.MaximumTokenCharacters }
            && value.All(static character =>
                character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_' or '-' or '.' or '$');
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
            && Math.Abs(value) <= CadContextJsonV2Constants.MaximumCoordinateMagnitude;
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

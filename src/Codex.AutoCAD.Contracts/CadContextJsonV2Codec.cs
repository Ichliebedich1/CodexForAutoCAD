using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Codex.AutoCAD.Contracts;

public static class CadContextJsonV2Codec
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string SerializeCanonical(CadContextJsonV2 context)
    {
        ThrowIfInvalid(context);
        return SerializeCanonicalUnchecked(context);
    }

    public static byte[] SerializeCanonicalUtf8(CadContextJsonV2 context)
    {
        return StrictUtf8.GetBytes(SerializeCanonical(context));
    }

    public static string ComputeCanonicalSha256(CadContextJsonV2 context)
    {
        var bytes = SerializeCanonicalUtf8(context);
        try
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                try
                {
                    return ToLowerHex(hash);
                }
                finally
                {
                    Array.Clear(hash, 0, hash.Length);
                }
            }
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    internal static string SerializeCanonicalUnchecked(CadContextJsonV2 context)
    {
        var builder = new StringBuilder(16_384);
        builder.Append('{');
        AppendProperty(builder, "schema", context.Schema);
        builder.Append(',');
        AppendProperty(builder, "schemaVersion", context.SchemaVersion);
        builder.Append(',');
        AppendProperty(builder, "capturedAtUtc", context.CapturedAtUtc);
        builder.Append(',');
        AppendProperty(builder, "source", context.Source);
        builder.Append(',');
        AppendProperty(builder, "egressRisk", context.EgressRisk);
        builder.Append(',');
        AppendPropertyName(builder, "document");
        AppendDocument(builder, context.Document);
        builder.Append(',');
        AppendPropertyName(builder, "selection");
        AppendSelection(builder, context.Selection);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendDocument(StringBuilder builder, CadContextDocumentV2 document)
    {
        builder.Append('{');
        AppendProperty(builder, "documentId", document.DocumentId);
        builder.Append(',');
        AppendProperty(builder, "drawingFingerprint", document.DrawingFingerprint);
        builder.Append(',');
        AppendProperty(builder, "revision", document.Revision);
        builder.Append(',');
        AppendProperty(builder, "currentSpace", document.CurrentSpace);
        builder.Append(',');
        AppendProperty(builder, "drawingVersion", document.DrawingVersion);
        builder.Append(',');
        AppendProperty(builder, "units", document.Units);
        builder.Append('}');
    }

    private static void AppendSelection(StringBuilder builder, CadContextSelectionV2 selection)
    {
        builder.Append('{');
        AppendProperty(builder, "snapshotHash", selection.SnapshotHash);
        builder.Append(',');
        AppendProperty(builder, "entityCount", selection.EntityCount);
        builder.Append(',');
        AppendProperty(builder, "parsedEntityCount", selection.ParsedEntityCount);
        builder.Append(',');
        AppendProperty(builder, "unsupportedEntityCount", selection.UnsupportedEntityCount);
        builder.Append(',');
        AppendProperty(builder, "complete", selection.Complete);
        builder.Append(',');
        AppendPropertyName(builder, "entities");
        builder.Append('[');

        var entities = (CadContextEntityV2[])selection.Entities.Clone();
        Array.Sort(entities, CompareEntities);
        for (var index = 0; index < entities.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }
            AppendEntity(builder, entities[index]);
        }
        builder.Append(']');
        builder.Append('}');
    }

    private static void AppendEntity(StringBuilder builder, CadContextEntityV2 entity)
    {
        builder.Append('{');
        AppendProperty(builder, "handle", entity.Handle);
        builder.Append(',');
        AppendProperty(builder, "ownerSpaceHandle", entity.OwnerSpaceHandle);
        builder.Append(',');
        AppendProperty(builder, "entityType", entity.EntityType);
        builder.Append(',');
        AppendProperty(builder, "stateHash", entity.StateHash);
        builder.Append(',');
        AppendProperty(builder, "layer", entity.Layer);
        builder.Append(',');

        switch (entity.EntityType)
        {
            case CadContextEntityTypesV2.Line:
                AppendPropertyName(builder, "line");
                AppendLine(builder, entity.Line!);
                break;
            case CadContextEntityTypesV2.Circle:
                AppendPropertyName(builder, "circle");
                AppendCircle(builder, entity.Circle!);
                break;
            case CadContextEntityTypesV2.Polyline:
                AppendPropertyName(builder, "polyline");
                AppendPolyline(builder, entity.Polyline!);
                break;
            case CadContextEntityTypesV2.DbText:
                AppendPropertyName(builder, "dbText");
                AppendDbText(builder, entity.DbText!);
                break;
            case CadContextEntityTypesV2.MText:
                AppendPropertyName(builder, "mText");
                AppendMText(builder, entity.MText!);
                break;
            case CadContextEntityTypesV2.BlockReference:
                AppendPropertyName(builder, "blockReference");
                AppendBlockReference(builder, entity.BlockReference!);
                break;
            case CadContextEntityTypesV2.Arc:
                AppendPropertyName(builder, "arc");
                AppendArc(builder, entity.Arc!);
                break;
            case CadContextEntityTypesV2.Ellipse:
                AppendPropertyName(builder, "ellipse");
                AppendEllipse(builder, entity.Ellipse!);
                break;
            case CadContextEntityTypesV2.Spline:
                AppendPropertyName(builder, "spline");
                AppendSpline(builder, entity.Spline!);
                break;
            case CadContextEntityTypesV2.Point:
                AppendPropertyName(builder, "point");
                AppendPoint(builder, entity.Point!);
                break;
            case CadContextEntityTypesV2.Ray:
                AppendPropertyName(builder, "ray");
                AppendRay(builder, entity.Ray!);
                break;
            case CadContextEntityTypesV2.Xline:
                AppendPropertyName(builder, "xline");
                AppendXline(builder, entity.Xline!);
                break;
            case CadContextEntityTypesV2.Polyline2d:
                AppendPropertyName(builder, "polyline2d");
                AppendPolyline2d(builder, entity.Polyline2d!);
                break;
            case CadContextEntityTypesV2.Polyline3d:
                AppendPropertyName(builder, "polyline3d");
                AppendPolyline3d(builder, entity.Polyline3d!);
                break;
            case CadContextEntityTypesV2.Dimension:
                AppendPropertyName(builder, "dimension");
                AppendDimension(builder, entity.Dimension!);
                break;
            case CadContextEntityTypesV2.Hatch:
                AppendPropertyName(builder, "hatch");
                AppendHatch(builder, entity.Hatch!);
                break;
            case CadContextEntityTypesV2.Leader:
                AppendPropertyName(builder, "leader");
                AppendLeader(builder, entity.Leader!);
                break;
            case CadContextEntityTypesV2.MLeader:
                AppendPropertyName(builder, "mLeader");
                AppendMLeader(builder, entity.MLeader!);
                break;
            case CadContextEntityTypesV2.Table:
                AppendPropertyName(builder, "table");
                AppendTable(builder, entity.Table!);
                break;
            case CadContextEntityTypesV2.Unsupported:
                AppendPropertyName(builder, "unsupported");
                AppendUnsupported(builder, entity.Unsupported!);
                break;
            default:
                throw new InvalidOperationException("CadContextJson v2 entity type was not validated.");
        }
        builder.Append('}');
    }

    private static void AppendLine(StringBuilder builder, CadContextLineV2 line)
    {
        builder.Append('{');
        AppendPropertyName(builder, "start");
        AppendPoint3(builder, line.Start);
        builder.Append(',');
        AppendPropertyName(builder, "end");
        AppendPoint3(builder, line.End);
        builder.Append('}');
    }

    private static void AppendCircle(StringBuilder builder, CadContextCircleV2 circle)
    {
        builder.Append('{');
        AppendPropertyName(builder, "center");
        AppendPoint3(builder, circle.Center);
        builder.Append(',');
        AppendProperty(builder, "radius", circle.Radius);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, circle.Normal);
        builder.Append('}');
    }

    private static void AppendPolyline(StringBuilder builder, CadContextPolylineV2 polyline)
    {
        builder.Append('{');
        AppendProperty(builder, "closed", polyline.Closed);
        builder.Append(',');
        AppendProperty(builder, "elevation", polyline.Elevation);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, polyline.Normal);
        builder.Append(',');
        AppendPropertyName(builder, "vertices");
        builder.Append('[');
        for (var index = 0; index < polyline.Vertices.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }
            var vertex = polyline.Vertices[index];
            builder.Append('{');
            AppendPropertyName(builder, "position");
            AppendPoint2(builder, vertex.Position);
            builder.Append(',');
            AppendProperty(builder, "bulge", vertex.Bulge);
            builder.Append('}');
        }
        builder.Append(']');
        builder.Append('}');
    }

    private static void AppendDbText(StringBuilder builder, CadContextDbTextV2 text)
    {
        builder.Append('{');
        AppendProperty(builder, "text", text.Text);
        builder.Append(',');
        AppendPropertyName(builder, "position");
        AppendPoint3(builder, text.Position);
        builder.Append(',');
        AppendProperty(builder, "height", text.Height);
        builder.Append(',');
        AppendProperty(builder, "rotation", text.Rotation);
        builder.Append('}');
    }

    private static void AppendMText(StringBuilder builder, CadContextMTextV2 text)
    {
        builder.Append('{');
        AppendProperty(builder, "text", text.Text);
        builder.Append(',');
        AppendPropertyName(builder, "location");
        AppendPoint3(builder, text.Location);
        builder.Append(',');
        AppendProperty(builder, "textHeight", text.TextHeight);
        builder.Append(',');
        AppendProperty(builder, "rotation", text.Rotation);
        builder.Append('}');
    }

    private static void AppendBlockReference(
        StringBuilder builder,
        CadContextBlockReferenceV2 block)
    {
        builder.Append('{');
        AppendPropertyName(builder, "position");
        AppendPoint3(builder, block.Position);
        builder.Append(',');
        AppendProperty(builder, "rotation", block.Rotation);
        builder.Append(',');
        AppendPropertyName(builder, "scale");
        AppendPoint3(builder, block.Scale);
        builder.Append(',');
        AppendProperty(builder, "effectiveName", block.EffectiveName);
        builder.Append(',');
        AppendProperty(builder, "isDynamic", block.IsDynamic);
        builder.Append(',');
        AppendProperty(builder, "isExternalReference", block.IsExternalReference);
        builder.Append('}');
    }

    private static void AppendArc(StringBuilder builder, CadContextArcV2 arc)
    {
        builder.Append('{');
        AppendPropertyName(builder, "center");
        AppendPoint3(builder, arc.Center);
        builder.Append(',');
        AppendProperty(builder, "radius", arc.Radius);
        builder.Append(',');
        AppendProperty(builder, "startAngle", arc.StartAngle);
        builder.Append(',');
        AppendProperty(builder, "endAngle", arc.EndAngle);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, arc.Normal);
        builder.Append('}');
    }

    private static void AppendEllipse(StringBuilder builder, CadContextEllipseV2 ellipse)
    {
        builder.Append('{');
        AppendPropertyName(builder, "center");
        AppendPoint3(builder, ellipse.Center);
        builder.Append(',');
        AppendPropertyName(builder, "majorAxis");
        AppendPoint3(builder, ellipse.MajorAxis);
        builder.Append(',');
        AppendProperty(builder, "radiusRatio", ellipse.RadiusRatio);
        builder.Append(',');
        AppendProperty(builder, "startParameter", ellipse.StartParameter);
        builder.Append(',');
        AppendProperty(builder, "endParameter", ellipse.EndParameter);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, ellipse.Normal);
        builder.Append('}');
    }

    private static void AppendSpline(StringBuilder builder, CadContextSplineV2 spline)
    {
        builder.Append('{');
        AppendProperty(builder, "degree", spline.Degree);
        builder.Append(',');
        AppendProperty(builder, "isRational", spline.IsRational);
        builder.Append(',');
        AppendProperty(builder, "hasFitData", spline.HasFitData);
        builder.Append(',');
        AppendPropertyName(builder, "controlPoints");
        AppendPoint3Array(builder, spline.ControlPoints);
        builder.Append(',');
        AppendPropertyName(builder, "fitPoints");
        AppendPoint3Array(builder, spline.FitPoints);
        builder.Append('}');
    }

    private static void AppendPoint(StringBuilder builder, CadContextPointV2 point)
    {
        builder.Append('{');
        AppendPropertyName(builder, "position");
        AppendPoint3(builder, point.Position);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, point.Normal);
        builder.Append(',');
        AppendProperty(builder, "ecsRotation", point.EcsRotation);
        builder.Append('}');
    }

    private static void AppendRay(StringBuilder builder, CadContextRayV2 ray)
    {
        builder.Append('{');
        AppendPropertyName(builder, "basePoint");
        AppendPoint3(builder, ray.BasePoint);
        builder.Append(',');
        AppendPropertyName(builder, "secondPoint");
        AppendPoint3(builder, ray.SecondPoint);
        builder.Append('}');
    }

    private static void AppendXline(StringBuilder builder, CadContextXlineV2 xline)
    {
        builder.Append('{');
        AppendPropertyName(builder, "basePoint");
        AppendPoint3(builder, xline.BasePoint);
        builder.Append(',');
        AppendPropertyName(builder, "secondPoint");
        AppendPoint3(builder, xline.SecondPoint);
        builder.Append('}');
    }

    private static void AppendPolyline2d(
        StringBuilder builder,
        CadContextPolyline2dV2 polyline)
    {
        builder.Append('{');
        AppendProperty(builder, "closed", polyline.Closed);
        builder.Append(',');
        AppendProperty(builder, "elevation", polyline.Elevation);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, polyline.Normal);
        builder.Append(',');
        AppendPropertyName(builder, "vertices");
        builder.Append('[');
        for (var index = 0; index < polyline.Vertices.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }
            var vertex = polyline.Vertices[index];
            builder.Append('{');
            AppendPropertyName(builder, "position");
            AppendPoint3(builder, vertex.Position);
            builder.Append(',');
            AppendProperty(builder, "bulge", vertex.Bulge);
            builder.Append(',');
            AppendProperty(builder, "startWidth", vertex.StartWidth);
            builder.Append(',');
            AppendProperty(builder, "endWidth", vertex.EndWidth);
            builder.Append('}');
        }
        builder.Append(']');
        builder.Append('}');
    }

    private static void AppendPolyline3d(
        StringBuilder builder,
        CadContextPolyline3dV2 polyline)
    {
        builder.Append('{');
        AppendProperty(builder, "closed", polyline.Closed);
        builder.Append(',');
        AppendPropertyName(builder, "vertices");
        AppendPoint3Array(builder, polyline.Vertices);
        builder.Append('}');
    }

    private static void AppendDimension(
        StringBuilder builder,
        CadContextDimensionV2 dimension)
    {
        builder.Append('{');
        AppendProperty(builder, "dimensionType", dimension.DimensionType);
        builder.Append(',');
        AppendProperty(builder, "measurement", dimension.Measurement);
        builder.Append(',');
        AppendProperty(builder, "dimensionText", dimension.DimensionText);
        builder.Append(',');
        AppendPropertyName(builder, "textPosition");
        AppendPoint3(builder, dimension.TextPosition);
        builder.Append(',');
        AppendProperty(builder, "textRotation", dimension.TextRotation);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, dimension.Normal);
        builder.Append(',');
        AppendProperty(builder, "styleName", dimension.StyleName);
        builder.Append('}');
    }

    private static void AppendHatch(StringBuilder builder, CadContextHatchV2 hatch)
    {
        builder.Append('{');
        AppendProperty(builder, "associative", hatch.Associative);
        builder.Append(',');
        AppendProperty(builder, "isGradient", hatch.IsGradient);
        builder.Append(',');
        AppendProperty(builder, "isSolidFill", hatch.IsSolidFill);
        builder.Append(',');
        AppendProperty(builder, "patternName", hatch.PatternName);
        builder.Append(',');
        AppendProperty(builder, "patternAngle", hatch.PatternAngle);
        builder.Append(',');
        AppendProperty(builder, "patternScale", hatch.PatternScale);
        builder.Append(',');
        AppendProperty(builder, "elevation", hatch.Elevation);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, hatch.Normal);
        builder.Append(',');
        AppendPropertyName(builder, "loopTypes");
        AppendStringArray(builder, hatch.LoopTypes);
        builder.Append('}');
    }

    private static void AppendLeader(StringBuilder builder, CadContextLeaderV2 leader)
    {
        builder.Append('{');
        AppendProperty(builder, "isSplined", leader.IsSplined);
        builder.Append(',');
        AppendProperty(builder, "hasArrowHead", leader.HasArrowHead);
        builder.Append(',');
        AppendProperty(builder, "annotationType", leader.AnnotationType);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, leader.Normal);
        builder.Append(',');
        AppendPropertyName(builder, "vertices");
        AppendPoint3Array(builder, leader.Vertices);
        builder.Append('}');
    }

    private static void AppendMLeader(StringBuilder builder, CadContextMLeaderV2 leader)
    {
        builder.Append('{');
        AppendProperty(builder, "contentType", leader.ContentType);
        builder.Append(',');
        AppendPropertyName(builder, "normal");
        AppendPoint3(builder, leader.Normal);
        builder.Append(',');
        AppendProperty(builder, "text", leader.Text);
        builder.Append(',');
        AppendPropertyName(builder, "leaderLines");
        builder.Append('[');
        for (var index = 0; index < leader.LeaderLines.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }
            builder.Append('{');
            AppendPropertyName(builder, "vertices");
            AppendPoint3Array(builder, leader.LeaderLines[index].Vertices);
            builder.Append('}');
        }
        builder.Append(']');
        builder.Append('}');
    }

    private static void AppendTable(StringBuilder builder, CadContextTableV2 table)
    {
        builder.Append('{');
        AppendPropertyName(builder, "position");
        AppendPoint3(builder, table.Position);
        builder.Append(',');
        AppendPropertyName(builder, "direction");
        AppendPoint3(builder, table.Direction);
        builder.Append(',');
        AppendProperty(builder, "rows", table.Rows);
        builder.Append(',');
        AppendProperty(builder, "columns", table.Columns);
        builder.Append(',');
        AppendProperty(builder, "width", table.Width);
        builder.Append(',');
        AppendProperty(builder, "height", table.Height);
        builder.Append(',');
        AppendProperty(builder, "styleName", table.StyleName);
        builder.Append(',');
        AppendPropertyName(builder, "cells");
        builder.Append('[');

        var cells = (CadContextTableCellV2[])table.Cells.Clone();
        Array.Sort(cells, CompareTableCells);
        for (var index = 0; index < cells.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }
            var cell = cells[index];
            builder.Append('{');
            AppendProperty(builder, "row", cell.Row);
            builder.Append(',');
            AppendProperty(builder, "column", cell.Column);
            builder.Append(',');
            AppendProperty(builder, "text", cell.Text);
            builder.Append('}');
        }
        builder.Append(']');
        builder.Append('}');
    }

    private static void AppendUnsupported(
        StringBuilder builder,
        CadContextUnsupportedV2 unsupported)
    {
        builder.Append('{');
        AppendProperty(builder, "dxfName", unsupported.DxfName);
        builder.Append(',');
        AppendProperty(builder, "reason", unsupported.Reason);
        builder.Append('}');
    }

    private static void AppendPoint2(StringBuilder builder, CadPoint2 point)
    {
        builder.Append('{');
        AppendProperty(builder, "x", point.X);
        builder.Append(',');
        AppendProperty(builder, "y", point.Y);
        builder.Append('}');
    }

    private static void AppendPoint3(StringBuilder builder, CadPoint3 point)
    {
        builder.Append('{');
        AppendProperty(builder, "x", point.X);
        builder.Append(',');
        AppendProperty(builder, "y", point.Y);
        builder.Append(',');
        AppendProperty(builder, "z", point.Z);
        builder.Append('}');
    }

    private static void AppendPoint3Array(StringBuilder builder, CadPoint3[] points)
    {
        builder.Append('[');
        for (var index = 0; index < points.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }
            AppendPoint3(builder, points[index]);
        }
        builder.Append(']');
    }

    private static void AppendStringArray(StringBuilder builder, string[] values)
    {
        builder.Append('[');
        for (var index = 0; index < values.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }
            AppendJsonString(builder, values[index]);
        }
        builder.Append(']');
    }

    private static void AppendProperty(StringBuilder builder, string name, string value)
    {
        AppendPropertyName(builder, name);
        AppendJsonString(builder, value);
    }

    private static void AppendProperty(StringBuilder builder, string name, int value)
    {
        AppendPropertyName(builder, name);
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendProperty(StringBuilder builder, string name, long value)
    {
        AppendPropertyName(builder, name);
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendProperty(StringBuilder builder, string name, double value)
    {
        AppendPropertyName(builder, name);
        builder.Append(FormatDouble(value));
    }

    private static void AppendProperty(StringBuilder builder, string name, bool value)
    {
        AppendPropertyName(builder, name);
        builder.Append(value ? "true" : "false");
    }

    private static void AppendPropertyName(StringBuilder builder, string name)
    {
        AppendJsonString(builder, name);
        builder.Append(':');
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString(
                            "x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');
    }

    private static string FormatDouble(double value)
    {
        if (value == 0d)
        {
            return "0";
        }
        var formatted = value.ToString("G17", CultureInfo.InvariantCulture);
        var exponentIndex = formatted.IndexOf('E');
        if (exponentIndex < 0)
        {
            exponentIndex = formatted.IndexOf('e');
        }
        if (exponentIndex < 0)
        {
            return formatted;
        }
        var mantissa = formatted.Substring(0, exponentIndex);
        var exponent = int.Parse(
            formatted.Substring(exponentIndex + 1),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);
        return mantissa + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private static int CompareEntities(CadContextEntityV2 left, CadContextEntityV2 right)
    {
        var leftValue = ParseHandle(left.Handle);
        var rightValue = ParseHandle(right.Handle);
        return leftValue < rightValue ? -1 : leftValue > rightValue ? 1 : 0;
    }

    private static int CompareTableCells(
        CadContextTableCellV2 left,
        CadContextTableCellV2 right)
    {
        var row = left.Row.CompareTo(right.Row);
        return row != 0 ? row : left.Column.CompareTo(right.Column);
    }

    private static ulong ParseHandle(string value)
    {
        ulong parsed;
        if (!ulong.TryParse(value, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out parsed))
        {
            throw new InvalidOperationException("CadContextJson v2 Handle was not validated.");
        }
        return parsed;
    }

    private static string ToLowerHex(byte[] bytes)
    {
        const string alphabet = "0123456789abcdef";
        var characters = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            characters[index * 2] = alphabet[bytes[index] >> 4];
            characters[(index * 2) + 1] = alphabet[bytes[index] & 0x0F];
        }
        return new string(characters);
    }

    private static void ThrowIfInvalid(CadContextJsonV2? context)
    {
        var failures = CadContextJsonV2Validator.Validate(context);
        if (failures.Length == 0)
        {
            return;
        }
        var detail = string.Join("; ", failures
            .Take(8)
            .Select(static failure => failure.Code + "@" + failure.Path));
        throw new ArgumentException(
            "CadContextJson v2 validation failed: " + detail, nameof(context));
    }
}

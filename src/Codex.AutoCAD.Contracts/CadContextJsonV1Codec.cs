using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Codex.AutoCAD.Contracts;

/// <summary>
/// Deterministic CadContextJson v1 writer shared by net45 and net8. It emits no insignificant
/// whitespace, uses a frozen property order, sorts entities by numeric Handle, writes UTF-8 without
/// a BOM, and formats finite doubles with a normalized G17 representation.
/// </summary>
public static class CadContextJsonV1Codec
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string SerializeCanonical(CadContextJsonV1 context)
    {
        ThrowIfInvalid(context);
        return SerializeCanonicalUnchecked(context);
    }

    public static byte[] SerializeCanonicalUtf8(CadContextJsonV1 context)
    {
        var json = SerializeCanonical(context);
        return StrictUtf8.GetBytes(json);
    }

    public static string ComputeCanonicalSha256(CadContextJsonV1 context)
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

    internal static string SerializeCanonicalUnchecked(CadContextJsonV1 context)
    {
        var builder = new StringBuilder(4_096);
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

    private static void AppendDocument(StringBuilder builder, CadContextDocumentV1 document)
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

    private static void AppendSelection(StringBuilder builder, CadContextSelectionV1 selection)
    {
        builder.Append('{');
        AppendProperty(builder, "snapshotHash", selection.SnapshotHash);
        builder.Append(',');
        AppendProperty(builder, "entityCount", selection.EntityCount);
        builder.Append(',');
        AppendPropertyName(builder, "entities");
        builder.Append('[');

        var entities = (CadContextEntityV1[])selection.Entities.Clone();
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

    private static void AppendEntity(StringBuilder builder, CadContextEntityV1 entity)
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
            case CadContextEntityTypes.Line:
                AppendPropertyName(builder, "line");
                AppendLine(builder, entity.Line!);
                break;
            case CadContextEntityTypes.Circle:
                AppendPropertyName(builder, "circle");
                AppendCircle(builder, entity.Circle!);
                break;
            case CadContextEntityTypes.Polyline:
                AppendPropertyName(builder, "polyline");
                AppendPolyline(builder, entity.Polyline!);
                break;
            case CadContextEntityTypes.DbText:
                AppendPropertyName(builder, "dbText");
                AppendDbText(builder, entity.DbText!);
                break;
            case CadContextEntityTypes.MText:
                AppendPropertyName(builder, "mText");
                AppendMText(builder, entity.MText!);
                break;
            case CadContextEntityTypes.BlockReference:
                AppendPropertyName(builder, "blockReference");
                AppendBlockReference(builder, entity.BlockReference!);
                break;
            default:
                throw new InvalidOperationException("CadContextJson v1 entity type was not validated.");
        }

        builder.Append('}');
    }

    private static void AppendLine(StringBuilder builder, CadContextLineV1 line)
    {
        builder.Append('{');
        AppendPropertyName(builder, "start");
        AppendPoint3(builder, line.Start);
        builder.Append(',');
        AppendPropertyName(builder, "end");
        AppendPoint3(builder, line.End);
        builder.Append('}');
    }

    private static void AppendCircle(StringBuilder builder, CadContextCircleV1 circle)
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

    private static void AppendPolyline(StringBuilder builder, CadContextPolylineV1 polyline)
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

    private static void AppendDbText(StringBuilder builder, CadContextDbTextV1 text)
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

    private static void AppendMText(StringBuilder builder, CadContextMTextV1 text)
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
        CadContextBlockReferenceV1 block)
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
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
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

    private static int CompareEntities(CadContextEntityV1 left, CadContextEntityV1 right)
    {
        var leftValue = ParseHandle(left.Handle);
        var rightValue = ParseHandle(right.Handle);
        return leftValue < rightValue ? -1 : leftValue > rightValue ? 1 : 0;
    }

    private static ulong ParseHandle(string value)
    {
        ulong parsed;
        if (!ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed))
        {
            throw new InvalidOperationException("CadContextJson v1 Handle was not validated.");
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

    private static void ThrowIfInvalid(CadContextJsonV1? context)
    {
        var failures = CadContextJsonV1Validator.Validate(context);
        if (failures.Length == 0)
        {
            return;
        }

        var detail = string.Join("; ", failures
            .Take(8)
            .Select(static failure => failure.Code + "@" + failure.Path));
        throw new ArgumentException("CadContextJson v1 validation failed: " + detail, nameof(context));
    }
}

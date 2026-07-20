using System.Globalization;
using System.Text;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class CadContextDocumentMetadata
    {
        internal CadContextDocumentMetadata(
            string documentId,
            string drawingFingerprint,
            long revision,
            string currentSpace,
            string drawingVersion,
            string units)
        {
            DocumentId = documentId;
            DrawingFingerprint = drawingFingerprint;
            Revision = revision;
            CurrentSpace = currentSpace;
            DrawingVersion = drawingVersion;
            Units = units;
        }

        internal string DocumentId { get; private set; }

        internal string DrawingFingerprint { get; private set; }

        internal long Revision { get; private set; }

        internal string CurrentSpace { get; private set; }

        internal string DrawingVersion { get; private set; }

        internal string Units { get; private set; }
    }

    internal static class CadContextJsonMapper
    {
        private const int MaximumSummaryTextCharacters = 160;
        private const int MaximumSummaryPolylineVertices = 8;

        internal static CadContextJsonV1 Build(
            CadContextDocumentMetadata document,
            ContextSelectionSnapshot selection,
            DateTimeOffset capturedAtUtc)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }

            var entities = new CadContextEntityV1[selection.Entities.Count];
            for (var index = 0; index < selection.Entities.Count; index++)
            {
                entities[index] = MapEntity(selection.Entities[index]);
            }

            return new CadContextJsonV1
            {
                CapturedAtUtc = capturedAtUtc.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture),
                Document = new CadContextDocumentV1
                {
                    DocumentId = document.DocumentId,
                    DrawingFingerprint = document.DrawingFingerprint,
                    Revision = document.Revision,
                    CurrentSpace = document.CurrentSpace,
                    DrawingVersion = document.DrawingVersion,
                    Units = document.Units,
                },
                Selection = new CadContextSelectionV1
                {
                    SnapshotHash = selection.SnapshotHash,
                    EntityCount = entities.Length,
                    Entities = entities,
                },
            };
        }

        internal static string BuildReadableSummary(
            CadContextJsonV1 context,
            string contextSha256,
            int canonicalBytes)
        {
            if (context == null)
            {
                return "尚未捕获选择上下文。";
            }

            var builder = new StringBuilder();
            builder.Append("捕获时间（UTC）：").AppendLine(context.CapturedAtUtc);
            builder.Append("文档修订：").Append(context.Document.Revision.ToString(CultureInfo.InvariantCulture));
            builder.Append("  空间：").Append(context.Document.CurrentSpace);
            builder.Append("  单位：").AppendLine(context.Document.Units);
            builder.Append("选择图元：").Append(context.Selection.EntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  JSON 字节：").AppendLine(canonicalBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("上下文 SHA-256：").AppendLine(contextSha256);

            for (var index = 0; index < context.Selection.Entities.Length; index++)
            {
                AppendEntitySummary(builder, context.Selection.Entities[index]);
            }

            return builder.ToString().TrimEnd();
        }

        private static CadContextEntityV1 MapEntity(ContextEntitySnapshot snapshot)
        {
            var draft = snapshot.Draft;
            var entity = new CadContextEntityV1
            {
                Handle = draft.Handle.ToString("X", CultureInfo.InvariantCulture),
                OwnerSpaceHandle = draft.OwnerSpaceHandle.ToString("X", CultureInfo.InvariantCulture),
                StateHash = snapshot.StateHash,
                Layer = draft.Layer,
            };

            switch (draft.Kind)
            {
                case ContextEntityKind.Line:
                    entity.EntityType = CadContextEntityTypes.Line;
                    entity.Line = new CadContextLineV1
                    {
                        Start = Point3(draft.Line.Start),
                        End = Point3(draft.Line.End),
                    };
                    break;
                case ContextEntityKind.Circle:
                    entity.EntityType = CadContextEntityTypes.Circle;
                    entity.Circle = new CadContextCircleV1
                    {
                        Center = Point3(draft.Circle.Center),
                        Radius = draft.Circle.Radius,
                        Normal = Point3(draft.Circle.Normal),
                    };
                    break;
                case ContextEntityKind.Polyline:
                    entity.EntityType = CadContextEntityTypes.Polyline;
                    var vertices = new CadContextPolylineVertexV1[draft.Polyline.Vertices.Count];
                    for (var index = 0; index < vertices.Length; index++)
                    {
                        var vertex = draft.Polyline.Vertices[index];
                        vertices[index] = new CadContextPolylineVertexV1
                        {
                            Position = new CadPoint2(vertex.Position.X, vertex.Position.Y),
                            Bulge = vertex.Bulge,
                        };
                    }

                    entity.Polyline = new CadContextPolylineV1
                    {
                        Closed = draft.Polyline.Closed,
                        Elevation = draft.Polyline.Elevation,
                        Normal = Point3(draft.Polyline.Normal),
                        Vertices = vertices,
                    };
                    break;
                case ContextEntityKind.DbText:
                    entity.EntityType = CadContextEntityTypes.DbText;
                    entity.DbText = new CadContextDbTextV1
                    {
                        Text = draft.DbText.Text,
                        Position = Point3(draft.DbText.Position),
                        Height = draft.DbText.Height,
                        Rotation = draft.DbText.Rotation,
                    };
                    break;
                case ContextEntityKind.MText:
                    entity.EntityType = CadContextEntityTypes.MText;
                    entity.MText = new CadContextMTextV1
                    {
                        Text = draft.MText.Text,
                        Location = Point3(draft.MText.Location),
                        TextHeight = draft.MText.TextHeight,
                        Rotation = draft.MText.Rotation,
                    };
                    break;
                case ContextEntityKind.BlockReference:
                    entity.EntityType = CadContextEntityTypes.BlockReference;
                    entity.BlockReference = new CadContextBlockReferenceV1
                    {
                        Position = Point3(draft.Block.Position),
                        Rotation = draft.Block.Rotation,
                        Scale = Point3(draft.Block.Scale),
                        EffectiveName = draft.Block.EffectiveName,
                        IsDynamic = draft.Block.Dynamic,
                        IsExternalReference = draft.Block.Xref,
                    };
                    break;
                default:
                    throw new ContextValidationException("unsupported-entity-kind");
            }

            return entity;
        }

        private static CadPoint3 Point3(ContextPoint3 point)
        {
            return new CadPoint3(point.X, point.Y, point.Z);
        }

        private static CadPoint3 Point3(ContextVector3 vector)
        {
            return new CadPoint3(vector.X, vector.Y, vector.Z);
        }

        private static void AppendEntitySummary(StringBuilder builder, CadContextEntityV1 entity)
        {
            builder.AppendLine();
            builder.Append('[').Append(entity.Handle).Append("] ");
            builder.Append(DisplayType(entity.EntityType));
            builder.Append(" | 图层：").Append(entity.Layer);

            switch (entity.EntityType)
            {
                case CadContextEntityTypes.Line:
                    var line = entity.Line!;
                    builder.Append(" | 起点：").Append(FormatPoint(line.Start));
                    builder.Append(" | 终点：").Append(FormatPoint(line.End));
                    break;
                case CadContextEntityTypes.Circle:
                    var circle = entity.Circle!;
                    builder.Append(" | 圆心：").Append(FormatPoint(circle.Center));
                    builder.Append(" | 半径：").Append(FormatNumber(circle.Radius));
                    break;
                case CadContextEntityTypes.Polyline:
                    var polyline = entity.Polyline!;
                    builder.Append(" | 闭合：").Append(polyline.Closed ? "是" : "否");
                    builder.Append(" | 顶点：").Append(polyline.Vertices.Length.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | ");
                    var shown = Math.Min(polyline.Vertices.Length, MaximumSummaryPolylineVertices);
                    for (var index = 0; index < shown; index++)
                    {
                        if (index > 0)
                        {
                            builder.Append("; ");
                        }

                        builder.Append(FormatPoint(polyline.Vertices[index].Position));
                    }

                    if (shown < polyline.Vertices.Length)
                    {
                        builder.Append("; …");
                    }
                    break;
                case CadContextEntityTypes.DbText:
                    var dbText = entity.DbText!;
                    builder.Append(" | 文字：").Append(CompactText(dbText.Text));
                    builder.Append(" | 位置：").Append(FormatPoint(dbText.Position));
                    builder.Append(" | 高度：").Append(FormatNumber(dbText.Height));
                    break;
                case CadContextEntityTypes.MText:
                    var mText = entity.MText!;
                    builder.Append(" | 多行文字：").Append(CompactText(mText.Text));
                    builder.Append(" | 位置：").Append(FormatPoint(mText.Location));
                    builder.Append(" | 高度：").Append(FormatNumber(mText.TextHeight));
                    break;
                case CadContextEntityTypes.BlockReference:
                    var block = entity.BlockReference!;
                    builder.Append(" | 块名：").Append(block.EffectiveName);
                    builder.Append(" | 插入点：").Append(FormatPoint(block.Position));
                    builder.Append(" | 动态：").Append(block.IsDynamic ? "是" : "否");
                    builder.Append(" | 外部参照：").Append(block.IsExternalReference ? "是" : "否");
                    break;
            }
        }

        private static string DisplayType(string entityType)
        {
            switch (entityType)
            {
                case CadContextEntityTypes.Line:
                    return "直线";
                case CadContextEntityTypes.Circle:
                    return "圆";
                case CadContextEntityTypes.Polyline:
                    return "多段线";
                case CadContextEntityTypes.DbText:
                    return "单行文字";
                case CadContextEntityTypes.MText:
                    return "多行文字";
                case CadContextEntityTypes.BlockReference:
                    return "块参照";
                default:
                    return entityType;
            }
        }

        private static string CompactText(string value)
        {
            var compact = (value ?? string.Empty)
                .Replace("\r\n", " ↵ ")
                .Replace("\r", " ↵ ")
                .Replace("\n", " ↵ ")
                .Replace("\t", " ");
            return compact.Length <= MaximumSummaryTextCharacters
                ? compact
                : compact.Substring(0, MaximumSummaryTextCharacters) + "…";
        }

        private static string FormatPoint(CadPoint3 point)
        {
            return "(" + FormatNumber(point.X) + ", " + FormatNumber(point.Y) + ", "
                + FormatNumber(point.Z) + ")";
        }

        private static string FormatPoint(CadPoint2 point)
        {
            return "(" + FormatNumber(point.X) + ", " + FormatNumber(point.Y) + ")";
        }

        private static string FormatNumber(double value)
        {
            return value == 0d
                ? "0"
                : value.ToString("G17", CultureInfo.InvariantCulture);
        }
    }
}

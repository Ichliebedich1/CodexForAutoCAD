using System;
using System.Globalization;
using System.Text;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

namespace Codex.AutoCAD.Host2016
{
    internal static class CadContextJsonV2Mapper
    {
        private const int MaximumSummaryTextCharacters = 160;

        internal static CadContextJsonV2 Build(
            CadContextDocumentMetadata document,
            V2SelectionSnapshot snapshot,
            DateTimeOffset capturedAtUtc)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new CadContextJsonV2
            {
                CapturedAtUtc = capturedAtUtc.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture),
                Document = new CadContextDocumentV2
                {
                    DocumentId = document.DocumentId,
                    DrawingFingerprint = document.DrawingFingerprint,
                    Revision = document.Revision,
                    CurrentSpace = document.CurrentSpace,
                    DrawingVersion = document.DrawingVersion,
                    Units = document.Units,
                },
                Selection = snapshot.Selection,
            };
        }

        internal static string BuildReadableSummary(
            CadContextJsonV2 context,
            string contextSha256,
            int canonicalBytes)
        {
            if (context == null)
            {
                return "尚未捕获选择上下文。";
            }

            var builder = new StringBuilder();
            builder.Append("捕获时间（UTC）：").AppendLine(context.CapturedAtUtc);
            builder.Append("文档修订：")
                .Append(context.Document.Revision.ToString(CultureInfo.InvariantCulture));
            builder.Append("  空间：").Append(context.Document.CurrentSpace);
            builder.Append("  单位：").AppendLine(context.Document.Units);
            builder.Append("选择图元：")
                .Append(context.Selection.EntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  成功解析：")
                .Append(context.Selection.ParsedEntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  未解析：")
                .Append(context.Selection.UnsupportedEntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  完整：").AppendLine(context.Selection.Complete ? "是" : "否");
            if (context.Selection.UnsupportedEntityCount > 0)
            {
                builder.Append("占位明细：").AppendLine(
                    CadReadTypeStatistics.FormatSummary(
                        CadReadTypeStatistics.FromSelection(context.Selection),
                        12));
            }
            builder.Append("JSON 字节：")
                .AppendLine(canonicalBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("上下文 SHA-256：").AppendLine(contextSha256);

            for (var index = 0; index < context.Selection.Entities.Length; index++)
            {
                AppendEntitySummary(builder, context.Selection.Entities[index]);
            }
            return builder.ToString().TrimEnd();
        }

        private static void AppendEntitySummary(
            StringBuilder builder,
            CadContextEntityV2 entity)
        {
            builder.AppendLine();
            builder.Append('[').Append(entity.Handle).Append("] ");
            builder.Append(DisplayType(entity.EntityType));
            builder.Append(" | 图层：").Append(entity.Layer);

            switch (entity.EntityType)
            {
                case CadContextEntityTypesV2.Line:
                    builder.Append(" | 起点：").Append(FormatPoint(entity.Line.Start));
                    builder.Append(" | 终点：").Append(FormatPoint(entity.Line.End));
                    break;
                case CadContextEntityTypesV2.Circle:
                    builder.Append(" | 圆心：").Append(FormatPoint(entity.Circle.Center));
                    builder.Append(" | 半径：").Append(FormatNumber(entity.Circle.Radius));
                    break;
                case CadContextEntityTypesV2.Polyline:
                    builder.Append(" | 顶点：")
                        .Append(entity.Polyline.Vertices.Length.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 闭合：").Append(entity.Polyline.Closed ? "是" : "否");
                    break;
                case CadContextEntityTypesV2.DbText:
                    builder.Append(" | 文字：").Append(CompactText(entity.DbText.Text));
                    builder.Append(" | 位置：").Append(FormatPoint(entity.DbText.Position));
                    break;
                case CadContextEntityTypesV2.MText:
                    builder.Append(" | 多行文字：").Append(CompactText(entity.MText.Text));
                    builder.Append(" | 位置：").Append(FormatPoint(entity.MText.Location));
                    break;
                case CadContextEntityTypesV2.BlockReference:
                    builder.Append(" | 块名：").Append(entity.BlockReference.EffectiveName);
                    builder.Append(" | 插入点：").Append(FormatPoint(entity.BlockReference.Position));
                    break;
                case CadContextEntityTypesV2.Arc:
                    builder.Append(" | 圆心：").Append(FormatPoint(entity.Arc.Center));
                    builder.Append(" | 半径：").Append(FormatNumber(entity.Arc.Radius));
                    builder.Append(" | 角度：")
                        .Append(FormatNumber(entity.Arc.StartAngle))
                        .Append(" → ")
                        .Append(FormatNumber(entity.Arc.EndAngle));
                    break;
                case CadContextEntityTypesV2.Ellipse:
                    builder.Append(" | 中心：").Append(FormatPoint(entity.Ellipse.Center));
                    builder.Append(" | 长轴：").Append(FormatPoint(entity.Ellipse.MajorAxis));
                    builder.Append(" | 半径比：")
                        .Append(FormatNumber(entity.Ellipse.RadiusRatio));
                    break;
                case CadContextEntityTypesV2.Spline:
                    builder.Append(" | 次数：")
                        .Append(entity.Spline.Degree.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 控制点：")
                        .Append(entity.Spline.ControlPoints.Length.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 拟合点：")
                        .Append(entity.Spline.FitPoints.Length.ToString(CultureInfo.InvariantCulture));
                    break;
                case CadContextEntityTypesV2.Point:
                    builder.Append(" | 位置：").Append(FormatPoint(entity.Point.Position));
                    break;
                case CadContextEntityTypesV2.Ray:
                    builder.Append(" | 基点：").Append(FormatPoint(entity.Ray.BasePoint));
                    builder.Append(" | 方向点：").Append(FormatPoint(entity.Ray.SecondPoint));
                    break;
                case CadContextEntityTypesV2.Xline:
                    builder.Append(" | 基点：").Append(FormatPoint(entity.Xline.BasePoint));
                    builder.Append(" | 方向点：").Append(FormatPoint(entity.Xline.SecondPoint));
                    break;
                case CadContextEntityTypesV2.Polyline2d:
                    builder.Append(" | 顶点：")
                        .Append(entity.Polyline2d.Vertices.Length.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 闭合：").Append(entity.Polyline2d.Closed ? "是" : "否");
                    break;
                case CadContextEntityTypesV2.Polyline3d:
                    builder.Append(" | 顶点：")
                        .Append(entity.Polyline3d.Vertices.Length.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 闭合：").Append(entity.Polyline3d.Closed ? "是" : "否");
                    break;
                case CadContextEntityTypesV2.Dimension:
                    builder.Append(" | 类型：").Append(entity.Dimension.DimensionType);
                    builder.Append(" | 测量值：")
                        .Append(FormatNumber(entity.Dimension.Measurement));
                    builder.Append(" | 文字：")
                        .Append(CompactText(entity.Dimension.DimensionText));
                    break;
                case CadContextEntityTypesV2.Hatch:
                    builder.Append(" | 图案：").Append(entity.Hatch.PatternName);
                    builder.Append(" | 边界环：")
                        .Append(entity.Hatch.LoopTypes.Length.ToString(CultureInfo.InvariantCulture));
                    break;
                case CadContextEntityTypesV2.Leader:
                    builder.Append(" | 顶点：")
                        .Append(entity.Leader.Vertices.Length.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 注释：").Append(entity.Leader.AnnotationType);
                    break;
                case CadContextEntityTypesV2.MLeader:
                    builder.Append(" | 引线：")
                        .Append(entity.MLeader.LeaderLines.Length.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 内容：").Append(CompactText(entity.MLeader.Text));
                    break;
                case CadContextEntityTypesV2.Table:
                    builder.Append(" | 表格：")
                        .Append(entity.Table.Rows.ToString(CultureInfo.InvariantCulture))
                        .Append('×')
                        .Append(entity.Table.Columns.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" | 位置：").Append(FormatPoint(entity.Table.Position));
                    break;
                case CadContextEntityTypesV2.Unsupported:
                    builder.Append(" | DXF：").Append(entity.Unsupported.DxfName);
                    builder.Append(" | 原因：").Append(entity.Unsupported.Reason);
                    break;
            }
        }

        private static string DisplayType(string entityType)
        {
            switch (entityType)
            {
                case CadContextEntityTypesV2.Line:
                    return "直线";
                case CadContextEntityTypesV2.Circle:
                    return "圆";
                case CadContextEntityTypesV2.Polyline:
                    return "轻量多段线";
                case CadContextEntityTypesV2.DbText:
                    return "单行文字";
                case CadContextEntityTypesV2.MText:
                    return "多行文字";
                case CadContextEntityTypesV2.BlockReference:
                    return "块参照";
                case CadContextEntityTypesV2.Arc:
                    return "圆弧";
                case CadContextEntityTypesV2.Ellipse:
                    return "椭圆";
                case CadContextEntityTypesV2.Spline:
                    return "样条曲线";
                case CadContextEntityTypesV2.Point:
                    return "点";
                case CadContextEntityTypesV2.Ray:
                    return "射线";
                case CadContextEntityTypesV2.Xline:
                    return "构造线";
                case CadContextEntityTypesV2.Polyline2d:
                    return "旧式二维多段线";
                case CadContextEntityTypesV2.Polyline3d:
                    return "三维多段线";
                case CadContextEntityTypesV2.Dimension:
                    return "标注";
                case CadContextEntityTypesV2.Hatch:
                    return "填充";
                case CadContextEntityTypesV2.Leader:
                    return "引线";
                case CadContextEntityTypesV2.MLeader:
                    return "多重引线";
                case CadContextEntityTypesV2.Table:
                    return "表格";
                case CadContextEntityTypesV2.Unsupported:
                    return "未解析对象";
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

        private static string FormatNumber(double value)
        {
            return value == 0d
                ? "0"
                : value.ToString("G17", CultureInfo.InvariantCulture);
        }
    }
}

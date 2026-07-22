using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class CadReadIssueTypeBucket
    {
        internal CadReadIssueTypeBucket(string actualType, int count)
        {
            ActualType = actualType ?? "UNKNOWN";
            Count = count;
        }

        internal string ActualType { get; private set; }

        internal int Count { get; private set; }
    }

    internal sealed class CadReadIssueSnapshot
    {
        internal CadReadIssueSnapshot(
            int unknownTypeCount,
            int dataLimitedCount,
            int readFailedCount,
            int unlistedTypeEntityCount,
            CadReadIssueTypeBucket[] actualTypeCounts)
        {
            UnknownTypeCount = unknownTypeCount;
            DataLimitedCount = dataLimitedCount;
            ReadFailedCount = readFailedCount;
            UnlistedTypeEntityCount = unlistedTypeEntityCount;
            ActualTypeCounts = actualTypeCounts ?? new CadReadIssueTypeBucket[0];
        }

        internal int UnknownTypeCount { get; private set; }

        internal int DataLimitedCount { get; private set; }

        internal int ReadFailedCount { get; private set; }

        internal int UnlistedTypeEntityCount { get; private set; }

        internal CadReadIssueTypeBucket[] ActualTypeCounts { get; private set; }

        internal int TotalCount
        {
            get
            {
                return UnknownTypeCount + DataLimitedCount + ReadFailedCount;
            }
        }

        internal static CadReadIssueSnapshot Empty()
        {
            return new CadReadIssueSnapshot(0, 0, 0, 0, new CadReadIssueTypeBucket[0]);
        }
    }

    internal sealed class CadReadIssueAccumulator
    {
        private const string UnknownActualType = "UNKNOWN";
        private readonly Dictionary<string, MutableTypeBucket> actualTypeCounts =
            new Dictionary<string, MutableTypeBucket>(StringComparer.OrdinalIgnoreCase);
        private int unknownTypeCount;
        private int dataLimitedCount;
        private int readFailedCount;
        private int unlistedTypeEntityCount;

        internal void AddSelectionEntity(CadContextEntityV2 entity)
        {
            if (entity == null
                || !string.Equals(
                    entity.EntityType,
                    CadContextEntityTypesV2.Unsupported,
                    StringComparison.Ordinal)
                || entity.Unsupported == null)
            {
                return;
            }

            Add(entity.Unsupported.DxfName, ClassifySelectionReason(entity.Unsupported.Reason));
        }

        internal void AddDrawingIndexEntity(CadQueryEntity entity)
        {
            if (entity == null || !entity.Unsupported)
            {
                return;
            }

            Add(entity.ActualType, ClassifyDrawingReadStatus(entity.ReadStatus));
        }

        internal CadReadIssueSnapshot Snapshot()
        {
            var buckets = actualTypeCounts.Values
                .OrderBy(value => value.ActualType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.ActualType, StringComparer.Ordinal)
                .Select(value => new CadReadIssueTypeBucket(value.ActualType, value.Count))
                .ToArray();
            return new CadReadIssueSnapshot(
                unknownTypeCount,
                dataLimitedCount,
                readFailedCount,
                unlistedTypeEntityCount,
                buckets);
        }

        private void Add(string actualType, CadReadIssueKind kind)
        {
            switch (kind)
            {
                case CadReadIssueKind.UnknownType:
                    unknownTypeCount++;
                    break;
                case CadReadIssueKind.DataLimited:
                    dataLimitedCount++;
                    break;
                default:
                    readFailedCount++;
                    break;
            }

            var safeType = NormalizeActualType(actualType);
            MutableTypeBucket? bucket;
            if (actualTypeCounts.TryGetValue(safeType, out bucket) && bucket != null)
            {
                bucket.Increment();
                return;
            }

            if (actualTypeCounts.Count >= DrawingIndexContractConstants.MaximumCountBuckets)
            {
                unlistedTypeEntityCount++;
                return;
            }

            actualTypeCounts.Add(safeType, new MutableTypeBucket(safeType));
        }

        private static CadReadIssueKind ClassifySelectionReason(string reason)
        {
            if (string.Equals(
                reason,
                CadContextUnsupportedReasonsV2.UnknownEntityType,
                StringComparison.Ordinal))
            {
                return CadReadIssueKind.UnknownType;
            }
            if (string.Equals(
                reason,
                CadContextUnsupportedReasonsV2.EntityDataLimit,
                StringComparison.Ordinal))
            {
                return CadReadIssueKind.DataLimited;
            }
            return CadReadIssueKind.ReadFailed;
        }

        private static CadReadIssueKind ClassifyDrawingReadStatus(string readStatus)
        {
            if (string.Equals(
                readStatus,
                CadQueryReadStatuses.Unsupported,
                StringComparison.Ordinal))
            {
                return CadReadIssueKind.UnknownType;
            }
            if (string.Equals(
                readStatus,
                CadQueryReadStatuses.DataLimited,
                StringComparison.Ordinal))
            {
                return CadReadIssueKind.DataLimited;
            }
            return CadReadIssueKind.ReadFailed;
        }

        private static string NormalizeActualType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return UnknownActualType;
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0
                || trimmed.Length > DrawingIndexContractConstants.MaximumTypeCharacters)
            {
                return UnknownActualType;
            }

            for (var index = 0; index < trimmed.Length; index++)
            {
                var character = trimmed[index];
                var allowed =
                    (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '_'
                    || character == '-'
                    || character == '.'
                    || character == '+'
                    || character == '$';
                if (!allowed)
                {
                    return UnknownActualType;
                }
            }
            return trimmed;
        }

        private enum CadReadIssueKind
        {
            UnknownType,
            DataLimited,
            ReadFailed,
        }

        private sealed class MutableTypeBucket
        {
            internal MutableTypeBucket(string actualType)
            {
                ActualType = actualType;
                Count = 1;
            }

            internal string ActualType { get; private set; }

            internal int Count { get; private set; }

            internal void Increment()
            {
                if (Count < int.MaxValue)
                {
                    Count++;
                }
            }
        }
    }

    internal static class CadReadTypeStatistics
    {
        internal static CadReadIssueSnapshot FromSelection(CadContextSelectionV2 selection)
        {
            if (selection == null || selection.Entities == null)
            {
                return CadReadIssueSnapshot.Empty();
            }

            var accumulator = new CadReadIssueAccumulator();
            for (var index = 0; index < selection.Entities.Length; index++)
            {
                accumulator.AddSelectionEntity(selection.Entities[index]);
            }
            return accumulator.Snapshot();
        }

        internal static string FormatReasonCounts(CadReadIssueSnapshot snapshot)
        {
            var current = snapshot ?? CadReadIssueSnapshot.Empty();
            return "未支持类型 "
                   + current.UnknownTypeCount.ToString(CultureInfo.InvariantCulture)
                   + "，数据超限 "
                   + current.DataLimitedCount.ToString(CultureInfo.InvariantCulture)
                   + "，读取失败 "
                   + current.ReadFailedCount.ToString(CultureInfo.InvariantCulture);
        }

        internal static string FormatActualTypeCounts(
            CadReadIssueSnapshot snapshot,
            int maximumDisplayedBuckets)
        {
            var current = snapshot ?? CadReadIssueSnapshot.Empty();
            if (current.TotalCount == 0)
            {
                return "无";
            }
            if (maximumDisplayedBuckets <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDisplayedBuckets));
            }

            var builder = new StringBuilder();
            var displayed = Math.Min(
                maximumDisplayedBuckets,
                current.ActualTypeCounts.Length);
            for (var index = 0; index < displayed; index++)
            {
                if (builder.Length > 0)
                {
                    builder.Append("，");
                }
                var bucket = current.ActualTypeCounts[index];
                builder.Append(DisplayName(bucket.ActualType));
                builder.Append('(').Append(bucket.ActualType).Append(") x");
                builder.Append(bucket.Count.ToString(CultureInfo.InvariantCulture));
            }

            var hiddenBucketCount = current.ActualTypeCounts.Length - displayed;
            if (hiddenBucketCount > 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append("，");
                }
                builder.Append("另有 ")
                    .Append(hiddenBucketCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" 类未展开");
            }
            if (current.UnlistedTypeEntityCount > 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append("，");
                }
                builder.Append("另有 ")
                    .Append(current.UnlistedTypeEntityCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" 个对象的类型未记录");
            }
            return builder.Length == 0 ? "未知类型" : builder.ToString();
        }

        internal static string FormatSummary(
            CadReadIssueSnapshot snapshot,
            int maximumDisplayedBuckets)
        {
            return FormatReasonCounts(snapshot)
                   + "；占位实际类型："
                   + FormatActualTypeCounts(snapshot, maximumDisplayedBuckets);
        }

        internal static string BuildSupportedTypeCatalog()
        {
            var builder = new StringBuilder();
            builder.AppendLine("--- Codex AutoCAD 2016 中文对象测试目录 ---");
            builder.AppendLine("当前 19 类强类型读取对象：");
            AppendCatalogEntry(builder, 1, "直线", "Line", "绘图功能区 > 直线");
            AppendCatalogEntry(builder, 2, "圆", "Circle", "绘图功能区 > 圆");
            AppendCatalogEntry(builder, 3, "轻量多段线", "Polyline", "绘图功能区 > 多段线");
            AppendCatalogEntry(builder, 4, "单行文字", "DBText", "注释功能区 > 单行文字");
            AppendCatalogEntry(builder, 5, "多行文字", "MText", "注释功能区 > 多行文字");
            AppendCatalogEntry(builder, 6, "块参照", "BlockReference", "先定义块，再从插入功能区放置块");
            AppendCatalogEntry(builder, 7, "圆弧", "Arc", "绘图功能区 > 圆弧");
            AppendCatalogEntry(builder, 8, "椭圆", "Ellipse", "绘图功能区 > 椭圆");
            AppendCatalogEntry(builder, 9, "样条曲线", "Spline", "绘图功能区 > 样条曲线");
            AppendCatalogEntry(builder, 10, "点", "DBPoint", "绘图功能区 > 多点");
            AppendCatalogEntry(builder, 11, "射线", "Ray", "绘图功能区 > 射线");
            AppendCatalogEntry(builder, 12, "构造线", "Xline", "绘图功能区 > 构造线");
            AppendCatalogEntry(builder, 13, "旧式二维多段线", "Polyline2d", "使用已有旧格式测试图；普通多段线通常不是此类型");
            AppendCatalogEntry(builder, 14, "三维多段线", "Polyline3d", "三维建模功能区 > 三维多段线");
            AppendCatalogEntry(builder, 15, "标注", "Dimension", "注释功能区 > 标注");
            AppendCatalogEntry(builder, 16, "图案填充", "Hatch", "绘图功能区 > 图案填充");
            AppendCatalogEntry(builder, 17, "旧式引线", "Leader", "使用已有旧格式引线测试图");
            AppendCatalogEntry(builder, 18, "多重引线", "MLeader", "注释功能区 > 多重引线");
            AppendCatalogEntry(builder, 19, "表格", "Table", "注释功能区 > 表格");
            builder.AppendLine("高价值受限读取候选：面域、三维实体、网格、曲面、光栅图像、PDF/DWF/DGN 参考底图和代理对象。");
            builder.AppendLine("捕获后查看 Palette、CODEX16CTXINFO 或 CODEX16INDEXINFO；占位对象会显示实际类型与数量。");
            builder.AppendLine("此目录只说明人工测试入口，不创建、修改或保存图纸。");
            builder.Append("--- End 中文对象测试目录 ---");
            return builder.ToString();
        }

        private static string DisplayName(string actualType)
        {
            var token = (actualType ?? string.Empty).ToUpperInvariant();
            if (token.StartsWith("ACDB", StringComparison.Ordinal))
            {
                token = token.Substring(4);
            }
            token = token.Replace("_", string.Empty).Replace("-", string.Empty);

            switch (token)
            {
                case "LINE": return "直线";
                case "CIRCLE": return "圆";
                case "ARC": return "圆弧";
                case "ELLIPSE": return "椭圆";
                case "SPLINE": return "样条曲线";
                case "POINT":
                case "DBPOINT": return "点";
                case "RAY": return "射线";
                case "XLINE": return "构造线";
                case "POLYLINE": return "多段线";
                case "POLYLINE2D": return "旧式二维多段线";
                case "POLYLINE3D": return "三维多段线";
                case "TEXT":
                case "DBTEXT": return "单行文字";
                case "MTEXT": return "多行文字";
                case "INSERT":
                case "BLOCKREFERENCE": return "块参照";
                case "HATCH": return "图案填充";
                case "LEADER": return "旧式引线";
                case "MLEADER": return "多重引线";
                case "TABLE": return "表格";
                case "REGION": return "面域";
                case "3DSOLID":
                case "SOLID3D": return "三维实体";
                case "MESH":
                case "SUBDMESH": return "网格";
                case "IMAGE":
                case "RASTERIMAGE": return "光栅图像";
                case "PDFUNDERLAY":
                case "PDFREFERENCE": return "PDF 参考底图";
                case "DWFUNDERLAY":
                case "DWFREFERENCE": return "DWF 参考底图";
                case "DGNUNDERLAY":
                case "DGNREFERENCE": return "DGN 参考底图";
                case "ACADPROXYENTITY":
                case "PROXYENTITY": return "代理对象";
                case "UNKNOWN": return "未知类型";
            }

            if (token.IndexOf("DIMENSION", StringComparison.Ordinal) >= 0)
            {
                return "标注";
            }
            if (token.IndexOf("SURFACE", StringComparison.Ordinal) >= 0)
            {
                return "曲面";
            }
            if (token.IndexOf("UNDERLAY", StringComparison.Ordinal) >= 0)
            {
                return "参考底图";
            }
            if (token.IndexOf("PROXY", StringComparison.Ordinal) >= 0)
            {
                return "代理对象";
            }
            return "未支持对象";
        }

        private static void AppendCatalogEntry(
            StringBuilder builder,
            int index,
            string chineseName,
            string managedName,
            string creationRoute)
        {
            builder.Append(index.ToString("00", CultureInfo.InvariantCulture));
            builder.Append(". ").Append(chineseName).Append(" (").Append(managedName).Append(") | 人工创建：");
            builder.AppendLine(creationRoute);
        }
    }
}

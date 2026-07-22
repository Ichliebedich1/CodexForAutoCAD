using System;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal static class DrawingIndexEntityReader
    {
        private const string Unknown = "UNKNOWN";

        internal static CadQueryEntity Read(
            Transaction transaction,
            Entity entity,
            string space,
            string objectToken)
        {
            if (transaction == null)
            {
                throw new ArgumentNullException(nameof(transaction));
            }
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var limited = false;
            var entityType = Classify(entity);
            var actualType = Sanitize(
                entity.GetType().Name,
                DrawingIndexContractConstants.MaximumTypeCharacters,
                Unknown,
                ref limited);
            var layer = ReadLayer(entity, ref limited);
            var safeSpace = Sanitize(
                space,
                DrawingIndexContractConstants.MaximumNameCharacters,
                Unknown,
                ref limited);
            var blockName = ReadBlockName(transaction, entity, ref limited);
            var text = ReadText(entity, ref limited);
            var unsupported = entityType == CadContextEntityTypesV2.Unsupported;
            var readStatus = unsupported
                ? CadQueryReadStatuses.Unsupported
                : limited
                    ? CadQueryReadStatuses.DataLimited
                    : CadQueryReadStatuses.Parsed;

            return new CadQueryEntity
            {
                ObjectId = RequireObjectToken(objectToken),
                EntityType = entityType,
                ActualType = actualType,
                Layer = layer,
                Space = safeSpace,
                BlockName = blockName,
                TextExcerpt = text,
                Bounds = TryReadBounds(entity),
                Unsupported = unsupported || limited,
                ReadStatus = readStatus,
            };
        }

        internal static CadQueryEntity ReadFailed(string objectToken, string space)
        {
            var limited = false;
            return new CadQueryEntity
            {
                ObjectId = RequireObjectToken(objectToken),
                EntityType = CadContextEntityTypesV2.Unsupported,
                ActualType = Unknown,
                Layer = Unknown,
                Space = Sanitize(
                    space,
                    DrawingIndexContractConstants.MaximumNameCharacters,
                    Unknown,
                    ref limited),
                BlockName = string.Empty,
                TextExcerpt = string.Empty,
                Bounds = null,
                Unsupported = true,
                ReadStatus = CadQueryReadStatuses.ReadFailed,
            };
        }

        private static string Classify(Entity entity)
        {
            if (entity is Table) return CadContextEntityTypesV2.Table;
            if (entity is MLeader) return CadContextEntityTypesV2.MLeader;
            if (entity is Leader) return CadContextEntityTypesV2.Leader;
            if (entity is Dimension) return CadContextEntityTypesV2.Dimension;
            if (entity is Hatch) return CadContextEntityTypesV2.Hatch;
            if (entity is Polyline2d) return CadContextEntityTypesV2.Polyline2d;
            if (entity is Polyline3d) return CadContextEntityTypesV2.Polyline3d;
            if (entity is Polyline) return CadContextEntityTypesV2.Polyline;
            if (entity is Spline) return CadContextEntityTypesV2.Spline;
            if (entity is Ellipse) return CadContextEntityTypesV2.Ellipse;
            if (entity is Arc) return CadContextEntityTypesV2.Arc;
            if (entity is Circle) return CadContextEntityTypesV2.Circle;
            if (entity is Line) return CadContextEntityTypesV2.Line;
            if (entity is Ray) return CadContextEntityTypesV2.Ray;
            if (entity is Xline) return CadContextEntityTypesV2.Xline;
            if (entity is DBPoint) return CadContextEntityTypesV2.Point;
            if (entity is MText) return CadContextEntityTypesV2.MText;
            if (entity is DBText) return CadContextEntityTypesV2.DbText;
            if (entity is BlockReference) return CadContextEntityTypesV2.BlockReference;
            return CadContextEntityTypesV2.Unsupported;
        }

        private static string ReadLayer(Entity entity, ref bool limited)
        {
            try
            {
                return Sanitize(
                    entity.Layer,
                    DrawingIndexContractConstants.MaximumNameCharacters,
                    Unknown,
                    ref limited);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                limited = true;
                return Unknown;
            }
        }

        private static string ReadBlockName(
            Transaction transaction,
            Entity entity,
            ref bool limited)
        {
            var blockReference = entity as BlockReference;
            if (blockReference == null)
            {
                return string.Empty;
            }

            try
            {
                var definitionId = blockReference.IsDynamicBlock
                    ? blockReference.DynamicBlockTableRecord
                    : blockReference.BlockTableRecord;
                if (definitionId.IsNull || definitionId.IsErased)
                {
                    limited = true;
                    return Unknown;
                }

                var definition = transaction.GetObject(
                    definitionId,
                    OpenMode.ForRead,
                    false) as BlockTableRecord;
                if (definition == null)
                {
                    limited = true;
                    return Unknown;
                }

                return Sanitize(
                    definition.Name,
                    DrawingIndexContractConstants.MaximumNameCharacters,
                    Unknown,
                    ref limited);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                limited = true;
                return Unknown;
            }
        }

        private static string ReadText(Entity entity, ref bool limited)
        {
            string value;
            try
            {
                var dbText = entity as DBText;
                if (dbText != null)
                {
                    value = dbText.TextString;
                }
                else
                {
                    var mText = entity as MText;
                    if (mText != null)
                    {
                        value = mText.Contents;
                    }
                    else
                    {
                        var dimension = entity as Dimension;
                        if (dimension != null)
                        {
                            value = dimension.DimensionText;
                        }
                        else
                        {
                            var mLeader = entity as MLeader;
                            value = mLeader == null || mLeader.MText == null
                                ? string.Empty
                                : mLeader.MText.Contents;
                        }
                    }
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                limited = true;
                return string.Empty;
            }

            return Sanitize(
                value,
                DrawingIndexContractConstants.MaximumTextExcerptCharacters,
                string.Empty,
                ref limited);
        }

        private static CadExtents3 TryReadBounds(Entity entity)
        {
            try
            {
                var extents = entity.GeometricExtents;
                if (!IsSafe(extents.MinPoint)
                    || !IsSafe(extents.MaxPoint)
                    || extents.MinPoint.X > extents.MaxPoint.X
                    || extents.MinPoint.Y > extents.MaxPoint.Y
                    || extents.MinPoint.Z > extents.MaxPoint.Z)
                {
                    return null;
                }

                return new CadExtents3
                {
                    Minimum = ToContractPoint(extents.MinPoint),
                    Maximum = ToContractPoint(extents.MaxPoint),
                };
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static bool IsSafe(Point3d point)
        {
            return IsSafe(point.X) && IsSafe(point.Y) && IsSafe(point.Z);
        }

        private static bool IsSafe(double value)
        {
            return !double.IsNaN(value)
                   && !double.IsInfinity(value)
                   && Math.Abs(value) <= DrawingIndexContractConstants.MaximumCoordinateMagnitude;
        }

        private static CadPoint3 ToContractPoint(Point3d point)
        {
            return new CadPoint3(point.X, point.Y, point.Z);
        }

        internal static string ReadObjectToken(ObjectId objectId)
        {
            if (objectId.IsNull)
            {
                return "0";
            }

            try
            {
                return objectId.Handle.ToString();
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                try
                {
                    var oldId = objectId.OldIdPtr.ToInt64();
                    return oldId == 0
                        ? "0"
                        : "oid-" + unchecked((ulong)oldId).ToString("X16", CultureInfo.InvariantCulture);
                }
                catch (Exception exception)
                    when (exception is Autodesk.AutoCAD.Runtime.Exception
                          || exception is InvalidOperationException
                          || exception is OverflowException)
                {
                    return "0";
                }
            }
        }

        private static string RequireObjectToken(string objectToken)
        {
            return string.IsNullOrWhiteSpace(objectToken) ? "0" : objectToken;
        }

        private static string Sanitize(
            string value,
            int maximumCharacters,
            string fallback,
            ref bool limited)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var builder = new StringBuilder(Math.Min(value.Length, maximumCharacters));
            for (var index = 0; index < value.Length; index++)
            {
                if (builder.Length >= maximumCharacters)
                {
                    limited = true;
                    break;
                }

                var character = value[index];
                var category = CharUnicodeInfo.GetUnicodeCategory(value, index);
                if (character == '\0'
                    || char.IsControl(character)
                    || category == UnicodeCategory.Format
                    || category == UnicodeCategory.LineSeparator
                    || category == UnicodeCategory.ParagraphSeparator)
                {
                    limited = true;
                    continue;
                }

                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    {
                        limited = true;
                        continue;
                    }
                    if (builder.Length + 2 > maximumCharacters)
                    {
                        limited = true;
                        break;
                    }
                    builder.Append(character);
                    builder.Append(value[++index]);
                    continue;
                }
                if (char.IsLowSurrogate(character))
                {
                    limited = true;
                    continue;
                }

                builder.Append(character);
            }

            var result = builder.ToString().Trim();
            return result.Length == 0 ? fallback : result;
        }
    }
}

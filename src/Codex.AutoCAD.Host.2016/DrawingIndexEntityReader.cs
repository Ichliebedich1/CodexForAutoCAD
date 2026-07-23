using System;
using System.Collections.Generic;
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
            var blockDetails = ReadBlockDetails(
                transaction,
                entity,
                out var blockName,
                ref limited);
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
                BlockDetails = blockDetails,
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

        private static CadQueryBlockDetails ReadBlockDetails(
            Transaction transaction,
            Entity entity,
            out string blockName,
            ref bool limited)
        {
            var blockReference = entity as BlockReference;
            if (blockReference == null)
            {
                blockName = string.Empty;
                return null;
            }

            try
            {
                var isDynamic = blockReference.IsDynamicBlock;
                var definitionId = isDynamic
                    ? blockReference.DynamicBlockTableRecord
                    : blockReference.BlockTableRecord;
                if (definitionId.IsNull || definitionId.IsErased)
                {
                    limited = true;
                    blockName = Unknown;
                    return LimitedBlockDetails(isDynamic);
                }

                var definition = transaction.GetObject(
                    definitionId,
                    OpenMode.ForRead,
                    false) as BlockTableRecord;
                if (definition == null)
                {
                    limited = true;
                    blockName = Unknown;
                    return LimitedBlockDetails(isDynamic);
                }

                var detailsLimited = false;
                blockName = Sanitize(
                    definition.Name,
                    DrawingIndexContractConstants.MaximumNameCharacters,
                    Unknown,
                    ref detailsLimited);
                var details = new CadQueryBlockDetails
                {
                    IsDynamic = isDynamic,
                    IsExternalReference = definition.IsFromExternalReference,
                    IsOverlayReference = definition.IsFromOverlayReference,
                    IsAnonymousDefinition = definition.IsAnonymous,
                    IsLayoutDefinition = definition.IsLayout,
                    HasAttributeDefinitions = definition.HasAttributeDefinitions,
                };

                ReadLayoutDetails(transaction, definition, details, ref detailsLimited);
                ReadAttributeDetails(transaction, blockReference, details, ref detailsLimited);
                ReadDynamicPropertyDetails(blockReference, details, ref detailsLimited);
                ReadNestedBlockDetails(transaction, definition, details, ref detailsLimited);

                if (detailsLimited)
                {
                    details.DetailStatus = CadQueryBlockDetailStatuses.Limited;
                    limited = true;
                }
                return details;
            }
            catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
            {
                limited = true;
                blockName = Unknown;
                return LimitedBlockDetails(false);
            }
        }

        private static CadQueryBlockDetails LimitedBlockDetails(bool isDynamic)
        {
            return new CadQueryBlockDetails
            {
                DetailStatus = CadQueryBlockDetailStatuses.Limited,
                IsDynamic = isDynamic,
            };
        }

        private static void ReadLayoutDetails(
            Transaction transaction,
            BlockTableRecord definition,
            CadQueryBlockDetails details,
            ref bool limited)
        {
            if (!details.IsLayoutDefinition)
            {
                return;
            }
            if (definition.LayoutId.IsNull || definition.LayoutId.IsErased)
            {
                details.LayoutKind = CadQueryLayoutKinds.Unavailable;
                limited = true;
                return;
            }

            var layout = transaction.GetObject(
                definition.LayoutId,
                OpenMode.ForRead,
                false) as Layout;
            if (layout == null)
            {
                details.LayoutKind = CadQueryLayoutKinds.Unavailable;
                limited = true;
                return;
            }

            details.LayoutName = Sanitize(
                layout.LayoutName,
                DrawingIndexContractConstants.MaximumNameCharacters,
                Unknown,
                ref limited);
            details.LayoutKind = layout.ModelType
                ? CadQueryLayoutKinds.Model
                : CadQueryLayoutKinds.Paper;
        }

        private static void ReadAttributeDetails(
            Transaction transaction,
            BlockReference blockReference,
            CadQueryBlockDetails details,
            ref bool limited)
        {
            var attributes = new List<CadQueryBlockAttribute>();
            var collection = blockReference.AttributeCollection;
            details.AttributeCount = collection.Count;
            if (details.AttributeCount < 0
                || details.AttributeCount > DrawingIndexContractConstants.MaximumReportedEntities)
            {
                details.AttributeCount = details.AttributeCount < 0
                    ? 0
                    : DrawingIndexContractConstants.MaximumReportedEntities;
                limited = true;
                details.Attributes = new CadQueryBlockAttribute[0];
                return;
            }

            foreach (ObjectId attributeId in collection)
            {
                if (attributes.Count >= DrawingIndexContractConstants.MaximumBlockAttributes)
                {
                    limited = true;
                    break;
                }
                if (attributeId.IsNull || attributeId.IsErased)
                {
                    limited = true;
                    continue;
                }

                var attribute = transaction.GetObject(
                    attributeId,
                    OpenMode.ForRead,
                    false) as AttributeReference;
                if (attribute == null)
                {
                    limited = true;
                    continue;
                }

                var attributeLimited = false;
                var tag = Sanitize(
                    attribute.Tag,
                    DrawingIndexContractConstants.MaximumBlockAttributeTagCharacters,
                    Unknown,
                    ref attributeLimited);
                var value = Sanitize(
                    attribute.TextString,
                    DrawingIndexContractConstants.MaximumBlockAttributeValueCharacters,
                    string.Empty,
                    ref attributeLimited);
                attributes.Add(new CadQueryBlockAttribute
                {
                    Tag = tag,
                    Value = value,
                    IsInvisible = attribute.Invisible,
                    IsMText = attribute.IsMTextAttribute,
                });
                limited |= attributeLimited;
            }

            details.Attributes = attributes.ToArray();
            if (attributes.Count != details.AttributeCount)
            {
                limited = true;
            }
        }

        private static void ReadDynamicPropertyDetails(
            BlockReference blockReference,
            CadQueryBlockDetails details,
            ref bool limited)
        {
            if (!details.IsDynamic)
            {
                return;
            }

            var properties = new List<CadQueryDynamicBlockProperty>();
            var total = 0;
            foreach (DynamicBlockReferenceProperty property
                in blockReference.DynamicBlockReferencePropertyCollection)
            {
                total++;
                if (properties.Count >= DrawingIndexContractConstants.MaximumDynamicBlockProperties)
                {
                    limited = true;
                    break;
                }
                if (property == null)
                {
                    limited = true;
                    continue;
                }

                var propertyLimited = false;
                var name = Sanitize(
                    property.PropertyName,
                    DrawingIndexContractConstants.MaximumDynamicBlockPropertyNameCharacters,
                    Unknown,
                    ref propertyLimited);
                var value = FormatDynamicPropertyValue(
                    property.Value,
                    out var valueKind,
                    ref propertyLimited);
                properties.Add(new CadQueryDynamicBlockProperty
                {
                    Name = name,
                    ValueKind = valueKind,
                    Value = value,
                    IsReadOnly = property.ReadOnly,
                    IsVisible = property.VisibleInCurrentVisibilityState,
                });
                limited |= propertyLimited;
            }

            details.DynamicPropertyCount = total;
            details.DynamicProperties = properties.ToArray();
            if (properties.Count != total)
            {
                limited = true;
            }
        }

        private static void ReadNestedBlockDetails(
            Transaction transaction,
            BlockTableRecord rootDefinition,
            CadQueryBlockDetails details,
            ref bool limited)
        {
            if (details.IsExternalReference)
            {
                // Do not inspect external definitions. This keeps Xref file metadata out of the index.
                limited = true;
                return;
            }

            var pending = new Queue<BlockDefinitionDepth>();
            var visited = new HashSet<ObjectId>();
            pending.Enqueue(new BlockDefinitionDepth(rootDefinition.ObjectId, 0));
            visited.Add(rootDefinition.ObjectId);
            var inspectedEntities = 0;

            while (pending.Count != 0)
            {
                var current = pending.Dequeue();
                if (current.DefinitionId.IsNull || current.DefinitionId.IsErased)
                {
                    limited = true;
                    continue;
                }
                var definition = transaction.GetObject(
                    current.DefinitionId,
                    OpenMode.ForRead,
                    false) as BlockTableRecord;
                if (definition == null)
                {
                    limited = true;
                    continue;
                }

                foreach (ObjectId entityId in definition)
                {
                    if (inspectedEntities >= DrawingIndexContractConstants.MaximumNestedBlockDefinitionEntities)
                    {
                        limited = true;
                        return;
                    }
                    inspectedEntities++;
                    if (entityId.IsNull || entityId.IsErased)
                    {
                        limited = true;
                        continue;
                    }
                    var nested = transaction.GetObject(
                        entityId,
                        OpenMode.ForRead,
                        false) as BlockReference;
                    if (nested == null)
                    {
                        continue;
                    }
                    if (details.NestedBlockReferenceCount
                        >= DrawingIndexContractConstants.MaximumNestedBlockReferences)
                    {
                        limited = true;
                        return;
                    }

                    details.NestedBlockReferenceCount++;
                    var nestedDepth = current.Depth + 1;
                    if (nestedDepth > details.MaximumNestedBlockDepth)
                    {
                        details.MaximumNestedBlockDepth = nestedDepth;
                    }
                    if (nestedDepth >= DrawingIndexContractConstants.MaximumNestedBlockDepth)
                    {
                        limited = true;
                        continue;
                    }

                    var nestedDefinitionId = nested.IsDynamicBlock
                        ? nested.DynamicBlockTableRecord
                        : nested.BlockTableRecord;
                    if (nestedDefinitionId.IsNull || nestedDefinitionId.IsErased)
                    {
                        limited = true;
                        continue;
                    }
                    var nestedDefinition = transaction.GetObject(
                        nestedDefinitionId,
                        OpenMode.ForRead,
                        false) as BlockTableRecord;
                    if (nestedDefinition == null)
                    {
                        limited = true;
                        continue;
                    }
                    if (nestedDefinition.IsFromExternalReference)
                    {
                        limited = true;
                        continue;
                    }
                    if (visited.Add(nestedDefinitionId))
                    {
                        pending.Enqueue(new BlockDefinitionDepth(nestedDefinitionId, nestedDepth));
                    }
                }
            }
        }

        private static string FormatDynamicPropertyValue(
            object value,
            out string valueKind,
            ref bool limited)
        {
            if (value == null)
            {
                valueKind = CadQueryDynamicValueKinds.Unavailable;
                limited = true;
                return string.Empty;
            }
            if (value is string text)
            {
                valueKind = CadQueryDynamicValueKinds.Text;
                return Sanitize(
                    text,
                    DrawingIndexContractConstants.MaximumDynamicBlockPropertyValueCharacters,
                    string.Empty,
                    ref limited);
            }
            if (value is bool boolean)
            {
                valueKind = CadQueryDynamicValueKinds.Boolean;
                return boolean ? "true" : "false";
            }
            if (value is Point3d point3)
            {
                return FormatDynamicPoint(
                    point3.X,
                    point3.Y,
                    point3.Z,
                    out valueKind,
                    ref limited);
            }
            if (value is Point2d point2)
            {
                return FormatDynamicPoint(
                    point2.X,
                    point2.Y,
                    0d,
                    out valueKind,
                    ref limited);
            }
            if (value is double doubleValue)
            {
                return FormatDynamicNumber(doubleValue, out valueKind, ref limited);
            }
            if (value is float floatValue)
            {
                return FormatDynamicNumber(floatValue, out valueKind, ref limited);
            }
            if (value is decimal decimalValue)
            {
                valueKind = CadQueryDynamicValueKinds.Number;
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }
            if (value is byte || value is sbyte || value is short || value is ushort
                || value is int || value is uint || value is long || value is ulong)
            {
                valueKind = CadQueryDynamicValueKinds.Number;
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            if (value.GetType().IsEnum)
            {
                valueKind = CadQueryDynamicValueKinds.Enum;
                return Sanitize(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    DrawingIndexContractConstants.MaximumDynamicBlockPropertyValueCharacters,
                    string.Empty,
                    ref limited);
            }

            valueKind = CadQueryDynamicValueKinds.Unavailable;
            limited = true;
            return string.Empty;
        }

        private static string FormatDynamicNumber(
            double value,
            out string valueKind,
            ref bool limited)
        {
            if (!IsSafe(value))
            {
                valueKind = CadQueryDynamicValueKinds.Unavailable;
                limited = true;
                return string.Empty;
            }
            valueKind = CadQueryDynamicValueKinds.Number;
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatDynamicPoint(
            double x,
            double y,
            double z,
            out string valueKind,
            ref bool limited)
        {
            if (!IsSafe(x) || !IsSafe(y) || !IsSafe(z))
            {
                valueKind = CadQueryDynamicValueKinds.Unavailable;
                limited = true;
                return string.Empty;
            }

            var formatted = FormatPoint(x, y, z);
            if (formatted.Length > DrawingIndexContractConstants.MaximumDynamicBlockPropertyValueCharacters)
            {
                valueKind = CadQueryDynamicValueKinds.Unavailable;
                limited = true;
                return string.Empty;
            }

            valueKind = CadQueryDynamicValueKinds.Point;
            return formatted;
        }

        private static string FormatPoint(double x, double y, double z)
        {
            return x.ToString("R", CultureInfo.InvariantCulture)
                   + ","
                   + y.ToString("R", CultureInfo.InvariantCulture)
                   + ","
                   + z.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool IsRecoverableBlockReadFailure(Exception exception)
        {
            return exception is Autodesk.AutoCAD.Runtime.Exception
                || exception is InvalidOperationException
                || exception is ArgumentException
                || exception is InvalidCastException
                || exception is OverflowException
                || exception is NullReferenceException;
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

        private sealed class BlockDefinitionDepth
        {
            internal BlockDefinitionDepth(ObjectId definitionId, int depth)
            {
                DefinitionId = definitionId;
                Depth = depth;
            }

            internal ObjectId DefinitionId { get; private set; }

            internal int Depth { get; private set; }
        }
    }
}

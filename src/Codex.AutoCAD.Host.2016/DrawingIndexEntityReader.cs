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
            string objectToken,
            DrawingIndexBlockDefinitionSummaryCache<ObjectId> blockDefinitionCache,
            DrawingIndexReadBudget readBudget)
        {
            if (transaction == null)
            {
                throw new ArgumentNullException(nameof(transaction));
            }
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }
            if (blockDefinitionCache == null)
            {
                throw new ArgumentNullException(nameof(blockDefinitionCache));
            }
            if (readBudget == null)
            {
                throw new ArgumentNullException(nameof(readBudget));
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
                blockDefinitionCache,
                readBudget,
                out var blockName,
                ref limited);
            var text = string.Empty;
            if (!readBudget.IsExpired)
            {
                text = ReadText(entity, ref limited);
            }
            else
            {
                limited = true;
            }
            CadExtents3 bounds = null;
            if (!readBudget.IsExpired)
            {
                bounds = TryReadBounds(entity);
            }
            else
            {
                limited = true;
            }
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
                Bounds = bounds,
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

        internal static CadQueryEntity ReadFailed(
            Entity entity,
            string objectToken,
            string space)
        {
            if (entity == null)
            {
                return ReadFailed(objectToken, space);
            }

            var limited = false;
            var entityType = Classify(entity);
            var actualType = Sanitize(
                entity.GetType().Name,
                DrawingIndexContractConstants.MaximumTypeCharacters,
                Unknown,
                ref limited);
            var blockReference = entity as BlockReference;
            CadQueryBlockDetails blockDetails = null;
            var blockName = string.Empty;
            if (blockReference != null)
            {
                var isDynamic = false;
                try
                {
                    isDynamic = blockReference.IsDynamicBlock;
                }
                catch (Exception exception)
                    when (IsRecoverableBlockReadFailure(exception)
                          || exception is NullReferenceException)
                {
                }

                blockName = Unknown;
                blockDetails = LimitedBlockDetails(isDynamic);
            }

            return new CadQueryEntity
            {
                ObjectId = RequireObjectToken(objectToken),
                EntityType = entityType,
                ActualType = actualType,
                Layer = Unknown,
                Space = Sanitize(
                    space,
                    DrawingIndexContractConstants.MaximumNameCharacters,
                    Unknown,
                    ref limited),
                BlockName = blockName,
                BlockDetails = blockDetails,
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
            DrawingIndexBlockDefinitionSummaryCache<ObjectId> blockDefinitionCache,
            DrawingIndexReadBudget readBudget,
            out string blockName,
            ref bool limited)
        {
            var blockReference = entity as BlockReference;
            if (blockReference == null)
            {
                blockName = string.Empty;
                return null;
            }

            var isDynamic = false;
            ObjectId definitionId;
            try
            {
                isDynamic = blockReference.IsDynamicBlock;
                definitionId = isDynamic
                    ? blockReference.DynamicBlockTableRecord
                    : blockReference.BlockTableRecord;
            }
            catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
            {
                limited = true;
                blockName = Unknown;
                return LimitedBlockDetails(isDynamic);
            }

            if (definitionId.IsNull || definitionId.IsErased)
            {
                limited = true;
                blockName = Unknown;
                return LimitedBlockDetails(isDynamic);
            }

            var definitionSummary = ResolveBlockDefinitionSummary(
                transaction,
                definitionId,
                blockDefinitionCache,
                readBudget);
            blockName = definitionSummary.BlockName;
            var details = definitionSummary.CreateDetails(isDynamic);
            var detailsLimited = definitionSummary.Limited;

            if (!readBudget.IsExpired)
            {
                try
                {
                    ReadAttributeDetails(
                        transaction,
                        blockReference,
                        details,
                        readBudget,
                        ref detailsLimited);
                }
                catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
                {
                    detailsLimited = true;
                }
            }
            else
            {
                detailsLimited = true;
            }

            if (!readBudget.IsExpired)
            {
                try
                {
                    ReadDynamicPropertyDetails(
                        blockReference,
                        details,
                        readBudget,
                        ref detailsLimited);
                }
                catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
                {
                    detailsLimited = true;
                }
            }
            else
            {
                detailsLimited = true;
            }

            var nested = DrawingIndexBlockTraversal.Traverse(
                definitionId,
                id => ResolveBlockDefinitionSummary(
                    transaction,
                    id,
                    blockDefinitionCache,
                    readBudget),
                () => readBudget.IsExpired,
                DrawingIndexContractConstants.MaximumNestedBlockReferences,
                DrawingIndexContractConstants.MaximumNestedBlockDepth,
                DrawingIndexContractConstants.MaximumNestedBlockDefinitionEntities,
                EqualityComparer<ObjectId>.Default);
            details.NestedBlockReferenceCount = nested.NestedBlockReferenceCount;
            details.MaximumNestedBlockDepth = nested.MaximumNestedBlockDepth;
            detailsLimited |= nested.Limited || readBudget.IsExpired;

            if (detailsLimited)
            {
                details.DetailStatus = CadQueryBlockDetailStatuses.Limited;
                limited = true;
            }

            return details;
        }

        private static CadQueryBlockDetails LimitedBlockDetails(bool isDynamic)
        {
            return new CadQueryBlockDetails
            {
                DetailStatus = CadQueryBlockDetailStatuses.Limited,
                IsDynamic = isDynamic,
            };
        }

        private static DrawingIndexBlockDefinitionSummary<ObjectId>
            ResolveBlockDefinitionSummary(
                Transaction transaction,
                ObjectId definitionId,
                DrawingIndexBlockDefinitionSummaryCache<ObjectId> blockDefinitionCache,
                DrawingIndexReadBudget readBudget)
        {
            if (blockDefinitionCache.TryGet(definitionId, out var cached))
            {
                return cached;
            }

            var summary = ReadBlockDefinitionSummary(
                transaction,
                definitionId,
                readBudget);
            blockDefinitionCache.StoreIfReusable(definitionId, summary);
            return summary;
        }

        private static DrawingIndexBlockDefinitionSummary<ObjectId>
            ReadBlockDefinitionSummary(
                Transaction transaction,
                ObjectId definitionId,
                DrawingIndexReadBudget readBudget)
        {
            var summary = new DrawingIndexBlockDefinitionSummary<ObjectId>
            {
                BlockName = Unknown,
            };
            if (readBudget.IsExpired)
            {
                summary.Limited = true;
                summary.BudgetExpired = true;
                return summary;
            }

            BlockTableRecord definition;
            try
            {
                definition = transaction.GetObject(
                    definitionId,
                    OpenMode.ForRead,
                    false) as BlockTableRecord;
            }
            catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
            {
                summary.Limited = true;
                return summary;
            }
            if (definition == null)
            {
                summary.Limited = true;
                return summary;
            }
            if (readBudget.IsExpired)
            {
                summary.Limited = true;
                summary.BudgetExpired = true;
                return summary;
            }

            var nameLimited = false;
            try
            {
                summary.BlockName = Sanitize(
                    definition.Name,
                    DrawingIndexContractConstants.MaximumNameCharacters,
                    Unknown,
                    ref nameLimited);
            }
            catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
            {
                summary.Limited = true;
                return summary;
            }
            summary.Limited |= nameLimited;
            if (readBudget.IsExpired)
            {
                summary.Limited = true;
                summary.BudgetExpired = true;
                return summary;
            }

            try
            {
                summary.IsExternalReference = definition.IsFromExternalReference;
                summary.IsOverlayReference = definition.IsFromOverlayReference;
                summary.IsAnonymousDefinition = definition.IsAnonymous;
                summary.IsLayoutDefinition = definition.IsLayout;
                summary.HasAttributeDefinitions = definition.HasAttributeDefinitions;
            }
            catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
            {
                summary.Limited = true;
                return summary;
            }
            if (readBudget.IsExpired)
            {
                summary.Limited = true;
                summary.BudgetExpired = true;
                return summary;
            }

            try
            {
                var layoutLimited = false;
                ReadLayoutDefinitionSummary(
                    transaction,
                    definition,
                    summary,
                    ref layoutLimited);
                summary.Limited |= layoutLimited;
            }
            catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
            {
                summary.LayoutName = string.Empty;
                summary.LayoutKind = CadQueryLayoutKinds.Unavailable;
                summary.Limited = true;
            }

            if (summary.IsExternalReference)
            {
                summary.Limited = true;
                return summary;
            }

            try
            {
                ReadNestedDefinitionIds(
                    transaction,
                    definition,
                    summary,
                    readBudget);
            }
            catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
            {
                summary.Limited = true;
            }

            return summary;
        }

        private static void ReadLayoutDefinitionSummary(
            Transaction transaction,
            BlockTableRecord definition,
            DrawingIndexBlockDefinitionSummary<ObjectId> summary,
            ref bool limited)
        {
            if (!summary.IsLayoutDefinition)
            {
                return;
            }
            if (definition.LayoutId.IsNull || definition.LayoutId.IsErased)
            {
                summary.LayoutKind = CadQueryLayoutKinds.Unavailable;
                limited = true;
                return;
            }

            var layout = transaction.GetObject(
                definition.LayoutId,
                OpenMode.ForRead,
                false) as Layout;
            if (layout == null)
            {
                summary.LayoutKind = CadQueryLayoutKinds.Unavailable;
                limited = true;
                return;
            }

            summary.LayoutName = Sanitize(
                layout.LayoutName,
                DrawingIndexContractConstants.MaximumNameCharacters,
                Unknown,
                ref limited);
            summary.LayoutKind = layout.ModelType
                ? CadQueryLayoutKinds.Model
                : CadQueryLayoutKinds.Paper;
        }

        private static void ReadAttributeDetails(
            Transaction transaction,
            BlockReference blockReference,
            CadQueryBlockDetails details,
            DrawingIndexReadBudget readBudget,
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
                if (readBudget.IsExpired)
                {
                    limited = true;
                    break;
                }
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
            DrawingIndexReadBudget readBudget,
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
                if (readBudget.IsExpired)
                {
                    limited = true;
                    break;
                }
                var retain = DrawingIndexBlockReadPolicy.RegisterSummaryItem(
                    ref total,
                    properties.Count,
                    DrawingIndexContractConstants.MaximumDynamicBlockProperties,
                    DrawingIndexContractConstants.MaximumReportedEntities,
                    ref limited);
                if (!retain)
                {
                    if (total >= DrawingIndexContractConstants.MaximumReportedEntities)
                    {
                        break;
                    }
                    continue;
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

        private static void ReadNestedDefinitionIds(
            Transaction transaction,
            BlockTableRecord definition,
            DrawingIndexBlockDefinitionSummary<ObjectId> summary,
            DrawingIndexReadBudget readBudget)
        {
            var nestedDefinitionIds = new List<ObjectId>();
            foreach (ObjectId entityId in definition)
            {
                if (readBudget.IsExpired)
                {
                    summary.Limited = true;
                    summary.BudgetExpired = true;
                    break;
                }
                if (summary.InspectedEntityCount
                    >= DrawingIndexContractConstants.MaximumNestedBlockDefinitionEntities)
                {
                    summary.Limited = true;
                    break;
                }
                summary.InspectedEntityCount++;
                if (entityId.IsNull || entityId.IsErased)
                {
                    summary.Limited = true;
                    continue;
                }

                BlockReference nested;
                try
                {
                    nested = transaction.GetObject(
                        entityId,
                        OpenMode.ForRead,
                        false) as BlockReference;
                }
                catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
                {
                    summary.Limited = true;
                    continue;
                }
                if (nested == null)
                {
                    continue;
                }

                ObjectId nestedDefinitionId;
                try
                {
                    nestedDefinitionId = nested.IsDynamicBlock
                        ? nested.DynamicBlockTableRecord
                        : nested.BlockTableRecord;
                }
                catch (Exception exception) when (IsRecoverableBlockReadFailure(exception))
                {
                    summary.Limited = true;
                    continue;
                }
                if (nestedDefinitionId.IsNull || nestedDefinitionId.IsErased)
                {
                    summary.Limited = true;
                    continue;
                }

                nestedDefinitionIds.Add(nestedDefinitionId);
            }

            summary.NestedDefinitionIds = nestedDefinitionIds.ToArray();
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
                || exception is OverflowException;
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

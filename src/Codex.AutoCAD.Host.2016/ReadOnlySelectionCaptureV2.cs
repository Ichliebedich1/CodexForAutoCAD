using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class SelectionCaptureDataV2
    {
        internal SelectionCaptureDataV2(
            int selectedCount,
            IList<CadContextEntityV2> entities)
        {
            SelectedCount = selectedCount;
            Entities = entities;
        }

        internal int SelectedCount { get; private set; }

        internal IList<CadContextEntityV2> Entities { get; private set; }
    }

    internal static class ReadOnlySelectionCaptureV2
    {
        private const string ZeroHash =
            "0000000000000000000000000000000000000000000000000000000000000000";

        internal static SelectionCaptureDataV2 Capture(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var result = document.Editor.SelectImplied();
            if (result.Status != PromptStatus.OK || result.Value == null)
            {
                throw new ContextValidationException("v2-no-implied-selection");
            }

            var objectIds = result.Value.GetObjectIds();
            if (objectIds.Length == 0)
            {
                throw new ContextValidationException("v2-no-implied-selection");
            }
            if (objectIds.Length > CadContextJsonV2Constants.MaximumEntities)
            {
                throw new ContextValidationException("v2-entity-limit");
            }

            var currentSpaceId = document.Database.CurrentSpaceId;
            if (currentSpaceId.IsNull || currentSpaceId.IsErased)
            {
                throw new ContextValidationException("v2-invalid-current-space");
            }
            var fallbackOwnerSpaceHandle = ReadHandle(currentSpaceId.Handle);

            var entities = new List<CadContextEntityV2>(objectIds.Length);
            using (var transaction = document.Database.TransactionManager.StartOpenCloseTransaction())
            {
                for (var index = 0; index < objectIds.Length; index++)
                {
                    var objectId = objectIds[index];
                    if (objectId.IsNull)
                    {
                        throw new ContextValidationException("v2-invalid-object-id");
                    }

                    var fallbackIdentity = new EntityIdentity(
                        ReadHandle(objectId.Handle),
                        fallbackOwnerSpaceHandle);
                    if (objectId.IsErased)
                    {
                        entities.Add(Unreadable(fallbackIdentity));
                        continue;
                    }

                    try
                    {
                        var entity = transaction.GetObject(
                            objectId,
                            OpenMode.ForRead,
                            false) as Entity;
                        entities.Add(
                            entity == null
                                ? Unreadable(fallbackIdentity)
                                : CreateEntity(transaction, entity, fallbackIdentity));
                    }
                    catch (Exception exception)
                        when (IsRecoverableEntityReadFailure(exception))
                    {
                        entities.Add(Unreadable(fallbackIdentity));
                    }
                }
            }

            if (entities.Count != objectIds.Length)
            {
                throw new ContextValidationException("v2-partial-capture");
            }
            return new SelectionCaptureDataV2(objectIds.Length, entities);
        }

        private static CadContextEntityV2 CreateEntity(
            Transaction transaction,
            Entity entity,
            EntityIdentity fallbackIdentity)
        {
            EntityIdentity identity;
            try
            {
                identity = ReadIdentity(entity);
            }
            catch (Exception exception)
                when (IsRecoverableEntityReadFailure(exception))
            {
                return Unsupported(
                    ReadCommonFallback(entity, fallbackIdentity),
                    CadContextUnsupportedReasonsV2.EntityReadFailed);
            }

            EntityCommon common;
            try
            {
                common = ReadCommon(entity, identity);
            }
            catch (EntityDataLimitException)
            {
                return Unsupported(
                    ReadCommonFallback(entity, identity),
                    CadContextUnsupportedReasonsV2.EntityDataLimit);
            }
            catch (Exception exception)
                when (IsRecoverableEntityReadFailure(exception))
            {
                return Unsupported(
                    ReadCommonFallback(entity, identity),
                    CadContextUnsupportedReasonsV2.EntityReadFailed);
            }

            try
            {
                var parsed = TryCreateSupported(transaction, entity, common);
                if (parsed == null)
                {
                    return Unsupported(
                        common,
                        CadContextUnsupportedReasonsV2.UnknownEntityType);
                }

                CanonicalSelectionHashV2.Build(new[] { parsed });
                return parsed;
            }
            catch (EntityDataLimitException)
            {
                return Unsupported(
                    common,
                    CadContextUnsupportedReasonsV2.EntityDataLimit);
            }
            catch (ContextValidationException exception)
            {
                return Unsupported(
                    common,
                    CadContextV2CapturePolicy.ClassifyContractFailure(exception.Code));
            }
            catch (Exception exception)
                when (IsRecoverableEntityReadFailure(exception))
            {
                return Unsupported(
                    common,
                    CadContextUnsupportedReasonsV2.EntityReadFailed);
            }
        }

        private static CadContextEntityV2 TryCreateSupported(
            Transaction transaction,
            Entity entity,
            EntityCommon common)
        {
            var table = entity as Table;
            if (table != null)
            {
                return TableEntity(table, common);
            }

            var mLeader = entity as MLeader;
            if (mLeader != null)
            {
                return MLeaderEntity(mLeader, common);
            }

            var leader = entity as Leader;
            if (leader != null)
            {
                return LeaderEntity(leader, common);
            }

            var dimension = entity as Dimension;
            if (dimension != null)
            {
                return DimensionEntity(dimension, common);
            }

            var hatch = entity as Hatch;
            if (hatch != null)
            {
                return HatchEntity(hatch, common);
            }

            var polyline2d = entity as Polyline2d;
            if (polyline2d != null)
            {
                return Polyline2dEntity(transaction, polyline2d, common);
            }

            var polyline3d = entity as Polyline3d;
            if (polyline3d != null)
            {
                return Polyline3dEntity(transaction, polyline3d, common);
            }

            var arc = entity as Arc;
            if (arc != null)
            {
                var result = Base(common, CadContextEntityTypesV2.Arc);
                result.Arc = new CadContextArcV2
                {
                    Center = Point3(arc.Center),
                    Radius = arc.Radius,
                    StartAngle = arc.StartAngle,
                    EndAngle = arc.EndAngle,
                    Normal = Point3(arc.Normal),
                };
                return result;
            }

            var ellipse = entity as Ellipse;
            if (ellipse != null)
            {
                var result = Base(common, CadContextEntityTypesV2.Ellipse);
                result.Ellipse = new CadContextEllipseV2
                {
                    Center = Point3(ellipse.Center),
                    MajorAxis = Point3(ellipse.MajorAxis),
                    RadiusRatio = ellipse.RadiusRatio,
                    StartParameter = ellipse.StartParam,
                    EndParameter = ellipse.EndParam,
                    Normal = Point3(ellipse.Normal),
                };
                return result;
            }

            var spline = entity as Spline;
            if (spline != null)
            {
                return SplineEntity(spline, common);
            }

            var point = entity as DBPoint;
            if (point != null)
            {
                var result = Base(common, CadContextEntityTypesV2.Point);
                result.Point = new CadContextPointV2
                {
                    Position = Point3(point.Position),
                    Normal = Point3(point.Normal),
                    EcsRotation = point.EcsRotation,
                };
                return result;
            }

            var ray = entity as Ray;
            if (ray != null)
            {
                var result = Base(common, CadContextEntityTypesV2.Ray);
                result.Ray = new CadContextRayV2
                {
                    BasePoint = Point3(ray.BasePoint),
                    SecondPoint = Point3(ray.SecondPoint),
                };
                return result;
            }

            var xline = entity as Xline;
            if (xline != null)
            {
                var result = Base(common, CadContextEntityTypesV2.Xline);
                result.Xline = new CadContextXlineV2
                {
                    BasePoint = Point3(xline.BasePoint),
                    SecondPoint = Point3(xline.SecondPoint),
                };
                return result;
            }

            var line = entity as Line;
            if (line != null)
            {
                var result = Base(common, CadContextEntityTypesV2.Line);
                result.Line = new CadContextLineV2
                {
                    Start = Point3(line.StartPoint),
                    End = Point3(line.EndPoint),
                };
                return result;
            }

            var circle = entity as Circle;
            if (circle != null)
            {
                var result = Base(common, CadContextEntityTypesV2.Circle);
                result.Circle = new CadContextCircleV2
                {
                    Center = Point3(circle.Center),
                    Radius = circle.Radius,
                    Normal = Point3(circle.Normal),
                };
                return result;
            }

            var polyline = entity as Polyline;
            if (polyline != null)
            {
                return PolylineEntity(polyline, common);
            }

            var dbText = entity as DBText;
            if (dbText != null)
            {
                var result = Base(common, CadContextEntityTypesV2.DbText);
                result.DbText = new CadContextDbTextV2
                {
                    Text = dbText.TextString ?? string.Empty,
                    Position = Point3(dbText.Position),
                    Height = dbText.Height,
                    Rotation = dbText.Rotation,
                };
                return result;
            }

            var mText = entity as MText;
            if (mText != null)
            {
                var result = Base(common, CadContextEntityTypesV2.MText);
                result.MText = new CadContextMTextV2
                {
                    Text = mText.Text ?? string.Empty,
                    Location = Point3(mText.Location),
                    TextHeight = mText.TextHeight,
                    Rotation = mText.Rotation,
                };
                return result;
            }

            var block = entity as BlockReference;
            if (block != null)
            {
                return BlockEntity(transaction, block, common);
            }
            return null;
        }

        private static CadContextEntityV2 PolylineEntity(
            Polyline polyline,
            EntityCommon common)
        {
            if (polyline.NumberOfVertices <= 0
                || polyline.NumberOfVertices
                    > CadContextJsonV2Constants.MaximumPolylineVertices)
            {
                throw new EntityDataLimitException();
            }

            var vertices = new CadContextPolylineVertexV2[polyline.NumberOfVertices];
            for (var index = 0; index < vertices.Length; index++)
            {
                vertices[index] = new CadContextPolylineVertexV2
                {
                    Position = Point2(polyline.GetPoint2dAt(index)),
                    Bulge = polyline.GetBulgeAt(index),
                };
            }

            var result = Base(common, CadContextEntityTypesV2.Polyline);
            result.Polyline = new CadContextPolylineV2
            {
                Closed = polyline.Closed,
                Elevation = polyline.Elevation,
                Normal = Point3(polyline.Normal),
                Vertices = vertices,
            };
            return result;
        }

        private static CadContextEntityV2 SplineEntity(
            Spline spline,
            EntityCommon common)
        {
            var controlCount = spline.NumControlPoints;
            var fitCount = spline.NumFitPoints;
            if (controlCount <= 0
                || controlCount < 0
                || fitCount < 0
                || (long)controlCount + fitCount
                    > CadContextJsonV2Constants.MaximumSplinePoints)
            {
                throw new EntityDataLimitException();
            }

            var controlPoints = new CadPoint3[controlCount];
            for (var index = 0; index < controlPoints.Length; index++)
            {
                controlPoints[index] = Point3(spline.GetControlPointAt(index));
            }
            var fitPoints = new CadPoint3[fitCount];
            for (var index = 0; index < fitPoints.Length; index++)
            {
                fitPoints[index] = Point3(spline.GetFitPointAt(index));
            }

            var result = Base(common, CadContextEntityTypesV2.Spline);
            result.Spline = new CadContextSplineV2
            {
                Degree = spline.Degree,
                IsRational = spline.IsRational,
                HasFitData = spline.HasFitData,
                ControlPoints = controlPoints,
                FitPoints = fitPoints,
            };
            return result;
        }

        private static CadContextEntityV2 Polyline2dEntity(
            Transaction transaction,
            Polyline2d polyline,
            EntityCommon common)
        {
            var vertices = new List<CadContextPolyline2dVertexV2>();
            foreach (ObjectId vertexId in polyline)
            {
                if (vertices.Count >= CadContextJsonV2Constants.MaximumPolylineVertices)
                {
                    throw new EntityDataLimitException();
                }
                var vertex = transaction.GetObject(
                    vertexId,
                    OpenMode.ForRead,
                    false) as Vertex2d;
                if (vertex == null)
                {
                    throw new InvalidOperationException("Vertex2d unavailable.");
                }
                vertices.Add(new CadContextPolyline2dVertexV2
                {
                    Position = Point3(polyline.VertexPosition(vertex)),
                    Bulge = vertex.Bulge,
                    StartWidth = vertex.StartWidth,
                    EndWidth = vertex.EndWidth,
                });
            }
            if (vertices.Count == 0)
            {
                throw new InvalidOperationException("Polyline2d has no vertices.");
            }

            var result = Base(common, CadContextEntityTypesV2.Polyline2d);
            result.Polyline2d = new CadContextPolyline2dV2
            {
                Closed = polyline.Closed,
                Elevation = polyline.Elevation,
                Normal = Point3(polyline.Normal),
                Vertices = vertices.ToArray(),
            };
            return result;
        }

        private static CadContextEntityV2 Polyline3dEntity(
            Transaction transaction,
            Polyline3d polyline,
            EntityCommon common)
        {
            var vertices = new List<CadPoint3>();
            foreach (ObjectId vertexId in polyline)
            {
                if (vertices.Count >= CadContextJsonV2Constants.MaximumPolylineVertices)
                {
                    throw new EntityDataLimitException();
                }
                var vertex = transaction.GetObject(
                    vertexId,
                    OpenMode.ForRead,
                    false) as PolylineVertex3d;
                if (vertex == null)
                {
                    throw new InvalidOperationException("PolylineVertex3d unavailable.");
                }
                vertices.Add(Point3(vertex.Position));
            }
            if (vertices.Count == 0)
            {
                throw new InvalidOperationException("Polyline3d has no vertices.");
            }

            var result = Base(common, CadContextEntityTypesV2.Polyline3d);
            result.Polyline3d = new CadContextPolyline3dV2
            {
                Closed = polyline.Closed,
                Vertices = vertices.ToArray(),
            };
            return result;
        }

        private static CadContextEntityV2 DimensionEntity(
            Dimension dimension,
            EntityCommon common)
        {
            var result = Base(common, CadContextEntityTypesV2.Dimension);
            result.Dimension = new CadContextDimensionV2
            {
                DimensionType = dimension.GetType().Name,
                Measurement = dimension.Measurement,
                DimensionText = dimension.DimensionText ?? string.Empty,
                TextPosition = Point3(dimension.TextPosition),
                TextRotation = dimension.TextRotation,
                Normal = Point3(dimension.Normal),
                StyleName = dimension.DimensionStyleName ?? string.Empty,
            };
            return result;
        }

        private static CadContextEntityV2 HatchEntity(
            Hatch hatch,
            EntityCommon common)
        {
            var loopCount = hatch.NumberOfLoops;
            if (loopCount < 0 || loopCount > CadContextJsonV2Constants.MaximumHatchLoops)
            {
                throw new EntityDataLimitException();
            }
            var loopTypes = new string[loopCount];
            for (var index = 0; index < loopTypes.Length; index++)
            {
                loopTypes[index] = hatch.LoopTypeAt(index).ToString();
            }

            var result = Base(common, CadContextEntityTypesV2.Hatch);
            result.Hatch = new CadContextHatchV2
            {
                Associative = hatch.Associative,
                IsGradient = hatch.IsGradient,
                IsSolidFill = hatch.IsSolidFill,
                PatternName = hatch.PatternName ?? string.Empty,
                PatternAngle = hatch.PatternAngle,
                PatternScale = hatch.PatternScale,
                Elevation = hatch.Elevation,
                Normal = Point3(hatch.Normal),
                LoopTypes = loopTypes,
            };
            return result;
        }

        private static CadContextEntityV2 LeaderEntity(
            Leader leader,
            EntityCommon common)
        {
            var count = leader.NumVertices;
            if (count < 2 || count > CadContextJsonV2Constants.MaximumLeaderVertices)
            {
                throw new EntityDataLimitException();
            }
            var vertices = new CadPoint3[count];
            for (var index = 0; index < vertices.Length; index++)
            {
                vertices[index] = Point3(leader.VertexAt(index));
            }

            var result = Base(common, CadContextEntityTypesV2.Leader);
            result.Leader = new CadContextLeaderV2
            {
                IsSplined = leader.IsSplined,
                HasArrowHead = leader.HasArrowHead,
                AnnotationType = leader.AnnoType.ToString(),
                Normal = Point3(leader.Normal),
                Vertices = vertices,
            };
            return result;
        }

        private static CadContextEntityV2 MLeaderEntity(
            MLeader leader,
            EntityCommon common)
        {
            var leaderCount = leader.LeaderCount;
            var leaderLineCount = leader.LeaderLineCount;
            if (!CadContextV2CapturePolicy.IsWithinCountLimit(
                    leaderCount,
                    CadContextJsonV2Constants.MaximumMLeaderLines)
                || !CadContextV2CapturePolicy.IsWithinCountLimit(
                    leaderLineCount,
                    CadContextJsonV2Constants.MaximumMLeaderLines))
            {
                throw new EntityDataLimitException();
            }

            var leaderIndexes = ReadIndexes(
                leader.GetLeaderIndexes(),
                CadContextJsonV2Constants.MaximumMLeaderLines);
            if (leaderIndexes.Count != leaderCount)
            {
                throw new InvalidOperationException("MLeader leader count changed during capture.");
            }
            var lineIndexes = new List<int>();
            var seen = new HashSet<int>();
            for (var leaderIndex = 0; leaderIndex < leaderIndexes.Count; leaderIndex++)
            {
                var currentLines = ReadIndexes(
                    leader.GetLeaderLineIndexes(leaderIndexes[leaderIndex]),
                    CadContextJsonV2Constants.MaximumMLeaderLines);
                for (var lineIndex = 0; lineIndex < currentLines.Count; lineIndex++)
                {
                    if (seen.Add(currentLines[lineIndex]))
                    {
                        if (lineIndexes.Count
                            >= CadContextJsonV2Constants.MaximumMLeaderLines)
                        {
                            throw new EntityDataLimitException();
                        }
                        lineIndexes.Add(currentLines[lineIndex]);
                    }
                }
            }
            if (lineIndexes.Count != leaderLineCount)
            {
                throw new InvalidOperationException("MLeader line count changed during capture.");
            }

            var lines = new CadContextMLeaderLineV2[lineIndexes.Count];
            var totalVertices = 0;
            for (var index = 0; index < lineIndexes.Count; index++)
            {
                var count = leader.VerticesCount(lineIndexes[index]);
                if (count < 2)
                {
                    throw new InvalidOperationException("MLeader line has too few vertices.");
                }
                int nextTotalVertices;
                if (!CadContextV2CapturePolicy.TryAccumulateCount(
                        totalVertices,
                        count,
                        CadContextJsonV2Constants.MaximumMLeaderVertices,
                        out nextTotalVertices))
                {
                    throw new EntityDataLimitException();
                }
                totalVertices = nextTotalVertices;
                var vertices = new CadPoint3[count];
                for (var vertexIndex = 0; vertexIndex < count; vertexIndex++)
                {
                    vertices[vertexIndex] = Point3(
                        leader.GetVertex(lineIndexes[index], vertexIndex));
                }
                lines[index] = new CadContextMLeaderLineV2 { Vertices = vertices };
            }

            var text = string.Empty;
            if (leader.ContentType == ContentType.MTextContent)
            {
                var mText = leader.MText;
                if (mText != null)
                {
                    try
                    {
                        text = mText.Text ?? string.Empty;
                    }
                    finally
                    {
                        mText.Dispose();
                    }
                }
            }

            var result = Base(common, CadContextEntityTypesV2.MLeader);
            result.MLeader = new CadContextMLeaderV2
            {
                ContentType = leader.ContentType.ToString(),
                Normal = Point3(leader.Normal),
                Text = text,
                LeaderLines = lines,
            };
            return result;
        }

        private static CadContextEntityV2 TableEntity(
            Table table,
            EntityCommon common)
        {
            var rows = table.Rows.Count;
            var columns = table.Columns.Count;
            var cellCount = (long)rows * columns;
            if (rows <= 0
                || columns <= 0
                || cellCount > CadContextJsonV2Constants.MaximumTableCells)
            {
                throw new EntityDataLimitException();
            }

            var cells = new CadContextTableCellV2[checked((int)cellCount)];
            var cellIndex = 0;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var cell = table.Cells[row, column];
                    cells[cellIndex++] = new CadContextTableCellV2
                    {
                        Row = row,
                        Column = column,
                        Text = cell == null ? string.Empty : cell.TextString ?? string.Empty,
                    };
                }
            }

            var result = Base(common, CadContextEntityTypesV2.Table);
            result.Table = new CadContextTableV2
            {
                Position = Point3(table.Position),
                Direction = Point3(table.Direction),
                Rows = rows,
                Columns = columns,
                Width = table.Width,
                Height = table.Height,
                StyleName = table.TableStyleName ?? string.Empty,
                Cells = cells,
            };
            return result;
        }

        private static CadContextEntityV2 BlockEntity(
            Transaction transaction,
            BlockReference block,
            EntityCommon common)
        {
            var dynamic = block.IsDynamicBlock;
            var definitionId = dynamic
                ? block.DynamicBlockTableRecord
                : block.BlockTableRecord;
            if (definitionId.IsNull || definitionId.IsErased)
            {
                throw new InvalidOperationException("Block definition unavailable.");
            }
            var definition = transaction.GetObject(
                definitionId,
                OpenMode.ForRead,
                false) as BlockTableRecord;
            if (definition == null)
            {
                throw new InvalidOperationException("Block definition unreadable.");
            }

            var result = Base(common, CadContextEntityTypesV2.BlockReference);
            result.BlockReference = new CadContextBlockReferenceV2
            {
                Position = Point3(block.Position),
                Rotation = block.Rotation,
                Scale = new CadPoint3(
                    block.ScaleFactors.X,
                    block.ScaleFactors.Y,
                    block.ScaleFactors.Z),
                EffectiveName = definition.Name ?? string.Empty,
                IsDynamic = dynamic,
                IsExternalReference = definition.IsFromExternalReference,
            };
            return result;
        }

        private static List<int> ReadIndexes(ArrayList values, int maximumCount)
        {
            var result = new List<int>();
            if (values == null)
            {
                return result;
            }
            if (!CadContextV2CapturePolicy.IsWithinCountLimit(values.Count, maximumCount))
            {
                throw new EntityDataLimitException();
            }
            for (var index = 0; index < values.Count; index++)
            {
                result.Add(Convert.ToInt32(values[index]));
            }
            return result;
        }

        private static EntityIdentity ReadIdentity(Entity entity)
        {
            var ownerId = entity.OwnerId;
            if (ownerId.IsNull || ownerId.IsErased)
            {
                throw new ContextValidationException("v2-invalid-owner-space");
            }

            return new EntityIdentity(
                ReadHandle(entity.Handle),
                ReadHandle(ownerId.Handle));
        }

        private static EntityCommon ReadCommon(
            Entity entity,
            EntityIdentity identity)
        {
            var layer = entity.Layer ?? string.Empty;
            if (CadContextV2CapturePolicy.IsNameDataLimit(layer))
            {
                throw new EntityDataLimitException();
            }
            if (!CadContextV2CapturePolicy.IsSafeRequiredName(layer))
            {
                throw new InvalidOperationException("Entity layer is unavailable or invalid.");
            }

            return new EntityCommon(
                identity.Handle,
                identity.OwnerSpaceHandle,
                layer,
                ReadDxfName(entity));
        }

        private static EntityCommon ReadCommonFallback(
            Entity entity,
            EntityIdentity identity)
        {
            var layer = CadContextV2CapturePolicy.UnknownCommonValue;
            try
            {
                var candidate = entity.Layer;
                if (CadContextV2CapturePolicy.IsSafeRequiredName(candidate))
                {
                    layer = candidate;
                }
            }
            catch (Exception exception)
                when (IsRecoverableEntityReadFailure(exception))
            {
            }

            var dxfName = CadContextV2CapturePolicy.UnknownCommonValue;
            try
            {
                dxfName = ReadDxfName(entity);
            }
            catch (Exception exception)
                when (exception is EntityDataLimitException
                    || IsRecoverableEntityReadFailure(exception))
            {
            }

            return new EntityCommon(
                identity.Handle,
                identity.OwnerSpaceHandle,
                layer,
                dxfName);
        }

        private static CadContextEntityV2 Unreadable(EntityIdentity identity)
        {
            return Unsupported(
                new EntityCommon(
                    identity.Handle,
                    identity.OwnerSpaceHandle,
                    CadContextV2CapturePolicy.UnknownCommonValue,
                    CadContextV2CapturePolicy.UnknownCommonValue),
                CadContextUnsupportedReasonsV2.EntityReadFailed);
        }

        private static CadContextEntityV2 Base(
            EntityCommon common,
            string entityType)
        {
            return new CadContextEntityV2
            {
                Handle = common.Handle,
                OwnerSpaceHandle = common.OwnerSpaceHandle,
                EntityType = entityType,
                StateHash = ZeroHash,
                Layer = common.Layer,
            };
        }

        private static CadContextEntityV2 Unsupported(
            EntityCommon common,
            string reason)
        {
            var result = Base(common, CadContextEntityTypesV2.Unsupported);
            result.Unsupported = new CadContextUnsupportedV2
            {
                DxfName = common.DxfName,
                Reason = reason,
            };
            return result;
        }

        private static string ReadHandle(Handle handle)
        {
            var value = handle.Value;
            if (value <= 0)
            {
                throw new ContextValidationException("v2-invalid-handle");
            }
            return checked((ulong)value).ToString("X");
        }

        private static string ReadDxfName(Entity entity)
        {
            var rxClass = entity.GetRXClass();
            var value = rxClass == null ? null : rxClass.DxfName;
            if (string.IsNullOrEmpty(value))
            {
                return CadContextV2CapturePolicy.UnknownCommonValue;
            }
            if (value.Length > CadContextJsonV2Constants.MaximumTokenCharacters)
            {
                throw new EntityDataLimitException();
            }

            var builder = new System.Text.StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var allowed =
                    (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '_'
                    || character == '-'
                    || character == '.'
                    || character == '$';
                builder.Append(allowed ? character : '_');
            }
            return builder.Length == 0
                ? CadContextV2CapturePolicy.UnknownCommonValue
                : builder.ToString();
        }

        private static bool IsRecoverableEntityReadFailure(Exception exception)
        {
            return exception is ContextValidationException
                || exception is Autodesk.AutoCAD.Runtime.Exception
                || exception is InvalidOperationException
                || exception is ArgumentException
                || exception is InvalidCastException
                || exception is FormatException
                || exception is NullReferenceException
                || exception is OverflowException
                || exception is IndexOutOfRangeException
                || exception is ObjectDisposedException
                || exception is NotSupportedException;
        }

        private static CadPoint2 Point2(Point2d point)
        {
            return new CadPoint2(point.X, point.Y);
        }

        private static CadPoint3 Point3(Point3d point)
        {
            return new CadPoint3(point.X, point.Y, point.Z);
        }

        private static CadPoint3 Point3(Vector3d vector)
        {
            return new CadPoint3(vector.X, vector.Y, vector.Z);
        }

        private sealed class EntityIdentity
        {
            internal EntityIdentity(string handle, string ownerSpaceHandle)
            {
                Handle = handle;
                OwnerSpaceHandle = ownerSpaceHandle;
            }

            internal string Handle { get; private set; }

            internal string OwnerSpaceHandle { get; private set; }
        }

        private sealed class EntityCommon
        {
            internal EntityCommon(
                string handle,
                string ownerSpaceHandle,
                string layer,
                string dxfName)
            {
                Handle = handle;
                OwnerSpaceHandle = ownerSpaceHandle;
                Layer = layer;
                DxfName = dxfName;
            }

            internal string Handle { get; private set; }

            internal string OwnerSpaceHandle { get; private set; }

            internal string Layer { get; private set; }

            internal string DxfName { get; private set; }
        }

        private sealed class EntityDataLimitException : Exception
        {
        }
    }
}

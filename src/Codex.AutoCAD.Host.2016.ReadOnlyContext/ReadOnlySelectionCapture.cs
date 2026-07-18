using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext
{
    internal sealed class SelectionCaptureData
    {
        internal SelectionCaptureData(int selectedCount, IList<ContextEntityDraft> entities)
        {
            SelectedCount = selectedCount;
            Entities = entities;
        }

        internal int SelectedCount { get; private set; }

        internal IList<ContextEntityDraft> Entities { get; private set; }
    }

    internal static class ReadOnlySelectionCapture
    {
        internal static SelectionCaptureData Capture(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var result = document.Editor.SelectImplied();
            if (result.Status != PromptStatus.OK || result.Value == null)
            {
                throw new ContextValidationException("no-implied-selection");
            }

            var objectIds = result.Value.GetObjectIds();
            if (objectIds.Length == 0)
            {
                throw new ContextValidationException("no-implied-selection");
            }

            if (objectIds.Length > CanonicalSelectionHash.MaximumEntities)
            {
                throw new ContextValidationException("entity-limit");
            }

            var entities = new List<ContextEntityDraft>(objectIds.Length);
            using (var transaction = document.Database.TransactionManager.StartOpenCloseTransaction())
            {
                for (var index = 0; index < objectIds.Length; index++)
                {
                    var objectId = objectIds[index];
                    if (objectId.IsNull || objectId.IsErased)
                    {
                        throw new ContextValidationException("invalid-object-id");
                    }

                    var entity = OpenObjectForRead(transaction, objectId) as Entity;
                    if (entity == null)
                    {
                        throw new ContextValidationException("selected-object-not-entity");
                    }

                    entities.Add(CreateEntityDraft(transaction, entity));
                }
            }

            if (entities.Count != objectIds.Length)
            {
                throw new ContextValidationException("partial-capture");
            }

            return new SelectionCaptureData(objectIds.Length, entities);
        }

        private static DBObject OpenObjectForRead(Transaction transaction, ObjectId objectId)
        {
            return transaction.GetObject(objectId, OpenMode.ForRead, false);
        }

        private static ContextEntityDraft CreateEntityDraft(Transaction transaction, Entity entity)
        {
            var handle = ReadHandle(entity.Handle);
            if (entity.OwnerId.IsNull || entity.OwnerId.IsErased)
            {
                throw new ContextValidationException("invalid-owner-space");
            }

            var ownerSpaceHandle = ReadHandle(entity.OwnerId.Handle);
            var layer = entity.Layer;

            var line = entity as Line;
            if (line != null)
            {
                return Draft(
                    ContextEntityKind.Line,
                    handle,
                    ownerSpaceHandle,
                    layer,
                    new ContextLineData(Point3(line.StartPoint), Point3(line.EndPoint)),
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            var circle = entity as Circle;
            if (circle != null)
            {
                return Draft(
                    ContextEntityKind.Circle,
                    handle,
                    ownerSpaceHandle,
                    layer,
                    null,
                    new ContextCircleData(Point3(circle.Center), circle.Radius, Vector3(circle.Normal)),
                    null,
                    null,
                    null,
                    null);
            }

            var polyline = entity as Polyline;
            if (polyline != null)
            {
                if (polyline.NumberOfVertices <= 0
                    || polyline.NumberOfVertices > CanonicalSelectionHash.MaximumPolylineVertices)
                {
                    throw new ContextValidationException("polyline-vertex-limit");
                }

                var vertices = new List<ContextPolylineVertex>(polyline.NumberOfVertices);
                for (var vertexIndex = 0; vertexIndex < polyline.NumberOfVertices; vertexIndex++)
                {
                    vertices.Add(new ContextPolylineVertex(
                        Point2(polyline.GetPoint2dAt(vertexIndex)),
                        polyline.GetBulgeAt(vertexIndex)));
                }

                return Draft(
                    ContextEntityKind.Polyline,
                    handle,
                    ownerSpaceHandle,
                    layer,
                    null,
                    null,
                    new ContextPolylineData(
                        polyline.Closed,
                        polyline.Elevation,
                        Vector3(polyline.Normal),
                        vertices),
                    null,
                    null,
                    null);
            }

            var dbText = entity as DBText;
            if (dbText != null)
            {
                return Draft(
                    ContextEntityKind.DbText,
                    handle,
                    ownerSpaceHandle,
                    layer,
                    null,
                    null,
                    null,
                    new ContextDbTextData(
                        dbText.TextString,
                        Point3(dbText.Position),
                        dbText.Height,
                        dbText.Rotation),
                    null,
                    null);
            }

            var mText = entity as MText;
            if (mText != null)
            {
                return Draft(
                    ContextEntityKind.MText,
                    handle,
                    ownerSpaceHandle,
                    layer,
                    null,
                    null,
                    null,
                    null,
                    new ContextMTextData(
                        mText.Text,
                        Point3(mText.Location),
                        mText.TextHeight,
                        mText.Rotation),
                    null);
            }

            var block = entity as BlockReference;
            if (block != null)
            {
                var dynamic = block.IsDynamicBlock;
                var definitionId = dynamic ? block.DynamicBlockTableRecord : block.BlockTableRecord;
                if (definitionId.IsNull || definitionId.IsErased)
                {
                    throw new ContextValidationException("invalid-block-definition");
                }

                var definition = OpenObjectForRead(transaction, definitionId) as BlockTableRecord;
                if (definition == null)
                {
                    throw new ContextValidationException("block-definition-unreadable");
                }

                return Draft(
                    ContextEntityKind.BlockReference,
                    handle,
                    ownerSpaceHandle,
                    layer,
                    null,
                    null,
                    null,
                    null,
                    null,
                    new ContextBlockData(
                        Point3(block.Position),
                        block.Rotation,
                        new ContextVector3(
                            block.ScaleFactors.X,
                            block.ScaleFactors.Y,
                            block.ScaleFactors.Z),
                        definition.Name,
                        dynamic,
                        definition.IsFromExternalReference));
            }

            throw new ContextValidationException("unsupported-entity-kind");
        }

        private static ContextEntityDraft Draft(
            ContextEntityKind kind,
            ulong handle,
            ulong ownerSpaceHandle,
            string layer,
            ContextLineData line,
            ContextCircleData circle,
            ContextPolylineData polyline,
            ContextDbTextData dbText,
            ContextMTextData mText,
            ContextBlockData block)
        {
            return new ContextEntityDraft(
                kind,
                handle,
                ownerSpaceHandle,
                layer,
                line,
                circle,
                polyline,
                dbText,
                mText,
                block);
        }

        private static ulong ReadHandle(Handle handle)
        {
            var value = handle.Value;
            if (value <= 0)
            {
                throw new ContextValidationException("invalid-handle");
            }

            return checked((ulong)value);
        }

        private static ContextPoint2 Point2(Point2d point)
        {
            return new ContextPoint2(point.X, point.Y);
        }

        private static ContextPoint3 Point3(Point3d point)
        {
            return new ContextPoint3(point.X, point.Y, point.Z);
        }

        private static ContextVector3 Vector3(Vector3d vector)
        {
            return new ContextVector3(vector.X, vector.Y, vector.Z);
        }
    }
}

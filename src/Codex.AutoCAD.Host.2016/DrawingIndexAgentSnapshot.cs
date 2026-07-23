using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// Pure managed invalidation token shared by a published DrawingIndex and any active Agent turn.
    /// AutoCAD document events invalidate it on the document thread; Bridge worker threads only read it.
    /// </summary>
    internal sealed class DrawingIndexSnapshotValidity
    {
        private int invalidated;

        internal bool IsCurrent
        {
            get { return Volatile.Read(ref invalidated) == 0; }
        }

        internal void Invalidate()
        {
            Interlocked.Exchange(ref invalidated, 1);
        }
    }

    /// <summary>
    /// Deep-managed, immutable DrawingIndex view. It deliberately contains no Autodesk object and can
    /// therefore be queried from authenticated Bridge worker threads without entering AutoCAD APIs.
    /// </summary>
    internal sealed class DrawingIndexAgentSnapshot
    {
        private readonly DrawingIndexDescriptor descriptor;
        private readonly CadQueryEntity[] entities;
        private readonly DrawingIndexSnapshotValidity validity;
        private readonly DrawingIndexPerformanceMetrics performanceMetrics;
        private readonly DrawingIndexCursorRegistry cursorRegistry =
            new DrawingIndexCursorRegistry();

        internal DrawingIndexAgentSnapshot(
            int generation,
            DrawingIndexDescriptor sourceDescriptor,
            IReadOnlyList<CadQueryEntity> sourceEntities,
            DrawingIndexSnapshotValidity sourceValidity)
            : this(
                generation,
                sourceDescriptor,
                sourceEntities,
                sourceValidity,
                false,
                null)
        {
        }

        internal static DrawingIndexAgentSnapshot CreateFromOwnedFrozenEntities(
            int generation,
            DrawingIndexDescriptor sourceDescriptor,
            CadQueryEntity[] ownedFrozenEntities,
            DrawingIndexSnapshotValidity sourceValidity)
        {
            return CreateFromOwnedFrozenEntities(
                generation,
                sourceDescriptor,
                ownedFrozenEntities,
                sourceValidity,
                null);
        }

        internal static DrawingIndexAgentSnapshot CreateFromOwnedFrozenEntities(
            int generation,
            DrawingIndexDescriptor sourceDescriptor,
            CadQueryEntity[] ownedFrozenEntities,
            DrawingIndexSnapshotValidity sourceValidity,
            DrawingIndexPerformanceMetrics performanceMetrics)
        {
            return new DrawingIndexAgentSnapshot(
                generation,
                sourceDescriptor,
                ownedFrozenEntities,
                sourceValidity,
                true,
                performanceMetrics);
        }

        private DrawingIndexAgentSnapshot(
            int generation,
            DrawingIndexDescriptor sourceDescriptor,
            IReadOnlyList<CadQueryEntity> sourceEntities,
            DrawingIndexSnapshotValidity sourceValidity,
            bool takeFrozenEntityOwnership,
            DrawingIndexPerformanceMetrics sourcePerformanceMetrics)
        {
            if (generation <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }
            if (sourceDescriptor == null)
            {
                throw new ArgumentNullException(nameof(sourceDescriptor));
            }
            if (sourceEntities == null)
            {
                throw new ArgumentNullException(nameof(sourceEntities));
            }
            if (sourceValidity == null)
            {
                throw new ArgumentNullException(nameof(sourceValidity));
            }
            if (!IsQueryableStatus(sourceDescriptor.Status))
            {
                throw new DrawingIndexQueryException(
                    "drawing_index_unavailable",
                    "DrawingIndex尚未形成可查询的冻结快照。");
            }

            var failures = DrawingIndexContractValidator.Validate(sourceDescriptor);
            if (failures.Length != 0)
            {
                throw new DrawingIndexQueryException(
                    "drawing_index_descriptor_invalid",
                    "DrawingIndex描述未通过冻结契约。");
            }
            if (sourceDescriptor.IndexedEntityCount != sourceEntities.Count)
            {
                throw new DrawingIndexQueryException(
                    "drawing_index_entity_count_mismatch",
                    "DrawingIndex描述与冻结实体数量不一致。");
            }

            Generation = generation;
            descriptor = CloneDescriptor(sourceDescriptor);
            var ownedEntities = takeFrozenEntityOwnership
                ? sourceEntities as CadQueryEntity[]
                : null;
            if (takeFrozenEntityOwnership && ownedEntities == null)
            {
                throw new ArgumentException(
                    "DrawingIndex冻结实体所有权只能从数组转移。",
                    nameof(sourceEntities));
            }
            entities = ownedEntities ?? new CadQueryEntity[sourceEntities.Count];
            for (var index = 0; index < sourceEntities.Count; index++)
            {
                var entity = sourceEntities[index];
                if (entity == null)
                {
                    throw new DrawingIndexQueryException(
                        "drawing_index_entity_invalid",
                        "DrawingIndex冻结实体不能为空。");
                }
                if (!takeFrozenEntityOwnership)
                {
                    entities[index] = DrawingIndexQueryEngine.CloneEntity(entity);
                }
            }
            validity = sourceValidity;
            performanceMetrics = sourcePerformanceMetrics;
        }

        internal int Generation { get; private set; }

        internal string DocumentId
        {
            get { return descriptor.DocumentId; }
        }

        internal long DocumentRevision
        {
            get { return descriptor.DocumentRevision; }
        }

        internal bool IsCurrent
        {
            get { return validity.IsCurrent; }
        }

        internal CadQueryResponse Query(
            AgentDrawingQueryRequest request,
            CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!validity.IsCurrent)
                {
                    throw new DrawingIndexQueryException(
                        "drawing_index_stale",
                        "DrawingIndex已因图纸变化而失效。");
                }

                var failures = AgentBridgeContractValidator.Validate(request);
                if (failures.Length != 0)
                {
                    throw new DrawingIndexQueryException(
                        failures[0].Code,
                        "整图查询请求未通过冻结契约。");
                }

                var boundRequest = new CadQueryRequest
                {
                    IndexId = descriptor.IndexId,
                    DocumentId = descriptor.DocumentId,
                    DocumentRevision = descriptor.DocumentRevision,
                    QueryId = request.QueryId,
                    Filter = CloneFilter(request.Filter),
                    PageSize = request.PageSize,
                    Cursor = request.Cursor ?? string.Empty,
                };
                var response = DrawingIndexQueryEngine.Execute(
                    descriptor,
                    entities,
                    boundRequest,
                    cursorRegistry,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!validity.IsCurrent)
                {
                    throw new DrawingIndexQueryException(
                        "drawing_index_stale",
                        "DrawingIndex在查询期间失效；结果已拒绝。");
                }
                return response;
            }
            finally
            {
                timer.Stop();
                if (performanceMetrics != null)
                {
                    performanceMetrics.RecordQuery(timer.Elapsed);
                }
            }
        }

        private static bool IsQueryableStatus(string status)
        {
            return string.Equals(status, DrawingIndexStatuses.Ready, StringComparison.Ordinal)
                   || string.Equals(status, DrawingIndexStatuses.Partial, StringComparison.Ordinal)
                   || string.Equals(status, DrawingIndexStatuses.Limited, StringComparison.Ordinal);
        }

        private static DrawingIndexDescriptor CloneDescriptor(DrawingIndexDescriptor value)
        {
            return new DrawingIndexDescriptor
            {
                Schema = value.Schema,
                SchemaVersion = value.SchemaVersion,
                EgressRisk = value.EgressRisk,
                IndexId = value.IndexId,
                DocumentId = value.DocumentId,
                DrawingFingerprint = value.DrawingFingerprint,
                DocumentRevision = value.DocumentRevision,
                Scope = value.Scope,
                Status = value.Status,
                Complete = value.Complete,
                Limited = value.Limited,
                EntityCount = value.EntityCount,
                IndexedEntityCount = value.IndexedEntityCount,
                UnsupportedEntityCount = value.UnsupportedEntityCount,
                FailedEntityCount = value.FailedEntityCount,
                ProgressPercent = value.ProgressPercent,
                EstimatedManagedBytes = value.EstimatedManagedBytes,
                StartedAtUtc = value.StartedAtUtc,
                CompletedAtUtc = value.CompletedAtUtc,
                LimitReason = value.LimitReason,
                TypeCounts = CloneBuckets(value.TypeCounts),
                LayerCounts = CloneBuckets(value.LayerCounts),
                SpaceCounts = CloneBuckets(value.SpaceCounts),
                BlockCounts = CloneBuckets(value.BlockCounts),
            };
        }

        private static DrawingIndexCountBucket[] CloneBuckets(DrawingIndexCountBucket[] values)
        {
            if (values == null || values.Length == 0)
            {
                return new DrawingIndexCountBucket[0];
            }
            var result = new DrawingIndexCountBucket[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                result[index] = new DrawingIndexCountBucket
                {
                    Key = values[index].Key ?? string.Empty,
                    Count = values[index].Count,
                };
            }
            return result;
        }

        private static CadQueryFilter CloneFilter(CadQueryFilter value)
        {
            return new CadQueryFilter
            {
                EntityTypes = CloneStrings(value.EntityTypes),
                Layers = CloneStrings(value.Layers),
                Spaces = CloneStrings(value.Spaces),
                BlockNames = CloneStrings(value.BlockNames),
                ObjectIds = CloneStrings(value.ObjectIds),
                TextContains = value.TextContains ?? string.Empty,
                IncludeUnsupported = value.IncludeUnsupported,
                Bounds = value.Bounds == null
                    ? null
                    : new CadQueryBounds
                    {
                        Minimum = new CadPoint3(
                            value.Bounds.Minimum.X,
                            value.Bounds.Minimum.Y,
                            value.Bounds.Minimum.Z),
                        Maximum = new CadPoint3(
                            value.Bounds.Maximum.X,
                            value.Bounds.Maximum.Y,
                            value.Bounds.Maximum.Z),
                    },
            };
        }

        private static string[] CloneStrings(string[] values)
        {
            return values == null || values.Length == 0
                ? new string[0]
                : values.ToArray();
        }
    }
}

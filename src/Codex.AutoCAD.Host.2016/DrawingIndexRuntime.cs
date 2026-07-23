using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Codex.AutoCAD.Contracts;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016
{
    internal static class DrawingIndexRuntime
    {
        private const int MaximumEntitiesPerIdleSlice = 128;
        private const int MaximumIdsPerPreparationSlice = 4096;
        private const int MaximumMillisecondsPerIdleSlice = 12;
        private const long MaximumEstimatedManagedBytes = 64L * 1024L * 1024L;
        private static readonly TimeSpan MaximumScanDuration = TimeSpan.FromMinutes(2);

        private static readonly CadQueryEntity[] EmptyEntities = new CadQueryEntity[0];
        private static readonly DrawingIndexCursorRegistry LocalCursorRegistry =
            new DrawingIndexCursorRegistry();
        private static DrawingIndexDescriptor descriptor = CreateEmptyDescriptor();
        private static CadQueryEntity[] publishedEntities = EmptyEntities;
        private static DrawingIndexBuildSession activeSession;
        private static CadQueryRequest lastQueryRequest;
        private static CadQueryResponse lastQueryResponse;
        private static DrawingIndexAgentSnapshot publishedAgentSnapshot;
        private static DrawingIndexSnapshotValidity publishedAgentSnapshotValidity;
        private static DrawingIndexPerformanceMetrics performanceMetrics =
            new DrawingIndexPerformanceMetrics();
        private static CadReadIssueSnapshot readIssues = CadReadIssueSnapshot.Empty();
        private static Database observedDatabase;
        private static bool initialized;
        private static bool idleAttached;
        private static bool processingIdle;
        private static int generation;
        private static string indexedCurrentSpace = string.Empty;

        internal static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            AutoCadApplication.DocumentManager.DocumentActivated += OnDocumentActivated;
            AutoCadApplication.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            initialized = true;
            NotifyPalette();
        }

        internal static void Terminate()
        {
            if (!initialized)
            {
                return;
            }

            DetachIdle();
            DetachDatabaseEvents();
            AutoCadApplication.DocumentManager.DocumentActivated -= OnDocumentActivated;
            AutoCadApplication.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            var session = activeSession;
            activeSession = null;
            if (session != null)
            {
                session.Dispose();
            }
            InvalidatePublishedAgentSnapshot();
            publishedEntities = EmptyEntities;
            lastQueryRequest = null;
            lastQueryResponse = null;
            descriptor = CreateEmptyDescriptor();
            performanceMetrics = new DrawingIndexPerformanceMetrics();
            readIssues = CadReadIssueSnapshot.Empty();
            indexedCurrentSpace = string.Empty;
            initialized = false;
        }

        internal static DrawingIndexDescriptor Start(string scope)
        {
            Initialize();
            if (!IsKnownScope(scope))
            {
                throw new ArgumentException("DrawingIndex扫描范围不受支持。", nameof(scope));
            }

            var document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                throw new InvalidOperationException("当前没有活动图纸。");
            }

            int dbmodBefore;
            if (!TryReadDbmod(out dbmodBefore))
            {
                throw new InvalidOperationException("无法读取DBMOD；拒绝启动只读索引。");
            }

            var metadata = UnifiedReadOnlyContextRuntime.CaptureDocumentMetadata(document);
            DiscardActiveSession();
            InvalidatePublishedAgentSnapshot();
            generation++;
            var startedAt = DateTimeOffset.UtcNow;
            descriptor = new DrawingIndexDescriptor
            {
                IndexId = "idx-" + Guid.NewGuid().ToString("N"),
                DocumentId = metadata.DocumentId,
                DrawingFingerprint = metadata.DrawingFingerprint,
                DocumentRevision = metadata.Revision,
                Scope = scope,
                Status = DrawingIndexStatuses.Preparing,
                Complete = false,
                Limited = false,
                EntityCount = 0,
                IndexedEntityCount = 0,
                UnsupportedEntityCount = 0,
                FailedEntityCount = 0,
                ProgressPercent = 0,
                EstimatedManagedBytes = 0,
                StartedAtUtc = FormatTimestamp(startedAt),
                CompletedAtUtc = string.Empty,
            };
            publishedEntities = EmptyEntities;
            lastQueryRequest = null;
            lastQueryResponse = null;
            indexedCurrentSpace = ReadCurrentSpaceToken(document.Database);
            performanceMetrics = new DrawingIndexPerformanceMetrics();
            readIssues = CadReadIssueSnapshot.Empty();
            activeSession = new DrawingIndexBuildSession(
                document,
                metadata,
                descriptor.IndexId,
                scope,
                dbmodBefore,
                startedAt,
                new DrawingIndexAccumulator(MaximumEstimatedManagedBytes),
                performanceMetrics);
            AttachDatabaseEvents(document.Database);
            AttachIdle();
            NotifyPalette();
            return CloneDescriptor(descriptor);
        }

        internal static void Cancel()
        {
            Initialize();
            var session = activeSession;
            if (session == null)
            {
                return;
            }

            session.CancelRequested = true;
            FinalizeSession(session, DrawingIndexStatuses.Cancelled, false, false, "user_cancelled");
        }

        internal static DrawingIndexDescriptor GetDescriptor()
        {
            Initialize();
            EnsureCurrentRevision();
            return CloneDescriptor(descriptor);
        }

        /// <summary>
        /// Must be called on the AutoCAD document thread. The returned object is already a deep,
        /// pure-managed snapshot and is safe for authenticated Bridge worker threads to query.
        /// </summary>
        internal static bool TryFreezeAgentSnapshot(out DrawingIndexAgentSnapshot snapshot)
        {
            Initialize();
            EnsureCurrentRevision();
            snapshot = publishedAgentSnapshot;
            if (snapshot == null || !snapshot.IsCurrent)
            {
                snapshot = null;
                return false;
            }
            return true;
        }

        internal static CadQueryResponse QueryFirst(CadQueryFilter filter, int pageSize)
        {
            Initialize();
            EnsureCurrentRevision();
            if (string.IsNullOrWhiteSpace(descriptor.IndexId))
            {
                throw new DrawingIndexQueryException(
                    "drawing_index_missing",
                    "尚未建立DrawingIndex。先执行CODEX16INDEX。");
            }

            var request = new CadQueryRequest
            {
                IndexId = descriptor.IndexId,
                DocumentId = descriptor.DocumentId,
                DocumentRevision = descriptor.DocumentRevision,
                QueryId = "qry-" + Guid.NewGuid().ToString("N"),
                Filter = filter ?? new CadQueryFilter(),
                PageSize = pageSize,
                Cursor = string.Empty,
            };
            return ExecuteAndRemember(request);
        }

        internal static CadQueryResponse QueryNext()
        {
            Initialize();
            EnsureCurrentRevision();
            if (lastQueryRequest == null
                || lastQueryResponse == null
                || string.IsNullOrEmpty(lastQueryResponse.NextCursor))
            {
                throw new DrawingIndexQueryException(
                    "cad_query_no_next_page",
                    "当前没有可继续读取的查询页。");
            }

            var request = CloneRequest(lastQueryRequest);
            request.Cursor = lastQueryResponse.NextCursor;
            return ExecuteAndRemember(request);
        }

        internal static string BuildInfo()
        {
            Initialize();
            EnsureCurrentRevision();
            var current = descriptor;
            var performance = performanceMetrics.Snapshot();
            var builder = new StringBuilder();
            builder.AppendLine("--- Codex AutoCAD 2016 DrawingIndex ---");
            builder.Append("Schema: ").Append(DrawingIndexContractConstants.Schema).Append('/')
                .AppendLine(DrawingIndexContractConstants.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append("Generation: ").AppendLine(generation.ToString(CultureInfo.InvariantCulture));
            builder.Append("Status: ").AppendLine(current.Status);
            builder.Append("Index ID: ").AppendLine(
                string.IsNullOrEmpty(current.IndexId) ? "unavailable" : current.IndexId);
            builder.Append("Scope: ").AppendLine(current.Scope);
            builder.Append("Document revision: ").AppendLine(
                current.DocumentRevision.ToString(CultureInfo.InvariantCulture));
            builder.Append("Entity count: ").AppendLine(current.EntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Indexed count: ").AppendLine(current.IndexedEntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Unsupported count: ").AppendLine(current.UnsupportedEntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Read-failed count: ").AppendLine(current.FailedEntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Placeholder reasons: ").AppendLine(
                CadReadTypeStatistics.FormatReasonCounts(readIssues));
            builder.Append("Placeholder actual types: ").AppendLine(
                CadReadTypeStatistics.FormatActualTypeCounts(readIssues, 64));
            builder.Append("Progress: ").Append(current.ProgressPercent.ToString(CultureInfo.InvariantCulture)).AppendLine("%");
            builder.Append("Complete: ").AppendLine(current.Complete ? "true" : "false");
            builder.Append("Limited: ").AppendLine(current.Limited ? "true" : "false");
            builder.Append("Limit reason: ").AppendLine(
                string.IsNullOrEmpty(current.LimitReason) ? "none" : current.LimitReason);
            builder.Append("Estimated managed bytes: ").AppendLine(
                current.EstimatedManagedBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("Type counts: ").AppendLine(FormatCounts(current.TypeCounts));
            builder.Append("Layer count buckets: ").AppendLine(
                current.LayerCounts.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append("Space counts: ").AppendLine(FormatCounts(current.SpaceCounts));
            builder.Append("Block count buckets: ").AppendLine(
                current.BlockCounts.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append("Last query status: ").AppendLine(
                lastQueryResponse == null ? "none" : lastQueryResponse.Status);
            builder.Append("Last query matches/returned: ").Append(
                lastQueryResponse == null ? "0" : lastQueryResponse.TotalMatches.ToString(CultureInfo.InvariantCulture));
            builder.Append('/').AppendLine(
                lastQueryResponse == null ? "0" : lastQueryResponse.ReturnedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Idle slice budget ms: ").AppendLine(
                MaximumMillisecondsPerIdleSlice.ToString(CultureInfo.InvariantCulture));
            builder.Append("Idle slices total/preparation/read: ")
                .Append(performance.IdleSliceCount.ToString(CultureInfo.InvariantCulture)).Append('/')
                .Append(performance.PreparationSliceCount.ToString(CultureInfo.InvariantCulture)).Append('/')
                .AppendLine(performance.ReadSliceCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Maximum idle slice ms: ").AppendLine(
                DrawingIndexPerformanceMetrics.FormatMilliseconds(performance.MaximumIdleSliceDuration));
            builder.Append("Maximum preparation slice ms: ").AppendLine(
                DrawingIndexPerformanceMetrics.FormatMilliseconds(performance.MaximumPreparationSliceDuration));
            builder.Append("Maximum read slice ms: ").AppendLine(
                DrawingIndexPerformanceMetrics.FormatMilliseconds(performance.MaximumReadSliceDuration));
            builder.Append("Total scan elapsed ms: ").AppendLine(
                DrawingIndexPerformanceMetrics.FormatMilliseconds(performance.TotalScanDuration));
            builder.Append("Scan timeout ms: ").AppendLine(
                MaximumScanDuration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
            builder.Append("Managed memory budget bytes: ").AppendLine(
                MaximumEstimatedManagedBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("Query page entity limit: ").AppendLine(
                DrawingIndexContractConstants.MaximumPageSize.ToString(CultureInfo.InvariantCulture));
            builder.Append("IPC message hard limit bytes: ").AppendLine(
                ProtocolConstants.MaximumMessageBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("Queries: ").AppendLine(performance.QueryCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Last query ms: ").AppendLine(
                DrawingIndexPerformanceMetrics.FormatMilliseconds(performance.LastQueryDuration));
            builder.Append("Maximum query ms: ").AppendLine(
                DrawingIndexPerformanceMetrics.FormatMilliseconds(performance.MaximumQueryDuration));
            builder.AppendLine("Document name/path capture: disabled");
            builder.AppendLine("CAD write: disabled");
            builder.AppendLine("Plugin-initiated save: disabled");
            builder.Append("--- End DrawingIndex ---");
            return builder.ToString();
        }

        internal static string FormatQueryResponse(CadQueryResponse response)
        {
            if (response == null)
            {
                return "CadQuery响应为空。";
            }

            var builder = new StringBuilder();
            builder.Append("CadQuery status=").Append(response.Status);
            builder.Append(", matches=").Append(response.TotalMatches.ToString(CultureInfo.InvariantCulture));
            builder.Append(", returned=").Append(response.ReturnedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(", complete=").Append(response.Complete ? "true" : "false");
            builder.Append(", next=").Append(string.IsNullOrEmpty(response.NextCursor) ? "false" : "true");
            for (var index = 0; index < response.Entities.Length; index++)
            {
                var entity = response.Entities[index];
                builder.AppendLine();
                builder.Append(index + 1).Append(". id=").Append(entity.ObjectId);
                builder.Append(" type=").Append(entity.EntityType);
                builder.Append(" layer=").Append(entity.Layer);
                builder.Append(" space=").Append(entity.Space);
                if (!string.IsNullOrEmpty(entity.BlockName))
                {
                    builder.Append(" block=").Append(entity.BlockName);
                }
                if (entity.Unsupported)
                {
                    builder.Append(" readStatus=").Append(entity.ReadStatus);
                    builder.Append(" actualType=").Append(entity.ActualType);
                }
            }
            if (!string.IsNullOrEmpty(response.Message))
            {
                builder.AppendLine().Append("Note: ").Append(response.Message);
            }
            return builder.ToString();
        }

        private static CadQueryResponse ExecuteAndRemember(CadQueryRequest request)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                var response = DrawingIndexQueryEngine.Execute(
                    descriptor,
                    publishedEntities,
                    request,
                    LocalCursorRegistry);
                lastQueryRequest = CloneRequest(request);
                lastQueryResponse = response;
                NotifyPalette();
                return response;
            }
            finally
            {
                timer.Stop();
                performanceMetrics.RecordQuery(timer.Elapsed);
            }
        }

        private static void OnIdle(object sender, EventArgs eventArgs)
        {
            if (processingIdle)
            {
                return;
            }

            var session = activeSession;
            if (session == null)
            {
                DetachIdle();
                return;
            }

            processingIdle = true;
            var preparationPhase = session.Items == null;
            var idleTimer = Stopwatch.StartNew();
            try
            {
                ProcessSession(session);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                FinalizeSession(
                    session,
                    session.Accumulator.Count == 0
                        ? DrawingIndexStatuses.Failed
                        : DrawingIndexStatuses.Partial,
                    false,
                    false,
                    "autocad_read_failed");
            }
            catch (Exception exception)
                when (exception is ArgumentException
                      || exception is InvalidOperationException
                      || exception is OverflowException)
            {
                FinalizeSession(
                    session,
                    session.Accumulator.Count == 0
                        ? DrawingIndexStatuses.Failed
                        : DrawingIndexStatuses.Partial,
                    false,
                    false,
                    "scan_failed");
            }
            finally
            {
                idleTimer.Stop();
                session.PerformanceMetrics.RecordIdleSlice(
                    preparationPhase,
                    idleTimer.Elapsed,
                    session.ScanTimer.Elapsed);
                processingIdle = false;
            }
        }

        private static void ProcessSession(DrawingIndexBuildSession session)
        {
            if (!ReferenceEquals(activeSession, session))
            {
                return;
            }
            if (!string.IsNullOrEmpty(session.PendingInvalidationReason))
            {
                FinalizeSession(
                    session,
                    DrawingIndexStatuses.Stale,
                    false,
                    false,
                    session.PendingInvalidationReason);
                return;
            }
            if (session.CancelRequested)
            {
                FinalizeSession(session, DrawingIndexStatuses.Cancelled, false, false, "user_cancelled");
                return;
            }
            if (!ReferenceEquals(
                    AutoCadApplication.DocumentManager.MdiActiveDocument,
                    session.Document))
            {
                FinalizeSession(session, DrawingIndexStatuses.Stale, false, false, "document_activated");
                return;
            }
            if (!IsMetadataCurrent(session))
            {
                FinalizeSession(session, DrawingIndexStatuses.Stale, false, false, "document_revision_changed");
                return;
            }
            if (DateTimeOffset.UtcNow - session.StartedAtUtc > MaximumScanDuration)
            {
                FinalizeSession(session, DrawingIndexStatuses.Limited, false, true, "scan_timeout");
                return;
            }

            if (session.Items == null)
            {
                var preparationComplete = PrepareItemsSlice(session);
                if (!ReferenceEquals(activeSession, session))
                {
                    return;
                }
                if (!string.IsNullOrEmpty(session.PendingInvalidationReason))
                {
                    FinalizeSession(
                        session,
                        DrawingIndexStatuses.Stale,
                        false,
                        false,
                        session.PendingInvalidationReason);
                    return;
                }
                if (!preparationComplete)
                {
                    return;
                }
                if (session.Items.Count == 0)
                {
                    FinalizeSession(session, DrawingIndexStatuses.Ready, true, false, string.Empty);
                    return;
                }
            }

            var timer = Stopwatch.StartNew();
            var readBudget = new DrawingIndexReadBudget(
                timer,
                MaximumMillisecondsPerIdleSlice);
            var processed = 0;
            using (session.Document.LockDocument())
            using (var transaction = session.Document.Database.TransactionManager.StartOpenCloseTransaction())
            {
                while (session.NextItemIndex < session.Items.Count
                       && processed < MaximumEntitiesPerIdleSlice
                       && !readBudget.IsExpired)
                {
                    var item = session.Items[session.NextItemIndex];
                    var entry = ReadItem(
                        transaction,
                        item,
                        session.BlockDefinitionCache,
                        readBudget);
                    if (!string.IsNullOrEmpty(session.PendingInvalidationReason))
                    {
                        break;
                    }
                    if (!session.Accumulator.TryAdd(entry))
                    {
                        FinalizeSession(
                            session,
                            DrawingIndexStatuses.Limited,
                            false,
                            true,
                            "memory_budget");
                        return;
                    }

                    session.NextItemIndex++;
                    processed++;
                }
            }

            if (!string.IsNullOrEmpty(session.PendingInvalidationReason))
            {
                FinalizeSession(
                    session,
                    DrawingIndexStatuses.Stale,
                    false,
                    false,
                    session.PendingInvalidationReason);
                return;
            }

            if (!TryReadDbmod(out var dbmodAfter) || dbmodAfter != session.DbmodBefore)
            {
                FinalizeSession(session, DrawingIndexStatuses.Stale, false, false, "dbmod_changed");
                return;
            }
            if (!IsMetadataCurrent(session))
            {
                FinalizeSession(session, DrawingIndexStatuses.Stale, false, false, "document_revision_changed");
                return;
            }

            if (session.NextItemIndex >= session.Items.Count)
            {
                var completion = DrawingIndexBuildPolicy.Complete(
                    session.EntityBudgetExceeded,
                    session.Accumulator.CountBucketsLimited,
                    session.Accumulator.UnsupportedCount);
                FinalizeSession(
                    session,
                    completion.Status,
                    completion.Complete,
                    completion.Limited,
                    completion.Reason);
                return;
            }

            UpdateProgress(session);
        }

        private static bool PrepareItemsSlice(DrawingIndexBuildSession session)
        {
            var timer = Stopwatch.StartNew();
            var processed = 0;
            using (session.Document.LockDocument())
            {
                var preparation = session.Preparation;
                if (preparation == null)
                {
                    preparation = CreatePreparationState(session);
                    session.Preparation = preparation;
                }

                while (!preparation.Complete
                       && processed < MaximumIdsPerPreparationSlice
                       && timer.ElapsedMilliseconds < MaximumMillisecondsPerIdleSlice)
                {
                    ObjectId objectId;
                    string space;
                    if (!TryReadNextPreparationObject(session, preparation, out objectId, out space))
                    {
                        preparation.Complete = true;
                        break;
                    }

                    AddScanItem(preparation, objectId, space);
                    processed++;
                    if (preparation.CountOverflow
                        || !string.IsNullOrEmpty(session.PendingInvalidationReason))
                    {
                        preparation.Complete = preparation.CountOverflow;
                        break;
                    }
                }

                session.TotalEntityCount = preparation.TotalEntityCount;
                session.EntityBudgetExceeded = preparation.EntityBudgetExceeded;
            }

            if (!string.IsNullOrEmpty(session.PendingInvalidationReason))
            {
                return false;
            }
            if (!TryReadDbmod(out var dbmodAfter) || dbmodAfter != session.DbmodBefore)
            {
                session.RequestInvalidation("dbmod_changed");
                return false;
            }
            if (!IsMetadataCurrent(session))
            {
                session.RequestInvalidation("document_revision_changed");
                return false;
            }

            var current = session.Preparation;
            descriptor.Status = current.Complete
                ? DrawingIndexStatuses.Scanning
                : DrawingIndexStatuses.Preparing;
            descriptor.EntityCount = current.TotalEntityCount;
            descriptor.IndexedEntityCount = 0;
            descriptor.UnsupportedEntityCount = 0;
            descriptor.FailedEntityCount = 0;
            descriptor.ProgressPercent = 0;
            descriptor.EstimatedManagedBytes = 0;

            if (!current.Complete)
            {
                NotifyPalette();
                return false;
            }

            session.Items = current.Items;
            current.Dispose();
            session.Preparation = null;
            descriptor.EntityCount = session.TotalEntityCount;
            descriptor.ProgressPercent = session.Items.Count == 0 ? 100 : 0;
            NotifyPalette();
            return true;
        }

        private static DrawingIndexPreparationState CreatePreparationState(
            DrawingIndexBuildSession session)
        {
            using (var transaction = session.Document.Database.TransactionManager.StartOpenCloseTransaction())
            {
                if (session.Scope == DrawingIndexScopes.Selection)
                {
                    var selection = session.Document.Editor.SelectImplied();
                    if (selection.Status != PromptStatus.OK || selection.Value == null)
                    {
                        throw new InvalidOperationException("当前没有可用于索引的预选对象。");
                    }

                    var currentSpace = ReadSpaceName(
                        transaction,
                        session.Document.Database.CurrentSpaceId,
                        session.Document.Database);
                    return DrawingIndexPreparationState.ForSelection(
                        selection.Value.GetObjectIds(),
                        currentSpace);
                }

                return DrawingIndexPreparationState.ForSpaces(
                    ResolveSpaceSources(transaction, session.Document.Database, session.Scope));
            }
        }

        private static DrawingIndexSpaceSource[] ResolveSpaceSources(
            Transaction transaction,
            Database database,
            string scope)
        {
            var blockTable = transaction.GetObject(
                database.BlockTableId,
                OpenMode.ForRead,
                false) as BlockTable;
            if (blockTable == null)
            {
                throw new InvalidOperationException("无法读取BlockTable。");
            }

            var modelSpaceId = blockTable[BlockTableRecord.ModelSpace];
            if (scope == DrawingIndexScopes.CurrentSpace)
            {
                return new[]
                {
                    new DrawingIndexSpaceSource(
                        database.CurrentSpaceId,
                        ReadSpaceName(transaction, database.CurrentSpaceId, database)),
                };
            }
            if (scope == DrawingIndexScopes.ModelSpace)
            {
                return new[] { new DrawingIndexSpaceSource(modelSpaceId, "model") };
            }

            var sources = new List<DrawingIndexSpaceSource>();
            if (scope == DrawingIndexScopes.Drawing)
            {
                sources.Add(new DrawingIndexSpaceSource(modelSpaceId, "model"));
            }

            var layouts = transaction.GetObject(
                database.LayoutDictionaryId,
                OpenMode.ForRead,
                false) as DBDictionary;
            if (layouts == null)
            {
                throw new InvalidOperationException("无法读取LayoutDictionary。");
            }

            foreach (DBDictionaryEntry entry in layouts)
            {
                var layout = transaction.GetObject(
                    entry.Value,
                    OpenMode.ForRead,
                    false) as Layout;
                if (layout == null
                    || layout.BlockTableRecordId.IsNull
                    || layout.BlockTableRecordId.IsErased
                    || layout.BlockTableRecordId == modelSpaceId)
                {
                    continue;
                }

                sources.Add(new DrawingIndexSpaceSource(
                    layout.BlockTableRecordId,
                    "layout:" + SanitizeSpaceToken(layout.LayoutName)));
            }

            return sources
                .OrderBy(source => source.Space == "model" ? 0 : 1)
                .ThenBy(source => source.Space, StringComparer.OrdinalIgnoreCase)
                .ThenBy(source => source.Space, StringComparer.Ordinal)
                .ThenBy(source => DrawingIndexEntityReader.ReadObjectToken(source.RecordId), StringComparer.Ordinal)
                .ToArray();
        }

        private static bool TryReadNextPreparationObject(
            DrawingIndexBuildSession session,
            DrawingIndexPreparationState preparation,
            out ObjectId objectId,
            out string space)
        {
            if (preparation.SelectionObjectIds != null)
            {
                if (preparation.SelectionIndex >= preparation.SelectionObjectIds.Length)
                {
                    objectId = ObjectId.Null;
                    space = string.Empty;
                    return false;
                }

                objectId = preparation.SelectionObjectIds[preparation.SelectionIndex++];
                space = preparation.SelectionSpace;
                return true;
            }

            while (preparation.SpaceIndex < preparation.SpaceSources.Length)
            {
                if (preparation.CurrentObjectIds == null)
                {
                    var source = preparation.SpaceSources[preparation.SpaceIndex];
                    var maximumCollected = Math.Max(
                        0,
                        DrawingIndexContractConstants.MaximumIndexedEntities
                        - preparation.Items.Count);
                    var snapshot = ReadSpaceObjectIds(
                        session.Document.Database,
                        source.RecordId,
                        maximumCollected);
                    if (snapshot.CountOverflow)
                    {
                        preparation.CountOverflow = true;
                        preparation.EntityBudgetExceeded = true;
                        objectId = ObjectId.Null;
                        space = string.Empty;
                        return false;
                    }

                    var skipped = snapshot.TotalEntityCount - snapshot.ObjectIds.Length;
                    if (preparation.TotalEntityCount
                        > DrawingIndexContractConstants.MaximumReportedEntities - skipped)
                    {
                        preparation.CountOverflow = true;
                        preparation.EntityBudgetExceeded = true;
                        objectId = ObjectId.Null;
                        space = string.Empty;
                        return false;
                    }

                    preparation.TotalEntityCount += skipped;
                    preparation.EntityBudgetExceeded |= skipped > 0;
                    preparation.CurrentObjectIds = snapshot.ObjectIds;
                    preparation.CurrentObjectIndex = 0;
                    preparation.CurrentSpace = source.Space;
                }

                if (preparation.CurrentObjectIndex < preparation.CurrentObjectIds.Length)
                {
                    objectId = preparation.CurrentObjectIds[preparation.CurrentObjectIndex++];
                    space = preparation.CurrentSpace;
                    return true;
                }

                preparation.ClearCurrentSpace();
                preparation.SpaceIndex++;
            }

            objectId = ObjectId.Null;
            space = string.Empty;
            return false;
        }

        private static DrawingIndexSpaceSnapshot ReadSpaceObjectIds(
            Database database,
            ObjectId recordId,
            int maximumCollected)
        {
            if (recordId.IsNull || recordId.IsErased)
            {
                return DrawingIndexSpaceSnapshot.Empty;
            }

            using (var transaction = database.TransactionManager.StartOpenCloseTransaction())
            {
                var record = transaction.GetObject(
                    recordId,
                    OpenMode.ForRead,
                    false) as BlockTableRecord;
                if (record == null)
                {
                    return DrawingIndexSpaceSnapshot.Empty;
                }

                var objectIds = new List<ObjectId>(Math.Min(maximumCollected, 4096));
                var total = 0;
                using (var enumerator = record.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        if (total >= DrawingIndexContractConstants.MaximumReportedEntities)
                        {
                            return new DrawingIndexSpaceSnapshot(
                                objectIds.ToArray(),
                                total,
                                true);
                        }

                        total++;
                        if (objectIds.Count < maximumCollected)
                        {
                            objectIds.Add(enumerator.Current);
                        }
                    }
                }

                return new DrawingIndexSpaceSnapshot(
                    objectIds.ToArray(),
                    total,
                    false);
            }
        }

        private static void AddScanItem(
            DrawingIndexPreparationState preparation,
            ObjectId objectId,
            string space)
        {
            if (objectId.IsNull)
            {
                return;
            }
            if (preparation.TotalEntityCount >= DrawingIndexContractConstants.MaximumReportedEntities)
            {
                preparation.CountOverflow = true;
                preparation.EntityBudgetExceeded = true;
                return;
            }

            if (preparation.Items.Count < DrawingIndexContractConstants.MaximumIndexedEntities)
            {
                if (!preparation.SeenObjectIds.Add(objectId))
                {
                    return;
                }

                var objectToken = CadQueryEntityTokens.Create(preparation.Items.Count + 1);
                preparation.Items.Add(new DrawingIndexScanItem(objectId, objectToken, space));
            }
            else
            {
                preparation.EntityBudgetExceeded = true;
            }

            preparation.TotalEntityCount++;
        }

        private static CadQueryEntity ReadItem(
            Transaction transaction,
            DrawingIndexScanItem item,
            DrawingIndexBlockDefinitionSummaryCache<ObjectId> blockDefinitionCache,
            DrawingIndexReadBudget readBudget)
        {
            if (item.ObjectId.IsNull || item.ObjectId.IsErased)
            {
                return DrawingIndexEntityReader.ReadFailed(item.ObjectToken, item.Space);
            }

            Entity entity = null;
            try
            {
                entity = transaction.GetObject(
                    item.ObjectId,
                    OpenMode.ForRead,
                    false) as Entity;
                return entity == null
                    ? DrawingIndexEntityReader.ReadFailed(item.ObjectToken, item.Space)
                    : DrawingIndexEntityReader.Read(
                        transaction,
                        entity,
                        item.Space,
                        item.ObjectToken,
                        blockDefinitionCache,
                        readBudget);
            }
            catch (Exception exception)
                when (exception is Autodesk.AutoCAD.Runtime.Exception
                      || exception is ArgumentException
                      || exception is InvalidOperationException
                      || exception is OverflowException
                      || exception is NullReferenceException)
            {
                return entity == null
                    ? DrawingIndexEntityReader.ReadFailed(item.ObjectToken, item.Space)
                    : DrawingIndexEntityReader.ReadFailed(
                        entity,
                        item.ObjectToken,
                        item.Space);
            }
        }

        private static void UpdateProgress(DrawingIndexBuildSession session)
        {
            descriptor.Status = DrawingIndexStatuses.Scanning;
            descriptor.EntityCount = session.TotalEntityCount;
            descriptor.IndexedEntityCount = session.Accumulator.Count;
            descriptor.UnsupportedEntityCount = session.Accumulator.UnsupportedCount;
            descriptor.FailedEntityCount = session.Accumulator.FailedCount;
            descriptor.ProgressPercent = CalculateProgress(
                session.Accumulator.Count,
                session.TotalEntityCount);
            descriptor.EstimatedManagedBytes = session.Accumulator.EstimatedBytes;
            NotifyPalette();
        }

        private static void FinalizeSession(
            DrawingIndexBuildSession session,
            string status,
            bool complete,
            bool limited,
            string reason)
        {
            if (!ReferenceEquals(activeSession, session))
            {
                return;
            }

            activeSession = null;
            DetachIdle();
            session.PerformanceMetrics.CompleteScan(session.ScanTimer.Elapsed);
            session.Dispose();
            InvalidatePublishedAgentSnapshot();
            descriptor.Status = status;
            descriptor.Complete = complete;
            descriptor.Limited = limited;
            descriptor.EntityCount = session.TotalEntityCount;
            descriptor.IndexedEntityCount = session.Accumulator.Count;
            descriptor.UnsupportedEntityCount = session.Accumulator.UnsupportedCount;
            descriptor.FailedEntityCount = session.Accumulator.FailedCount;
            descriptor.ProgressPercent = complete
                ? 100
                : CalculateProgress(session.Accumulator.Count, session.TotalEntityCount);
            descriptor.EstimatedManagedBytes = session.Accumulator.EstimatedBytes;
            descriptor.CompletedAtUtc = FormatTimestamp(DateTimeOffset.UtcNow);
            descriptor.LimitReason = reason ?? string.Empty;
            descriptor.TypeCounts = session.Accumulator.SnapshotTypeCounts();
            descriptor.LayerCounts = session.Accumulator.SnapshotLayerCounts();
            descriptor.SpaceCounts = session.Accumulator.SnapshotSpaceCounts();
            descriptor.BlockCounts = session.Accumulator.SnapshotBlockCounts();
            readIssues = session.Accumulator.SnapshotReadIssues();
            publishedEntities = session.Accumulator.FreezeEntities();

            var failures = DrawingIndexContractValidator.Validate(descriptor);
            if (failures.Length != 0)
            {
                descriptor.Status = DrawingIndexStatuses.Failed;
                descriptor.Complete = false;
                descriptor.Limited = false;
                descriptor.LimitReason = "descriptor_invalid";
                publishedEntities = EmptyEntities;
            }

            if (descriptor.Status == DrawingIndexStatuses.Stale
                || descriptor.Status == DrawingIndexStatuses.Cancelled
                || descriptor.Status == DrawingIndexStatuses.Failed)
            {
                publishedEntities = EmptyEntities;
                lastQueryRequest = null;
                lastQueryResponse = null;
                DetachDatabaseEvents();
            }
            else if (descriptor.Status == DrawingIndexStatuses.Ready
                     || descriptor.Status == DrawingIndexStatuses.Partial
                     || descriptor.Status == DrawingIndexStatuses.Limited)
            {
                var validity = new DrawingIndexSnapshotValidity();
                try
                {
                    publishedAgentSnapshot =
                        DrawingIndexAgentSnapshot.CreateFromOwnedFrozenEntities(
                            generation,
                            descriptor,
                            publishedEntities,
                            validity,
                            session.PerformanceMetrics);
                    publishedAgentSnapshotValidity = validity;
                }
                catch (DrawingIndexQueryException)
                {
                    validity.Invalidate();
                    descriptor.Status = DrawingIndexStatuses.Failed;
                    descriptor.Complete = false;
                    descriptor.Limited = false;
                    descriptor.LimitReason = "agent_snapshot_invalid";
                    publishedEntities = EmptyEntities;
                    lastQueryRequest = null;
                    lastQueryResponse = null;
                    DetachDatabaseEvents();
                }
            }
            NotifyPalette();
        }

        private static void EnsureCurrentRevision()
        {
            if (string.IsNullOrEmpty(descriptor.IndexId)
                || descriptor.Status == DrawingIndexStatuses.Stale
                || descriptor.Status == DrawingIndexStatuses.Cancelled
                || descriptor.Status == DrawingIndexStatuses.Failed)
            {
                return;
            }
            var document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            if (document == null || observedDatabase == null || !ReferenceEquals(document.Database, observedDatabase))
            {
                Invalidate("document_activated");
                return;
            }
            var metadata = UnifiedReadOnlyContextRuntime.CaptureDocumentMetadata(document);
            if (!string.Equals(metadata.DocumentId, descriptor.DocumentId, StringComparison.Ordinal)
                || metadata.Revision != descriptor.DocumentRevision
                || (descriptor.Scope == DrawingIndexScopes.CurrentSpace
                    && !string.Equals(
                        ReadCurrentSpaceToken(document.Database),
                        indexedCurrentSpace,
                        StringComparison.Ordinal)))
            {
                Invalidate(
                    descriptor.Scope == DrawingIndexScopes.CurrentSpace
                    && !string.Equals(
                        ReadCurrentSpaceToken(document.Database),
                        indexedCurrentSpace,
                        StringComparison.Ordinal)
                        ? "current_space_changed"
                        : "document_revision_changed");
            }
        }

        private static bool IsMetadataCurrent(DrawingIndexBuildSession session)
        {
            var current = UnifiedReadOnlyContextRuntime.CaptureDocumentMetadata(session.Document);
            return DrawingIndexBuildPolicy.IsIdentityCurrent(
                       session.Metadata.DocumentId,
                       session.Metadata.Revision,
                       current.DocumentId,
                       current.Revision)
                   && (session.Scope != DrawingIndexScopes.CurrentSpace
                       || string.Equals(
                           session.CurrentSpaceToken,
                           ReadCurrentSpaceToken(session.Document.Database),
                           StringComparison.Ordinal));
        }

        private static void Invalidate(string reason)
        {
            var session = activeSession;
            if (session != null)
            {
                if (processingIdle)
                {
                    session.RequestInvalidation(reason);
                    return;
                }
                FinalizeSession(session, DrawingIndexStatuses.Stale, false, false, reason);
                return;
            }
            if (string.IsNullOrEmpty(descriptor.IndexId)
                || descriptor.Status == DrawingIndexStatuses.Stale)
            {
                return;
            }

            descriptor.Status = DrawingIndexStatuses.Stale;
            descriptor.Complete = false;
            descriptor.Limited = false;
            descriptor.CompletedAtUtc = FormatTimestamp(DateTimeOffset.UtcNow);
            descriptor.LimitReason = reason ?? "stale";
            InvalidatePublishedAgentSnapshot();
            publishedEntities = EmptyEntities;
            lastQueryRequest = null;
            lastQueryResponse = null;
            DetachDatabaseEvents();
            NotifyPalette();
        }

        private static void DiscardActiveSession()
        {
            var session = activeSession;
            activeSession = null;
            DetachIdle();
            DetachDatabaseEvents();
            if (session != null)
            {
                session.Dispose();
            }
        }

        private static void InvalidatePublishedAgentSnapshot()
        {
            LocalCursorRegistry.Clear();
            var validity = publishedAgentSnapshotValidity;
            publishedAgentSnapshotValidity = null;
            publishedAgentSnapshot = null;
            if (validity != null)
            {
                validity.Invalidate();
            }
        }

        private static void AttachIdle()
        {
            if (idleAttached)
            {
                return;
            }
            AutoCadApplication.Idle += OnIdle;
            idleAttached = true;
        }

        private static void DetachIdle()
        {
            if (!idleAttached)
            {
                return;
            }
            AutoCadApplication.Idle -= OnIdle;
            idleAttached = false;
        }

        private static void AttachDatabaseEvents(Database database)
        {
            if (ReferenceEquals(observedDatabase, database))
            {
                return;
            }
            DetachDatabaseEvents();
            observedDatabase = database;
            observedDatabase.ObjectAppended += OnDatabaseObjectAppended;
            observedDatabase.ObjectModified += OnDatabaseObjectModified;
            observedDatabase.ObjectErased += OnDatabaseObjectErased;
        }

        private static void DetachDatabaseEvents()
        {
            var database = observedDatabase;
            observedDatabase = null;
            if (database == null)
            {
                return;
            }
            database.ObjectAppended -= OnDatabaseObjectAppended;
            database.ObjectModified -= OnDatabaseObjectModified;
            database.ObjectErased -= OnDatabaseObjectErased;
        }

        private static void OnDatabaseObjectAppended(object sender, ObjectEventArgs eventArgs)
        {
            Invalidate("object_appended");
        }

        private static void OnDatabaseObjectModified(object sender, ObjectEventArgs eventArgs)
        {
            Invalidate("object_modified");
        }

        private static void OnDatabaseObjectErased(object sender, ObjectErasedEventArgs eventArgs)
        {
            Invalidate("object_erased");
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs eventArgs)
        {
            var document = eventArgs == null ? null : eventArgs.Document;
            if (document == null || observedDatabase == null || !ReferenceEquals(document.Database, observedDatabase))
            {
                Invalidate("document_activated");
            }
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs eventArgs)
        {
            var document = eventArgs == null ? null : eventArgs.Document;
            if (document != null && observedDatabase != null && ReferenceEquals(document.Database, observedDatabase))
            {
                Invalidate("document_to_be_destroyed");
            }
        }

        private static string ReadSpaceName(
            Transaction transaction,
            ObjectId recordId,
            Database database)
        {
            if (recordId.IsNull || recordId.IsErased)
            {
                return "UNKNOWN";
            }
            var record = transaction.GetObject(recordId, OpenMode.ForRead, false) as BlockTableRecord;
            if (record == null)
            {
                return "UNKNOWN";
            }

            var blockTable = transaction.GetObject(database.BlockTableId, OpenMode.ForRead, false) as BlockTable;
            if (blockTable != null && recordId == blockTable[BlockTableRecord.ModelSpace])
            {
                return "model";
            }
            if (!record.IsLayout || record.LayoutId.IsNull || record.LayoutId.IsErased)
            {
                return "block_definition";
            }

            var layout = transaction.GetObject(record.LayoutId, OpenMode.ForRead, false) as Layout;
            var name = layout == null ? "UNKNOWN" : SanitizeSpaceToken(layout.LayoutName);
            return "layout:" + name;
        }

        private static string ReadCurrentSpaceToken(Database database)
        {
            return database == null
                ? string.Empty
                : DrawingIndexEntityReader.ReadObjectToken(database.CurrentSpaceId);
        }

        private static string SanitizeSpaceToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNKNOWN";
            }
            var builder = new StringBuilder();
            var maximum = DrawingIndexContractConstants.MaximumNameCharacters - "layout:".Length;
            for (var index = 0; index < value.Length && builder.Length < maximum; index++)
            {
                var character = value[index];
                if (!char.IsControl(character) && character != '\0')
                {
                    builder.Append(character);
                }
            }
            var result = builder.ToString().Trim();
            return result.Length == 0 ? "UNKNOWN" : result;
        }

        private static int CalculateProgress(int indexed, int total)
        {
            return DrawingIndexBuildPolicy.CalculateProgress(indexed, total);
        }

        private static bool TryReadDbmod(out int value)
        {
            try
            {
                var raw = AutoCadApplication.GetSystemVariable("DBMOD");
                if (raw == null)
                {
                    value = 0;
                    return false;
                }
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                value = 0;
                return false;
            }
            catch (Exception exception)
                when (exception is FormatException
                      || exception is InvalidCastException
                      || exception is OverflowException)
            {
                value = 0;
                return false;
            }
        }

        private static void NotifyPalette()
        {
            UnifiedPaletteRuntime.UpdateDrawingIndexStatus(BuildPaletteStatus());
        }

        private static string BuildPaletteStatus()
        {
            var current = descriptor;
            var builder = new StringBuilder();
            builder.Append("整图索引：").Append(current.Status);
            if (!string.IsNullOrEmpty(current.IndexId))
            {
                builder.Append(" · ").Append(current.IndexedEntityCount.ToString(CultureInfo.InvariantCulture));
                builder.Append('/').Append(current.EntityCount.ToString(CultureInfo.InvariantCulture));
                builder.Append(" · ").Append(current.ProgressPercent.ToString(CultureInfo.InvariantCulture)).Append('%');
                if (current.UnsupportedEntityCount > 0)
                {
                    builder.Append(" · 占位 ").Append(
                        current.UnsupportedEntityCount.ToString(CultureInfo.InvariantCulture));
                    if (readIssues.TotalCount > 0)
                    {
                        builder.Append("（")
                            .Append(CadReadTypeStatistics.FormatReasonCounts(readIssues))
                            .Append("；")
                            .Append(CadReadTypeStatistics.FormatActualTypeCounts(readIssues, 4))
                            .Append('）');
                    }
                }
            }
            if (lastQueryResponse != null)
            {
                builder.Append(" · 查询 ").Append(lastQueryResponse.Status);
                builder.Append(' ').Append(lastQueryResponse.ReturnedCount.ToString(CultureInfo.InvariantCulture));
                builder.Append('/').Append(lastQueryResponse.TotalMatches.ToString(CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static DrawingIndexDescriptor CreateEmptyDescriptor()
        {
            return new DrawingIndexDescriptor
            {
                IndexId = string.Empty,
                DocumentId = string.Empty,
                DrawingFingerprint = string.Empty,
                DocumentRevision = 0,
                Scope = DrawingIndexScopes.Drawing,
                Status = DrawingIndexStatuses.NotBuilt,
                StartedAtUtc = string.Empty,
            };
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
                    Key = values[index].Key,
                    Count = values[index].Count,
                };
            }
            return result;
        }

        private static CadQueryRequest CloneRequest(CadQueryRequest request)
        {
            return new CadQueryRequest
            {
                Schema = request.Schema,
                SchemaVersion = request.SchemaVersion,
                IndexId = request.IndexId,
                DocumentId = request.DocumentId,
                DocumentRevision = request.DocumentRevision,
                QueryId = request.QueryId,
                Filter = new CadQueryFilter
                {
                    EntityTypes = CloneStrings(request.Filter.EntityTypes),
                    Layers = CloneStrings(request.Filter.Layers),
                    Spaces = CloneStrings(request.Filter.Spaces),
                    BlockNames = CloneStrings(request.Filter.BlockNames),
                    ObjectIds = CloneStrings(request.Filter.ObjectIds),
                    TextContains = request.Filter.TextContains,
                    IncludeUnsupported = request.Filter.IncludeUnsupported,
                    Bounds = request.Filter.Bounds == null
                        ? null
                        : new CadQueryBounds
                        {
                            Minimum = new CadPoint3(
                                request.Filter.Bounds.Minimum.X,
                                request.Filter.Bounds.Minimum.Y,
                                request.Filter.Bounds.Minimum.Z),
                            Maximum = new CadPoint3(
                                request.Filter.Bounds.Maximum.X,
                                request.Filter.Bounds.Maximum.Y,
                                request.Filter.Bounds.Maximum.Z),
                        },
                },
                PageSize = request.PageSize,
                Cursor = request.Cursor,
            };
        }

        private static string[] CloneStrings(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return new string[0];
            }
            return values.ToArray();
        }

        private static string FormatCounts(DrawingIndexCountBucket[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "none";
            }
            var builder = new StringBuilder();
            for (var index = 0; index < values.Length; index++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(values[index].Key).Append('=')
                    .Append(values[index].Count.ToString(CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static bool IsKnownScope(string scope)
        {
            return scope == DrawingIndexScopes.Selection
                   || scope == DrawingIndexScopes.CurrentSpace
                   || scope == DrawingIndexScopes.ModelSpace
                   || scope == DrawingIndexScopes.Layouts
                   || scope == DrawingIndexScopes.Drawing;
        }

        private static string FormatTimestamp(DateTimeOffset value)
        {
            return value.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture);
        }

        private sealed class DrawingIndexBuildSession : IDisposable
        {
            internal DrawingIndexBuildSession(
                Document document,
                CadContextDocumentMetadata metadata,
                string indexId,
                string scope,
                int dbmodBefore,
                DateTimeOffset startedAtUtc,
                DrawingIndexAccumulator accumulator,
                DrawingIndexPerformanceMetrics sourcePerformanceMetrics)
            {
                Document = document;
                Metadata = metadata;
                IndexId = indexId;
                Scope = scope;
                DbmodBefore = dbmodBefore;
                StartedAtUtc = startedAtUtc;
                Accumulator = accumulator;
                PerformanceMetrics = sourcePerformanceMetrics;
                ScanTimer = Stopwatch.StartNew();
                CurrentSpaceToken = ReadCurrentSpaceToken(document.Database);
                BlockDefinitionCache =
                    new DrawingIndexBlockDefinitionSummaryCache<ObjectId>(
                        EqualityComparer<ObjectId>.Default);
            }

            internal Document Document { get; private set; }

            internal CadContextDocumentMetadata Metadata { get; private set; }

            internal string IndexId { get; private set; }

            internal string Scope { get; private set; }

            internal int DbmodBefore { get; private set; }

            internal DateTimeOffset StartedAtUtc { get; private set; }

            internal DrawingIndexAccumulator Accumulator { get; private set; }

            internal DrawingIndexPerformanceMetrics PerformanceMetrics { get; private set; }

            internal Stopwatch ScanTimer { get; private set; }

            internal string CurrentSpaceToken { get; private set; }

            internal DrawingIndexBlockDefinitionSummaryCache<ObjectId>
                BlockDefinitionCache { get; private set; }

            internal DrawingIndexPreparationState Preparation { get; set; }

            internal List<DrawingIndexScanItem> Items { get; set; }

            internal int TotalEntityCount { get; set; }

            internal int NextItemIndex { get; set; }

            internal bool EntityBudgetExceeded;

            internal bool CancelRequested;

            internal string PendingInvalidationReason { get; private set; }

            internal void RequestInvalidation(string reason)
            {
                if (string.IsNullOrEmpty(PendingInvalidationReason))
                {
                    PendingInvalidationReason = string.IsNullOrEmpty(reason) ? "stale" : reason;
                }
            }

            public void Dispose()
            {
                ScanTimer.Stop();
                var preparation = Preparation;
                Preparation = null;
                if (preparation != null)
                {
                    preparation.Dispose();
                }
                BlockDefinitionCache.Clear();
            }
        }

        private sealed class DrawingIndexPreparationState : IDisposable
        {
            private DrawingIndexPreparationState()
            {
            }

            internal List<DrawingIndexScanItem> Items { get; } =
                new List<DrawingIndexScanItem>();

            internal HashSet<ObjectId> SeenObjectIds { get; } =
                new HashSet<ObjectId>();

            internal ObjectId[] SelectionObjectIds { get; private set; }

            internal string SelectionSpace { get; private set; }

            internal int SelectionIndex { get; set; }

            internal DrawingIndexSpaceSource[] SpaceSources { get; private set; }

            internal int SpaceIndex { get; set; }

            internal ObjectId[] CurrentObjectIds { get; set; }

            internal string CurrentSpace { get; set; }

            internal int CurrentObjectIndex { get; set; }

            internal int TotalEntityCount { get; set; }

            internal bool CountOverflow { get; set; }

            internal bool EntityBudgetExceeded { get; set; }

            internal bool Complete { get; set; }

            internal static DrawingIndexPreparationState ForSelection(
                ObjectId[] objectIds,
                string space)
            {
                return new DrawingIndexPreparationState
                {
                    SelectionObjectIds = objectIds ?? new ObjectId[0],
                    SelectionSpace = space ?? "UNKNOWN",
                    SpaceSources = new DrawingIndexSpaceSource[0],
                };
            }

            internal static DrawingIndexPreparationState ForSpaces(
                DrawingIndexSpaceSource[] sources)
            {
                return new DrawingIndexPreparationState
                {
                    SelectionObjectIds = null,
                    SelectionSpace = string.Empty,
                    SpaceSources = sources ?? new DrawingIndexSpaceSource[0],
                };
            }

            internal void ClearCurrentSpace()
            {
                CurrentObjectIds = null;
                CurrentSpace = string.Empty;
                CurrentObjectIndex = 0;
            }

            public void Dispose()
            {
                ClearCurrentSpace();
            }
        }

        private sealed class DrawingIndexSpaceSnapshot
        {
            internal static readonly DrawingIndexSpaceSnapshot Empty =
                new DrawingIndexSpaceSnapshot(new ObjectId[0], 0, false);

            internal DrawingIndexSpaceSnapshot(
                ObjectId[] objectIds,
                int totalEntityCount,
                bool countOverflow)
            {
                ObjectIds = objectIds ?? new ObjectId[0];
                TotalEntityCount = totalEntityCount;
                CountOverflow = countOverflow;
            }

            internal ObjectId[] ObjectIds { get; private set; }

            internal int TotalEntityCount { get; private set; }

            internal bool CountOverflow { get; private set; }
        }

        private sealed class DrawingIndexSpaceSource
        {
            internal DrawingIndexSpaceSource(ObjectId recordId, string space)
            {
                RecordId = recordId;
                Space = space ?? "UNKNOWN";
            }

            internal ObjectId RecordId { get; private set; }

            internal string Space { get; private set; }
        }

        private sealed class DrawingIndexScanItem
        {
            internal DrawingIndexScanItem(
                ObjectId objectId,
                string objectToken,
                string space)
            {
                ObjectId = objectId;
                ObjectToken = string.IsNullOrWhiteSpace(objectToken) ? "0" : objectToken;
                Space = space ?? "UNKNOWN";
            }

            internal ObjectId ObjectId { get; private set; }

            internal string ObjectToken { get; private set; }

            internal string Space { get; private set; }
        }
    }
}

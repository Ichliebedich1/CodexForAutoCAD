using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016;

internal static class DrawingIndexContractsSpecs
{
    internal static void FiftyThousandEntityDescriptorPasses()
    {
        var descriptor = CreateDescriptor(50_000, DrawingIndexStatuses.Ready, complete: true);
        Equal(0, DrawingIndexContractValidator.Validate(descriptor).Length);
    }

    internal static void DescriptorInvariantsFailClosed()
    {
        var descriptor = CreateDescriptor(100, DrawingIndexStatuses.Ready, complete: true);
        descriptor.IndexedEntityCount = 99;
        descriptor.ProgressPercent = 99;
        Contains(DrawingIndexContractValidator.Validate(descriptor), "drawing_ready_count");
        Contains(DrawingIndexContractValidator.Validate(descriptor), "drawing_ready_progress");

        descriptor = CreateDescriptor(100, DrawingIndexStatuses.Limited, complete: false);
        descriptor.Limited = true;
        descriptor.LimitReason = string.Empty;
        Contains(DrawingIndexContractValidator.Validate(descriptor), "drawing_limit_reason_required");

        var request = CreateRequest(CreateDescriptor(1, DrawingIndexStatuses.Ready, true), 10);
        request.Filter.TextContains = "\uD800";
        Contains(DrawingIndexContractValidator.Validate(request), "cad_query_text");
    }

    internal static void FiltersAreCombined()
    {
        var descriptor = CreateDescriptor(4, DrawingIndexStatuses.Ready, complete: true);
        var request = CreateRequest(descriptor, 20);
        request.Filter.EntityTypes = new[] { "blockReference" };
        request.Filter.Layers = new[] { "A-WALL" };
        request.Filter.Spaces = new[] { "model" };
        request.Filter.BlockNames = new[] { "Door" };
        request.Filter.ObjectIds = new[] { "10" };
        request.Filter.TextContains = "fire";
        request.Filter.Bounds = new CadQueryBounds
        {
            Minimum = new CadPoint3(0, 0, 0),
            Maximum = new CadPoint3(10, 10, 10),
        };

        var response = DrawingIndexQueryEngine.Execute(
            descriptor,
            new[]
            {
                Entity("10", "blockReference", "A-WALL", "model", "Door", "Fire rated", 1),
                Entity("11", "blockReference", "A-WALL", "model", "Door", "Fire rated", 100),
                Entity("12", "blockReference", "A-DOOR", "model", "Door", "Fire rated", 1),
                Entity("13", "line", "A-WALL", "model", string.Empty, string.Empty, 1),
            },
            request);

        Equal(CadQueryStatuses.Ok, response.Status);
        Equal(1, response.TotalMatches);
        Equal("10", response.Entities[0].ObjectId);
        Equal(0, DrawingIndexContractValidator.Validate(response).Length);
    }

    internal static void CursorPaginationIsStable()
    {
        var descriptor = CreateDescriptor(5, DrawingIndexStatuses.Ready, complete: true);
        var entities = new[]
        {
            Entity("100", "line"),
            Entity("F", "line"),
            Entity("10", "line"),
            Entity("1A", "line"),
            Entity("1", "line"),
        };
        var request = CreateRequest(descriptor, 2);

        var first = DrawingIndexQueryEngine.Execute(descriptor, entities, request);
        Equal("1", first.Entities[0].ObjectId);
        Equal("F", first.Entities[1].ObjectId);
        True(!string.IsNullOrEmpty(first.NextCursor));
        True(!first.Complete);

        request.Cursor = first.NextCursor;
        var second = DrawingIndexQueryEngine.Execute(descriptor, entities, request);
        Equal("10", second.Entities[0].ObjectId);
        Equal("1A", second.Entities[1].ObjectId);

        request.Cursor = second.NextCursor;
        var third = DrawingIndexQueryEngine.Execute(descriptor, entities, request);
        Equal(1, third.ReturnedCount);
        Equal("100", third.Entities[0].ObjectId);
        Equal(string.Empty, third.NextCursor);
        True(third.Complete);
    }

    internal static void CursorIsBoundToQueryIdentity()
    {
        var descriptor = CreateDescriptor(3, DrawingIndexStatuses.Ready, complete: true);
        var entities = new[] { Entity("1", "line"), Entity("2", "line"), Entity("3", "line") };
        var request = CreateRequest(descriptor, 1);
        var first = DrawingIndexQueryEngine.Execute(descriptor, entities, request);

        request.PageSize = 2;
        request.Cursor = first.NextCursor;
        var exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(descriptor, entities, request));
        Equal("cad_query_cursor_invalid", exception.Code);

        request = CreateRequest(descriptor, 1);
        request.QueryId = "query-other";
        request.Cursor = first.NextCursor;
        exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(descriptor, entities, request));
        Equal("cad_query_cursor_invalid", exception.Code);
    }

    internal static void RevisionMismatchReturnsStale()
    {
        var descriptor = CreateDescriptor(1, DrawingIndexStatuses.Ready, complete: true);
        var request = CreateRequest(descriptor, 10);
        request.DocumentRevision++;
        var response = DrawingIndexQueryEngine.Execute(
            descriptor,
            new[] { Entity("1", "line") },
            request);
        Equal(CadQueryStatuses.Stale, response.Status);
        Equal(0, response.ReturnedCount);
        True(!response.Complete);
    }

    internal static void PartialAndLimitedStayExplicit()
    {
        var partial = CreateDescriptor(10, DrawingIndexStatuses.Partial, complete: false);
        partial.IndexedEntityCount = 2;
        partial.ProgressPercent = 20;
        var partialResponse = DrawingIndexQueryEngine.Execute(
            partial,
            new[] { Entity("1", "line"), Entity("2", "line") },
            CreateRequest(partial, 10));
        Equal(CadQueryStatuses.Partial, partialResponse.Status);
        True(!partialResponse.Complete);

        var limited = CreateDescriptor(10, DrawingIndexStatuses.Limited, complete: false);
        limited.Limited = true;
        limited.LimitReason = "memory_budget";
        limited.IndexedEntityCount = 2;
        limited.ProgressPercent = 20;
        var limitedResponse = DrawingIndexQueryEngine.Execute(
            limited,
            new[] { Entity("1", "line"), Entity("2", "line") },
            CreateRequest(limited, 10));
        Equal(CadQueryStatuses.Limited, limitedResponse.Status);
        True(!limitedResponse.Complete);
    }

    internal static void AccumulatorHonorsMemoryBudget()
    {
        var entity = Entity("1", "line");
        var accumulator = new DrawingIndexAccumulator(2_000);
        True(accumulator.TryAdd(entity));
        var before = accumulator.Count;

        var large = Entity("2", "line");
        large.TextExcerpt = new string('x', DrawingIndexContractConstants.MaximumTextExcerptCharacters);
        while (accumulator.TryAdd(large))
        {
            large.ObjectId = (accumulator.Count + 2).ToString("X");
        }

        True(accumulator.Count >= before);
        True(accumulator.EstimatedBytes <= 2_000);
        Equal(accumulator.Count, accumulator.SnapshotEntities().Length);
    }

    internal static void FiftyThousandEntitiesCanBeQueried()
    {
        const int count = 50_000;
        var descriptor = CreateDescriptor(count, DrawingIndexStatuses.Ready, complete: true);
        var entities = new CadQueryEntity[count];
        for (var index = 0; index < entities.Length; index++)
        {
            entities[index] = Entity(
                (index + 1).ToString("X"),
                index % 5 == 0 ? "circle" : "line",
                index % 2 == 0 ? "A" : "B");
        }

        var request = CreateRequest(descriptor, DrawingIndexContractConstants.MaximumPageSize);
        request.Filter.EntityTypes = new[] { "circle" };
        request.Filter.Layers = new[] { "A" };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var response = DrawingIndexQueryEngine.Execute(descriptor, entities, request);
        timer.Stop();

        Equal(5_000, response.TotalMatches);
        Equal(DrawingIndexContractConstants.MaximumPageSize, response.ReturnedCount);
        True(!string.IsNullOrEmpty(response.NextCursor));
        True(timer.Elapsed < TimeSpan.FromSeconds(5));
    }

    internal static void CompletionPolicyIsFailClosed()
    {
        var ready = DrawingIndexBuildPolicy.Complete(false, false, 0);
        Equal(DrawingIndexStatuses.Ready, ready.Status);
        True(ready.Complete);
        True(!ready.Limited);

        var unsupported = DrawingIndexBuildPolicy.Complete(false, false, 1);
        Equal(DrawingIndexStatuses.Partial, unsupported.Status);
        True(!unsupported.Complete);

        var bucketLimited = DrawingIndexBuildPolicy.Complete(false, true, 0);
        Equal(DrawingIndexStatuses.Limited, bucketLimited.Status);
        True(bucketLimited.Limited);

        var entityLimited = DrawingIndexBuildPolicy.Complete(true, false, 0);
        Equal(DrawingIndexStatuses.Limited, entityLimited.Status);
        Equal("entity_budget", entityLimited.Reason);
        Equal(50, DrawingIndexBuildPolicy.CalculateProgress(25_000, 50_000));
    }

    internal static void IdentityPolicyRejectsStaleIndex()
    {
        True(DrawingIndexBuildPolicy.IsIdentityCurrent("doc-a", 7, "doc-a", 7));
        True(!DrawingIndexBuildPolicy.IsIdentityCurrent("doc-a", 7, "doc-b", 7));
        True(!DrawingIndexBuildPolicy.IsIdentityCurrent("doc-a", 7, "doc-a", 8));
    }

    internal static void DuplicateObjectTokensFailClosed()
    {
        var descriptor = CreateDescriptor(2, DrawingIndexStatuses.Ready, complete: true);
        var response = new CadQueryResponse
        {
            IndexId = descriptor.IndexId,
            DocumentId = descriptor.DocumentId,
            DocumentRevision = descriptor.DocumentRevision,
            QueryId = "query-duplicate-token",
            Status = CadQueryStatuses.Ok,
            Complete = true,
            TotalMatches = 2,
            ReturnedCount = 2,
            Entities = new[] { Entity("A", "line"), Entity("a", "circle") },
        };
        Contains(
            DrawingIndexContractValidator.Validate(response),
            "cad_query_object_id_duplicate");

        var accumulator = new DrawingIndexAccumulator(10_000);
        True(accumulator.TryAdd(Entity("A", "line")));
        var exception = Throws<DrawingIndexQueryException>(
            () => accumulator.TryAdd(Entity("a", "circle")));
        Equal("drawing_index_duplicate_object_id", exception.Code);
        Equal(1, accumulator.Count);
    }

    internal static void ReadIssueStatisticsStayStructuredAndBounded()
    {
        var accumulator = new DrawingIndexAccumulator(2_000_000);
        True(accumulator.TryAdd(UnsupportedEntity(
            "1",
            "Solid3d",
            CadQueryReadStatuses.DataLimited)));
        True(accumulator.TryAdd(UnsupportedEntity(
            "2",
            "solid3d",
            CadQueryReadStatuses.Unsupported)));
        True(accumulator.TryAdd(UnsupportedEntity(
            "3",
            "ProxyEntity",
            CadQueryReadStatuses.ReadFailed)));

        var statistics = accumulator.SnapshotReadIssues();
        Equal(3, statistics.TotalCount);
        Equal(1, statistics.UnknownTypeCount);
        Equal(1, statistics.DataLimitedCount);
        Equal(1, statistics.ReadFailedCount);
        Equal(2, statistics.ActualTypeCounts.Length);
        var summary = CadReadTypeStatistics.FormatSummary(statistics, 8);
        True(summary.IndexOf("三维实体(Solid3d) x2", StringComparison.Ordinal) >= 0);
        True(summary.IndexOf("代理对象(ProxyEntity) x1", StringComparison.Ordinal) >= 0);
        True(summary.IndexOf("A-SECRET", StringComparison.Ordinal) < 0);
        True(summary.IndexOf("object-secret", StringComparison.Ordinal) < 0);

        var unsafeAccumulator = new DrawingIndexAccumulator(100_000);
        True(unsafeAccumulator.TryAdd(UnsupportedEntity(
            "4", "C:/private/drawing.dwg", CadQueryReadStatuses.Unsupported)));
        True(unsafeAccumulator.TryAdd(UnsupportedEntity(
            "5", "/srv/private/model.dwg", CadQueryReadStatuses.ReadFailed)));
        var unsafeSummary = CadReadTypeStatistics.FormatSummary(
            unsafeAccumulator.SnapshotReadIssues(), 8);
        True(unsafeSummary.IndexOf("未知类型(UNKNOWN) x2", StringComparison.Ordinal) >= 0);
        True(unsafeSummary.IndexOf("private", StringComparison.OrdinalIgnoreCase) < 0);
        True(unsafeSummary.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase) < 0);

        var bounded = new DrawingIndexAccumulator(2_000_000);
        for (var index = 0;
             index <= DrawingIndexContractConstants.MaximumCountBuckets;
             index++)
        {
            True(bounded.TryAdd(UnsupportedEntity(
                (index + 1).ToString("X"),
                "CustomType" + index,
                CadQueryReadStatuses.Unsupported)));
        }
        var boundedStatistics = bounded.SnapshotReadIssues();
        Equal(
            DrawingIndexContractConstants.MaximumCountBuckets,
            boundedStatistics.ActualTypeCounts.Length);
        Equal(1, boundedStatistics.UnlistedTypeEntityCount);
        Equal(
            DrawingIndexContractConstants.MaximumCountBuckets + 1,
            boundedStatistics.TotalCount);
    }

    private static DrawingIndexDescriptor CreateDescriptor(
        int count,
        string status,
        bool complete)
    {
        return new DrawingIndexDescriptor
        {
            IndexId = "index-0123456789abcdef",
            DocumentId = "document-0123456789abcdef",
            DrawingFingerprint = new string('a', 64),
            DocumentRevision = 7,
            Scope = DrawingIndexScopes.Drawing,
            Status = status,
            Complete = complete,
            Limited = false,
            EntityCount = count,
            IndexedEntityCount = complete ? count : 0,
            UnsupportedEntityCount = 0,
            FailedEntityCount = 0,
            ProgressPercent = complete ? 100 : 0,
            EstimatedManagedBytes = count * 320L,
            StartedAtUtc = "2026-07-22T00:00:00.000Z",
            CompletedAtUtc = complete ? "2026-07-22T00:00:01.000Z" : string.Empty,
        };
    }

    private static CadQueryRequest CreateRequest(DrawingIndexDescriptor descriptor, int pageSize)
    {
        return new CadQueryRequest
        {
            IndexId = descriptor.IndexId,
            DocumentId = descriptor.DocumentId,
            DocumentRevision = descriptor.DocumentRevision,
            QueryId = "query-0123456789abcdef",
            PageSize = pageSize,
        };
    }

    private static CadQueryEntity Entity(
        string objectId,
        string type,
        string layer = "0",
        string space = "model",
        string block = "",
        string text = "",
        double coordinate = 0)
    {
        return new CadQueryEntity
        {
            ObjectId = objectId,
            EntityType = type,
            ActualType = "AcDb" + type,
            Layer = layer,
            Space = space,
            BlockName = block,
            TextExcerpt = text,
            Bounds = new CadExtents3
            {
                Minimum = new CadPoint3(coordinate, coordinate, coordinate),
                Maximum = new CadPoint3(coordinate + 1, coordinate + 1, coordinate + 1),
            },
            Unsupported = false,
            ReadStatus = CadQueryReadStatuses.Parsed,
        };
    }

    private static CadQueryEntity UnsupportedEntity(
        string objectId,
        string actualType,
        string readStatus)
    {
        return new CadQueryEntity
        {
            ObjectId = objectId,
            EntityType = CadContextEntityTypesV2.Unsupported,
            ActualType = actualType,
            Layer = "A-SECRET",
            Space = "model",
            BlockName = string.Empty,
            TextExcerpt = string.Empty,
            Unsupported = true,
            ReadStatus = readStatus,
        };
    }

    private static void Contains(CadValidationFailure[] failures, string code)
    {
        if (!failures.Any(failure => failure.Code == code))
        {
            throw new InvalidOperationException("Expected validation failure: " + code);
        }
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected exception: " + typeof(T).Name);
    }

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                "Expected " + expected + ", actual " + actual + ".");
        }
    }
}

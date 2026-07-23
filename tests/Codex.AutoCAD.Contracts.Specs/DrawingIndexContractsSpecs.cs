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
        request.Filter.ObjectIds = new[] { Token("10") };
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
            request,
            new DrawingIndexCursorRegistry());

        Equal(CadQueryStatuses.Ok, response.Status);
        Equal(1, response.TotalMatches);
        Equal(Token("10"), response.Entities[0].ObjectId);
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
        var cursorRegistry = new DrawingIndexCursorRegistry();

        var first = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            cursorRegistry);
        Equal(Token("1"), first.Entities[0].ObjectId);
        Equal(Token("F"), first.Entities[1].ObjectId);
        True(!string.IsNullOrEmpty(first.NextCursor));
        True(!first.Complete);

        request.Cursor = first.NextCursor;
        var second = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            cursorRegistry);
        Equal(Token("10"), second.Entities[0].ObjectId);
        Equal(Token("1A"), second.Entities[1].ObjectId);

        request.Cursor = second.NextCursor;
        var third = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            cursorRegistry);
        Equal(1, third.ReturnedCount);
        Equal(Token("100"), third.Entities[0].ObjectId);
        Equal(string.Empty, third.NextCursor);
        True(third.Complete);
    }

    internal static void CursorIsBoundToQueryShapeAcrossRequestIdentities()
    {
        var descriptor = CreateDescriptor(3, DrawingIndexStatuses.Ready, complete: true);
        var entities = new[] { Entity("1", "line"), Entity("2", "line"), Entity("3", "line") };
        var request = CreateRequest(descriptor, 1);
        var cursorRegistry = new DrawingIndexCursorRegistry();
        var first = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            cursorRegistry);

        request.PageSize = 2;
        request.Cursor = first.NextCursor;
        var exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(
                descriptor,
                entities,
                request,
                cursorRegistry));
        Equal("cad_query_cursor_invalid", exception.Code);

        request = CreateRequest(descriptor, 1);
        request.QueryId = "query-other";
        request.Cursor = first.NextCursor;
        var second = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            cursorRegistry);
        Equal(1, second.ReturnedCount);
        Equal(Token("2"), second.Entities[0].ObjectId);

        request = CreateRequest(descriptor, 1);
        request.Filter.EntityTypes = new[] { "circle" };
        request.Cursor = first.NextCursor;
        exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(
                descriptor,
                entities,
                request,
                cursorRegistry));
        Equal("cad_query_cursor_invalid", exception.Code);
    }

    internal static void CursorCannotCrossIndexOrRevision()
    {
        var descriptor = CreateDescriptor(2, DrawingIndexStatuses.Ready, complete: true);
        var entities = new[] { Entity("1", "line"), Entity("2", "line") };
        var cursorRegistry = new DrawingIndexCursorRegistry();
        var firstRequest = CreateRequest(descriptor, 1);
        var first = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            firstRequest,
            cursorRegistry);

        var otherIndex = CreateDescriptor(2, DrawingIndexStatuses.Ready, complete: true);
        otherIndex.IndexId = "index-fedcba9876543210";
        var otherIndexRequest = CreateRequest(otherIndex, 1);
        otherIndexRequest.Cursor = first.NextCursor;
        var exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(
                otherIndex,
                entities,
                otherIndexRequest,
                cursorRegistry));
        Equal("cad_query_cursor_invalid", exception.Code);

        var otherRevision = CreateDescriptor(2, DrawingIndexStatuses.Ready, complete: true);
        otherRevision.DocumentRevision++;
        var otherRevisionRequest = CreateRequest(otherRevision, 1);
        otherRevisionRequest.Cursor = first.NextCursor;
        exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(
                otherRevision,
                entities,
                otherRevisionRequest,
                cursorRegistry));
        Equal("cad_query_cursor_invalid", exception.Code);
    }

    internal static void ForgedCursorOffsetIsRejected()
    {
        var descriptor = CreateDescriptor(4, DrawingIndexStatuses.Ready, complete: true);
        var entities = new[]
        {
            Entity("1", "line"),
            Entity("2", "line"),
            Entity("3", "line"),
            Entity("4", "line"),
        };
        var request = CreateRequest(descriptor, 1);
        var cursorRegistry = new DrawingIndexCursorRegistry();
        var first = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            cursorRegistry);
        request.Cursor = MutateCursor(first.NextCursor);

        var exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(
                descriptor,
                entities,
                request,
                cursorRegistry));
        Equal("cad_query_cursor_invalid", exception.Code);
    }

    internal static void ExpiredCursorIsRejected()
    {
        var now = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var cursorRegistry = new DrawingIndexCursorRegistry(
            TimeSpan.FromSeconds(1),
            () => now,
            () => "dq1_expiry_token");
        var descriptor = CreateDescriptor(2, DrawingIndexStatuses.Ready, complete: true);
        var entities = new[] { Entity("1", "line"), Entity("2", "line") };
        var request = CreateRequest(descriptor, 1);
        var first = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            cursorRegistry);

        now = now.AddSeconds(1);
        request.Cursor = first.NextCursor;
        var exception = Throws<DrawingIndexQueryException>(
            () => DrawingIndexQueryEngine.Execute(
                descriptor,
                entities,
                request,
                cursorRegistry));
        Equal("cad_query_cursor_invalid", exception.Code);
    }

    private static string MutateCursor(string cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            throw new InvalidOperationException("Expected a non-empty cursor.");
        }

        var characters = cursor.ToCharArray();
        var index = characters.Length - 1;
        characters[index] = characters[index] == 'A' ? 'B' : 'A';
        return new string(characters);
    }

    internal static void RevisionMismatchReturnsStale()
    {
        var descriptor = CreateDescriptor(1, DrawingIndexStatuses.Ready, complete: true);
        var request = CreateRequest(descriptor, 10);
        request.DocumentRevision++;
        var response = DrawingIndexQueryEngine.Execute(
            descriptor,
            new[] { Entity("1", "line") },
            request,
            new DrawingIndexCursorRegistry());
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
            CreateRequest(partial, 10),
            new DrawingIndexCursorRegistry());
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
            CreateRequest(limited, 10),
            new DrawingIndexCursorRegistry());
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
        var response = DrawingIndexQueryEngine.Execute(
            descriptor,
            entities,
            request,
            new DrawingIndexCursorRegistry());
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

    internal static void RawHandleShapedObjectTokensFailClosed()
    {
        var descriptor = CreateDescriptor(1, DrawingIndexStatuses.Ready, complete: true);
        var entity = Entity("1A", "line");
        entity.ObjectId = "1A";
        var response = new CadQueryResponse
        {
            IndexId = descriptor.IndexId,
            DocumentId = descriptor.DocumentId,
            DocumentRevision = descriptor.DocumentRevision,
            QueryId = "query-raw-handle-token",
            Status = CadQueryStatuses.Ok,
            Complete = true,
            TotalMatches = 1,
            ReturnedCount = 1,
            Entities = new[] { entity },
        };
        Contains(
            DrawingIndexContractValidator.Validate(response),
            "cad_query_object_id");

        var request = CreateRequest(descriptor, 1);
        request.Filter.ObjectIds = new[] { "1A" };
        Contains(
            DrawingIndexContractValidator.Validate(request),
            "cad_query_object_id");

        True(CadQueryEntityTokens.IsValid(Token("1A")));
        True(!CadQueryEntityTokens.IsValid("1A"));
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
            ObjectId = Token(objectId),
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

    private static string Token(string hexadecimalOrdinal)
    {
        var ordinal = int.Parse(
            hexadecimalOrdinal,
            System.Globalization.NumberStyles.AllowHexSpecifier,
            System.Globalization.CultureInfo.InvariantCulture);
        return CadQueryEntityTokens.Create(ordinal);
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

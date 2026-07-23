using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class DrawingIndexCompletionDecision
    {
        internal DrawingIndexCompletionDecision(
            string status,
            bool complete,
            bool limited,
            string reason)
        {
            Status = status;
            Complete = complete;
            Limited = limited;
            Reason = reason;
        }

        internal string Status { get; private set; }

        internal bool Complete { get; private set; }

        internal bool Limited { get; private set; }

        internal string Reason { get; private set; }
    }

    internal static class DrawingIndexBuildPolicy
    {
        internal static DrawingIndexCompletionDecision Complete(
            bool entityBudgetExceeded,
            bool countBucketsLimited,
            int unsupportedCount)
        {
            if (entityBudgetExceeded)
            {
                return new DrawingIndexCompletionDecision(
                    DrawingIndexStatuses.Limited,
                    false,
                    true,
                    "entity_budget");
            }
            if (countBucketsLimited)
            {
                return new DrawingIndexCompletionDecision(
                    DrawingIndexStatuses.Limited,
                    false,
                    true,
                    "summary_bucket_budget");
            }
            if (unsupportedCount > 0)
            {
                return new DrawingIndexCompletionDecision(
                    DrawingIndexStatuses.Partial,
                    false,
                    false,
                    "unsupported_entities");
            }
            return new DrawingIndexCompletionDecision(
                DrawingIndexStatuses.Ready,
                true,
                false,
                string.Empty);
        }

        internal static bool IsIdentityCurrent(
            string expectedDocumentId,
            long expectedRevision,
            string currentDocumentId,
            long currentRevision)
        {
            return string.Equals(
                       expectedDocumentId,
                       currentDocumentId,
                       StringComparison.Ordinal)
                   && expectedRevision >= 0
                   && expectedRevision == currentRevision;
        }

        internal static int CalculateProgress(int indexed, int total)
        {
            if (indexed < 0 || total < 0)
            {
                throw new ArgumentOutOfRangeException(indexed < 0 ? nameof(indexed) : nameof(total));
            }
            if (total == 0)
            {
                return indexed == 0 ? 0 : 100;
            }
            var value = (long)indexed * 100L / total;
            return (int)Math.Max(0L, Math.Min(100L, value));
        }
    }

    internal sealed class DrawingIndexQueryException : InvalidOperationException
    {
        internal DrawingIndexQueryException(string code, string message)
            : base(message)
        {
            Code = code ?? "cad_query_invalid";
        }

        internal string Code { get; private set; }
    }

    internal sealed class DrawingIndexAccumulator
    {
        private readonly long maximumEstimatedBytes;
        private readonly List<CadQueryEntity> entities = new List<CadQueryEntity>();
        private readonly HashSet<string> objectIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> typeCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> layerCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> spaceCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> blockCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly CadReadIssueAccumulator readIssueAccumulator =
            new CadReadIssueAccumulator();
        private long estimatedBytes;
        private int unsupportedCount;
        private int failedCount;
        private bool countBucketsLimited;

        internal DrawingIndexAccumulator(long maximumEstimatedBytes)
        {
            if (maximumEstimatedBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEstimatedBytes));
            }

            this.maximumEstimatedBytes = maximumEstimatedBytes;
        }

        internal int Count
        {
            get { return entities.Count; }
        }

        internal int UnsupportedCount
        {
            get { return unsupportedCount; }
        }

        internal int FailedCount
        {
            get { return failedCount; }
        }

        internal long EstimatedBytes
        {
            get { return estimatedBytes; }
        }

        internal bool CountBucketsLimited
        {
            get
            {
                return countBucketsLimited;
            }
        }

        internal bool TryAdd(CadQueryEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var clone = DrawingIndexQueryEngine.CloneEntity(entity);
            if (!objectIds.Add(clone.ObjectId))
            {
                throw new DrawingIndexQueryException(
                    "drawing_index_duplicate_object_id",
                    "DrawingIndex实体令牌不能重复。");
            }
            var estimate = EstimateBytes(clone);
            if (estimate > maximumEstimatedBytes - estimatedBytes)
            {
                objectIds.Remove(clone.ObjectId);
                return false;
            }

            entities.Add(clone);
            estimatedBytes += estimate;
            Increment(typeCounts, clone.EntityType);
            Increment(layerCounts, clone.Layer);
            Increment(spaceCounts, clone.Space);
            if (!string.IsNullOrEmpty(clone.BlockName))
            {
                Increment(blockCounts, clone.BlockName);
            }

            if (clone.Unsupported)
            {
                unsupportedCount++;
                readIssueAccumulator.AddDrawingIndexEntity(clone);
            }
            if (clone.ReadStatus == CadQueryReadStatuses.ReadFailed)
            {
                failedCount++;
            }

            return true;
        }

        internal CadQueryEntity[] SnapshotEntities()
        {
            var result = new CadQueryEntity[entities.Count];
            for (var index = 0; index < entities.Count; index++)
            {
                result[index] = DrawingIndexQueryEngine.CloneEntity(entities[index]);
            }
            return result;
        }

        internal CadQueryEntity[] FreezeEntities()
        {
            return entities.ToArray();
        }

        internal DrawingIndexCountBucket[] SnapshotTypeCounts()
        {
            return SnapshotCounts(typeCounts);
        }

        internal DrawingIndexCountBucket[] SnapshotLayerCounts()
        {
            return SnapshotCounts(layerCounts);
        }

        internal DrawingIndexCountBucket[] SnapshotSpaceCounts()
        {
            return SnapshotCounts(spaceCounts);
        }

        internal DrawingIndexCountBucket[] SnapshotBlockCounts()
        {
            return SnapshotCounts(blockCounts);
        }

        internal CadReadIssueSnapshot SnapshotReadIssues()
        {
            return readIssueAccumulator.Snapshot();
        }

        private void Increment(Dictionary<string, int> counts, string key)
        {
            var safeKey = string.IsNullOrWhiteSpace(key) ? "UNKNOWN" : key;
            int current;
            if (counts.TryGetValue(safeKey, out current))
            {
                counts[safeKey] = current == int.MaxValue ? int.MaxValue : current + 1;
                return;
            }
            if (counts.Count >= DrawingIndexContractConstants.MaximumCountBuckets)
            {
                countBucketsLimited = true;
                return;
            }
            counts.Add(safeKey, 1);
        }

        private static DrawingIndexCountBucket[] SnapshotCounts(
            Dictionary<string, int> counts)
        {
            return counts
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(DrawingIndexContractConstants.MaximumCountBuckets)
                .Select(pair => new DrawingIndexCountBucket
                {
                    Key = pair.Key,
                    Count = pair.Value,
                })
                .ToArray();
        }

        private static long EstimateBytes(CadQueryEntity entity)
        {
            const long fixedOverhead = 256L;
            var characters = (long)(entity.ObjectId ?? string.Empty).Length
                             + (entity.EntityType ?? string.Empty).Length
                             + (entity.ActualType ?? string.Empty).Length
                             + (entity.Layer ?? string.Empty).Length
                             + (entity.Space ?? string.Empty).Length
                             + (entity.BlockName ?? string.Empty).Length
                             + (entity.TextExcerpt ?? string.Empty).Length
                             + (entity.ReadStatus ?? string.Empty).Length
                             + EstimateBlockDetailCharacters(entity.BlockDetails);
            return fixedOverhead + (characters * 2L) + (entity.Bounds == null ? 0L : 64L);
        }

        private static long EstimateBlockDetailCharacters(CadQueryBlockDetails? details)
        {
            if (details == null)
            {
                return 0L;
            }

            long characters = 96L
                              + (details.DetailStatus ?? string.Empty).Length
                              + (details.LayoutName ?? string.Empty).Length
                              + (details.LayoutKind ?? string.Empty).Length;
            var attributes = details.Attributes ?? new CadQueryBlockAttribute[0];
            var properties = details.DynamicProperties ?? new CadQueryDynamicBlockProperty[0];
            for (var index = 0; index < attributes.Length; index++)
            {
                var attribute = attributes[index];
                if (attribute != null)
                {
                    characters += (attribute.Tag ?? string.Empty).Length
                                  + (attribute.Value ?? string.Empty).Length
                                  + 24L;
                }
            }
            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (property != null)
                {
                    characters += (property.Name ?? string.Empty).Length
                                  + (property.ValueKind ?? string.Empty).Length
                                  + (property.Value ?? string.Empty).Length
                                  + 24L;
                }
            }
            return characters;
        }
    }

    internal sealed class DrawingIndexCursorRegistry
    {
        private const int MaximumEntries = 4096;
        private const int MaximumTokenAttempts = 16;
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

        private readonly object sync = new object();
        private readonly Dictionary<string, CursorEntry> entries =
            new Dictionary<string, CursorEntry>(StringComparer.Ordinal);
        private readonly TimeSpan lifetime;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> tokenFactory;

        internal DrawingIndexCursorRegistry()
            : this(DefaultLifetime, () => DateTimeOffset.UtcNow, CreateToken)
        {
        }

        internal DrawingIndexCursorRegistry(
            TimeSpan cursorLifetime,
            Func<DateTimeOffset> clock,
            Func<string> createToken)
        {
            if (cursorLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cursorLifetime));
            }
            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }
            if (createToken == null)
            {
                throw new ArgumentNullException(nameof(createToken));
            }

            lifetime = cursorLifetime;
            utcNow = clock;
            tokenFactory = createToken;
        }

        internal string Issue(
            string indexId,
            long documentRevision,
            string queryFingerprint,
            int offset)
        {
            if (string.IsNullOrEmpty(indexId)
                || string.IsNullOrEmpty(queryFingerprint)
                || documentRevision < 0
                || offset < 0)
            {
                throw new ArgumentException("查询游标绑定参数无效。");
            }

            lock (sync)
            {
                var now = utcNow();
                RemoveExpired(now);
                if (entries.Count >= MaximumEntries)
                {
                    throw new DrawingIndexQueryException(
                        "cad_query_cursor_capacity",
                        "查询游标容量已满；请重新建立索引或稍后重试。");
                }

                for (var attempt = 0; attempt < MaximumTokenAttempts; attempt++)
                {
                    var token = tokenFactory();
                    if (!IsValidToken(token) || entries.ContainsKey(token))
                    {
                        continue;
                    }

                    entries.Add(
                        token,
                        new CursorEntry(
                            indexId,
                            documentRevision,
                            queryFingerprint,
                            offset,
                            now.Add(lifetime)));
                    return token;
                }
            }

            throw new DrawingIndexQueryException(
                "cad_query_cursor_generation",
                "无法生成唯一查询游标。");
        }

        internal int Resolve(
            string token,
            string indexId,
            long documentRevision,
            string queryFingerprint)
        {
            if (!IsValidToken(token))
            {
                throw InvalidCursor();
            }

            lock (sync)
            {
                var now = utcNow();
                RemoveExpired(now);
                CursorEntry? entry;
                if (!entries.TryGetValue(token, out entry)
                    || entry == null
                    || !string.Equals(entry.IndexId, indexId, StringComparison.Ordinal)
                    || entry.DocumentRevision != documentRevision
                    || !string.Equals(
                        entry.QueryFingerprint,
                        queryFingerprint,
                        StringComparison.Ordinal))
                {
                    throw InvalidCursor();
                }

                return entry.Offset;
            }
        }

        internal void Clear()
        {
            lock (sync)
            {
                entries.Clear();
            }
        }

        private void RemoveExpired(DateTimeOffset now)
        {
            if (entries.Count == 0)
            {
                return;
            }

            var expired = new List<string>();
            foreach (var pair in entries)
            {
                if (pair.Value.ExpiresAtUtc <= now)
                {
                    expired.Add(pair.Key);
                }
            }
            for (var index = 0; index < expired.Count; index++)
            {
                entries.Remove(expired[index]);
            }
        }

        private static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token)
                || token.Length > DrawingIndexContractConstants.MaximumCursorCharacters
                || !token.StartsWith("dq1_", StringComparison.Ordinal))
            {
                return false;
            }
            for (var index = 0; index < token.Length; index++)
            {
                var character = token[index];
                if (!(character >= 'A' && character <= 'Z')
                    && !(character >= 'a' && character <= 'z')
                    && !(character >= '0' && character <= '9')
                    && character != '-'
                    && character != '_')
                {
                    return false;
                }
            }
            return true;
        }

        private static string CreateToken()
        {
            var bytes = new byte[24];
            try
            {
                using (var random = RandomNumberGenerator.Create())
                {
                    random.GetBytes(bytes);
                }
                return "dq1_" + Convert.ToBase64String(bytes)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static DrawingIndexQueryException InvalidCursor()
        {
            return new DrawingIndexQueryException(
                "cad_query_cursor_invalid",
                "查询游标无效、被篡改、已过期或不属于当前查询。");
        }

        private sealed class CursorEntry
        {
            internal CursorEntry(
                string indexId,
                long documentRevision,
                string queryFingerprint,
                int offset,
                DateTimeOffset expiresAtUtc)
            {
                IndexId = indexId;
                DocumentRevision = documentRevision;
                QueryFingerprint = queryFingerprint;
                Offset = offset;
                ExpiresAtUtc = expiresAtUtc;
            }

            internal string IndexId { get; private set; }

            internal long DocumentRevision { get; private set; }

            internal string QueryFingerprint { get; private set; }

            internal int Offset { get; private set; }

            internal DateTimeOffset ExpiresAtUtc { get; private set; }
        }
    }

    internal static class DrawingIndexQueryEngine
    {
        internal static CadQueryResponse Execute(
            DrawingIndexDescriptor descriptor,
            IReadOnlyList<CadQueryEntity> entities,
            CadQueryRequest request,
            DrawingIndexCursorRegistry cursorRegistry)
        {
            return Execute(
                descriptor,
                entities,
                request,
                cursorRegistry,
                CancellationToken.None);
        }

        internal static CadQueryResponse Execute(
            DrawingIndexDescriptor descriptor,
            IReadOnlyList<CadQueryEntity> entities,
            CadQueryRequest request,
            DrawingIndexCursorRegistry cursorRegistry,
            CancellationToken cancellationToken)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (entities == null)
            {
                throw new ArgumentNullException(nameof(entities));
            }
            if (cursorRegistry == null)
            {
                throw new ArgumentNullException(nameof(cursorRegistry));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var requestFailures = DrawingIndexContractValidator.Validate(request);
            if (requestFailures.Length != 0)
            {
                throw new DrawingIndexQueryException(
                    requestFailures[0].Code,
                    requestFailures[0].Message);
            }

            if (!string.Equals(request.IndexId, descriptor.IndexId, StringComparison.Ordinal)
                || !string.Equals(request.DocumentId, descriptor.DocumentId, StringComparison.Ordinal)
                || request.DocumentRevision != descriptor.DocumentRevision)
            {
                return CreateTerminalResponse(
                    descriptor,
                    request,
                    CadQueryStatuses.Stale,
                    "查询绑定的索引或图纸修订已失效。");
            }

            if (descriptor.Status == DrawingIndexStatuses.Stale)
            {
                return CreateTerminalResponse(
                    descriptor,
                    request,
                    CadQueryStatuses.Stale,
                    "DrawingIndex已因图纸变化而失效。");
            }
            if (descriptor.Status == DrawingIndexStatuses.Cancelled)
            {
                return CreateTerminalResponse(
                    descriptor,
                    request,
                    CadQueryStatuses.Cancelled,
                    "DrawingIndex构建已取消。");
            }
            if (descriptor.Status == DrawingIndexStatuses.Failed
                || descriptor.Status == DrawingIndexStatuses.NotBuilt
                || descriptor.Status == DrawingIndexStatuses.Preparing
                || descriptor.Status == DrawingIndexStatuses.Scanning)
            {
                return CreateTerminalResponse(
                    descriptor,
                    request,
                    CadQueryStatuses.Failed,
                    "DrawingIndex尚不可查询。");
            }

            var fingerprint = ComputeFingerprint(request);
            var offset = string.IsNullOrEmpty(request.Cursor)
                ? 0
                : cursorRegistry.Resolve(
                    request.Cursor,
                    descriptor.IndexId,
                    descriptor.DocumentRevision,
                    fingerprint);
            var matches = new List<CadQueryEntity>();
            for (var index = 0; index < entities.Count; index++)
            {
                if ((index & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var entity = entities[index];
                if (Matches(entity, request.Filter))
                {
                    matches.Add(entity);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            matches.Sort(CompareEntities);
            cancellationToken.ThrowIfCancellationRequested();
            if (offset < 0 || offset > matches.Count)
            {
                throw new DrawingIndexQueryException(
                    "cad_query_cursor_range",
                    "查询游标超出匹配结果范围。");
            }

            var returned = Math.Min(request.PageSize, matches.Count - offset);
            var page = new CadQueryEntity[returned];
            for (var index = 0; index < returned; index++)
            {
                if ((index & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                page[index] = CloneEntity(matches[offset + index]);
            }

            var nextOffset = offset + returned;
            var nextCursor = nextOffset < matches.Count
                ? cursorRegistry.Issue(
                    descriptor.IndexId,
                    descriptor.DocumentRevision,
                    fingerprint,
                    nextOffset)
                : string.Empty;
            var response = new CadQueryResponse
            {
                IndexId = descriptor.IndexId,
                DocumentId = descriptor.DocumentId,
                DocumentRevision = descriptor.DocumentRevision,
                QueryId = request.QueryId,
                Status = QueryStatus(descriptor),
                Complete = descriptor.Complete && string.IsNullOrEmpty(nextCursor),
                TotalMatches = matches.Count,
                ReturnedCount = page.Length,
                Entities = page,
                NextCursor = nextCursor,
                Message = descriptor.Complete
                    ? string.Empty
                    : "结果来自不完整或受限的DrawingIndex。",
            };

            var responseFailures = DrawingIndexContractValidator.Validate(response);
            if (responseFailures.Length != 0)
            {
                throw new DrawingIndexQueryException(
                    "cad_query_response_invalid",
                    "内部查询响应未通过冻结契约。" + responseFailures[0].Code);
            }

            return response;
        }

        internal static CadQueryEntity CloneEntity(CadQueryEntity entity)
        {
            return new CadQueryEntity
            {
                ObjectId = entity.ObjectId ?? string.Empty,
                EntityType = entity.EntityType ?? string.Empty,
                ActualType = entity.ActualType ?? string.Empty,
                Layer = entity.Layer ?? string.Empty,
                Space = entity.Space ?? string.Empty,
                BlockName = entity.BlockName ?? string.Empty,
                BlockDetails = CadQueryBlockDetailsCloner.Clone(entity.BlockDetails),
                TextExcerpt = entity.TextExcerpt ?? string.Empty,
                Bounds = entity.Bounds == null
                    ? null
                    : new CadExtents3
                    {
                        Minimum = ClonePoint(entity.Bounds.Minimum),
                        Maximum = ClonePoint(entity.Bounds.Maximum),
                    },
                Unsupported = entity.Unsupported,
                ReadStatus = entity.ReadStatus ?? string.Empty,
            };
        }

        private static CadQueryResponse CreateTerminalResponse(
            DrawingIndexDescriptor descriptor,
            CadQueryRequest request,
            string status,
            string message)
        {
            return new CadQueryResponse
            {
                IndexId = request.IndexId,
                DocumentId = request.DocumentId,
                DocumentRevision = request.DocumentRevision,
                QueryId = request.QueryId,
                Status = status,
                Complete = false,
                TotalMatches = 0,
                ReturnedCount = 0,
                Entities = new CadQueryEntity[0],
                NextCursor = string.Empty,
                Message = message,
            };
        }

        private static string QueryStatus(DrawingIndexDescriptor descriptor)
        {
            if (descriptor.Status == DrawingIndexStatuses.Limited)
            {
                return CadQueryStatuses.Limited;
            }
            if (descriptor.Status == DrawingIndexStatuses.Partial)
            {
                return CadQueryStatuses.Partial;
            }
            return CadQueryStatuses.Ok;
        }

        private static bool Matches(CadQueryEntity entity, CadQueryFilter filter)
        {
            if (!filter.IncludeUnsupported && entity.Unsupported)
            {
                return false;
            }
            if (!MatchesAny(entity.EntityType, filter.EntityTypes)
                || !MatchesAny(entity.Layer, filter.Layers)
                || !MatchesAny(entity.Space, filter.Spaces)
                || !MatchesAny(entity.BlockName, filter.BlockNames)
                || !MatchesAny(entity.ObjectId, filter.ObjectIds))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(filter.TextContains)
                && (string.IsNullOrEmpty(entity.TextExcerpt)
                    || entity.TextExcerpt.IndexOf(
                        filter.TextContains,
                        StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }
            if (filter.Bounds != null
                && (entity.Bounds == null || !Intersects(entity.Bounds, filter.Bounds)))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesAny(string value, string[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
            {
                return true;
            }
            for (var index = 0; index < candidates.Length; index++)
            {
                if (string.Equals(value, candidates[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Intersects(CadExtents3 entity, CadQueryBounds query)
        {
            return entity.Maximum.X >= query.Minimum.X
                   && entity.Minimum.X <= query.Maximum.X
                   && entity.Maximum.Y >= query.Minimum.Y
                   && entity.Minimum.Y <= query.Maximum.Y
                   && entity.Maximum.Z >= query.Minimum.Z
                   && entity.Minimum.Z <= query.Maximum.Z;
        }

        private static int CompareEntities(CadQueryEntity left, CadQueryEntity right)
        {
            return string.Compare(left.ObjectId, right.ObjectId, StringComparison.Ordinal);
        }

        private static string ComputeFingerprint(CadQueryRequest request)
        {
            var builder = new StringBuilder();
            AppendNormalized(builder, request.Filter.EntityTypes);
            AppendNormalized(builder, request.Filter.Layers);
            AppendNormalized(builder, request.Filter.Spaces);
            AppendNormalized(builder, request.Filter.BlockNames);
            AppendNormalized(builder, request.Filter.ObjectIds);
            builder.Append((request.Filter.TextContains ?? string.Empty).ToUpperInvariant()).Append('|');
            builder.Append(request.Filter.IncludeUnsupported ? '1' : '0').Append('|');
            if (request.Filter.Bounds != null)
            {
                builder.Append(request.Filter.Bounds.Minimum.ToCanonicalString()).Append('|');
                builder.Append(request.Filter.Bounds.Maximum.ToCanonicalString()).Append('|');
            }
            builder.Append(request.PageSize.ToString(CultureInfo.InvariantCulture));

            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    return ToLowerHex(sha256.ComputeHash(bytes));
                }
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static void AppendNormalized(StringBuilder builder, string[] values)
        {
            if (values == null || values.Length == 0)
            {
                builder.Append('|');
                return;
            }
            var normalized = values
                .Select(value => (value ?? string.Empty).ToUpperInvariant())
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            for (var index = 0; index < normalized.Length; index++)
            {
                builder.Append(normalized[index]).Append(',');
            }
            builder.Append('|');
        }

        private static CadPoint3 ClonePoint(CadPoint3 point)
        {
            return new CadPoint3(point.X, point.Y, point.Z);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            const string alphabet = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = alphabet[bytes[index] >> 4];
                characters[(index * 2) + 1] = alphabet[bytes[index] & 0x0F];
            }
            return new string(characters);
        }
    }
}

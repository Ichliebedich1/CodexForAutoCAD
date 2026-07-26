using System;
using System.Collections.Generic;
using System.Diagnostics;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class DrawingIndexReadBudget
    {
        private readonly Func<long> elapsedMilliseconds;
        private readonly int maximumMilliseconds;

        internal DrawingIndexReadBudget(Stopwatch stopwatch, int maximumMilliseconds)
            : this(
                stopwatch == null
                    ? throw new ArgumentNullException(nameof(stopwatch))
                    : new Func<long>(() => stopwatch.ElapsedMilliseconds),
                maximumMilliseconds)
        {
        }

        internal DrawingIndexReadBudget(
            Func<long> elapsedMilliseconds,
            int maximumMilliseconds)
        {
            this.elapsedMilliseconds = elapsedMilliseconds
                ?? throw new ArgumentNullException(nameof(elapsedMilliseconds));
            if (maximumMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMilliseconds));
            }

            this.maximumMilliseconds = maximumMilliseconds;
        }

        internal bool IsExpired
        {
            get { return elapsedMilliseconds() >= maximumMilliseconds; }
        }
    }

    internal static class DrawingIndexBlockReadPolicy
    {
        internal static bool RegisterSummaryItem(
            ref int totalCount,
            int retainedCount,
            int maximumRetainedCount,
            int maximumReportedCount,
            ref bool limited)
        {
            if (totalCount < 0
                || retainedCount < 0
                || maximumRetainedCount < 0
                || maximumReportedCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalCount));
            }

            if (totalCount >= maximumReportedCount)
            {
                limited = true;
                return false;
            }

            totalCount++;
            if (retainedCount >= maximumRetainedCount)
            {
                limited = true;
                return false;
            }

            return true;
        }
    }

    internal sealed class DrawingIndexBlockDefinitionSummary<TDefinitionId>
        where TDefinitionId : notnull
    {
        internal string BlockName { get; set; } = string.Empty;

        internal bool IsExternalReference { get; set; }

        internal bool IsOverlayReference { get; set; }

        internal bool IsAnonymousDefinition { get; set; }

        internal bool IsLayoutDefinition { get; set; }

        internal bool HasAttributeDefinitions { get; set; }

        internal string LayoutName { get; set; } = string.Empty;

        internal string LayoutKind { get; set; } = CadQueryLayoutKinds.None;

        internal TDefinitionId[] NestedDefinitionIds { get; set; } =
            new TDefinitionId[0];

        internal int InspectedEntityCount { get; set; }

        internal bool Limited { get; set; }

        internal bool BudgetExpired { get; set; }

        internal CadQueryBlockDetails CreateDetails(bool isDynamic)
        {
            return new CadQueryBlockDetails
            {
                DetailStatus = Limited
                    ? CadQueryBlockDetailStatuses.Limited
                    : CadQueryBlockDetailStatuses.Complete,
                IsDynamic = isDynamic,
                IsExternalReference = IsExternalReference,
                IsOverlayReference = IsOverlayReference,
                IsAnonymousDefinition = IsAnonymousDefinition,
                IsLayoutDefinition = IsLayoutDefinition,
                HasAttributeDefinitions = HasAttributeDefinitions,
                LayoutName = LayoutName ?? string.Empty,
                LayoutKind = LayoutKind ?? CadQueryLayoutKinds.None,
            };
        }
    }

    internal sealed class DrawingIndexBlockDefinitionSummaryCache<TDefinitionId>
        where TDefinitionId : notnull
    {
        private readonly Dictionary<TDefinitionId, DrawingIndexBlockDefinitionSummary<TDefinitionId>>
            summaries;

        internal DrawingIndexBlockDefinitionSummaryCache(
            IEqualityComparer<TDefinitionId> comparer)
        {
            summaries =
                new Dictionary<TDefinitionId, DrawingIndexBlockDefinitionSummary<TDefinitionId>>(
                    comparer ?? EqualityComparer<TDefinitionId>.Default);
        }

        internal int Count
        {
            get { return summaries.Count; }
        }

        internal bool TryGet(
            TDefinitionId definitionId,
            out DrawingIndexBlockDefinitionSummary<TDefinitionId> summary)
        {
            if (summaries.TryGetValue(definitionId, out var found))
            {
                summary = found;
                return true;
            }

            summary = null!;
            return false;
        }

        internal bool StoreIfReusable(
            TDefinitionId definitionId,
            DrawingIndexBlockDefinitionSummary<TDefinitionId> summary)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }
            if (summary.BudgetExpired)
            {
                return false;
            }

            summaries[definitionId] = summary;
            return true;
        }

        internal void Clear()
        {
            summaries.Clear();
        }
    }

    internal sealed class DrawingIndexNestedBlockSummary
    {
        internal int NestedBlockReferenceCount { get; set; }

        internal int MaximumNestedBlockDepth { get; set; }

        internal int InspectedDefinitionEntityCount { get; set; }

        internal bool Limited { get; set; }
    }

    internal static class DrawingIndexBlockTraversal
    {
        internal static DrawingIndexNestedBlockSummary Traverse<TDefinitionId>(
            TDefinitionId rootDefinitionId,
            Func<TDefinitionId, DrawingIndexBlockDefinitionSummary<TDefinitionId>>
                resolveDefinition,
            Func<bool> budgetExpired,
            int maximumNestedReferences,
            int maximumDepth,
            int maximumInspectedEntities,
            IEqualityComparer<TDefinitionId> comparer)
            where TDefinitionId : notnull
        {
            if (resolveDefinition == null)
            {
                throw new ArgumentNullException(nameof(resolveDefinition));
            }
            if (budgetExpired == null)
            {
                throw new ArgumentNullException(nameof(budgetExpired));
            }
            if (maximumNestedReferences < 0
                || maximumDepth < 1
                || maximumInspectedEntities < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNestedReferences));
            }

            var equalityComparer = comparer ?? EqualityComparer<TDefinitionId>.Default;
            var rootPath = new HashSet<TDefinitionId>(equalityComparer)
            {
                rootDefinitionId,
            };
            var pending = new Queue<TraversalNode<TDefinitionId>>();
            pending.Enqueue(new TraversalNode<TDefinitionId>(
                rootDefinitionId,
                0,
                rootPath));
            var result = new DrawingIndexNestedBlockSummary();

            while (pending.Count != 0)
            {
                if (budgetExpired())
                {
                    result.Limited = true;
                    return result;
                }

                var current = pending.Dequeue();
                var definition = resolveDefinition(current.DefinitionId);
                if (definition == null)
                {
                    result.Limited = true;
                    continue;
                }

                result.Limited |= definition.Limited;
                if (definition.InspectedEntityCount < 0
                    || result.InspectedDefinitionEntityCount
                    > maximumInspectedEntities - definition.InspectedEntityCount)
                {
                    result.Limited = true;
                    return result;
                }

                result.InspectedDefinitionEntityCount += definition.InspectedEntityCount;
                if (definition.IsExternalReference)
                {
                    result.Limited = true;
                    continue;
                }

                var children = definition.NestedDefinitionIds ?? new TDefinitionId[0];
                for (var index = 0; index < children.Length; index++)
                {
                    if (budgetExpired())
                    {
                        result.Limited = true;
                        return result;
                    }
                    if (result.NestedBlockReferenceCount >= maximumNestedReferences)
                    {
                        result.Limited = true;
                        return result;
                    }

                    result.NestedBlockReferenceCount++;
                    var nestedDepth = current.Depth + 1;
                    if (nestedDepth > result.MaximumNestedBlockDepth)
                    {
                        result.MaximumNestedBlockDepth = nestedDepth;
                    }
                    if (nestedDepth >= maximumDepth)
                    {
                        result.Limited = true;
                        continue;
                    }

                    var childDefinitionId = children[index];
                    if (current.Path.Contains(childDefinitionId))
                    {
                        result.Limited = true;
                        continue;
                    }

                    var childPath = new HashSet<TDefinitionId>(
                        current.Path,
                        equalityComparer)
                    {
                        childDefinitionId,
                    };
                    pending.Enqueue(new TraversalNode<TDefinitionId>(
                        childDefinitionId,
                        nestedDepth,
                        childPath));
                }
            }

            return result;
        }

        private sealed class TraversalNode<TDefinitionId>
            where TDefinitionId : notnull
        {
            internal TraversalNode(
                TDefinitionId definitionId,
                int depth,
                HashSet<TDefinitionId> path)
            {
                DefinitionId = definitionId;
                Depth = depth;
                Path = path;
            }

            internal TDefinitionId DefinitionId { get; private set; }

            internal int Depth { get; private set; }

            internal HashSet<TDefinitionId> Path { get; private set; }
        }
    }
}

using System;
using System.Globalization;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// Host-local performance evidence. It is deliberately excluded from the DrawingIndex wire
    /// contract so telemetry additions cannot break older AgentHost or contract consumers.
    /// </summary>
    internal sealed class DrawingIndexPerformanceMetrics
    {
        private readonly object gate = new object();
        private int idleSliceCount;
        private int preparationSliceCount;
        private int readSliceCount;
        private TimeSpan maximumIdleSliceDuration;
        private TimeSpan maximumPreparationSliceDuration;
        private TimeSpan maximumReadSliceDuration;
        private TimeSpan totalScanDuration;
        private int queryCount;
        private TimeSpan lastQueryDuration;
        private TimeSpan maximumQueryDuration;

        internal void RecordIdleSlice(
            bool preparationPhase,
            TimeSpan sliceDuration,
            TimeSpan elapsedScanDuration)
        {
            EnsureNonNegative(sliceDuration, nameof(sliceDuration));
            EnsureNonNegative(elapsedScanDuration, nameof(elapsedScanDuration));
            lock (gate)
            {
                idleSliceCount++;
                maximumIdleSliceDuration = Maximum(maximumIdleSliceDuration, sliceDuration);
                totalScanDuration = Maximum(totalScanDuration, elapsedScanDuration);
                if (preparationPhase)
                {
                    preparationSliceCount++;
                    maximumPreparationSliceDuration = Maximum(
                        maximumPreparationSliceDuration,
                        sliceDuration);
                }
                else
                {
                    readSliceCount++;
                    maximumReadSliceDuration = Maximum(maximumReadSliceDuration, sliceDuration);
                }
            }
        }

        internal void CompleteScan(TimeSpan elapsedScanDuration)
        {
            EnsureNonNegative(elapsedScanDuration, nameof(elapsedScanDuration));
            lock (gate)
            {
                totalScanDuration = Maximum(totalScanDuration, elapsedScanDuration);
            }
        }

        internal void RecordQuery(TimeSpan queryDuration)
        {
            EnsureNonNegative(queryDuration, nameof(queryDuration));
            lock (gate)
            {
                queryCount++;
                lastQueryDuration = queryDuration;
                maximumQueryDuration = Maximum(maximumQueryDuration, queryDuration);
            }
        }

        internal DrawingIndexPerformanceSnapshot Snapshot()
        {
            lock (gate)
            {
                return new DrawingIndexPerformanceSnapshot(
                    idleSliceCount,
                    preparationSliceCount,
                    readSliceCount,
                    maximumIdleSliceDuration,
                    maximumPreparationSliceDuration,
                    maximumReadSliceDuration,
                    totalScanDuration,
                    queryCount,
                    lastQueryDuration,
                    maximumQueryDuration);
            }
        }

        internal static string FormatMilliseconds(TimeSpan duration)
        {
            EnsureNonNegative(duration, nameof(duration));
            return duration.TotalMilliseconds.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static TimeSpan Maximum(TimeSpan left, TimeSpan right)
        {
            return left >= right ? left : right;
        }

        private static void EnsureNonNegative(TimeSpan value, string parameterName)
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    internal sealed class DrawingIndexPerformanceSnapshot
    {
        internal DrawingIndexPerformanceSnapshot(
            int idleSliceCount,
            int preparationSliceCount,
            int readSliceCount,
            TimeSpan maximumIdleSliceDuration,
            TimeSpan maximumPreparationSliceDuration,
            TimeSpan maximumReadSliceDuration,
            TimeSpan totalScanDuration,
            int queryCount,
            TimeSpan lastQueryDuration,
            TimeSpan maximumQueryDuration)
        {
            IdleSliceCount = idleSliceCount;
            PreparationSliceCount = preparationSliceCount;
            ReadSliceCount = readSliceCount;
            MaximumIdleSliceDuration = maximumIdleSliceDuration;
            MaximumPreparationSliceDuration = maximumPreparationSliceDuration;
            MaximumReadSliceDuration = maximumReadSliceDuration;
            TotalScanDuration = totalScanDuration;
            QueryCount = queryCount;
            LastQueryDuration = lastQueryDuration;
            MaximumQueryDuration = maximumQueryDuration;
        }

        internal int IdleSliceCount { get; private set; }

        internal int PreparationSliceCount { get; private set; }

        internal int ReadSliceCount { get; private set; }

        internal TimeSpan MaximumIdleSliceDuration { get; private set; }

        internal TimeSpan MaximumPreparationSliceDuration { get; private set; }

        internal TimeSpan MaximumReadSliceDuration { get; private set; }

        internal TimeSpan TotalScanDuration { get; private set; }

        internal int QueryCount { get; private set; }

        internal TimeSpan LastQueryDuration { get; private set; }

        internal TimeSpan MaximumQueryDuration { get; private set; }
    }
}

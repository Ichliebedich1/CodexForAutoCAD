using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext
{
    internal static class ReadOnlyContextRuntime
    {
        private static ContextRuntimeState state =
            new ContextRuntimeState("not-captured", 0, 0, null, null, null);
        private static bool initialized;
        private static long epoch;
        private static int generation;
        private static int clearCount;
        private static int documentActivatedCount;
        private static int documentToBeDestroyedCount;
        private static string lastClearReason = "none";

        internal static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            AutoCadApplication.DocumentManager.DocumentActivated += OnDocumentActivated;
            AutoCadApplication.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            initialized = true;
        }

        internal static void Terminate()
        {
            if (!initialized)
            {
                return;
            }

            AutoCadApplication.DocumentManager.DocumentActivated -= OnDocumentActivated;
            AutoCadApplication.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            initialized = false;
            ClearInternal("terminated", false, null, null);
        }

        internal static ContextRuntimeState CaptureCurrent()
        {
            Initialize();
            ClearInternal("capture-start", false, null, null);
            var captureEpoch = epoch;

            var document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                return Fail("no-active-document", 0, null, null);
            }

            int dbmodBefore;
            if (!TryReadDbmod(out dbmodBefore))
            {
                return Fail("dbmod-before-unavailable", 0, null, null);
            }

            SelectionCaptureData capture;
            ContextSelectionSnapshot snapshot;
            try
            {
                capture = ReadOnlySelectionCapture.Capture(document);
                snapshot = CanonicalSelectionHash.Build(capture.Entities);
            }
            catch (ContextValidationException exception)
            {
                return Fail("validation-" + exception.Code, 0, dbmodBefore, null);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return Fail("autocad-read-failed", 0, dbmodBefore, null);
            }
            catch (ArgumentException)
            {
                return Fail("capture-rejected", 0, dbmodBefore, null);
            }
            catch (InvalidOperationException)
            {
                return Fail("capture-rejected", 0, dbmodBefore, null);
            }
            catch (OverflowException)
            {
                return Fail("capture-rejected", 0, dbmodBefore, null);
            }

            int dbmodAfter;
            if (!TryReadDbmod(out dbmodAfter))
            {
                return Fail("dbmod-after-unavailable", capture.SelectedCount, dbmodBefore, null);
            }

            if (dbmodBefore != dbmodAfter)
            {
                return Fail("dbmod-changed", capture.SelectedCount, dbmodBefore, dbmodAfter);
            }

            if (epoch != captureEpoch
                || !ReferenceEquals(AutoCadApplication.DocumentManager.MdiActiveDocument, document))
            {
                return Fail("document-changed-during-capture", capture.SelectedCount, dbmodBefore, dbmodAfter);
            }

            generation++;
            state = new ContextRuntimeState(
                "published-read-only",
                generation,
                capture.SelectedCount,
                dbmodBefore,
                dbmodAfter,
                snapshot);
            return state;
        }

        internal static void Clear(string reason)
        {
            Initialize();
            int dbmod;
            var available = TryReadDbmod(out dbmod);
            ClearInternal(
                string.IsNullOrEmpty(reason) ? "user-clear" : reason,
                true,
                available ? (int?)dbmod : null,
                available ? (int?)dbmod : null);
        }

        internal static string BuildInfo()
        {
            var current = state;
            var typeCounts = new SortedDictionary<ContextEntityKind, int>();
            if (current.Snapshot != null)
            {
                for (var index = 0; index < current.Snapshot.Entities.Count; index++)
                {
                    var kind = current.Snapshot.Entities[index].Draft.Kind;
                    int count;
                    typeCounts.TryGetValue(kind, out count);
                    typeCounts[kind] = count + 1;
                }
            }

            var builder = new StringBuilder();
            builder.AppendLine("--- Codex AutoCAD 2016 Read-Only Context ---");
            builder.AppendLine("Module version: 1.0.0.0");
            builder.AppendLine("Target API: AutoCAD R20.1 / managed 20.1.0.0");
            builder.Append("Status: ").AppendLine(current.Status);
            builder.Append("Published: ").AppendLine(current.Published ? "true" : "false");
            builder.Append("Generation: ").AppendLine(current.Generation.ToString(CultureInfo.InvariantCulture));
            builder.Append("Selected count: ").AppendLine(current.SelectedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Entity types: ").AppendLine(FormatTypeCounts(typeCounts));
            builder.Append("Selection hash: ").AppendLine(
                current.Snapshot == null ? "unavailable" : current.Snapshot.SnapshotHash);
            builder.Append("Canonical bytes: ").AppendLine(
                current.Snapshot == null
                    ? "0"
                    : current.Snapshot.CanonicalLength.ToString(CultureInfo.InvariantCulture));
            builder.Append("DBMOD before: ").AppendLine(FormatNullable(current.DbmodBefore));
            builder.Append("DBMOD after: ").AppendLine(FormatNullable(current.DbmodAfter));
            builder.Append("DBMOD unchanged: ").AppendLine(current.DbmodUnchanged ? "true" : "false");
            builder.Append("Clear count: ").AppendLine(clearCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Last clear reason: ").AppendLine(lastClearReason);
            builder.Append("Anonymous DocumentActivated events: ").AppendLine(documentActivatedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Anonymous DocumentToBeDestroyed events: ").AppendLine(documentToBeDestroyedCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Document name/path capture: disabled");
            builder.AppendLine("Agent/IPC: disabled");
            builder.AppendLine("CAD write: disabled");
            builder.AppendLine("Automatic save: disabled");
            builder.Append("--- End Read-Only Context ---");
            return builder.ToString();
        }

        private static ContextRuntimeState Fail(
            string status,
            int selectedCount,
            int? dbmodBefore,
            int? dbmodAfter)
        {
            epoch++;
            state = new ContextRuntimeState(
                status,
                generation,
                selectedCount,
                dbmodBefore,
                dbmodAfter,
                null);
            return state;
        }

        private static void ClearInternal(
            string reason,
            bool countClear,
            int? dbmodBefore,
            int? dbmodAfter)
        {
            epoch++;
            if (countClear)
            {
                clearCount++;
            }

            lastClearReason = reason;
            state = new ContextRuntimeState(
                "cleared-" + reason,
                generation,
                0,
                dbmodBefore,
                dbmodAfter,
                null);
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
            catch (FormatException)
            {
                value = 0;
                return false;
            }
            catch (InvalidCastException)
            {
                value = 0;
                return false;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        private static string FormatNullable(int? value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "unavailable";
        }

        private static string FormatTypeCounts(
            SortedDictionary<ContextEntityKind, int> typeCounts)
        {
            if (typeCounts.Count == 0)
            {
                return "none";
            }

            var builder = new StringBuilder();
            foreach (var pair in typeCounts)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(pair.Key.ToString());
                builder.Append('=');
                builder.Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs eventArgs)
        {
            documentActivatedCount++;
            ClearInternal("document-activated", true, null, null);
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs eventArgs)
        {
            documentToBeDestroyedCount++;
            ClearInternal("document-to-be-destroyed", true, null, null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Codex.AutoCAD.Contracts;
using Codex.AutoCAD.Host2016.ReadOnlyContext;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016
{
    internal static class UnifiedReadOnlyContextRuntime
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly DocumentContextRegistry Documents = new DocumentContextRegistry();

        private static UnifiedContextState state = UnifiedContextState.Empty("not-captured");
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
            NotifyPalette();
        }

        internal static void Terminate()
        {
            if (initialized)
            {
                AutoCadApplication.DocumentManager.DocumentActivated -= OnDocumentActivated;
                AutoCadApplication.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
                initialized = false;
            }

            ClearInternal("terminated", false, null, null);
            Documents.Dispose();
        }

        internal static UnifiedContextState CaptureCurrent()
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

            SelectionCaptureDataV2 capture;
            V2SelectionSnapshot snapshot;
            CadContextJsonV2 context;
            string canonicalJson;
            string contextSha256;
            int canonicalBytes;
            try
            {
                capture = ReadOnlySelectionCaptureV2.Capture(document);
                snapshot = CanonicalSelectionHashV2.Build(capture.Entities);
                var documentMetadata = Documents.Capture(document);
                context = CadContextJsonV2Mapper.Build(
                    documentMetadata,
                    snapshot,
                    DateTimeOffset.UtcNow);
                canonicalJson = CadContextJsonV2Codec.SerializeCanonical(context);
                canonicalBytes = StrictUtf8.GetByteCount(canonicalJson);
                contextSha256 = CadContextJsonV2Codec.ComputeCanonicalSha256(context);
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
                return Fail(
                    "document-changed-during-capture",
                    capture.SelectedCount,
                    dbmodBefore,
                    dbmodAfter);
            }

            generation++;
            state = new UnifiedContextState(
                "published-read-only-json-v2",
                generation,
                capture.SelectedCount,
                dbmodBefore,
                dbmodAfter,
                snapshot,
                context,
                canonicalJson,
                contextSha256,
                canonicalBytes,
                CadContextJsonV2Mapper.BuildReadableSummary(
                    context,
                    contextSha256,
                    canonicalBytes));
            NotifyPalette();
            return state;
        }

        internal static UnifiedContextState GetCurrentState()
        {
            return state;
        }

        internal static bool IsCurrentPublishedState(UnifiedContextState candidate)
        {
            return candidate != null
                && ReferenceEquals(state, candidate)
                && candidate.Published;
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
            NotifyPalette();
        }

        internal static PaletteContextView GetPaletteView()
        {
            var current = state;
            return new PaletteContextView(
                current.Status,
                current.Published,
                current.SelectedCount,
                CadContextJsonV2Constants.Schema,
                CadContextJsonV2Constants.SchemaVersion,
                current.Context == null ? 0 : current.Context.Selection.ParsedEntityCount,
                current.Context == null ? 0 : current.Context.Selection.UnsupportedEntityCount,
                current.Context != null && current.Context.Selection.Complete,
                current.ContextSha256,
                current.CanonicalBytes,
                current.ReadableSummary,
                current.CanonicalJson);
        }

        internal static string BuildInfo()
        {
            var current = state;
            var typeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            if (current.Snapshot != null)
            {
                for (var index = 0; index < current.Snapshot.Selection.Entities.Length; index++)
                {
                    var kind = current.Snapshot.Selection.Entities[index].EntityType;
                    int count;
                    typeCounts.TryGetValue(kind, out count);
                    typeCounts[kind] = count + 1;
                }
            }

            var builder = new StringBuilder();
            builder.AppendLine("--- Codex AutoCAD 2016 Unified Read-Only Context ---");
            builder.Append("Module version: ").AppendLine(
                typeof(UnifiedReadOnlyContextRuntime).Assembly.GetName().Version.ToString());
            builder.AppendLine("Target API: AutoCAD R20.1 / managed 20.1.0.0");
            builder.Append("Status: ").AppendLine(current.Status);
            builder.Append("Published: ").AppendLine(current.Published ? "true" : "false");
            builder.Append("Generation: ").AppendLine(current.Generation.ToString(CultureInfo.InvariantCulture));
            builder.Append("Selected count: ").AppendLine(current.SelectedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Entity types: ").AppendLine(FormatTypeCounts(typeCounts));
            builder.Append("Selection hash: ").AppendLine(
                current.Snapshot == null ? "unavailable" : current.Snapshot.Selection.SnapshotHash);
            builder.Append("Binary canonical bytes: ").AppendLine(
                current.Snapshot == null
                    ? "0"
                    : current.Snapshot.CanonicalLength.ToString(CultureInfo.InvariantCulture));
            builder.Append("CadContext schema: ").Append(CadContextJsonV2Constants.Schema);
            builder.Append('/').AppendLine(CadContextJsonV2Constants.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append("CadContext JSON SHA-256: ").AppendLine(
                current.Published ? current.ContextSha256 : "unavailable");
            builder.Append("CadContext JSON bytes: ").AppendLine(
                current.CanonicalBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("DBMOD before: ").AppendLine(FormatNullable(current.DbmodBefore));
            builder.Append("DBMOD after: ").AppendLine(FormatNullable(current.DbmodAfter));
            builder.Append("DBMOD unchanged: ").AppendLine(current.DbmodUnchanged ? "true" : "false");
            builder.Append("Clear count: ").AppendLine(clearCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Last clear reason: ").AppendLine(lastClearReason);
            builder.Append("Anonymous DocumentActivated events: ").AppendLine(documentActivatedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Anonymous DocumentToBeDestroyed events: ").AppendLine(documentToBeDestroyedCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Document name/path capture: disabled");
            builder.AppendLine("Palette summary/JSON display: enabled");
            builder.AppendLine("Agent/IPC: disabled");
            builder.AppendLine("CAD write: disabled");
            builder.AppendLine("Plugin-initiated save: disabled");
            builder.AppendLine("AutoCAD SAVETIME setting: not modified");
            builder.Append("--- End Unified Read-Only Context ---");
            return builder.ToString();
        }

        private static UnifiedContextState Fail(
            string status,
            int selectedCount,
            int? dbmodBefore,
            int? dbmodAfter)
        {
            epoch++;
            state = new UnifiedContextState(
                status,
                generation,
                selectedCount,
                dbmodBefore,
                dbmodAfter,
                null,
                null,
                string.Empty,
                string.Empty,
                0,
                "捕获失败：" + status);
            NotifyPalette();
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
            state = new UnifiedContextState(
                "cleared-" + reason,
                generation,
                0,
                dbmodBefore,
                dbmodAfter,
                null,
                null,
                string.Empty,
                string.Empty,
                0,
                "尚未捕获选择上下文。先预选对象，再执行 CODEX16CTX。" );
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
            SortedDictionary<string, int> typeCounts)
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

        private static void NotifyPalette()
        {
            UnifiedPaletteRuntime.UpdateContext(GetPaletteView());
        }

        private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs eventArgs)
        {
            documentActivatedCount++;
            ClearInternal("document-activated", true, null, null);
            NotifyPalette();
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs eventArgs)
        {
            documentToBeDestroyedCount++;
            Documents.Remove(eventArgs == null ? null : eventArgs.Document);
            ClearInternal("document-to-be-destroyed", true, null, null);
            NotifyPalette();
        }
    }

    internal sealed class UnifiedContextState
    {
        internal UnifiedContextState(
            string status,
            int generation,
            int selectedCount,
            int? dbmodBefore,
            int? dbmodAfter,
            V2SelectionSnapshot snapshot,
            CadContextJsonV2 context,
            string canonicalJson,
            string contextSha256,
            int canonicalBytes,
            string readableSummary)
        {
            Status = status;
            Generation = generation;
            SelectedCount = selectedCount;
            DbmodBefore = dbmodBefore;
            DbmodAfter = dbmodAfter;
            Snapshot = snapshot;
            Context = context;
            CanonicalJson = canonicalJson ?? string.Empty;
            ContextSha256 = contextSha256 ?? string.Empty;
            CanonicalBytes = canonicalBytes;
            ReadableSummary = readableSummary ?? string.Empty;
        }

        internal string Status { get; private set; }

        internal int Generation { get; private set; }

        internal int SelectedCount { get; private set; }

        internal int? DbmodBefore { get; private set; }

        internal int? DbmodAfter { get; private set; }

        internal V2SelectionSnapshot Snapshot { get; private set; }

        internal CadContextJsonV2 Context { get; private set; }

        internal string CanonicalJson { get; private set; }

        internal string ContextSha256 { get; private set; }

        internal int CanonicalBytes { get; private set; }

        internal string ReadableSummary { get; private set; }

        internal bool Published
        {
            get { return Snapshot != null && Context != null; }
        }

        internal bool DbmodUnchanged
        {
            get
            {
                return DbmodBefore.HasValue
                    && DbmodAfter.HasValue
                    && DbmodBefore.Value == DbmodAfter.Value;
            }
        }

        internal static UnifiedContextState Empty(string status)
        {
            return new UnifiedContextState(
                status,
                0,
                0,
                null,
                null,
                null,
                null,
                string.Empty,
                string.Empty,
                0,
                "尚未捕获选择上下文。先预选对象，再执行 CODEX16CTX。" );
        }
    }
}

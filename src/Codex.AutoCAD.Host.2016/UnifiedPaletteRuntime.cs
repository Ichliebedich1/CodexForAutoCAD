using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class PaletteContextView
    {
        internal PaletteContextView(
            string status,
            bool published,
            int selectedCount,
            string schema,
            int schemaVersion,
            int parsedEntityCount,
            int unsupportedEntityCount,
            bool complete,
            string readIssueSummary,
            string contextSha256,
            int canonicalBytes,
            string readableSummary,
            string canonicalJson)
        {
            Status = status ?? string.Empty;
            Published = published;
            SelectedCount = selectedCount;
            Schema = schema ?? string.Empty;
            SchemaVersion = schemaVersion;
            ParsedEntityCount = parsedEntityCount;
            UnsupportedEntityCount = unsupportedEntityCount;
            Complete = complete;
            ReadIssueSummary = readIssueSummary ?? string.Empty;
            ContextSha256 = contextSha256 ?? string.Empty;
            CanonicalBytes = canonicalBytes;
            ReadableSummary = readableSummary ?? string.Empty;
            CanonicalJson = canonicalJson ?? string.Empty;
        }

        internal string Status { get; private set; }

        internal bool Published { get; private set; }

        internal int SelectedCount { get; private set; }

        internal string Schema { get; private set; }

        internal int SchemaVersion { get; private set; }

        internal int ParsedEntityCount { get; private set; }

        internal int UnsupportedEntityCount { get; private set; }

        internal bool Complete { get; private set; }

        internal string ReadIssueSummary { get; private set; }

        internal string ContextSha256 { get; private set; }

        internal int CanonicalBytes { get; private set; }

        internal string ReadableSummary { get; private set; }

        internal string CanonicalJson { get; private set; }
    }

    internal static class UnifiedPaletteRuntime
    {
        private static UnifiedPaletteController controller;
        private static string latestDrawingIndexStatus = "整图索引：not_built";
        private static PaletteContextView latestContext = new PaletteContextView(
            "not-captured",
            false,
            0,
            CadContextJsonV2Constants.Schema,
            CadContextJsonV2Constants.SchemaVersion,
            0,
            0,
            false,
            string.Empty,
            string.Empty,
            0,
            "尚未捕获选择上下文。先预选对象，再执行 CODEX16CTX。",
            string.Empty);

        internal static void Show()
        {
            GetOrCreateController().Show();
        }

        internal static string BuildInfo()
        {
            return GetOrCreateController().BuildInfo();
        }

        internal static void ResetAndShow()
        {
            GetOrCreateController().ResetAndShow();
        }

        internal static void UpdateContext(PaletteContextView context)
        {
            latestContext = context ?? latestContext;
            var current = controller;
            if (current != null)
            {
                current.UpdateContext(latestContext);
            }
        }

        internal static void UpdateAgentStatus(string value)
        {
            var current = controller;
            if (current != null)
            {
                current.UpdateAgentStatus(value);
            }
        }

        internal static void UpdateAgentText(string value)
        {
            var current = controller;
            if (current != null)
            {
                current.UpdateAgentText(value);
            }
        }

        internal static void UpdateDrawingIndexStatus(string value)
        {
            latestDrawingIndexStatus = value ?? latestDrawingIndexStatus;
            var current = controller;
            if (current != null)
            {
                current.UpdateDrawingIndexStatus(latestDrawingIndexStatus);
            }
        }

        internal static void Terminate()
        {
            var current = controller;
            controller = null;
            if (current != null)
            {
                current.Dispose();
            }
        }

        private static UnifiedPaletteController GetOrCreateController()
        {
            if (controller == null)
            {
                controller = new UnifiedPaletteController(latestContext);
                controller.UpdateDrawingIndexStatus(latestDrawingIndexStatus);
            }

            return controller;
        }
    }
}

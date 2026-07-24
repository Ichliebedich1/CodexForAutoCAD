using System;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using DrawingSize = System.Drawing.Size;
using WpfSize = System.Windows.Size;

namespace Codex.AutoCAD.Host2016
{
    internal sealed class UnifiedPaletteController : IDisposable
    {
        internal const string PaletteGuidText = "173d39c8-85d9-45fc-845f-e0520f8cddcc";

        private const string PaletteTitle = "Codex for AutoCAD 2016";
        private const string PaletteTabTitle = "Codex 2016";

        private static readonly Guid PaletteGuid = new Guid(PaletteGuidText);

        private readonly DocumentCollection documents;
        private readonly object agentSync = new object();
        private readonly PaletteConversationStore conversationStore = new PaletteConversationStore();
        private readonly Dictionary<long, string> drafts = new Dictionary<long, string>();
        private AgentClientSnapshot agentSnapshot = AgentClientSnapshot.Offline;
        private PaletteSet paletteSet;
        private UnifiedPalettePanel panel;
        private PaletteContextView context;
        private string agentStatus = "Agent 离线；只读模式。";
        private string drawingIndexStatus = "整图索引：not_built";
        private PaletteDrawingIndexView drawingIndexView = PaletteDrawingIndexView.Empty;
        private bool disposed;
        private int generationCount;
        private int resetCount;
        private int releaseCount;
        private int paletteStateChangedCount;
        private int paletteSizeChangedCount;
        private int paletteDestroyEventCount;
        private int documentActivatedCount;
        private int documentToBeDestroyedCount;
        private string lastPaletteState = "none";
        private int lastPhysicalWidth;
        private int lastPhysicalHeight;
        private double lastDeviceIndependentWidth;
        private double lastDeviceIndependentHeight;
        private double lastDpiX;
        private double lastDpiY;

        internal UnifiedPaletteController(PaletteContextView initialContext)
        {
            context = initialContext;
            documents = AutoCadApplication.DocumentManager;
            documents.DocumentActivated += OnDocumentActivated;
            documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        }

        internal void Show()
        {
            EnsureNotDisposed();
            EnsurePalette();
            paletteSet.Visible = true;
            RefreshSizeFromPalette();
            UpdatePanel();
        }

        internal void ResetAndShow()
        {
            EnsureNotDisposed();
            ReleasePalette();
            resetCount++;
            EnsurePalette();
            paletteSet.Visible = true;
            RefreshSizeFromPalette();
            UpdatePanel();
        }

        internal void UpdateContext(PaletteContextView value)
        {
            EnsureNotDisposed();
            context = value ?? context;
            UpdatePanel();
        }

        internal void UpdateAgentStatus(string value)
        {
            string currentStatus;
            lock (agentSync)
            {
                agentStatus = value ?? string.Empty;
                currentStatus = agentStatus;
                RecordSignificantStatusLocked(currentStatus);
            }

            UpdatePanel();
        }

        internal void UpdateAgentText(string value)
        {
            lock (agentSync)
            {
                if (string.IsNullOrEmpty(value))
                {
                    conversationStore.NoteStreamReset();
                }
                else
                {
                    conversationStore.AppendAssistantDelta(value);
                }
            }

            UpdatePanel();
        }

        internal void UpdateAgentSnapshot(AgentClientSnapshot value)
        {
            lock (agentSync)
            {
                agentSnapshot = value ?? AgentClientSnapshot.Offline;
                conversationStore.EnsureEpoch(agentSnapshot.ConversationEpoch);
                if (!agentSnapshot.HasActiveTurn)
                {
                    conversationStore.FinalizeAssistantStream();
                }
            }

            UpdatePanel();
        }

        /// <summary>Records a user prompt the panel just submitted through the real Ask chain.</summary>
        internal void RecordUserPrompt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            lock (agentSync)
            {
                conversationStore.AppendUserMessage(text.Trim());
            }

            UpdatePanel();
        }

        internal void RecordPromptError(string formatted)
        {
            if (string.IsNullOrWhiteSpace(formatted))
            {
                return;
            }

            lock (agentSync)
            {
                conversationStore.AddError(formatted.Trim());
            }

            UpdatePanel();
        }

        internal void SaveDraft(string text)
        {
            lock (agentSync)
            {
                drafts[agentSnapshot.ConversationEpoch] = text ?? string.Empty;
            }
        }

        private void RecordSignificantStatusLocked(string status)
        {
            // Only conversation-shaping Host lines become message entries; transient progress
            // lines stay in the session bar. Prefixes match the Host's own frozen formatters.
            if (string.IsNullOrEmpty(status))
            {
                return;
            }

            if (status.IndexOf("（error_code=", StringComparison.Ordinal) >= 0
                || status.StartsWith("Agent Bridge 已断开", StringComparison.Ordinal))
            {
                conversationStore.AddError(status);
                return;
            }

            if (status.StartsWith("Codex 回答完成", StringComparison.Ordinal)
                || status.StartsWith("Codex 回合已取消", StringComparison.Ordinal))
            {
                conversationStore.AddStatus(status);
            }
        }

        internal void UpdateDrawingIndexStatus(string value, PaletteDrawingIndexView view)
        {
            drawingIndexStatus = value ?? string.Empty;
            drawingIndexView = view ?? PaletteDrawingIndexView.Empty;
            UpdatePanel();
        }

        internal string BuildInfo()
        {
            EnsureNotDisposed();
            RefreshSizeFromPalette();

            var current = paletteSet;
            var created = current != null && !current.IsDisposed;
            var visible = created && current.Visible;
            var dock = created ? current.Dock.ToString() : "not-created";
            var paletteCount = created ? current.Count : 0;

            var builder = new StringBuilder();
            builder.AppendLine("--- Codex AutoCAD 2016 Unified Palette Info ---");
            builder.Append("Module version: ").AppendLine(
                typeof(UnifiedPaletteController).Assembly.GetName().Version.ToString());
            builder.AppendLine("Target API: AutoCAD R20.1 / managed 20.1.0.0");
            builder.Append("Palette GUID: ").AppendLine(PaletteGuidText);
            builder.Append("Created: ").AppendLine(created ? "true" : "false");
            builder.Append("Visible: ").AppendLine(visible ? "true" : "false");
            builder.Append("Dock: ").AppendLine(dock);
            builder.Append("Palette count: ").AppendLine(paletteCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Generation count: ").AppendLine(generationCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Reset count: ").AppendLine(resetCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Release count: ").AppendLine(releaseCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("StateChanged events: ").AppendLine(paletteStateChangedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Last state: ").AppendLine(lastPaletteState);
            builder.Append("SizeChanged events: ").AppendLine(paletteSizeChangedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("PaletteSetDestroy events: ").AppendLine(paletteDestroyEventCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Physical size: ").Append(lastPhysicalWidth.ToString(CultureInfo.InvariantCulture)).Append(" x ").AppendLine(lastPhysicalHeight.ToString(CultureInfo.InvariantCulture));
            builder.Append("DIP size: ").Append(FormatNumber(lastDeviceIndependentWidth)).Append(" x ").AppendLine(FormatNumber(lastDeviceIndependentHeight));
            builder.Append("DPI: ").Append(FormatDpi(lastDpiX)).Append(" x ").AppendLine(FormatDpi(lastDpiY));
            builder.Append("Anonymous DocumentActivated events: ").AppendLine(documentActivatedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Anonymous DocumentToBeDestroyed events: ").AppendLine(documentToBeDestroyedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("DBMOD: ").AppendLine(ReadDbmod());
            builder.Append("Context status: ").AppendLine(context.Status);
            builder.Append("Context published: ").AppendLine(context.Published ? "true" : "false");
            builder.Append("CadContext schema: ").Append(context.Schema).Append('/').AppendLine(
                context.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append("Selected count: ").AppendLine(context.SelectedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Parsed count: ").AppendLine(context.ParsedEntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Unsupported placeholder count: ").AppendLine(context.UnsupportedEntityCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Context complete: ").AppendLine(context.Complete ? "true" : "false");
            builder.Append("CadContext JSON bytes: ").AppendLine(context.CanonicalBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("DrawingIndex: ").AppendLine(drawingIndexStatus);
            builder.AppendLine("Readable summary: enabled");
            builder.AppendLine("Canonical JSON display: enabled");
            string currentAgentStatus;
            lock (agentSync)
            {
                currentAgentStatus = agentStatus;
            }
            builder.Append("Agent: ").AppendLine(currentAgentStatus);
            lock (agentSync)
            {
                builder.Append("Conversation messages: ").AppendLine(
                    conversationStore.Count.ToString(CultureInfo.InvariantCulture));
                builder.Append("Conversation epoch: ").AppendLine(
                    agentSnapshot.ConversationEpoch.ToString(CultureInfo.InvariantCulture));
                builder.Append("Late deltas ignored: ").AppendLine(
                    conversationStore.LateDeltasIgnored.ToString(CultureInfo.InvariantCulture));
            }
            builder.AppendLine("CAD write: disabled");
            builder.AppendLine("Plugin-initiated save: disabled");
            builder.AppendLine("AutoCAD SAVETIME setting: not modified");
            builder.Append("--- End Unified Palette Info ---");
            return builder.ToString();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            documents.DocumentActivated -= OnDocumentActivated;
            documents.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            ReleasePalette();
        }

        private void EnsurePalette()
        {
            if (paletteSet != null && !paletteSet.IsDisposed)
            {
                return;
            }

            var createdPalette = new PaletteSet(PaletteTitle, PaletteGuid);
            try
            {
                createdPalette.Style = PaletteSetStyles.ShowAutoHideButton |
                                       PaletteSetStyles.ShowCloseButton |
                                       PaletteSetStyles.ShowPropertiesMenu |
                                       PaletteSetStyles.ShowTabForSingle;
                createdPalette.DockEnabled = DockSides.Left | DockSides.Right;
                createdPalette.Dock = DockSides.Left;
                createdPalette.KeepFocus = true;
                createdPalette.MinimumSize = new DrawingSize(320, 360);
                createdPalette.DeviceIndependentSize = new WpfSize(520.0, 700.0);

                var createdPanel = new UnifiedPalettePanel();
                createdPalette.AddVisual(PaletteTabTitle, createdPanel, true);

                paletteSet = createdPalette;
                panel = createdPanel;
                generationCount++;
                AttachPaletteEvents(createdPalette);
                RefreshSizeFromPalette();
                UpdatePanel();
            }
            catch
            {
                if (!createdPalette.IsDisposed)
                {
                    createdPalette.Dispose();
                }

                throw;
            }
        }

        private void ReleasePalette()
        {
            var current = paletteSet;
            if (current == null)
            {
                panel = null;
                return;
            }

            paletteSet = null;
            panel = null;
            DetachPaletteEvents(current);
            releaseCount++;

            if (!current.IsDisposed)
            {
                current.Visible = false;
                current.Dispose();
            }
        }

        private void AttachPaletteEvents(PaletteSet current)
        {
            current.StateChanged += OnPaletteStateChanged;
            current.SizeChanged += OnPaletteSizeChanged;
            current.PaletteSetDestroy += OnPaletteSetDestroy;
        }

        private void DetachPaletteEvents(PaletteSet current)
        {
            current.StateChanged -= OnPaletteStateChanged;
            current.SizeChanged -= OnPaletteSizeChanged;
            current.PaletteSetDestroy -= OnPaletteSetDestroy;
        }

        private void OnPaletteStateChanged(object sender, PaletteSetStateEventArgs eventArgs)
        {
            paletteStateChangedCount++;
            lastPaletteState = eventArgs == null ? "null" : eventArgs.NewState.ToString();
            UpdatePanel();
        }

        private void OnPaletteSizeChanged(object sender, PaletteSetSizeEventArgs eventArgs)
        {
            paletteSizeChangedCount++;
            if (eventArgs != null)
            {
                lastPhysicalWidth = eventArgs.Width;
                lastPhysicalHeight = eventArgs.Height;
                lastDeviceIndependentWidth = eventArgs.DeviceIndependentWidth;
                lastDeviceIndependentHeight = eventArgs.DeviceIndependentHeight;
                UpdateDpiFromSizes();
            }

            UpdatePanel();
        }

        private void OnPaletteSetDestroy(object sender, EventArgs eventArgs)
        {
            paletteDestroyEventCount++;
            var destroyed = sender as PaletteSet;
            if (destroyed != null)
            {
                DetachPaletteEvents(destroyed);
            }

            if (ReferenceEquals(paletteSet, destroyed))
            {
                paletteSet = null;
                panel = null;
            }
        }

        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs eventArgs)
        {
            documentActivatedCount++;
            UpdatePanel();
        }

        private void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs eventArgs)
        {
            documentToBeDestroyedCount++;
            UpdatePanel();
        }

        private void RefreshSizeFromPalette()
        {
            var current = paletteSet;
            if (current == null || current.IsDisposed)
            {
                return;
            }

            var physical = current.Size;
            var deviceIndependent = current.DeviceIndependentSize;
            lastPhysicalWidth = physical.Width;
            lastPhysicalHeight = physical.Height;
            lastDeviceIndependentWidth = deviceIndependent.Width;
            lastDeviceIndependentHeight = deviceIndependent.Height;
            UpdateDpiFromSizes();
        }

        private void UpdateDpiFromSizes()
        {
            lastDpiX = lastDeviceIndependentWidth > 0.0
                ? 96.0 * lastPhysicalWidth / lastDeviceIndependentWidth
                : 0.0;
            lastDpiY = lastDeviceIndependentHeight > 0.0
                ? 96.0 * lastPhysicalHeight / lastDeviceIndependentHeight
                : 0.0;
        }

        private void UpdatePanel()
        {
            var currentPanel = panel;
            if (currentPanel == null)
            {
                return;
            }

            var visible = paletteSet != null && !paletteSet.IsDisposed && paletteSet.Visible;
            var metrics = new StringBuilder();
            metrics.Append("实例代数：").Append(generationCount.ToString(CultureInfo.InvariantCulture));
            metrics.Append("  可见：").Append(visible ? "是" : "否");
            metrics.Append("  DPI：").Append(FormatDpi(lastDpiX)).Append(" x ").AppendLine(FormatDpi(lastDpiY));
            metrics.Append("状态/尺寸/销毁事件：");
            metrics.Append(paletteStateChangedCount.ToString(CultureInfo.InvariantCulture));
            metrics.Append(" / ").Append(paletteSizeChangedCount.ToString(CultureInfo.InvariantCulture));
            metrics.Append(" / ").AppendLine(paletteDestroyEventCount.ToString(CultureInfo.InvariantCulture));
            metrics.Append("匿名文档事件 Activated/ToBeDestroyed：");
            metrics.Append(documentActivatedCount.ToString(CultureInfo.InvariantCulture));
            metrics.Append(" / ").Append(documentToBeDestroyedCount.ToString(CultureInfo.InvariantCulture));
            metrics.AppendLine();
            metrics.Append(drawingIndexStatus);
            currentPanel.UpdateMetrics(metrics.ToString());
            currentPanel.UpdateContext(context);
            currentPanel.UpdateDrawingIndex(drawingIndexStatus, drawingIndexView);
            string currentAgentStatus;
            AgentClientSnapshot currentSnapshot;
            IReadOnlyList<PaletteMessage> messages;
            string currentDraft;
            lock (agentSync)
            {
                currentAgentStatus = agentStatus;
                currentSnapshot = agentSnapshot;
                messages = conversationStore.Snapshot();
                string draftValue;
                currentDraft = drafts.TryGetValue(currentSnapshot.ConversationEpoch, out draftValue)
                    ? draftValue
                    : string.Empty;
            }
            currentPanel.UpdateAgentStatus(currentAgentStatus);
            currentPanel.UpdateAgentSnapshot(currentSnapshot);
            currentPanel.SyncMessages(messages);
            currentPanel.SetDraft(currentSnapshot.ConversationEpoch, currentDraft);
        }

        private static string ReadDbmod()
        {
            try
            {
                var value = AutoCadApplication.GetSystemVariable("DBMOD");
                return value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception exception)
            {
                return "unavailable (" + exception.ErrorStatus + ")";
            }
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatDpi(double value)
        {
            return value > 0.0 ? FormatNumber(value) : "unavailable";
        }

        private void EnsureNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(UnifiedPaletteController));
            }
        }
    }
}

using System;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using DrawingSize = System.Drawing.Size;
using WpfSize = System.Windows.Size;

namespace Codex.AutoCAD.Host2016.Palette
{
    internal sealed class PaletteController : IDisposable
    {
        internal const string PaletteGuidText = "173d39c8-85d9-45fc-845f-e0520f8cddcc";

        private const string PaletteTitle = "Codex for AutoCAD 2016";
        private const string PaletteTabTitle = "Codex 2016";

        private static readonly Guid PaletteGuid = new Guid(PaletteGuidText);

        private readonly DocumentCollection documents;

        private PaletteSet paletteSet;
        private CodexPalettePanel panel;
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

        internal PaletteController()
        {
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

        internal string BuildInfo()
        {
            EnsureNotDisposed();
            RefreshSizeFromPalette();

            PaletteSet current = paletteSet;
            bool created = current != null && !current.IsDisposed;
            bool visible = created && current.Visible;
            string dock = created ? current.Dock.ToString() : "not-created";
            int paletteCount = created ? current.Count : 0;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("--- Codex AutoCAD 2016 Palette Info ---");
            builder.AppendLine("Module version: 1.0.0.0");
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
            builder.AppendLine("Agent: disabled");
            builder.AppendLine("Selection read: disabled");
            builder.AppendLine("CAD write: disabled");
            builder.AppendLine("Automatic save: disabled");
            builder.Append("--- End Palette Info ---");
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

            PaletteSet createdPalette = new PaletteSet(PaletteTitle, PaletteGuid);
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
                createdPalette.DeviceIndependentSize = new WpfSize(440.0, 560.0);

                CodexPalettePanel createdPanel = new CodexPalettePanel();
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
            PaletteSet current = paletteSet;
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
            PaletteSet destroyed = sender as PaletteSet;
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
            PaletteSet current = paletteSet;
            if (current == null || current.IsDisposed)
            {
                return;
            }

            DrawingSize physical = current.Size;
            WpfSize deviceIndependent = current.DeviceIndependentSize;
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
            CodexPalettePanel currentPanel = panel;
            if (currentPanel == null)
            {
                return;
            }

            bool visible = paletteSet != null && !paletteSet.IsDisposed && paletteSet.Visible;
            StringBuilder builder = new StringBuilder();
            builder.Append("实例代数：").Append(generationCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  可见：").Append(visible ? "是" : "否");
            builder.Append("  最后状态：").AppendLine(lastPaletteState);
            builder.Append("StateChanged：").Append(paletteStateChangedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  SizeChanged：").Append(paletteSizeChangedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("  Destroy：").AppendLine(paletteDestroyEventCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("DPI：").Append(FormatDpi(lastDpiX)).Append(" x ").AppendLine(FormatDpi(lastDpiY));
            builder.Append("匿名文档事件 Activated/ToBeDestroyed：");
            builder.Append(documentActivatedCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(" / ").Append(documentToBeDestroyedCount.ToString(CultureInfo.InvariantCulture));
            currentPanel.UpdateMetrics(builder.ToString());
        }

        private static string ReadDbmod()
        {
            try
            {
                object value = AutoCadApplication.GetSystemVariable("DBMOD");
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
                throw new ObjectDisposedException(nameof(PaletteController));
            }
        }
    }
}

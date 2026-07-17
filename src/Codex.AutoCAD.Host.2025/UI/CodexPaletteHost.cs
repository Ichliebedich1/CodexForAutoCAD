using System.Drawing;
using Autodesk.AutoCAD.Windows;

namespace Codex.AutoCAD.Host.UI;

internal static class CodexPaletteHost
{
    private static readonly Guid PaletteId = new("36E631D9-F46E-4D14-81C1-4E924E8DCB56");

    private static PaletteSet? paletteSet;
    private static CodexPanelControl? panel;

    public static void Show()
    {
        EnsureCreated();
        panel!.RefreshSelectionSummary();
        paletteSet!.Visible = true;
    }

    public static void RefreshSelectionSummary()
    {
        panel?.RefreshSelectionSummary();
    }

    public static void Dispose()
    {
        if (paletteSet is not null)
        {
            paletteSet.Visible = false;
            paletteSet.Dispose();
        }

        panel = null;
        paletteSet = null;
    }

    private static void EnsureCreated()
    {
        if (paletteSet is not null)
        {
            return;
        }

        panel = new CodexPanelControl();
        paletteSet = new PaletteSet("Codex for AutoCAD", PaletteId)
        {
            DockEnabled = DockSides.Left | DockSides.Right,
            MinimumSize = new Size(320, 420),
            Size = new Size(390, 680),
            KeepFocus = false,
            Style = PaletteSetStyles.ShowAutoHideButton |
                    PaletteSetStyles.ShowCloseButton |
                    PaletteSetStyles.ShowPropertiesMenu,
        };
        paletteSet.AddVisual("Codex", panel);
    }
}

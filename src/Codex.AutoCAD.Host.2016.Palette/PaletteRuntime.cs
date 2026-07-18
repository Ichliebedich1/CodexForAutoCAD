namespace Codex.AutoCAD.Host2016.Palette
{
    internal static class PaletteRuntime
    {
        private static PaletteController controller;

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

        internal static void Terminate()
        {
            PaletteController current = controller;
            controller = null;
            if (current != null)
            {
                current.Dispose();
            }
        }

        private static PaletteController GetOrCreateController()
        {
            if (controller == null)
            {
                controller = new PaletteController();
            }

            return controller;
        }
    }
}

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016.Palette
{
    public sealed class CodexPaletteExtension : IExtensionApplication
    {
        public void Initialize()
        {
            Document document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            if (document != null)
            {
                document.Editor.WriteMessage(
                    "\nCodex AutoCAD 2016 正式 Palette 候选已加载。输入 CODEX16PAL 打开面板，CODEX16PALINFO 查看只读信息，CODEX16PALRESET 释放后重建。\n");
            }
        }

        public void Terminate()
        {
            PaletteRuntime.Terminate();
        }
    }
}

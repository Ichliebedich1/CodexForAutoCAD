using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016.Palette
{
    public sealed class CodexPaletteCommands
    {
        [CommandMethod("CODEX16PAL", CommandFlags.Modal)]
        public void ShowPalette()
        {
            Editor editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            PaletteRuntime.Show();
            editor.WriteMessage("\nCodex AutoCAD 2016 Palette 已打开；本阶段 Agent、选择读取、CAD 写入和自动保存均禁用。\n");
        }

        [CommandMethod("CODEX16PALINFO", CommandFlags.Modal)]
        public void ShowPaletteInfo()
        {
            Editor editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            editor.WriteMessage("\n{0}\n", PaletteRuntime.BuildInfo());
        }

        [CommandMethod("CODEX16PALRESET", CommandFlags.Modal)]
        public void ResetPalette()
        {
            Editor editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            PaletteRuntime.ResetAndShow();
            editor.WriteMessage("\nCodex AutoCAD 2016 Palette 已释放并重建；CAD 数据未读取、未写入、未保存。\n");
        }

        private static Editor GetActiveEditor()
        {
            Document document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            return document == null ? null : document.Editor;
        }
    }
}

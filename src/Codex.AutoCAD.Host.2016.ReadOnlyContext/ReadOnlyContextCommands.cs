using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext
{
    public sealed class ReadOnlyContextCommands
    {
        [CommandMethod("CODEX16CTX", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void CaptureSelection()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            var result = ReadOnlyContextRuntime.CaptureCurrent();
            editor.WriteMessage(
                "\nCodex AutoCAD 2016 只读选择结果：status={0}, published={1}, selected={2}, DBMOD={3}->{4}, unchanged={5}。输入 CODEX16CTXINFO 查看脱敏摘要。\n",
                result.Status,
                result.Published ? "true" : "false",
                result.SelectedCount,
                FormatDbmod(result.DbmodBefore),
                FormatDbmod(result.DbmodAfter),
                result.DbmodUnchanged ? "true" : "false");
        }

        [CommandMethod("CODEX16CTXINFO", CommandFlags.Modal)]
        public void ShowContextInfo()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            editor.WriteMessage("\n{0}\n", ReadOnlyContextRuntime.BuildInfo());
        }

        [CommandMethod("CODEX16CTXCLEAR", CommandFlags.Modal)]
        public void ClearContext()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            ReadOnlyContextRuntime.Clear("user-command");
            editor.WriteMessage("\nCodex AutoCAD 2016 只读选择缓存已清除；未修改或保存图纸。\n");
        }

        private static Editor GetActiveEditor()
        {
            Document document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            return document == null ? null : document.Editor;
        }

        private static string FormatDbmod(int? value)
        {
            return value.HasValue
                ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "unavailable";
        }
    }
}

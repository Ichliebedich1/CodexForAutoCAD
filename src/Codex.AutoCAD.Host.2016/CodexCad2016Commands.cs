using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// AutoCAD 2016 首次 NETLOAD 的只读诊断命令。
    /// </summary>
    public sealed class CodexCad2016Commands
    {
        [CommandMethod("CODEXCADDOCTOR", CommandFlags.Modal)]
        public void RunDoctor()
        {
            Document document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                return;
            }

            Editor editor = document.Editor;
            editor.WriteMessage("\n--- Codex AutoCAD 2016 Doctor ---");
            editor.WriteMessage("\nHost target: .NET Framework 4.5");
            editor.WriteMessage("\nProcess architecture: {0}", Environment.Is64BitProcess ? "x64" : "x86");
            editor.WriteMessage("\nCLR: {0}", Environment.Version);
            editor.WriteMessage("\nAcMgd assembly: {0}", typeof(IExtensionApplication).Assembly.GetName().Version);
            editor.WriteMessage("\nAcDbMgd assembly: {0}", typeof(Database).Assembly.GetName().Version);
            editor.WriteMessage("\nPalette capability: enabled");
            editor.WriteMessage("\nRead-only selection capability: enabled");
            editor.WriteMessage("\nCadContextJson: codex.autocad.cad-context/1");
            editor.WriteMessage("\nAgent/IPC: disabled");
            editor.WriteMessage("\nCAD write capability: disabled");
            editor.WriteMessage("\nPlugin-initiated save: disabled");
            editor.WriteMessage("\nAutoCAD SAVETIME setting: not modified");

            WriteSystemVariable(editor, "ACADVER");
            WriteSystemVariable(editor, "VERNUM");
            WriteSystemVariable(editor, "SECURELOAD");
            WriteSystemVariable(editor, "APPAUTOLOAD");
            WriteSystemVariable(editor, "DBMOD");

            editor.WriteMessage("\nTRUSTEDPATHS is intentionally omitted because it can contain sensitive local or network paths.");
            editor.WriteMessage("\n--- End Doctor ---\n");
        }

        [CommandMethod("CODEXCAD", CommandFlags.Modal)]
        public void ShowCandidateStatus()
        {
            Document document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                return;
            }

            document.Editor.WriteMessage(
                "\nHost.2016 当前为统一只读 MVP 候选：诊断、Palette、六类选择读取和 CadContextJson v1 已整合；Agent、CAD 写入和插件保存保持禁用。\n");
        }

        [CommandMethod("CODEX16PAL", CommandFlags.Modal)]
        public void ShowPalette()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedPaletteRuntime.Show();
            editor.WriteMessage(
                "\nCodex AutoCAD 2016 统一只读侧边栏已打开；预选对象后执行 CODEX16CTX。Agent、CAD 写入和插件保存均禁用。\n");
        }

        [CommandMethod("CODEX16PALINFO", CommandFlags.Modal)]
        public void ShowPaletteInfo()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            editor.WriteMessage("\n{0}\n", UnifiedPaletteRuntime.BuildInfo());
        }

        [CommandMethod("CODEX16PALRESET", CommandFlags.Modal)]
        public void ResetPalette()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedPaletteRuntime.ResetAndShow();
            editor.WriteMessage(
                "\nCodex AutoCAD 2016 统一只读侧边栏已释放并重建；当前只读上下文仍保留在内存，图纸未修改、未保存。\n");
        }

        [CommandMethod("CODEX16CTX", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void CaptureSelection()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            var result = UnifiedReadOnlyContextRuntime.CaptureCurrent();
            editor.WriteMessage(
                "\nCodex AutoCAD 2016 统一只读结果：status={0}, published={1}, selected={2}, jsonBytes={3}, DBMOD={4}->{5}, unchanged={6}。侧边栏显示可读摘要与 canonical JSON。\n",
                result.Status,
                result.Published ? "true" : "false",
                result.SelectedCount,
                result.CanonicalBytes,
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

            editor.WriteMessage("\n{0}\n", UnifiedReadOnlyContextRuntime.BuildInfo());
        }

        [CommandMethod("CODEX16CTXCLEAR", CommandFlags.Modal)]
        public void ClearContext()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedReadOnlyContextRuntime.Clear("user-command");
            editor.WriteMessage(
                "\nCodex AutoCAD 2016 统一只读上下文已从内存清除；图纸未修改、未保存。\n");
        }

        private static Editor GetActiveEditor()
        {
            var document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            return document == null ? null : document.Editor;
        }

        private static string FormatDbmod(int? value)
        {
            return value.HasValue
                ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "unavailable";
        }

        private static void WriteSystemVariable(Editor editor, string variableName)
        {
            try
            {
                object value = AutoCadApplication.GetSystemVariable(variableName);
                editor.WriteMessage("\n{0}: {1}", variableName, value ?? "<null>");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception exception)
            {
                editor.WriteMessage("\n{0}: unavailable ({1})", variableName, exception.ErrorStatus);
            }
        }
    }
}

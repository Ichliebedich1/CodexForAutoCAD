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
            editor.WriteMessage("\nWrite capability: disabled in diagnostic stage");
            editor.WriteMessage("\nAutomatic save: disabled");

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
                "\nHost.2016 当前仅为诊断薄宿主；Palette、Agent 与 CAD 写入均保持禁用。\n");
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

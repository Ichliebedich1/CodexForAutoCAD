using System;
using System.Threading.Tasks;
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
            editor.WriteMessage("\nCadContextJson: codex.autocad.cad-context/2");
            editor.WriteMessage("\nAgent/IPC: authenticated MVP candidate; manual start");
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
                "\nHost.2016 当前为统一只读 AI MVP 候选：诊断、Palette、CadContextJson v2 选择读取和认证 Agent Bridge 已整合；CAD 写入和插件保存保持禁用。\n");
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
                "\nCodex AutoCAD 2016 统一只读侧边栏已打开；预选对象后执行 CODEX16CTX，再在面板输入问题或执行 CODEX16ASK。CAD 写入和插件保存均禁用。\n");
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

        [CommandMethod("CODEX16AGENTSTART", CommandFlags.Modal)]
        public void StartAgent()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedPaletteRuntime.Show();
            Observe(
                MvpAgentRuntime.StartAsync(),
                "启动 AgentHost",
                MvpAgentFailureStages.StartingAgentHost);
            editor.WriteMessage(
                "\nAgentHost 启动请求已提交；状态将在侧边栏更新。CAD 写入仍禁用。\n");
        }

        [CommandMethod("CODEX16ASK", CommandFlags.Modal)]
        public void AskAgent()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            var prompt = new PromptStringOptions("\n输入要结合当前选择上下文分析的问题：")
            {
                AllowSpaces = true,
            };
            var result = editor.GetString(prompt);
            if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult))
            {
                return;
            }

            UnifiedPaletteRuntime.Show();
            Observe(
                MvpAgentRuntime.AskAsync(result.StringResult),
                "发送只读问题",
                MvpAgentFailureStages.SendingTurn);
            editor.WriteMessage("\n只读问题已提交；回答将在侧边栏流式显示。\n");
        }

        [CommandMethod("CODEX16AGENTSTOP", CommandFlags.Modal)]
        public void StopAgent()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedPaletteRuntime.Show();
            Observe(
                MvpAgentRuntime.StopAsync(),
                "停止 AgentHost",
                MvpAgentFailureStages.StoppingAgentHost);
            editor.WriteMessage("\nAgentHost 停止请求已提交。\n");
        }

        private static void Observe(
            Task task,
            string operationName,
            string errorStage)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(
                completed =>
                {
                    var aggregate = completed.Exception;
                    var exception = aggregate == null
                        ? null
                        : aggregate.GetBaseException();
                    UnifiedPaletteRuntime.UpdateAgentStatus(
                        MvpAgentFailureFormatter
                            .FromException(exception, errorStage)
                            .FormatForUser(operationName));
                },
                System.Threading.CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
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

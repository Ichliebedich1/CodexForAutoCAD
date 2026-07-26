using System;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Contracts;
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
            editor.WriteMessage("\nDrawingIndex: codex.autocad.drawing-index/1; Idle-chunked read-only scan");
            editor.WriteMessage("\nCadQuery: codex.autocad.cad-query/1; cursor pagination");
            editor.WriteMessage("\nCAD read type telemetry: enabled; bounded actual-type counts");
            editor.WriteMessage(
                "\nCodex drawing-query tool: authenticated AgentHost Bridge; manual Agent start");
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
                "\nHost.2016 当前为 M3 CAD 读取语义候选：保留 M2 的 DrawingIndex/CadQuery 调用链，并在选择快照、整图索引和 Palette 中按实际类型统计未支持、数据超限和读取失败对象。执行 CODEX16TYPEINFO 查看中文测试目录。CAD 写入和插件保存保持禁用。\n");
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

        [CommandMethod("CODEX16TYPEINFO", CommandFlags.Modal)]
        public void ShowReadTypeCatalog()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            editor.WriteMessage("\n{0}\n", CadReadTypeStatistics.BuildSupportedTypeCatalog());
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
                "\nCodex AutoCAD 2016 统一只读上下文已从内存清除；当前 Codex 对话仍保留，图纸未修改、未保存。\n");
        }

        [CommandMethod("CODEX16INDEX", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void StartDrawingIndex()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            var options = new PromptKeywordOptions(
                "\n选择只读索引范围 [Selection/Current/Model/Layouts/Drawing] <Drawing>: ")
            {
                AllowNone = true,
            };
            options.Keywords.Add("Selection");
            options.Keywords.Add("Current");
            options.Keywords.Add("Model");
            options.Keywords.Add("Layouts");
            options.Keywords.Add("Drawing");
            var prompt = editor.GetKeywords(options);
            if (prompt.Status != PromptStatus.OK && prompt.Status != PromptStatus.None)
            {
                return;
            }

            var scope = MapIndexScope(
                prompt.Status == PromptStatus.None ? "Drawing" : prompt.StringResult);
            try
            {
                var started = DrawingIndexRuntime.Start(scope);
                UnifiedPaletteRuntime.Show();
                editor.WriteMessage(
                    "\nDrawingIndex 已进入分片准备阶段：indexId={0}, scope={1}。扫描在 AutoCAD Idle 中按只读小片执行；使用 CODEX16INDEXINFO 查看进度，CODEX16INDEXCANCEL 取消。\n",
                    started.IndexId,
                    started.Scope);
            }
            catch (System.Exception exception)
            {
                var failure = HostCommandDiagnosticFormatter.FromUnexpectedException(
                    exception,
                    HostCommandFailureStages.DrawingIndexStart);
                editor.WriteMessage(
                    "\n{0}\n",
                    failure.FormatForUser(
                        "DrawingIndex 启动",
                        "图纸未修改、未保存。"));
            }
        }

        [CommandMethod("CODEX16INDEXINFO", CommandFlags.Modal)]
        public void ShowDrawingIndexInfo()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            editor.WriteMessage("\n{0}\n", DrawingIndexRuntime.BuildInfo());
        }

        [CommandMethod("CODEX16INDEXCANCEL", CommandFlags.Modal)]
        public void CancelDrawingIndex()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            DrawingIndexRuntime.Cancel();
            editor.WriteMessage(
                "\nDrawingIndex 取消请求已按幂等方式处理；图纸未修改、未保存。\n");
        }

        [CommandMethod("CODEX16QUERY", CommandFlags.Modal)]
        public void QueryDrawingIndex()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            var options = new PromptKeywordOptions(
                "\n查询过滤 [All/Type/Layer/Space/Block/Text/Object] <All>: ")
            {
                AllowNone = true,
            };
            options.Keywords.Add("All");
            options.Keywords.Add("Type");
            options.Keywords.Add("Layer");
            options.Keywords.Add("Space");
            options.Keywords.Add("Block");
            options.Keywords.Add("Text");
            options.Keywords.Add("Object");
            var keyword = editor.GetKeywords(options);
            if (keyword.Status != PromptStatus.OK && keyword.Status != PromptStatus.None)
            {
                return;
            }

            var kind = keyword.Status == PromptStatus.None ? "All" : keyword.StringResult;
            var filter = new CadQueryFilter();
            if (!string.Equals(kind, "All", StringComparison.OrdinalIgnoreCase))
            {
                var valueOptions = new PromptStringOptions("\n输入精确过滤值（Text 为包含匹配）:")
                {
                    AllowSpaces = true,
                };
                var value = editor.GetString(valueOptions);
                if (value.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(value.StringResult))
                {
                    return;
                }
                ApplyQueryFilter(filter, kind, value.StringResult.Trim());
            }

            try
            {
                var response = DrawingIndexRuntime.QueryFirst(filter, 20);
                editor.WriteMessage("\n{0}\n", DrawingIndexRuntime.FormatQueryResponse(response));
            }
            catch (DrawingIndexQueryException exception)
            {
                editor.WriteMessage(
                    "\nCadQuery 被拒绝：code={0}, message={1}\n",
                    exception.Code,
                    DiagnosticSanitizer
                        .SanitizeText(
                            DiagnosticDataClassification.Exception,
                            exception.Message)
                        .SafeText);
            }
            catch (System.Exception exception)
            {
                var failure = HostCommandDiagnosticFormatter.FromUnexpectedException(
                    exception,
                    HostCommandFailureStages.DrawingQuery);
                editor.WriteMessage(
                    "\n{0}\n",
                    failure.FormatForUser(
                        "CadQuery",
                        "未修改图纸。"));
            }
        }

        [CommandMethod("CODEX16QUERYNEXT", CommandFlags.Modal)]
        public void QueryDrawingIndexNextPage()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            try
            {
                var response = DrawingIndexRuntime.QueryNext();
                editor.WriteMessage("\n{0}\n", DrawingIndexRuntime.FormatQueryResponse(response));
            }
            catch (DrawingIndexQueryException exception)
            {
                editor.WriteMessage(
                    "\nCadQuery 下一页被拒绝：code={0}, message={1}\n",
                    exception.Code,
                    DiagnosticSanitizer
                        .SanitizeText(
                            DiagnosticDataClassification.Exception,
                            exception.Message)
                        .SafeText);
            }
            catch (System.Exception exception)
            {
                var failure = HostCommandDiagnosticFormatter.FromUnexpectedException(
                    exception,
                    HostCommandFailureStages.DrawingQueryNext);
                editor.WriteMessage(
                    "\n{0}\n",
                    failure.FormatForUser(
                        "CadQuery 下一页",
                        "未修改图纸。"));
            }
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

        [CommandMethod("CODEX16CANCEL", CommandFlags.Modal)]
        public void CancelAgentTurn()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedPaletteRuntime.Show();
            Observe(
                MvpAgentRuntime.CancelAsync(),
                "取消 Codex 回合",
                MvpAgentFailureStages.CancellingTurn);
            editor.WriteMessage("\nCodex 回合取消请求已提交；状态将在侧边栏更新。\n");
        }

        [CommandMethod("CODEX16NEWCHAT", CommandFlags.Modal)]
        public void NewAgentConversation()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedPaletteRuntime.Show();
            Observe(
                MvpAgentRuntime.NewConversationAsync(),
                "新建 Codex 对话",
                MvpAgentFailureStages.StartingConversation);
            editor.WriteMessage(
                "\n新建 Codex 对话请求已提交；当前 CAD 上下文保持不变，状态将在侧边栏更新。\n");
        }

        [CommandMethod("CODEX16CLEARALL", CommandFlags.Modal)]
        public void ClearAllAgentState()
        {
            var editor = GetActiveEditor();
            if (editor == null)
            {
                return;
            }

            UnifiedPaletteRuntime.Show();
            try
            {
                MvpAgentRuntime.ClearAll();
                editor.WriteMessage(
                    "\nCAD 上下文、回答文本和当前 Codex 对话已清除；图纸未修改、未保存。\n");
            }
            catch (System.Exception exception)
            {
                UnifiedPaletteRuntime.UpdateAgentStatus(
                    MvpAgentFailureFormatter
                        .FromException(
                            exception,
                            MvpAgentFailureStages.ClearingConversation)
                        .FormatForUser("清除全部"));
            }
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

        private static string MapIndexScope(string keyword)
        {
            if (string.Equals(keyword, "Selection", StringComparison.OrdinalIgnoreCase))
            {
                return DrawingIndexScopes.Selection;
            }
            if (string.Equals(keyword, "Current", StringComparison.OrdinalIgnoreCase))
            {
                return DrawingIndexScopes.CurrentSpace;
            }
            if (string.Equals(keyword, "Model", StringComparison.OrdinalIgnoreCase))
            {
                return DrawingIndexScopes.ModelSpace;
            }
            if (string.Equals(keyword, "Layouts", StringComparison.OrdinalIgnoreCase))
            {
                return DrawingIndexScopes.Layouts;
            }
            return DrawingIndexScopes.Drawing;
        }

        private static void ApplyQueryFilter(CadQueryFilter filter, string kind, string value)
        {
            if (string.Equals(kind, "Type", StringComparison.OrdinalIgnoreCase))
            {
                filter.EntityTypes = new[] { value };
            }
            else if (string.Equals(kind, "Layer", StringComparison.OrdinalIgnoreCase))
            {
                filter.Layers = new[] { value };
            }
            else if (string.Equals(kind, "Space", StringComparison.OrdinalIgnoreCase))
            {
                filter.Spaces = new[] { value };
            }
            else if (string.Equals(kind, "Block", StringComparison.OrdinalIgnoreCase))
            {
                filter.BlockNames = new[] { value };
            }
            else if (string.Equals(kind, "Text", StringComparison.OrdinalIgnoreCase))
            {
                filter.TextContains = value;
            }
            else if (string.Equals(kind, "Object", StringComparison.OrdinalIgnoreCase))
            {
                filter.ObjectIds = new[] { value };
            }
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

using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// AutoCAD 2016 进程内统一薄宿主入口。当前阶段整合诊断、Palette 和 CadContextJson v2 只读选择，
    /// 不启动 Agent、不建立未认证通道，也不执行或保存 CAD 写入。
    /// </summary>
    public sealed class CodexAutoCad2016Extension : IExtensionApplication
    {
        public void Initialize()
        {
            UnifiedReadOnlyContextRuntime.Initialize();
            DrawingIndexRuntime.Initialize();
            MvpAgentRuntime.Initialize();
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document != null)
            {
                document.Editor.WriteMessage(
                    "\nCodex AutoCAD 2016 M2 只读整图索引开发候选已加载。CODEX16INDEX 启动分片索引，CODEX16INDEXINFO 查看进度，CODEX16QUERY 分页查询；选择上下文和 Agent 命令继续保留。Codex 动态查询工具尚未接入，CAD 写入和插件保存均禁用。\n");
            }
        }

        public void Terminate()
        {
            MvpAgentRuntime.Terminate();
            DrawingIndexRuntime.Terminate();
            UnifiedReadOnlyContextRuntime.Terminate();
            UnifiedPaletteRuntime.Terminate();
        }
    }
}

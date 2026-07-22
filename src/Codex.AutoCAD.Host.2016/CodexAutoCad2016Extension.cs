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
            MvpAgentRuntime.Initialize();
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document != null)
            {
                document.Editor.WriteMessage(
                    "\nCodex AutoCAD 2016 统一只读 AI MVP 候选已加载。输入 CODEX16PAL 打开侧边栏；预选对象后输入 CODEX16CTX，再使用 CODEX16AGENTSTART/CODEX16ASK。CODEX16NEWCHAT 新建对话，CODEX16CLEARALL 清除全部。CAD 写入和插件保存均禁用。\n");
            }
        }

        public void Terminate()
        {
            MvpAgentRuntime.Terminate();
            UnifiedReadOnlyContextRuntime.Terminate();
            UnifiedPaletteRuntime.Terminate();
        }
    }
}

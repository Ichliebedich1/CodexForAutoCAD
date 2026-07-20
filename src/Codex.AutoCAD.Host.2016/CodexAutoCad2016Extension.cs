using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// AutoCAD 2016 进程内统一薄宿主入口。当前阶段整合诊断、Palette 和只读选择，
    /// 不启动 Agent、不建立未认证通道，也不执行或保存 CAD 写入。
    /// </summary>
    public sealed class CodexAutoCad2016Extension : IExtensionApplication
    {
        public void Initialize()
        {
            UnifiedReadOnlyContextRuntime.Initialize();
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document != null)
            {
                document.Editor.WriteMessage(
                    "\nCodex AutoCAD 2016 统一只读 MVP 候选已加载。输入 CODEX16PAL 打开侧边栏；预选对象后输入 CODEX16CTX 生成 CadContextJson v1。Agent、CAD 写入和插件保存均禁用。\n");
            }
        }

        public void Terminate()
        {
            UnifiedReadOnlyContextRuntime.Terminate();
            UnifiedPaletteRuntime.Terminate();
        }
    }
}

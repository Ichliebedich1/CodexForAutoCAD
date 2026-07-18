using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;

namespace Codex.AutoCAD.Host2016
{
    /// <summary>
    /// AutoCAD 2016 进程内薄宿主入口。这个最小阶段只提供只读诊断，
    /// 不启动 Agent、不建立未认证通道，也不执行或保存 CAD 写入。
    /// </summary>
    public sealed class CodexAutoCad2016Extension : IExtensionApplication
    {
        public void Initialize()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document != null)
            {
                document.Editor.WriteMessage(
                    "\nCodex for AutoCAD 2016 诊断薄宿主已加载。输入 CODEXCADDOCTOR 查看只读环境信息。\n");
            }
        }

        public void Terminate()
        {
            // 最小诊断阶段不创建线程、进程、管道或 UI，因此没有后台资源需要释放。
        }
    }
}

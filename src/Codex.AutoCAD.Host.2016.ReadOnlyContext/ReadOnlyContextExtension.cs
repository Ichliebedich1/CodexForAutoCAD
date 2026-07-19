using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AutoCadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Codex.AutoCAD.Host2016.ReadOnlyContext
{
    public sealed class ReadOnlyContextExtension : IExtensionApplication
    {
        public void Initialize()
        {
            ReadOnlyContextRuntime.Initialize();
            Document document = AutoCadApplication.DocumentManager.MdiActiveDocument;
            if (document != null)
            {
                document.Editor.WriteMessage(
                    "\nCodex AutoCAD 2016 只读选择 sidecar 已加载。先在图形区选择对象，再输入 CODEX16CTX；CODEX16CTXINFO 查看脱敏摘要，CODEX16CTXCLEAR 清除内存缓存。\n");
            }
        }

        public void Terminate()
        {
            ReadOnlyContextRuntime.Terminate();
        }
    }
}

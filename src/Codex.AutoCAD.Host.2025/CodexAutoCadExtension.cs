using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Host.UI;

namespace Codex.AutoCAD.Host;

/// <summary>
/// AutoCAD 2025 进程内入口。这里只负责原生 UI 和受控 CAD 能力，
/// 不在 AutoCAD 进程中启动 Shell、网络客户端或任意脚本。
/// </summary>
public sealed class CodexAutoCadExtension : IExtensionApplication
{
    public void Initialize()
    {
        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nCodex for AutoCAD 2025 已加载。输入 CODEXCAD 打开侧边栏。\n");
    }

    public void Terminate()
    {
        CodexPaletteHost.Dispose();
    }
}

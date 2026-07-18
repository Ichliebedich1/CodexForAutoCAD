using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Host.UI;

namespace Codex.AutoCAD.Host;

/// <summary>
/// AutoCAD 命令入口。CAD 写入在专属审批、重校验和事务门禁完成前保持禁用。
/// </summary>
public sealed class CodexCadCommands
{
    [CommandMethod("CODEXCAD", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ShowPalette()
    {
        CodexPaletteHost.Show();
    }

    [CommandMethod("CODEXCADLINE", CommandFlags.Modal)]
    public void CreateConfirmedLine()
    {
        Document? document = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        document.Editor.WriteMessage(
            "\nCODEXCADLINE 当前保持禁用；必须先完成一次审批、锁内重校验、单事务及不自动保存的专属验证。\n");
    }
}

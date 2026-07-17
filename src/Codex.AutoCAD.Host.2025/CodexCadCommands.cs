using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Host.UI;

namespace Codex.AutoCAD.Host;

/// <summary>
/// AutoCAD 命令入口。所有活动图纸写入必须保持为可审计、强类型的托管 API 调用。
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

        Editor editor = document.Editor;

        PromptPointResult startResult = editor.GetPoint(
            new PromptPointOptions("\n指定直线起点: "));
        if (startResult.Status != PromptStatus.OK)
        {
            editor.WriteMessage("\n已取消，图纸未发生变化。\n");
            return;
        }

        PromptPointOptions endOptions = new("\n指定直线终点: ")
        {
            BasePoint = startResult.Value,
            UseBasePoint = true,
            UseDashedLine = true,
        };

        PromptPointResult endResult = editor.GetPoint(endOptions);
        if (endResult.Status != PromptStatus.OK)
        {
            editor.WriteMessage("\n已取消，图纸未发生变化。\n");
            return;
        }

        Point3d startPoint = startResult.Value;
        Point3d endPoint = endResult.Value;
        if (startPoint.IsEqualTo(endPoint))
        {
            editor.WriteMessage("\n起点与终点重合，未创建直线。\n");
            return;
        }

        editor.WriteMessage(
            $"\n待创建直线: ({Format(startPoint.X)}, {Format(startPoint.Y)}, {Format(startPoint.Z)})" +
            $" -> ({Format(endPoint.X)}, {Format(endPoint.Y)}, {Format(endPoint.Z)})");

        PromptKeywordOptions confirmation = new("\n确认写入当前图纸? [是(Yes)/否(No)] <否>: ")
        {
            AllowNone = true,
        };
        confirmation.Keywords.Add("Yes", "Yes", "是(Yes)");
        confirmation.Keywords.Add("No", "No", "否(No)");
        confirmation.Keywords.Default = "No";

        PromptResult confirmationResult = editor.GetKeywords(confirmation);
        if (confirmationResult.Status != PromptStatus.OK ||
            !string.Equals(confirmationResult.StringResult, "Yes", StringComparison.OrdinalIgnoreCase))
        {
            editor.WriteMessage("\n用户未批准，图纸未发生变化。\n");
            return;
        }

        Database database = document.Database;

        // AutoCAD 为单个模态命令建立一个原生 Undo 标记。本命令不启动嵌套命令，
        // 并在该标记内只提交一个事务，因此一次 U 即可完整撤销本次写入。
        using (DocumentLock documentLock = document.LockDocument())
        using (Transaction transaction = database.TransactionManager.StartTransaction())
        {
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId,
                OpenMode.ForWrite);

            using Line line = new(startPoint, endPoint)
            {
                LayerId = database.Clayer,
            };

            currentSpace.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, add: true);
            transaction.Commit();
        }

        editor.WriteMessage("\n直线已创建。可执行一次 U 撤销；插件不会自动保存图纸。\n");
        CodexPaletteHost.RefreshSelectionSummary();
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}

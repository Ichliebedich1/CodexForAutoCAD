using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace Codex.AutoCAD.Host.Selection;

internal static class SelectionSummaryService
{
    public static SelectionSummary ReadCurrentSelection()
    {
        Document? document = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return SelectionSummary.Empty("当前没有活动图纸。");
        }

        PromptSelectionResult result = document.Editor.SelectImplied();
        if (result.Status != PromptStatus.OK || result.Value is null || result.Value.Count == 0)
        {
            return SelectionSummary.Empty("当前未选中图元。请在图形区选择后刷新。");
        }

        Dictionary<string, int> types = new(StringComparer.Ordinal);
        int readableCount = 0;

        using Transaction transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        foreach (SelectedObject selectedObject in result.Value)
        {
            if (selectedObject is null || selectedObject.ObjectId.IsNull || selectedObject.ObjectId.IsErased)
            {
                continue;
            }

            DBObject? databaseObject = transaction.GetObject(
                selectedObject.ObjectId,
                OpenMode.ForRead,
                openErased: false) as DBObject;
            if (databaseObject is null)
            {
                continue;
            }

            string typeName = databaseObject.GetType().Name;
            types[typeName] = types.TryGetValue(typeName, out int count) ? count + 1 : 1;
            readableCount++;
        }

        return readableCount == 0
            ? SelectionSummary.Empty("选中对象当前不可读取。")
            : new SelectionSummary(
                readableCount,
                types,
                $"已读取 {readableCount} 个图元（只读，不修改图纸）。");
    }
}

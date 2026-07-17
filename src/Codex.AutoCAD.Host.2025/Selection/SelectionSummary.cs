namespace Codex.AutoCAD.Host.Selection;

internal sealed record SelectionSummary(
    int Count,
    IReadOnlyDictionary<string, int> EntityTypes,
    string Message)
{
    public static SelectionSummary Empty(string message) =>
        new(0, new Dictionary<string, int>(), message);
}

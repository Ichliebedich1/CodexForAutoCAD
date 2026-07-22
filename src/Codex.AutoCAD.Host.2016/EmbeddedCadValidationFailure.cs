namespace Codex.AutoCAD.Contracts;

/// <summary>
/// Embedded copy of the validation result value used by the linked CadContextJson v1 sources.
/// It keeps the AutoCAD 2016 product candidate to one in-process DLL; the wire contract itself
/// remains sourced from the shared Contracts project.
/// </summary>
public sealed class CadValidationFailure
{
    public CadValidationFailure(string code, string path, string message)
    {
        Code = code;
        Path = path;
        Message = message;
    }

    public string Code { get; }

    public string Path { get; }

    public string Message { get; }
}

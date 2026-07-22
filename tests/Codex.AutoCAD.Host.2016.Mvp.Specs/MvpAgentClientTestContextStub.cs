using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016;

// MvpAgentClient itself is Autodesk-free, but its production context state is declared in the
// AutoCAD-bound capture source file. This narrow test double keeps lifecycle tests outside CAD.
internal sealed class UnifiedContextState
{
    internal bool Published { get; set; }

    internal CadContextJsonV1 Context { get; set; }

    internal string ContextSha256 { get; set; } = string.Empty;
}

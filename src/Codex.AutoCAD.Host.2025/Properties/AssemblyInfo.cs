using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Host;

[assembly: ExtensionApplication(typeof(CodexAutoCadExtension))]
[assembly: CommandClass(typeof(CodexCadCommands))]

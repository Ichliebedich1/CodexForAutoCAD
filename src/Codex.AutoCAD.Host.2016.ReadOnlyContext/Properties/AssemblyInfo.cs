using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Host2016.ReadOnlyContext;

[assembly: AssemblyTitle("Codex AutoCAD 2016 Read-Only Context Host")]
[assembly: AssemblyDescription("Read-only selected-entity context sidecar for AutoCAD 2016")]
[assembly: AssemblyCompany("CodexForAutoCAD")]
[assembly: AssemblyProduct("CodexForAutoCAD")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: ComVisible(false)]
[assembly: Guid("b50ff8ca-bc89-4eb0-bfc6-72c0c55d6f0a")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ExtensionApplication(typeof(ReadOnlyContextExtension))]
[assembly: CommandClass(typeof(ReadOnlyContextCommands))]

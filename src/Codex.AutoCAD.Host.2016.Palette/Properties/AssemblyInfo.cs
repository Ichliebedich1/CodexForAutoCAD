using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Host2016.Palette;

[assembly: AssemblyTitle("Codex AutoCAD 2016 Palette Host")]
[assembly: AssemblyDescription("Read-only PaletteSet and WPF validation host for AutoCAD 2016")]
[assembly: AssemblyCompany("CodexForAutoCAD")]
[assembly: AssemblyProduct("CodexForAutoCAD")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: ComVisible(false)]
[assembly: Guid("63ab3e77-35f1-4c06-b3a1-85874c044907")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ExtensionApplication(typeof(CodexPaletteExtension))]
[assembly: CommandClass(typeof(CodexPaletteCommands))]

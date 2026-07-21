using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;
using Codex.AutoCAD.Host2016;

[assembly: AssemblyTitle("Codex for AutoCAD 2016 Host")]
[assembly: AssemblyDescription("Unified read-only AI MVP host for AutoCAD 2016")]
[assembly: AssemblyCompany("Codex for AutoCAD")]
[assembly: AssemblyProduct("Codex for AutoCAD")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: ComVisible(false)]
[assembly: Guid("e6e012fa-550b-48cf-87df-31fe0a738ef7")]
[assembly: AssemblyVersion("0.3.1.0")]
[assembly: AssemblyFileVersion("0.3.1.0")]
[assembly: ExtensionApplication(typeof(CodexAutoCad2016Extension))]
[assembly: CommandClass(typeof(CodexCad2016Commands))]

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AutoCad2016Dir,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$MsBuildPath,

    [switch]$RuleSelfTestOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016.Palette\Codex.AutoCAD.Host.2016.Palette.csproj'
$solutionPath = Join-Path $repoRoot 'Codex.AutoCAD.2016.Palette.sln'
$mainSolutionPath = Join-Path $repoRoot 'Codex.AutoCAD.sln'
$diagnosticSolutionPath = Join-Path $repoRoot 'Codex.AutoCAD.2016.sln'
$nuGetConfigPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016.Palette\NuGet.Config'
$packageLockPath = Join-Path $repoRoot 'src\Codex.AutoCAD.Host.2016.Palette\packages.lock.json'
$vendoredPackagePath = Join-Path $repoRoot 'third_party\nuget\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3.nupkg'
$AutoCad2016Dir = [IO.Path]::GetFullPath($AutoCad2016Dir)

$projectGuid = '{63AB3E77-35F1-4C06-B3A1-85874C044907}'
$paletteGuidText = '173d39c8-85d9-45fc-845f-e0520f8cddcc'
$expectedAssemblyName = 'Codex.AutoCAD.Host.2016.Palette'
$expectedRootNamespace = 'Codex.AutoCAD.Host2016.Palette'
$expectedCommands = @('CODEX16PAL', 'CODEX16PALINFO', 'CODEX16PALRESET')
$expectedDocumentEvents = @('DocumentActivated', 'DocumentToBeDestroyed')
$expectedCompileItems = @(
    'CodexPaletteExtension.cs',
    'CodexPaletteCommands.cs',
    'PaletteRuntime.cs',
    'PaletteController.cs',
    'CodexPalettePanel.cs',
    'Properties\AssemblyInfo.cs'
)
$expectedProjectSha256 = '9C990A405103F5CDCD8ED855DA68DD616FFD1CB78795BBD2CA9E41EF0154D344'
$expectedSolutionSha256 = '29CBFCAE5ADD3256BB3D3C21446E58AC247D508336EA18646A64467A562E1C22'
$expectedNuGetConfigSha256 = '9138C28E1A457FA63A946AB0D286B55798AAC0190391AAB806FEA989787D88AE'
$expectedPackageLockSha256 = '2D06BE74E48A2E545ADBC4E4D05200376521A6D414D4CBE1D879AF318EA7E32D'
$expectedCandidateSha256 = '90620EA354AAE9A3C2B2E11C3FA60274F1EF9B0753734AF7AAB67BDAA0E01DFE'
$expectedSourceHashes = [ordered]@{
    'CodexPaletteExtension.cs' = 'F4BAD0124136AF6616BC9D489F6B021EC825E678D426D219C1567A3F2D31C630'
    'CodexPaletteCommands.cs' = 'FCDD4BC1915F6726999A65121E155AFE3B99999981261DC2CCEF68DAF702D0FA'
    'PaletteRuntime.cs' = '5AF0F5267F9D7C055F409BE8E615D0099B01C9EBCDD8A99771A7D575D72AC9FD'
    'PaletteController.cs' = 'AF77EA3CFC0336430C3ECEABC5391E26E64A0B9E2424095545F231572FE764A4'
    'CodexPalettePanel.cs' = '180147678525C0B2E1EE1A54D5C53FBEDF43FD2AA7B5104A5598A07AF32423EB'
    'Properties\AssemblyInfo.cs' = '0F9C2566A7454A0F8F9ED622DE2E3A3A1DCB7344130C556ACF2286A9EDF0B703'
}
$expectedOutputReferences = @(
    'mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089',
    'Acdbmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'accoremgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'Acmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a',
    'WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35',
    'PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35',
    'PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35'
)
$expectedMethodDefinitions = [ordered]@{
    '06000001' = 'CodexPaletteExtension::Initialize|public hidebysig newslot virtual final instance void Initialize() cil managed'
    '06000002' = 'CodexPaletteExtension::Terminate|public hidebysig newslot virtual final instance void Terminate() cil managed'
    '06000003' = 'CodexPaletteExtension::.ctor|public hidebysig specialname rtspecialname instance void .ctor() cil managed'
    '06000004' = 'CodexPaletteCommands::ShowPalette|public hidebysig instance void ShowPalette() cil managed'
    '06000005' = 'CodexPaletteCommands::ShowPaletteInfo|public hidebysig instance void ShowPaletteInfo() cil managed'
    '06000006' = 'CodexPaletteCommands::ResetPalette|public hidebysig instance void ResetPalette() cil managed'
    '06000007' = 'CodexPaletteCommands::GetActiveEditor|private hidebysig static class [accoremgd]Autodesk.AutoCAD.EditorInput.Editor GetActiveEditor() cil managed'
    '06000008' = 'CodexPaletteCommands::.ctor|public hidebysig specialname rtspecialname instance void .ctor() cil managed'
    '06000009' = 'PaletteRuntime::Show|assembly hidebysig static void Show() cil managed'
    '0600000A' = 'PaletteRuntime::BuildInfo|assembly hidebysig static string BuildInfo() cil managed'
    '0600000B' = 'PaletteRuntime::ResetAndShow|assembly hidebysig static void ResetAndShow() cil managed'
    '0600000C' = 'PaletteRuntime::Terminate|assembly hidebysig static void Terminate() cil managed'
    '0600000D' = 'PaletteRuntime::GetOrCreateController|private hidebysig static class Codex.AutoCAD.Host2016.Palette.PaletteController GetOrCreateController() cil managed'
    '0600000E' = 'PaletteController::.ctor|assembly hidebysig specialname rtspecialname instance void .ctor() cil managed'
    '0600000F' = 'PaletteController::Show|assembly hidebysig instance void Show() cil managed'
    '06000010' = 'PaletteController::ResetAndShow|assembly hidebysig instance void ResetAndShow() cil managed'
    '06000011' = 'PaletteController::BuildInfo|assembly hidebysig instance string BuildInfo() cil managed'
    '06000012' = 'PaletteController::Dispose|public hidebysig newslot virtual final instance void Dispose() cil managed'
    '06000013' = 'PaletteController::EnsurePalette|private hidebysig instance void EnsurePalette() cil managed'
    '06000014' = 'PaletteController::ReleasePalette|private hidebysig instance void ReleasePalette() cil managed'
    '06000015' = 'PaletteController::AttachPaletteEvents|private hidebysig instance void AttachPaletteEvents(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSet current) cil managed'
    '06000016' = 'PaletteController::DetachPaletteEvents|private hidebysig instance void DetachPaletteEvents(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSet current) cil managed'
    '06000017' = 'PaletteController::OnPaletteStateChanged|private hidebysig instance void OnPaletteStateChanged(object sender, class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetStateEventArgs eventArgs) cil managed'
    '06000018' = 'PaletteController::OnPaletteSizeChanged|private hidebysig instance void OnPaletteSizeChanged(object sender, class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventArgs eventArgs) cil managed'
    '06000019' = 'PaletteController::OnPaletteSetDestroy|private hidebysig instance void OnPaletteSetDestroy(object sender, class [mscorlib]System.EventArgs eventArgs) cil managed'
    '0600001A' = 'PaletteController::OnDocumentActivated|private hidebysig instance void OnDocumentActivated(object sender, class [accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs eventArgs) cil managed'
    '0600001B' = 'PaletteController::OnDocumentToBeDestroyed|private hidebysig instance void OnDocumentToBeDestroyed(object sender, class [accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs eventArgs) cil managed'
    '0600001C' = 'PaletteController::RefreshSizeFromPalette|private hidebysig instance void RefreshSizeFromPalette() cil managed'
    '0600001D' = 'PaletteController::UpdateDpiFromSizes|private hidebysig instance void UpdateDpiFromSizes() cil managed'
    '0600001E' = 'PaletteController::UpdatePanel|private hidebysig instance void UpdatePanel() cil managed'
    '0600001F' = 'PaletteController::ReadDbmod|private hidebysig static string ReadDbmod() cil managed'
    '06000020' = 'PaletteController::FormatNumber|private hidebysig static string FormatNumber(float64 ''value'') cil managed'
    '06000021' = 'PaletteController::FormatDpi|private hidebysig static string FormatDpi(float64 ''value'') cil managed'
    '06000022' = 'PaletteController::EnsureNotDisposed|private hidebysig instance void EnsureNotDisposed() cil managed'
    '06000023' = 'PaletteController::.cctor|private hidebysig specialname rtspecialname static void .cctor() cil managed'
    '06000024' = 'CodexPalettePanel::.ctor|assembly hidebysig specialname rtspecialname instance void .ctor() cil managed'
    '06000025' = 'CodexPalettePanel::UpdateMetrics|assembly hidebysig instance void UpdateMetrics(string ''value'') cil managed'
}
$expectedMemberReferences = [ordered]@{
    '0A000001' = '[mscorlib]System.Runtime.CompilerServices.CompilationRelaxationsAttribute::.ctor(int32)'
    '0A000002' = '[mscorlib]System.Runtime.CompilerServices.RuntimeCompatibilityAttribute::.ctor()'
    '0A000003' = '[mscorlib]System.Diagnostics.DebuggableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggableAttribute/DebuggingModes)'
    '0A000004' = '[mscorlib]System.Reflection.AssemblyTitleAttribute::.ctor(string)'
    '0A000005' = '[mscorlib]System.Reflection.AssemblyDescriptionAttribute::.ctor(string)'
    '0A000006' = '[mscorlib]System.Reflection.AssemblyCompanyAttribute::.ctor(string)'
    '0A000007' = '[mscorlib]System.Reflection.AssemblyProductAttribute::.ctor(string)'
    '0A000008' = '[mscorlib]System.Reflection.AssemblyCopyrightAttribute::.ctor(string)'
    '0A000009' = '[mscorlib]System.Runtime.InteropServices.ComVisibleAttribute::.ctor(bool)'
    '0A00000A' = '[mscorlib]System.Runtime.InteropServices.GuidAttribute::.ctor(string)'
    '0A00000B' = '[mscorlib]System.Reflection.AssemblyFileVersionAttribute::.ctor(string)'
    '0A00000C' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.ExtensionApplicationAttribute::.ctor(class [mscorlib]System.Type)'
    '0A00000D' = '[accoremgd]Autodesk.AutoCAD.Runtime.CommandClassAttribute::.ctor(class [mscorlib]System.Type)'
    '0A00000E' = '[mscorlib]System.Runtime.Versioning.TargetFrameworkAttribute::.ctor(string)'
    '0A00000F' = '[accoremgd]Autodesk.AutoCAD.Runtime.CommandMethodAttribute::.ctor(string, valuetype [accoremgd]Autodesk.AutoCAD.Runtime.CommandFlags)'
    '0A000010' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Core.Application::get_DocumentManager()'
    '0A000011' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollection::get_MdiActiveDocument()'
    '0A000012' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper::op_Inequality(class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper, class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper)'
    '0A000013' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Document::get_Editor()'
    '0A000014' = '[accoremgd]Autodesk.AutoCAD.EditorInput.Editor::WriteMessage(string)'
    '0A000015' = '[mscorlib]System.Object::.ctor()'
    '0A000016' = '[accoremgd]Autodesk.AutoCAD.EditorInput.Editor::WriteMessage(string, object[])'
    '0A000017' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper::op_Equality(class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper, class [Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper)'
    '0A000018' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventHandler::.ctor(object, native int)'
    '0A000019' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollection::add_DocumentActivated(class [accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventHandler)'
    '0A00001A' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollection::add_DocumentToBeDestroyed(class [accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventHandler)'
    '0A00001B' = '[accoremgd]Autodesk.AutoCAD.Windows.Window::set_Visible(bool)'
    '0A00001C' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper::get_IsDisposed()'
    '0A00001D' = '[accoremgd]Autodesk.AutoCAD.Windows.Window::get_Visible()'
    '0A00001E' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::get_Dock()'
    '0A00001F' = '[mscorlib]System.Object::ToString()'
    '0A000020' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::get_Count()'
    '0A000021' = '[mscorlib]System.Text.StringBuilder::.ctor()'
    '0A000022' = '[mscorlib]System.Text.StringBuilder::AppendLine(string)'
    '0A000023' = '[mscorlib]System.Text.StringBuilder::Append(string)'
    '0A000024' = '[mscorlib]System.Globalization.CultureInfo::get_InvariantCulture()'
    '0A000025' = '[mscorlib]System.Int32::ToString(class [mscorlib]System.IFormatProvider)'
    '0A000026' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollection::remove_DocumentActivated(class [accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventHandler)'
    '0A000027' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollection::remove_DocumentToBeDestroyed(class [accoremgd]Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventHandler)'
    '0A000028' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::.ctor(string, valuetype [mscorlib]System.Guid)'
    '0A000029' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::set_Style(valuetype [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetStyles)'
    '0A00002A' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::set_DockEnabled(valuetype [Acmgd]Autodesk.AutoCAD.Windows.DockSides)'
    '0A00002B' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::set_Dock(valuetype [Acmgd]Autodesk.AutoCAD.Windows.DockSides)'
    '0A00002C' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::set_KeepFocus(bool)'
    '0A00002D' = '[System.Drawing]System.Drawing.Size::.ctor(int32, int32)'
    '0A00002E' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::set_MinimumSize(valuetype [System.Drawing]System.Drawing.Size)'
    '0A00002F' = '[WindowsBase]System.Windows.Size::.ctor(float64, float64)'
    '0A000030' = '[accoremgd]Autodesk.AutoCAD.Windows.Window::set_DeviceIndependentSize(valuetype [WindowsBase]System.Windows.Size)'
    '0A000031' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::AddVisual(string, class [PresentationCore]System.Windows.Media.Visual, bool)'
    '0A000032' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.DisposableWrapper::Dispose()'
    '0A000033' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetStateEventHandler::.ctor(object, native int)'
    '0A000034' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::add_StateChanged(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetStateEventHandler)'
    '0A000035' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventHandler::.ctor(object, native int)'
    '0A000036' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::add_SizeChanged(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventHandler)'
    '0A000037' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetDestroyEventHandler::.ctor(object, native int)'
    '0A000038' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::add_PaletteSetDestroy(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetDestroyEventHandler)'
    '0A000039' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::remove_StateChanged(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetStateEventHandler)'
    '0A00003A' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::remove_SizeChanged(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventHandler)'
    '0A00003B' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::remove_PaletteSetDestroy(class [Acmgd]Autodesk.AutoCAD.Windows.PaletteSetDestroyEventHandler)'
    '0A00003C' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetStateEventArgs::get_NewState()'
    '0A00003D' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventArgs::get_Width()'
    '0A00003E' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventArgs::get_Height()'
    '0A00003F' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventArgs::get_DeviceIndependentWidth()'
    '0A000040' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSetSizeEventArgs::get_DeviceIndependentHeight()'
    '0A000041' = '[Acmgd]Autodesk.AutoCAD.Windows.PaletteSet::get_Size()'
    '0A000042' = '[accoremgd]Autodesk.AutoCAD.Windows.Window::get_DeviceIndependentSize()'
    '0A000043' = '[System.Drawing]System.Drawing.Size::get_Width()'
    '0A000044' = '[System.Drawing]System.Drawing.Size::get_Height()'
    '0A000045' = '[WindowsBase]System.Windows.Size::get_Width()'
    '0A000046' = '[WindowsBase]System.Windows.Size::get_Height()'
    '0A000047' = '[accoremgd]Autodesk.AutoCAD.ApplicationServices.Core.Application::GetSystemVariable(string)'
    '0A000048' = '[mscorlib]System.Convert::ToString(object, class [mscorlib]System.IFormatProvider)'
    '0A000049' = '[Acdbmgd]Autodesk.AutoCAD.Runtime.Exception::get_ErrorStatus()'
    '0A00004A' = '[mscorlib]System.String::Concat(string, string, string)'
    '0A00004B' = '[mscorlib]System.Double::ToString(string, class [mscorlib]System.IFormatProvider)'
    '0A00004C' = '[mscorlib]System.ObjectDisposedException::.ctor(string)'
    '0A00004D' = '[mscorlib]System.Guid::.ctor(string)'
    '0A00004E' = '[PresentationFramework]System.Windows.Controls.UserControl::.ctor()'
    '0A00004F' = '[PresentationCore]System.Windows.Media.Brushes::get_WhiteSmoke()'
    '0A000050' = '[PresentationFramework]System.Windows.Controls.Control::set_Background(class [PresentationCore]System.Windows.Media.Brush)'
    '0A000051' = '[PresentationFramework]System.Windows.Controls.Grid::.ctor()'
    '0A000052' = '[PresentationFramework]System.Windows.Thickness::.ctor(float64)'
    '0A000053' = '[PresentationFramework]System.Windows.FrameworkElement::set_Margin(valuetype [PresentationFramework]System.Windows.Thickness)'
    '0A000054' = '[PresentationFramework]System.Windows.Controls.Grid::get_RowDefinitions()'
    '0A000055' = '[PresentationFramework]System.Windows.Controls.RowDefinition::.ctor()'
    '0A000056' = '[PresentationFramework]System.Windows.GridLength::get_Auto()'
    '0A000057' = '[PresentationFramework]System.Windows.Controls.RowDefinition::set_Height(valuetype [PresentationFramework]System.Windows.GridLength)'
    '0A000058' = '[PresentationFramework]System.Windows.Controls.RowDefinitionCollection::Add(class [PresentationFramework]System.Windows.Controls.RowDefinition)'
    '0A000059' = '[PresentationFramework]System.Windows.GridLength::.ctor(float64, valuetype [PresentationFramework]System.Windows.GridUnitType)'
    '0A00005A' = '[PresentationFramework]System.Windows.Controls.TextBlock::.ctor()'
    '0A00005B' = '[PresentationFramework]System.Windows.Controls.TextBlock::set_Text(string)'
    '0A00005C' = '[PresentationFramework]System.Windows.Controls.TextBlock::set_FontSize(float64)'
    '0A00005D' = '[PresentationCore]System.Windows.FontWeights::get_SemiBold()'
    '0A00005E' = '[PresentationFramework]System.Windows.Controls.TextBlock::set_FontWeight(valuetype [PresentationCore]System.Windows.FontWeight)'
    '0A00005F' = '[PresentationCore]System.Windows.Media.Brushes::get_Black()'
    '0A000060' = '[PresentationFramework]System.Windows.Controls.TextBlock::set_Foreground(class [PresentationCore]System.Windows.Media.Brush)'
    '0A000061' = '[PresentationFramework]System.Windows.Thickness::.ctor(float64, float64, float64, float64)'
    '0A000062' = '[PresentationFramework]System.Windows.Controls.Grid::SetRow(class [PresentationCore]System.Windows.UIElement, int32)'
    '0A000063' = '[PresentationFramework]System.Windows.Controls.Panel::get_Children()'
    '0A000064' = '[PresentationFramework]System.Windows.Controls.UIElementCollection::Add(class [PresentationCore]System.Windows.UIElement)'
    '0A000065' = '[PresentationFramework]System.Windows.Controls.TextBlock::set_TextWrapping(valuetype [PresentationCore]System.Windows.TextWrapping)'
    '0A000066' = '[PresentationCore]System.Windows.Media.Brushes::get_DarkRed()'
    '0A000067' = '[PresentationFramework]System.Windows.Controls.TextBox::.ctor()'
    '0A000068' = '[PresentationFramework]System.Windows.Controls.TextBox::set_Text(string)'
    '0A000069' = '[PresentationFramework]System.Windows.Controls.Primitives.TextBoxBase::set_AcceptsReturn(bool)'
    '0A00006A' = '[PresentationFramework]System.Windows.Controls.Primitives.TextBoxBase::set_AcceptsTab(bool)'
    '0A00006B' = '[PresentationFramework]System.Windows.Controls.TextBox::set_TextWrapping(valuetype [PresentationCore]System.Windows.TextWrapping)'
    '0A00006C' = '[PresentationFramework]System.Windows.Controls.Primitives.TextBoxBase::set_VerticalScrollBarVisibility(valuetype [PresentationFramework]System.Windows.Controls.ScrollBarVisibility)'
    '0A00006D' = '[PresentationFramework]System.Windows.Controls.Primitives.TextBoxBase::set_HorizontalScrollBarVisibility(valuetype [PresentationFramework]System.Windows.Controls.ScrollBarVisibility)'
    '0A00006E' = '[PresentationFramework]System.Windows.Controls.Control::set_VerticalContentAlignment(valuetype [PresentationFramework]System.Windows.VerticalAlignment)'
    '0A00006F' = '[PresentationFramework]System.Windows.FrameworkElement::set_MinHeight(float64)'
    '0A000070' = '[PresentationFramework]System.Windows.Controls.Control::set_Padding(valuetype [PresentationFramework]System.Windows.Thickness)'
    '0A000071' = '[PresentationFramework]System.Windows.Controls.Control::set_FontSize(float64)'
    '0A000072' = '[PresentationFramework]System.Windows.Controls.Primitives.TextBoxBase::set_IsUndoEnabled(bool)'
    '0A000073' = '[PresentationCore]System.Windows.Input.InputMethod::SetIsInputMethodEnabled(class [WindowsBase]System.Windows.DependencyObject, bool)'
    '0A000074' = '[PresentationCore]System.Windows.Media.Brushes::get_DimGray()'
    '0A000075' = '[PresentationFramework]System.Windows.Controls.ContentControl::set_Content(object)'
}
$expectedCommandAttributes = [ordered]@{
    '06000004' = "Autodesk.AutoCAD.Runtime.CommandMethodAttribute::.ctor(string, valuetype [accoremgd]Autodesk.AutoCAD.Runtime.CommandFlags) = {string('CODEX16PAL') int32(0)}"
    '06000005' = "Autodesk.AutoCAD.Runtime.CommandMethodAttribute::.ctor(string, valuetype [accoremgd]Autodesk.AutoCAD.Runtime.CommandFlags) = {string('CODEX16PALINFO') int32(0)}"
    '06000006' = "Autodesk.AutoCAD.Runtime.CommandMethodAttribute::.ctor(string, valuetype [accoremgd]Autodesk.AutoCAD.Runtime.CommandFlags) = {string('CODEX16PALRESET') int32(0)}"
}

$verificationRoot = Join-Path $repoRoot ("artifacts\autocad2016-palette-verify-{0}" -f [Guid]::NewGuid().ToString('N'))
$outputDirectory = Join-Path $verificationRoot 'bin'
$baseIntermediateDirectory = Join-Path $verificationRoot 'obj-base'
$intermediateDirectory = Join-Path $verificationRoot 'obj-compile'
$projectExtensionsDirectory = Join-Path $verificationRoot 'obj-project-extensions'
$packageCache = Join-Path $verificationRoot 'packages'
$dotnetCliHome = Join-Path $verificationRoot 'dotnet-state\cli-home'
$dotnetNuGetPackages = Join-Path $verificationRoot 'dotnet-state\packages'
$dotnetHttpCache = Join-Path $verificationRoot 'dotnet-state\http-cache'

$strictUtf8 = New-Object Text.UTF8Encoding($false, $true)

function Read-Utf8File {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [IO.File]::ReadAllText($Path, $script:strictUtf8)
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory
    )

    $previousLocation = $null
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $previousLocation = Get-Location
        Set-Location -LiteralPath $WorkingDirectory
    }
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        if ($null -ne $previousLocation) {
            Set-Location -LiteralPath $previousLocation.Path
        }
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
        Text = $output -join "`n"
    }
}

function Invoke-DotNetIsolated {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $isolatedEnvironment = [ordered]@{
        DOTNET_CLI_HOME = $script:dotnetCliHome
        NUGET_PACKAGES = $script:dotnetNuGetPackages
        NUGET_HTTP_CACHE_PATH = $script:dotnetHttpCache
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
    }
    $originalEnvironment = @{}
    try {
        foreach ($entry in $isolatedEnvironment.GetEnumerator()) {
            $originalEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, [EnvironmentVariableTarget]::Process)
        }
        return Invoke-NativeCapture -FilePath $FilePath -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    }
    finally {
        foreach ($entry in $isolatedEnvironment.GetEnumerator()) {
            if ($null -eq $originalEnvironment[$entry.Key]) {
                Remove-Item -LiteralPath ("Env:{0}" -f $entry.Key) -ErrorAction SilentlyContinue
            }
            else {
                [Environment]::SetEnvironmentVariable($entry.Key, $originalEnvironment[$entry.Key], [EnvironmentVariableTarget]::Process)
            }
        }
    }
}

function Get-TrustedMicrosoftTool {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$DescriptionPattern,
        [int]$MinimumMajorVersion = 0,
        [int]$MaximumMajorVersion = 2147483647
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found at the resolved path: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.VersionInfo.CompanyName -cne 'Microsoft Corporation') {
        throw "$Label must have exact Microsoft Corporation company metadata: $($item.FullName)"
    }
    if ($item.VersionInfo.FileDescription -notmatch $DescriptionPattern) {
        throw "$Label has an unexpected file description '$($item.VersionInfo.FileDescription)': $($item.FullName)"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $item.FullName
    if ($signature.Status.ToString() -cne 'Valid' -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?i)(?:^|,\s*)O="?Microsoft Corporation"?(?:,|$)') {
        throw "$Label must carry a valid Microsoft Authenticode signature; status was $($signature.Status): $($item.FullName)"
    }

    $versionMatch = [regex]::Match([string]$item.VersionInfo.FileVersion, '\d+(?:[\.,]\d+){1,3}')
    if (-not $versionMatch.Success) {
        throw "$Label file version could not be parsed: $($item.VersionInfo.FileVersion)"
    }
    $versionParts = @($versionMatch.Value.Replace(',', '.') -split '\.')
    while ($versionParts.Count -lt 4) { $versionParts += '0' }
    $version = New-Object Version (($versionParts | Select-Object -First 4) -join '.')
    if ($version.Major -lt $MinimumMajorVersion -or $version.Major -gt $MaximumMajorVersion) {
        throw "$Label major version must be in [$MinimumMajorVersion, $MaximumMajorVersion], got $version at $($item.FullName)"
    }

    [pscustomobject]@{
        Name = $Label
        Path = $item.FullName
        FileVersion = $item.VersionInfo.FileVersion
        ProductVersion = $item.VersionInfo.ProductVersion
        Version = $version.ToString()
        CompanyName = $item.VersionInfo.CompanyName
        FileDescription = $item.VersionInfo.FileDescription
        Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        SignatureStatus = $signature.Status.ToString()
        SignerSubject = $signature.SignerCertificate.Subject
        SignerThumbprint = $signature.SignerCertificate.Thumbprint
    }
}

function Get-DirectoryManifestHash {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return '<absent>'
    }

    $root = (Get-Item -LiteralPath $Path).FullName.TrimEnd('\')
    $lines = @(
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)) {
            $relativePath = $file.FullName.Substring($root.Length).TrimStart('\')
            '{0}|{1}|{2}' -f $relativePath, $file.Length, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    )
    $bytes = [Text.Encoding]::UTF8.GetBytes($lines -join "`n")
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE file: $Path" }
            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $Path" }
            $machine = $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    switch ($machine) {
        0x8664 { 'x64' }
        0x014C { 'x86' }
        0xAA64 { 'arm64' }
        default { '0x{0:X4}' -f $machine }
    }
}

function Get-TrustedAutodeskFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][bool]$RequireAssemblyVersion
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required AutoCAD 2016 file is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.VersionInfo.FileVersion -notmatch '^R?20\.1\.') {
        throw "Expected AutoCAD R20.1 file version, got '$($item.VersionInfo.FileVersion)' for $Path"
    }
    if ($item.VersionInfo.CompanyName -ine 'Autodesk, Inc.') {
        throw "Expected exact Autodesk, Inc. company metadata for $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?i)(?:^|,\s*)O=(?:"Autodesk, Inc"|Autodesk, Inc\.?)(?:,|$)') {
        throw "Expected a valid Autodesk Authenticode signature for $Path; status was $($signature.Status)."
    }

    $assemblyVersion = $null
    if ($RequireAssemblyVersion) {
        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($item.FullName).Version.ToString()
        if ($assemblyVersion -ne '20.1.0.0') {
            throw "Expected AutoCAD 2016 assembly version 20.1.0.0, got '$assemblyVersion' for $Path"
        }
    }

    [pscustomobject]@{
        Name = $item.Name
        FileVersion = $item.VersionInfo.FileVersion
        AssemblyVersion = $assemblyVersion
        Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        SignatureStatus = $signature.Status.ToString()
        SignerSubject = $signature.SignerCertificate.Subject
        SignerThumbprint = $signature.SignerCertificate.Thumbprint
    }
}

function Get-IlMemberReferenceMap {
    param([Parameter(Mandatory = $true)][string]$IlText)

    $codeOnly = [regex]::Replace($IlText, '//[^\r\n]*(?=\r|\n|$)', '')
    $flat = [regex]::Replace($codeOnly, '\s+', ' ')
    $pattern = '(?<owner>(?:\[[^\]]+\])?[A-Za-z0-9_.$+`''/<>]+(?:/\*[0-9A-Fa-f]{8}\*/)?)(?:/\*[0-9A-Fa-f]{8}\*/)?::(?<method>[A-Za-z0-9_.$+`''<>]+)\s*\((?<args>[^)]{0,1000})\)\s*/\*\s*(?<token>0A[0-9A-Fa-f]{6})\s*\*/'
    $map = @{}
    foreach ($match in [regex]::Matches($flat, $pattern)) {
        $owner = [regex]::Replace($match.Groups['owner'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $owner = [regex]::Replace($owner, '\[([^/\]]+)(?:/\*[0-9A-Fa-f]{8}\*/)?\]', '[$1]')
        $arguments = [regex]::Replace($match.Groups['args'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $arguments = [regex]::Replace($arguments, '\[([^/\]]+)(?:/\*[0-9A-Fa-f]{8}\*/)?\]', '[$1]')
        $arguments = [regex]::Replace($arguments, '\s+', ' ').Trim()
        $token = $match.Groups['token'].Value.ToUpperInvariant()
        $canonical = '{0}::{1}({2})' -f $owner, $match.Groups['method'].Value, $arguments
        if ($map.ContainsKey($token) -and $map[$token] -cne $canonical) {
            throw "Ildasm emitted conflicting identities for MemberRef ${token}: '$($map[$token])' and '$canonical'."
        }
        $map[$token] = $canonical
    }
    $hiddenDebuggablePattern = '(?m)^\s*//\s+\.custom\s+/\*0C000003:0A000003\*/\s+instance\s+void\s+\[mscorlib/\*23000001\*/\]System\.Diagnostics\.DebuggableAttribute/\*01000003\*/::\.ctor\(valuetype\s+\[mscorlib/\*23000001\*/\]System\.Diagnostics\.DebuggableAttribute/\*01000003\*//DebuggingModes/\*01000004\*/\)\s+/\*\s*0A000003\s*\*/\s*$'
    $hiddenDebuggableMatches = @([regex]::Matches($IlText, $hiddenDebuggablePattern))
    if ($hiddenDebuggableMatches.Count -eq 1) {
        $map['0A000003'] = '[mscorlib]System.Diagnostics.DebuggableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggableAttribute/DebuggingModes)'
    }
    elseif ($hiddenDebuggableMatches.Count -gt 1) {
        throw 'Ildasm emitted duplicate hidden DebuggableAttribute MemberRefs.'
    }
    return $map
}

function Get-IlMethodDefinitions {
    param([Parameter(Mandatory = $true)][string]$IlText)

    $definitions = @()
    $pattern = '(?s)\.method\s+/\*(?<token>060[0-9A-Fa-f]{5})\*/(?<body>.*?)}\s*//\s*end of method\s+(?<name>[^\r\n]+)'
    foreach ($match in [regex]::Matches($IlText, $pattern)) {
        $header = ($match.Groups['body'].Value -split '\{', 2)[0]
        $header = [regex]::Replace($header, '/\*[0-9A-Fa-f]{8}\*/', '')
        $header = [regex]::Replace($header, '\s+', ' ').Trim()
        $bodyCanonical = [regex]::Replace($match.Groups['body'].Value, '/\*[0-9A-Fa-f]{8}\*/', '')
        $bodyCanonical = [regex]::Replace($bodyCanonical, '\s+', ' ').Trim()
        $definitions += [pscustomobject]@{
            Token = $match.Groups['token'].Value.ToUpperInvariant()
            Name = $match.Groups['name'].Value.Trim()
            Header = $header
            BodyCanonical = $bodyCanonical
        }
    }
    return $definitions
}

$forbiddenSourceRules = @(
    [pscustomobject]@{ Category = 'CAD database, selection, or transaction'; Pattern = '(?i)(?:Autodesk\s*\.\s*AutoCAD\s*\.\s*DatabaseServices|\bDocumentLock\b|\bLockDocument\s*\(|\bTransaction(?:Manager)?\b|\bStart(?:OpenClose)?Transaction\s*\(|\bOpenMode\s*\.\s*For(?:Read|Write)\b|\bObjectId\b|\bDBObject\b|\bEntity\b|\bBlockTable\b|\bLayerTable\b|\bAppendEntity\s*\(|\bAddNewlyCreatedDBObject\s*\(|\bErase\s*\(|\bEditor\s*\.\s*(?:GetSelection|SelectImplied|SetImpliedSelection|GetEntity|GetPoint|GetKeywords)\s*\(|\bSelectionSet\b|\bPromptSelection\w*\b)' }
    [pscustomobject]@{ Category = 'save, command injection, or application mutation'; Pattern = '(?i)(?:\bSave\s*\(|\bSaveAs\s*\(|\bDwgOut\s*\(|\bDxfOut\s*\(|\bQuit\s*\(|\bInvoke\s*\(|\bCloseAndSave\s*\(|\bSetSystemVariable\s*\(|\bSendStringToExecute\s*\(|\bExecuteInCommandContextAsync\s*\(|\.\s*Command(?:Async)?\s*\(|["''](?:_+|\.)?(?:QSAVE|SAVEAS|SAVE|WBLOCK|ERASE|LINE|PLINE)["''])' }
    [pscustomobject]@{ Category = 'process or shell'; Pattern = '(?i)(?:\bSystem\s*\.\s*Diagnostics\b|\bProcessStartInfo\b|\bProcess\s*\.\s*Start\s*\(|\bShellExecute\b|\bCreateProcess\b|\bcmd(?:\.exe)?\b|\bpowershell(?:\.exe)?\b)' }
    [pscustomobject]@{ Category = 'IPC or network'; Pattern = '(?i)(?:\bSystem\s*\.\s*IO\s*\.\s*Pipes\b|\bNamedPipe\w*\b|\bAnonymousPipe\w*\b|\bPipeStream\b|\bMemoryMappedFile\b|\bSystem\s*\.\s*Net\b|\bHttpClient\b|\bWebRequest\b|\bWebClient\b|\bHttpListener\b|\bSocket\b|\bTcpClient\b|\bUdpClient\b|\\\\\.\\pipe\\)' }
    [pscustomobject]@{ Category = 'file system or registry'; Pattern = '(?i)(?:\bSystem\s*\.\s*IO\s*\.\s*(?:File|Directory|FileInfo|DirectoryInfo|FileStream|StreamReader|StreamWriter|Path)\b|\bFile\s*\.\s*\w+\s*\(|\bDirectory\s*\.\s*\w+\s*\(|\bMicrosoft\s*\.\s*Win32\b|\bRegistry(?:Key)?\b)' }
    [pscustomobject]@{ Category = 'runtime reflection, native, or dynamic execution'; Pattern = '(?i)(?:\bSystem\s*\.\s*Reflection\b|\.\s*Assembly\b|\bAssembly\s*\.\s*Load(?:From|File)?\s*\(|\bGetName\s*\(|\bType\s*\.\s*GetType\s*\(|\bActivator\s*\.\s*CreateInstance\s*\(|\bGetMethod\s*\(|\bGetProperty\s*\(|\bMethodInfo\b|\bPropertyInfo\b|\bFieldInfo\b|\.\s*Invoke\s*\(|\bDllImport\b|\bMarshal\s*\.|\bLoadLibrary\b|\bGetProcAddress\b|\bunsafe\b|\bdynamic\b)' }
    [pscustomobject]@{ Category = 'background execution'; Pattern = '(?i)(?:\bSystem\s*\.\s*Threading\b|\bTask\b|\bThread\b|\bThreadPool\b|\bBackgroundWorker\b|\bDispatcherTimer\b|\bTimer\b|\basync\b|\bawait\b)' }
    [pscustomobject]@{ Category = 'authentication or Agent coupling'; Pattern = '(?i)(?:\bSystem\s*\.\s*Security\s*\.\s*Cryptography\b|\bHMAC\w*\b|\bRandomNumberGenerator\b|\bProtectedData\b|\bCadApprovalGate\b|\bIAgentBridgeClient\b|\bCodex\s*\.\s*AutoCAD\s*\.\s*(?:Bridge|AgentRuntime|Ipc|Security)\b)' }
    [pscustomobject]@{ Category = 'dynamic WPF content or browser'; Pattern = '(?i)(?:\bXamlReader\b|\bLoadComponent\s*\(|\bWebBrowser\b|\bFrame\b|\bNavigationService\b|\bResourceDictionary\s*\.\s*Source\b)' }
    [pscustomobject]@{ Category = 'document identity or content access'; Pattern = '(?i)(?:\b(?:document|doc|eventArgs\s*\.\s*Document)\s*\.\s*(?:Name|Database|TransactionManager)\b|\b(?:database|db)\s*\.\s*(?:Filename|OriginalFileName|SummaryInfo)\b|\bDocumentWindow\b|\bHostApplicationServices\s*\.\s*WorkingDatabase\b)' }
)

function Get-ForbiddenSourceMatches {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text,
        [switch]$AssemblyInfo
    )

    $violations = @()
    foreach ($rule in $script:forbiddenSourceRules) {
        if ($AssemblyInfo -and $rule.Category -eq 'runtime reflection, native, or dynamic execution') {
            continue
        }
        foreach ($match in [regex]::Matches($Text, $rule.Pattern)) {
            $prefix = $Text.Substring(0, $match.Index)
            $lineNumber = ([regex]::Matches($prefix, "`n").Count + 1)
            $violations += [pscustomobject]@{
                Category = $rule.Category
                File = $Path
                Line = $lineNumber
                Text = $match.Value
            }
        }
    }
    return $violations
}

function Assert-NoHighRiskMemberReferences {
    param([Parameter(Mandatory = $true)][string[]]$Signatures)

    $pattern = '(?i)(?:Autodesk\.AutoCAD\.(?:DatabaseServices|EditorInput)\.[^:]*::(?:Save|SaveAs|DwgOut|DxfOut|LockDocument|Start(?:OpenClose)?Transaction|GetSelection|SelectImplied|SetImpliedSelection|GetEntity|AppendEntity|AddNewlyCreatedDBObject|Erase|UpgradeOpen|Open)\s*\(|Autodesk\.AutoCAD\.[^:]*::(?:Quit|Invoke|SetSystemVariable|SendStringToExecute|ExecuteInCommandContextAsync|Command|CommandAsync)\s*\(|System\.Diagnostics\.(?:Process|ProcessStartInfo|EventLog)[^:]*::|System\.IO\.(?:File|Directory|FileInfo|DirectoryInfo|FileStream|StreamReader|StreamWriter|Path)[^:]*::|System\.Net\.[^:]*::|System\.Reflection\.(?:Assembly|AssemblyName|MethodInfo|PropertyInfo|FieldInfo|TypeInfo)::|System\.Threading\.[^:]*::|System\.Security\.Cryptography\.[^:]*::|Microsoft\.Win32\.[^:]*::)'
    $violations = @($Signatures | Where-Object { $_ -match $pattern })
    if ($violations.Count -ne 0) {
        throw "High-risk Palette MemberRef rejected: $($violations -join ', ')"
    }
}

function Invoke-VerificationRuleSelfTests {
    $safeSource = @'
PaletteSet palette = new PaletteSet("Codex", new Guid("173d39c8-85d9-45fc-845f-e0520f8cddcc"));
palette.AddVisual("Codex", panel, true);
object dbmod = AutoCadApplication.GetSystemVariable("DBMOD");
documents.DocumentActivated += OnDocumentActivated;
'@
    if (@(Get-ForbiddenSourceMatches -Path '<safe-sample>' -Text $safeSource).Count -ne 0) {
        throw 'Verifier self-test failed: the safe Palette/WPF/DBMOD/document-event sample was rejected.'
    }

    $dangerousSourceSamples = [ordered]@{
        'CAD write' = 'transaction.GetObject(id, OpenMode.ForWrite);'
        'command string' = 'document.SendStringToExecute("_.LINE", true, false, false);'
        'process' = 'System.Diagnostics.Process.Start("cmd.exe");'
        'IPC' = 'new NamedPipeClientStream(".", "pipe");'
        'file' = 'System.IO.File.WriteAllText(path, text);'
        'network' = 'new System.Net.Http.HttpClient();'
        'registry' = 'Microsoft.Win32.Registry.CurrentUser.OpenSubKey("x");'
        'reflection' = 'typeof(X).Assembly.GetName();'
        'background' = 'System.Threading.Tasks.Task.Run(action);'
        'selection' = 'editor.GetSelection();'
        'document identity' = 'string name = eventArgs.Document.Name;'
    }
    foreach ($sample in $dangerousSourceSamples.GetEnumerator()) {
        if (@(Get-ForbiddenSourceMatches -Path ("<dangerous-{0}>" -f $sample.Key) -Text $sample.Value).Count -eq 0) {
            throw "Verifier self-test failed to reject dangerous source sample '$($sample.Key)'."
        }
    }

    $commentSpoof = '// System.IO.File.WriteAllText(path, secret);'
    if (@(Get-ForbiddenSourceMatches -Path '<comment-spoof>' -Text $commentSpoof).Count -eq 0) {
        throw 'Verifier self-test failed: dangerous source text hidden in a comment was not rejected.'
    }

    $commentOnlyIl = '// IL_0000: call void [mscorlib]System.IO.File::WriteAllText(string, string) /* 0A000020 */'
    if ((Get-IlMemberReferenceMap -IlText $commentOnlyIl).Count -ne 0) {
        throw 'Verifier self-test failed: an IL comment was accepted as a real MemberRef.'
    }

    $activeDangerousIl = 'IL_0000: call void [mscorlib/*23000001*/]System.IO.File/*01000020*/::WriteAllText(string, string) /* 0A000020 */'
    $activeMap = Get-IlMemberReferenceMap -IlText $activeDangerousIl
    $rejected = $false
    try {
        Assert-NoHighRiskMemberReferences -Signatures @($activeMap.Values)
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'Verifier self-test failed: an active file-write MemberRef was not rejected.'
    }

    [pscustomobject]@{
        Passed = $true
        DangerousSourceSamplesRejected = @($dangerousSourceSamples.Keys)
        DangerousCommentTextRejected = $true
        IlCommentSpoofRejected = $true
        ActiveDangerousIlRejected = $true
    }
}

$ruleSelfTestEvidence = Invoke-VerificationRuleSelfTests

if ($RuleSelfTestOnly) {
    [pscustomobject]@{
        Ok = $true
        Status = 'palette-verifier-rule-self-tests-only'
        VerifierSelfTests = $ruleSelfTestEvidence
        NetLoadVerified = $false
        CadProcessStartedOrRestarted = $false
        CadCommandsSent = $false
    } | ConvertTo-Json -Depth 6
    return
}

$freezeValues = @(
    $expectedProjectSha256,
    $expectedSolutionSha256,
    $expectedNuGetConfigSha256,
    $expectedPackageLockSha256,
    $expectedCandidateSha256
) + @($expectedSourceHashes.Values) + $expectedOutputReferences + @($expectedMethodDefinitions.Keys) + @($expectedMemberReferences.Keys) + @($expectedCommandAttributes.Keys)
if (@($freezeValues | Where-Object { $_ -match '^PENDING_' }).Count -ne 0) {
    throw 'Palette verifier is intentionally fail-closed until project/source/output/IL freeze values replace every PENDING_* marker.'
}

foreach ($directory in @(
    $outputDirectory,
    $baseIntermediateDirectory,
    $intermediateDirectory,
    $projectExtensionsDirectory,
    $packageCache,
    $dotnetCliHome,
    $dotnetNuGetPackages,
    $dotnetHttpCache
)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $msbuildCommand = Get-Command msbuild.exe -ErrorAction Stop | Select-Object -First 1
    $MsBuildPath = $msbuildCommand.Source
}
$MsBuildPath = [IO.Path]::GetFullPath($MsBuildPath)
$msbuildEvidence = Get-TrustedMicrosoftTool -Path $MsBuildPath -Label 'MSBuild' -DescriptionPattern '^MSBuild\.exe$' -MinimumMajorVersion 17 -MaximumMajorVersion 17

$programFiles64 = [Environment]::GetEnvironmentVariable('ProgramW6432')
if ([string]::IsNullOrWhiteSpace($programFiles64)) {
    $programFiles64 = [Environment]::GetEnvironmentVariable('ProgramFiles')
}
if ([string]::IsNullOrWhiteSpace($programFiles64)) {
    throw 'The 64-bit Program Files directory is unavailable; trusted dotnet discovery cannot continue.'
}
$dotnetPath = Join-Path $programFiles64 'dotnet\dotnet.exe'
$dotnetEvidence = Get-TrustedMicrosoftTool -Path $dotnetPath -Label 'dotnet' -DescriptionPattern '^\.NET Host$' -MinimumMajorVersion 8
$dotnetVersionResult = Invoke-DotNetIsolated -FilePath $dotnetPath -Arguments @('--version') -WorkingDirectory $repoRoot
$resolvedDotnetSdk = $dotnetVersionResult.Text.Trim()
if ($dotnetVersionResult.ExitCode -ne 0 -or $resolvedDotnetSdk -cne '8.0.319') {
    throw "Repository must resolve exactly .NET SDK 8.0.319 from repoRoot; got '$resolvedDotnetSdk'."
}

foreach ($requiredFile in @($projectPath, $solutionPath, $mainSolutionPath, $diagnosticSolutionPath, $nuGetConfigPath, $packageLockPath, $vendoredPackagePath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required Palette verification input is missing: $requiredFile"
    }
}

$projectSha256 = (Get-FileHash -LiteralPath $projectPath -Algorithm SHA256).Hash
$solutionSha256 = (Get-FileHash -LiteralPath $solutionPath -Algorithm SHA256).Hash
$nuGetConfigSha256 = (Get-FileHash -LiteralPath $nuGetConfigPath -Algorithm SHA256).Hash
$packageLockSha256 = (Get-FileHash -LiteralPath $packageLockPath -Algorithm SHA256).Hash
if ($projectSha256 -cne $expectedProjectSha256) {
    throw 'Palette project file changed; implicit build-property, reference, target, or source injection is rejected until the project hash is reviewed.'
}
if ($solutionSha256 -cne $expectedSolutionSha256) {
    throw 'Palette solution changed; only the frozen dedicated solution is accepted.'
}
if ($nuGetConfigSha256 -cne $expectedNuGetConfigSha256 -or $packageLockSha256 -cne $expectedPackageLockSha256) {
    throw 'Palette project-local NuGet.Config or packages.lock.json changed; dependency restoration is rejected until the hashes are reviewed.'
}

foreach ($unrelatedSolutionPath in @($mainSolutionPath, $diagnosticSolutionPath)) {
    $unrelatedSolutionText = Read-Utf8File -Path $unrelatedSolutionPath
    if ($unrelatedSolutionText -match '(?i)Codex\.AutoCAD\.Host\.2016\.Palette' -or
        $unrelatedSolutionText -match [regex]::Escape($projectGuid)) {
        throw "The Palette project must remain absent from unrelated solution '$unrelatedSolutionPath'."
    }
}

$solutionText = Read-Utf8File -Path $solutionPath
$solutionProjectMatches = @([regex]::Matches($solutionText, '(?m)^Project\("[^"]+"\)\s*=\s*"([^"]+)",\s*"([^"]+\.csproj)",\s*"(\{[A-Fa-f0-9-]+\})"'))
if ($solutionProjectMatches.Count -ne 1 -or
    $solutionProjectMatches[0].Groups[1].Value -cne $expectedAssemblyName -or
    $solutionProjectMatches[0].Groups[2].Value.Replace('/', '\') -ine 'src\Codex.AutoCAD.Host.2016.Palette\Codex.AutoCAD.Host.2016.Palette.csproj' -or
    $solutionProjectMatches[0].Groups[3].Value -ine $projectGuid) {
    throw 'Codex.AutoCAD.2016.Palette.sln must contain exactly the reviewed Palette project and no other build project.'
}
$expectedSolutionMappings = @(
    "$projectGuid.Debug|Any CPU.ActiveCfg = Debug|x64",
    "$projectGuid.Debug|Any CPU.Build.0 = Debug|x64",
    "$projectGuid.Release|Any CPU.ActiveCfg = Release|x64",
    "$projectGuid.Release|Any CPU.Build.0 = Release|x64"
)
$actualSolutionMappings = @($solutionText -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "$projectGuid.*" })
if (@(Compare-Object -ReferenceObject $expectedSolutionMappings -DifferenceObject $actualSolutionMappings).Count -ne 0) {
    throw "Palette solution must map Debug/Release Any CPU exclusively to project x64.`n$($actualSolutionMappings -join [Environment]::NewLine)"
}

$acadEvidence = Get-TrustedAutodeskFile -Path (Join-Path $AutoCad2016Dir 'acad.exe') -RequireAssemblyVersion $false
if ((Get-PeMachine -Path (Join-Path $AutoCad2016Dir 'acad.exe')) -ne 'x64') {
    throw 'The target AutoCAD 2016 process must be x64 for this Palette build.'
}
$managedApiNames = @('accoremgd.dll', 'acdbmgd.dll', 'acmgd.dll')
$managedApiEvidence = @(
    foreach ($managedApiName in $managedApiNames) {
        Get-TrustedAutodeskFile -Path (Join-Path $AutoCad2016Dir $managedApiName) -RequireAssemblyVersion $true
    }
)

$projectText = Read-Utf8File -Path $projectPath
[xml]$project = $projectText
$namespaceManager = New-Object Xml.XmlNamespaceManager($project.NameTable)
$namespaceManager.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')

$exactProjectProperties = [ordered]@{
    TargetFrameworkVersion = 'v4.5'
    PlatformTarget = 'x64'
    Platforms = 'x64'
    Prefer32Bit = 'false'
    LangVersion = '7.3'
    Nullable = 'disable'
    ImplicitUsings = 'disable'
    AutoGenerateBindingRedirects = 'false'
    Deterministic = 'true'
    ContinuousIntegrationBuild = 'true'
    TreatWarningsAsErrors = 'true'
    RestoreProjectStyle = 'PackageReference'
    RestorePackagesWithLockFile = 'true'
    RestoreLockedMode = 'true'
    RestoreConfigFile = '$(MSBuildThisFileDirectory)NuGet.Config'
    NuGetLockFilePath = '$(MSBuildThisFileDirectory)packages.lock.json'
    AssemblyName = $expectedAssemblyName
    RootNamespace = $expectedRootNamespace
}
foreach ($requiredProperty in $exactProjectProperties.GetEnumerator()) {
    $nodes = @($project.SelectNodes("//msb:$($requiredProperty.Key)", $namespaceManager))
    if ($nodes.Count -ne 1 -or $nodes[0].InnerText -cne $requiredProperty.Value) {
        throw "Palette project property '$($requiredProperty.Key)' must equal '$($requiredProperty.Value)' exactly once."
    }
}

$commonPropsImportText = '<Import Project="$(MSBuildToolsPath)\Microsoft.Common.props"'
$commonPropsImportIndex = $projectText.IndexOf($commonPropsImportText, [StringComparison]::Ordinal)
if ($commonPropsImportIndex -lt 0) {
    throw 'Palette project is missing the reviewed Microsoft.Common.props import.'
}
foreach ($isolationProperty in @('ImportDirectoryBuildProps', 'ImportDirectoryBuildTargets')) {
    $nodes = @($project.SelectNodes("//msb:$isolationProperty", $namespaceManager))
    $rawPropertyText = "<$isolationProperty>false</$isolationProperty>"
    $rawPropertyIndex = $projectText.IndexOf($rawPropertyText, [StringComparison]::Ordinal)
    if ($nodes.Count -ne 1 -or $nodes[0].InnerText -cne 'false' -or
        $rawPropertyIndex -lt 0 -or $rawPropertyIndex -gt $commonPropsImportIndex) {
        throw "$isolationProperty=false must appear exactly once before Microsoft.Common.props."
    }
}
if ($project.SelectNodes('//msb:DirectoryBuildPropsPath | //msb:DirectoryBuildTargetsPath', $namespaceManager).Count -ne 0) {
    throw 'Palette project may not redirect Directory.Build props or targets imports.'
}

$releaseGroups = @($project.SelectNodes('//msb:PropertyGroup', $namespaceManager) | Where-Object { $null -ne $_.Attributes['Condition'] -and $_.GetAttribute('Condition') -match 'Release\|x64' })
if ($releaseGroups.Count -ne 1 -or
    $null -eq $releaseGroups[0].DebugSymbols -or $releaseGroups[0].DebugSymbols -cne 'false' -or
    $null -eq $releaseGroups[0].DebugType -or $releaseGroups[0].DebugType -cne 'none' -or
    $null -eq $releaseGroups[0].Optimize -or $releaseGroups[0].Optimize -cne 'true') {
    throw 'Palette Release|x64 must be optimized with DebugSymbols=false and DebugType=none.'
}

$allowedReferences = @(
    'System',
    'System.Core',
    'System.Drawing',
    'System.Xaml',
    'WindowsBase',
    'PresentationCore',
    'PresentationFramework',
    'accoremgd',
    'acdbmgd',
    'acmgd'
)
$referenceNodes = @($project.SelectNodes('//msb:Reference', $namespaceManager))
$referenceNames = @(
    foreach ($referenceNode in $referenceNodes) {
        ($referenceNode.Include -split ',', 2)[0].Trim()
    }
)
if (@(Compare-Object -ReferenceObject $allowedReferences -DifferenceObject $referenceNames).Count -ne 0 -or
    @($referenceNames | Sort-Object -Unique).Count -ne $allowedReferences.Count) {
    throw "Palette project assembly references differ from the exact allowlist: $($referenceNames -join ', ')."
}
foreach ($referenceName in @('accoremgd', 'acdbmgd', 'acmgd')) {
    $reference = $project.SelectSingleNode("//msb:Reference[starts-with(translate(@Include, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '$referenceName')]", $namespaceManager)
    $hintPathNode = $reference.SelectSingleNode('msb:HintPath', $namespaceManager)
    $specificVersionNode = $reference.SelectSingleNode('msb:SpecificVersion', $namespaceManager)
    $privateNode = $reference.SelectSingleNode('msb:Private', $namespaceManager)
    $expectedHintPath = '$(AutoCad2016Dir)\{0}.dll' -f $referenceName
    if ($null -eq $reference -or
        $null -eq $hintPathNode -or $hintPathNode.InnerText -ine $expectedHintPath -or
        $null -eq $specificVersionNode -or $specificVersionNode.InnerText -ine 'true' -or
        $null -eq $privateNode -or $privateNode.InnerText -ine 'false') {
        throw "Autodesk reference '$referenceName' must use the target-machine HintPath, SpecificVersion=true, and Private=false."
    }
}

$packageReferences = @($project.SelectNodes('//msb:PackageReference', $namespaceManager))
$packagePrivateAssets = if ($packageReferences.Count -eq 1) { $packageReferences[0].SelectSingleNode('msb:PrivateAssets', $namespaceManager) }
$packageIncludeAssets = if ($packageReferences.Count -eq 1) { $packageReferences[0].SelectSingleNode('msb:IncludeAssets', $namespaceManager) }
if ($packageReferences.Count -ne 1 -or
    $packageReferences[0].Include -ine 'Microsoft.NETFramework.ReferenceAssemblies.net45' -or
    $packageReferences[0].Version -ne '[1.0.3]' -or
    $null -eq $packagePrivateAssets -or $packagePrivateAssets.InnerText -ine 'all' -or
    $null -eq $packageIncludeAssets -or $packageIncludeAssets.InnerText -ine 'runtime;build;native;contentfiles;analyzers') {
    throw 'Palette project may use only the exact locked Microsoft.NETFramework.ReferenceAssemblies.net45 [1.0.3] compile-time package.'
}

foreach ($forbiddenItemType in @('ProjectReference', 'COMReference', 'NativeReference', 'Analyzer', 'UsingTask', 'Page', 'ApplicationDefinition')) {
    if ($project.SelectNodes("//msb:$forbiddenItemType", $namespaceManager).Count -ne 0) {
        throw "Palette project must not contain $forbiddenItemType items."
    }
}
if ($project.SelectNodes('//msb:Exec | //msb:CodeTaskFactory', $namespaceManager).Count -ne 0) {
    throw 'Palette project must not execute external programs or inline build tasks.'
}
$targetNodes = @($project.SelectNodes('//msb:Target', $namespaceManager))
$expectedTargetNames = @('ValidateAutoCad2016References', 'RejectAutodeskCopyLocal')
if (@(Compare-Object -ReferenceObject $expectedTargetNames -DifferenceObject @($targetNodes.Name)).Count -ne 0 -or
    @($targetNodes.Name | Sort-Object -Unique).Count -ne $expectedTargetNames.Count) {
    throw 'Palette project may contain only the two reviewed fail-closed validation targets.'
}
foreach ($targetNode in $targetNodes) {
    $taskElements = @($targetNode.ChildNodes | Where-Object { $_.NodeType -eq [Xml.XmlNodeType]::Element })
    if ($taskElements.Count -eq 0 -or @($taskElements | Where-Object { $_.LocalName -cne 'Error' }).Count -ne 0) {
        throw "Palette project target '$($targetNode.Name)' may contain only fail-closed Error tasks."
    }
}

$allowedImports = @(
    '$(MSBuildToolsPath)\Microsoft.Common.props',
    '$(MSBuildToolsPath)\Microsoft.CSharp.targets'
)
$projectImports = @($project.SelectNodes('//msb:Import', $namespaceManager) | ForEach-Object { $_.Project })
if (@(Compare-Object -ReferenceObject $allowedImports -DifferenceObject $projectImports).Count -ne 0 -or
    @($projectImports | Sort-Object -Unique).Count -ne $allowedImports.Count) {
    throw "Palette project contains unexpected or missing MSBuild imports: $($projectImports -join ', ')."
}

$compileItems = @($project.SelectNodes('//msb:Compile', $namespaceManager) | ForEach-Object { $_.Include.Replace('/', '\') })
if (@(Compare-Object -ReferenceObject $expectedCompileItems -DifferenceObject $compileItems).Count -ne 0 -or
    @($compileItems | Sort-Object -Unique).Count -ne $expectedCompileItems.Count) {
    throw 'Palette compile items differ from the exact reviewed allowlist.'
}

$projectDirectory = Split-Path -Parent $projectPath
$diskSourceItems = @(
    Get-ChildItem -LiteralPath $projectDirectory -Recurse -Filter '*.cs' -File |
        ForEach-Object { $_.FullName.Substring($projectDirectory.Length).TrimStart('\') }
)
if (@(Compare-Object -ReferenceObject $expectedCompileItems -DifferenceObject $diskSourceItems).Count -ne 0) {
    throw 'Palette source tree contains a .cs file that is not in the reviewed Compile allowlist, or a reviewed source is missing.'
}

$sourceHashEvidence = @()
$sourceViolations = @()
foreach ($compileItem in $expectedCompileItems) {
    $sourcePath = Join-Path $projectDirectory $compileItem
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Palette compile item is missing: $sourcePath"
    }
    $actualHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    if ($actualHash -cne $expectedSourceHashes[$compileItem]) {
        throw "Reviewed Palette source hash changed for '$compileItem'; source or comment spoofing is rejected."
    }
    $sourceHashEvidence += [pscustomobject]@{ Path = $sourcePath; Sha256 = $actualHash }
    $sourceText = Read-Utf8File -Path $sourcePath
    $sourceViolations += @(Get-ForbiddenSourceMatches -Path $sourcePath -Text $sourceText -AssemblyInfo:($compileItem -ceq 'Properties\AssemblyInfo.cs'))
}
if ($sourceViolations.Count -ne 0) {
    throw "Palette source contains forbidden APIs or data access:`n$($sourceViolations | Format-Table -AutoSize | Out-String)"
}

$nonAssemblySourcePaths = @($expectedCompileItems | Where-Object { $_ -cne 'Properties\AssemblyInfo.cs' } | ForEach-Object { Join-Path $projectDirectory $_ })
$getSystemVariableMatches = @(Select-String -LiteralPath $nonAssemblySourcePaths -Pattern '\bGetSystemVariable\s*\(' -AllMatches)
$getSystemVariableCount = ($getSystemVariableMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
$safeDbmodMatches = @(Select-String -LiteralPath $nonAssemblySourcePaths -Pattern 'AutoCadApplication\s*\.\s*GetSystemVariable\s*\(\s*"DBMOD"\s*\)' -AllMatches)
$safeDbmodCount = ($safeDbmodMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
if ($getSystemVariableCount -ne 1 -or $safeDbmodCount -ne 1) {
    throw 'Palette source may call GetSystemVariable exactly once, with the fixed read-only DBMOD literal only.'
}
if (@(Select-String -LiteralPath $nonAssemblySourcePaths -Pattern 'TRUSTEDPATHS|DWGNAME|DWGPREFIX|FILENAME|FULLNAME' -AllMatches).Count -ne 0) {
    throw 'Palette source may not query or mention path/name-bearing CAD variables.'
}

$declaredCommands = @()
foreach ($match in @(Select-String -LiteralPath $nonAssemblySourcePaths -Pattern '\[CommandMethod\s*\(\s*"([^"]+)"\s*,\s*CommandFlags\s*\.\s*Modal\s*\)\]' -AllMatches)) {
    foreach ($regexMatch in $match.Matches) {
        $declaredCommands += $regexMatch.Groups[1].Value
    }
}
if (@(Compare-Object -ReferenceObject $expectedCommands -DifferenceObject $declaredCommands).Count -ne 0 -or
    @($declaredCommands | Sort-Object -Unique).Count -ne $expectedCommands.Count) {
    throw 'Palette source command surface must be exactly CODEX16PAL, CODEX16PALINFO, and CODEX16PALRESET with Modal flags.'
}

$guidMatches = @(Select-String -LiteralPath $nonAssemblySourcePaths -Pattern ('(?i)' + [regex]::Escape($paletteGuidText)) -AllMatches)
$guidMentionCount = ($guidMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
if ($guidMentionCount -ne 1) {
    throw 'The permanent Palette GUID must appear exactly once in non-assembly source.'
}

$documentEventSubscriptions = @()
foreach ($match in @(Select-String -LiteralPath $nonAssemblySourcePaths -Pattern '\bdocuments\s*\.\s*(Document[A-Za-z0-9_]+)\s*(?:\+=|-=)' -AllMatches)) {
    foreach ($regexMatch in $match.Matches) {
        $documentEventSubscriptions += $regexMatch.Groups[1].Value
    }
}
$unexpectedDocumentEvents = @($documentEventSubscriptions | Where-Object { $_ -notin $expectedDocumentEvents })
if ($unexpectedDocumentEvents.Count -ne 0) {
    throw "Palette source subscribes to an unreviewed document event: $($unexpectedDocumentEvents -join ', ')."
}
foreach ($eventName in $expectedDocumentEvents) {
    if (@($documentEventSubscriptions | Where-Object { $_ -ceq $eventName }).Count -ne 2) {
        throw "Palette source must subscribe and unsubscribe exactly once for anonymous document event '$eventName'."
    }
}
if (@(Select-String -LiteralPath $nonAssemblySourcePaths -Pattern '\beventArgs\s*\.\s*Document\b' -AllMatches).Count -ne 0) {
    throw 'Palette document handlers must remain anonymous counters and may not dereference eventArgs.Document.'
}

[xml]$nuGetConfig = Read-Utf8File -Path $nuGetConfigPath
$packageSourceNodes = @($nuGetConfig.configuration.packageSources.add)
$packageSourceClearNodes = @($nuGetConfig.configuration.packageSources.clear)
$expectedFeedRelativePath = '..\..\third_party\nuget'
if ($packageSourceClearNodes.Count -ne 1 -or $packageSourceNodes.Count -ne 1 -or
    $packageSourceNodes[0].value -ine $expectedFeedRelativePath) {
    throw 'Palette NuGet.Config must clear inherited sources and expose only the repository-local third_party\nuget feed.'
}
$signatureModeNode = @($nuGetConfig.configuration.config.add | Where-Object { $_.key -eq 'signatureValidationMode' })
$trustedCertificateNodes = @($nuGetConfig.configuration.trustedSigners.author.certificate)
$expectedAuthorFingerprint = 'AA12DA22A49BCE7D5C1AE64CC1F3D892F150DA76140F210ABD2CBFFCA2C18A27'
if ($signatureModeNode.Count -ne 1 -or $signatureModeNode[0].value -ine 'require' -or
    $trustedCertificateNodes.Count -ne 1 -or $trustedCertificateNodes[0].fingerprint -ine $expectedAuthorFingerprint -or
    $trustedCertificateNodes[0].hashAlgorithm -ine 'SHA256' -or $trustedCertificateNodes[0].allowUntrustedRoot -ine 'false') {
    throw 'Palette NuGet.Config must require the reviewed Microsoft author signature and may not trust an unbounded signer set.'
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'NuGet.Config') -PathType Leaf) {
    throw 'A repository-root NuGet.Config is forbidden; Palette restore must remain project-local.'
}

$vendoredPackages = @(Get-ChildItem -LiteralPath (Split-Path -Parent $vendoredPackagePath) -Filter '*.nupkg' -File)
if ($vendoredPackages.Count -ne 1 -or $vendoredPackages[0].FullName -ine (Get-Item -LiteralPath $vendoredPackagePath).FullName) {
    throw 'The offline NuGet feed must contain exactly the reviewed net45 reference package.'
}
$expectedPackageSha256 = '23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3'
$expectedPackageSha512 = 'zPJ5Pqc6+cBg4ir33AWryA8CUxJJj68Cs1Cfo8plZt1HH3Q0B/EqVon6LRXw9b8dfQyLYMqTJJk2maXgLhGJIw=='
$packageSha256 = (Get-FileHash -LiteralPath $vendoredPackagePath -Algorithm SHA256).Hash
$sha512Algorithm = [Security.Cryptography.SHA512]::Create()
try {
    $packageSha512 = [Convert]::ToBase64String($sha512Algorithm.ComputeHash([IO.File]::ReadAllBytes($vendoredPackagePath)))
}
finally {
    $sha512Algorithm.Dispose()
}
if ($packageSha256 -ine $expectedPackageSha256 -or $packageSha512 -cne $expectedPackageSha512) {
    throw 'The vendored net45 reference package does not match the reviewed SHA-256/SHA-512 identity.'
}

$packageLock = Read-Utf8File -Path $packageLockPath | ConvertFrom-Json
$expectedFrameworkLocks = @(
    '.NETFramework,Version=v4.5',
    '.NETFramework,Version=v4.5/win',
    '.NETFramework,Version=v4.5/win-arm64',
    '.NETFramework,Version=v4.5/win-x64',
    '.NETFramework,Version=v4.5/win-x86'
)
$actualFrameworkLocks = @($packageLock.dependencies.PSObject.Properties.Name)
$net45Lock = $packageLock.dependencies.'.NETFramework,Version=v4.5'.'Microsoft.NETFramework.ReferenceAssemblies.net45'
$expectedContentHash = 'dcSLNuUX2rfZejsyta2EWZ1W5U6ucbFt697lRg1qiTlTM5ZlYv4uAvuxE6ROy6xLWWhLhOaReCDxkhxcajRYtQ=='
if ($packageLock.version -ne 1 -or
    @(Compare-Object -ReferenceObject $expectedFrameworkLocks -DifferenceObject $actualFrameworkLocks).Count -ne 0 -or
    $null -eq $net45Lock -or $net45Lock.type -cne 'Direct' -or
    $net45Lock.requested -cne '[1.0.3, 1.0.3]' -or $net45Lock.resolved -cne '1.0.3' -or
    $net45Lock.contentHash -cne $expectedContentHash) {
    throw 'Palette packages.lock.json does not match the exact reviewed net45 dependency and content hash.'
}
foreach ($emptyFrameworkLock in $expectedFrameworkLocks | Where-Object { $_ -ne '.NETFramework,Version=v4.5' }) {
    if (@($packageLock.dependencies.$emptyFrameworkLock.PSObject.Properties).Count -ne 0) {
        throw "Unexpected locked dependency under $emptyFrameworkLock."
    }
}

$packageSignatureResult = Invoke-DotNetIsolated -FilePath $dotnetPath -Arguments @(
    'nuget', 'verify', $vendoredPackagePath, '--all', '--configfile', $nuGetConfigPath
) -WorkingDirectory $repoRoot
$expectedRepositoryFingerprint = '5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4'
$packageSignatureText = $packageSignatureResult.Text
if ($packageSignatureResult.ExitCode -ne 0 -or
    $packageSignatureText.IndexOf($expectedAuthorFingerprint, [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
    $packageSignatureText.IndexOf($expectedRepositoryFingerprint, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw 'NuGet author/repository signature verification failed for the vendored net45 reference package.'
}

$defaultIntermediatePath = Join-Path $projectDirectory 'obj'
$defaultIntermediateManifestBefore = Get-DirectoryManifestHash -Path $defaultIntermediatePath
$outputDirectoryProperty = $outputDirectory.TrimEnd('\') + '/'
$baseIntermediateProperty = $baseIntermediateDirectory.TrimEnd('\') + '/'
$intermediateProperty = $intermediateDirectory.TrimEnd('\') + '/'
$projectExtensionsProperty = $projectExtensionsDirectory.TrimEnd('\') + '/'
$packageCacheProperty = $packageCache.TrimEnd('\') + '/'

$globalBuildProperties = @(
    "/p:Configuration=$Configuration",
    '/p:Platform=Any CPU',
    "/p:AutoCad2016Dir=$AutoCad2016Dir",
    "/p:RestoreConfigFile=$nuGetConfigPath",
    "/p:RestorePackagesPath=$packageCacheProperty",
    '/p:RestoreLockedMode=true',
    '/p:RestorePackagesWithLockFile=true',
    '/p:ImportDirectoryBuildProps=false',
    '/p:ImportDirectoryBuildTargets=false',
    "/p:OutDir=$outputDirectoryProperty",
    "/p:BaseIntermediateOutputPath=$baseIntermediateProperty",
    "/p:IntermediateOutputPath=$intermediateProperty",
    "/p:MSBuildProjectExtensionsPath=$projectExtensionsProperty"
)
$buildArguments = @($solutionPath, '/restore', '/t:Rebuild', '/m:1') + $globalBuildProperties + @('/v:minimal')
$buildResult = Invoke-NativeCapture -FilePath $MsBuildPath -Arguments $buildArguments -WorkingDirectory $repoRoot
if ($buildResult.ExitCode -ne 0) {
    throw "Palette build failed with exit code $($buildResult.ExitCode):`n$($buildResult.Text)"
}
if ((Get-DirectoryManifestHash -Path $defaultIntermediatePath) -cne $defaultIntermediateManifestBefore) {
    throw 'The isolated Palette verification build modified the project-local obj directory.'
}

$evaluationArguments = @(
    $projectPath,
    '-nologo',
    '-getProperty:TargetFrameworkVersion,PlatformTarget,LangVersion,Deterministic,ContinuousIntegrationBuild,TreatWarningsAsErrors,RestoreLockedMode,ImportDirectoryBuildProps,ImportDirectoryBuildTargets,DebugSymbols,DebugType,MSBuildProjectExtensionsPath,BaseIntermediateOutputPath,IntermediateOutputPath,AssemblyName,RootNamespace',
    '-getItem:Compile,ProjectReference,PackageReference,Reference,Content,None,EmbeddedResource,Resource,Page,ApplicationDefinition,COMReference,NativeReference,Analyzer',
    "/p:Configuration=$Configuration",
    '/p:Platform=x64',
    "/p:AutoCad2016Dir=$AutoCad2016Dir",
    "/p:RestoreConfigFile=$nuGetConfigPath",
    "/p:RestorePackagesPath=$packageCacheProperty",
    '/p:RestoreLockedMode=true',
    '/p:ImportDirectoryBuildProps=false',
    '/p:ImportDirectoryBuildTargets=false',
    "/p:BaseIntermediateOutputPath=$baseIntermediateProperty",
    "/p:IntermediateOutputPath=$intermediateProperty",
    "/p:MSBuildProjectExtensionsPath=$projectExtensionsProperty"
)
$evaluationResult = Invoke-NativeCapture -FilePath $MsBuildPath -Arguments $evaluationArguments -WorkingDirectory $repoRoot
if ($evaluationResult.ExitCode -ne 0) {
    throw "Palette evaluated-graph inspection failed:`n$($evaluationResult.Text)"
}
$jsonStart = $evaluationResult.Text.IndexOf('{')
$jsonEnd = $evaluationResult.Text.LastIndexOf('}')
if ($jsonStart -lt 0 -or $jsonEnd -le $jsonStart) {
    throw "Palette evaluated-graph output was not JSON:`n$($evaluationResult.Text)"
}
$evaluatedGraph = $evaluationResult.Text.Substring($jsonStart, $jsonEnd - $jsonStart + 1) | ConvertFrom-Json
$expectedEvaluatedProperties = [ordered]@{
    TargetFrameworkVersion = 'v4.5'
    PlatformTarget = 'x64'
    LangVersion = '7.3'
    Deterministic = 'true'
    ContinuousIntegrationBuild = 'true'
    TreatWarningsAsErrors = 'true'
    RestoreLockedMode = 'true'
    ImportDirectoryBuildProps = 'false'
    ImportDirectoryBuildTargets = 'false'
    DebugSymbols = 'false'
    DebugType = 'none'
    AssemblyName = $expectedAssemblyName
    RootNamespace = $expectedRootNamespace
}
foreach ($property in $expectedEvaluatedProperties.GetEnumerator()) {
    if ([string]$evaluatedGraph.Properties.($property.Key) -cne $property.Value) {
        throw "Evaluated Palette property '$($property.Key)' was '$($evaluatedGraph.Properties.($property.Key))', expected '$($property.Value)'."
    }
}
foreach ($isolatedPathProperty in @{
    MSBuildProjectExtensionsPath = $projectExtensionsDirectory
    BaseIntermediateOutputPath = $baseIntermediateDirectory
    IntermediateOutputPath = $intermediateDirectory
}.GetEnumerator()) {
    $actualPath = [IO.Path]::GetFullPath([string]$evaluatedGraph.Properties.($isolatedPathProperty.Key)).TrimEnd('\')
    $expectedPath = [IO.Path]::GetFullPath([string]$isolatedPathProperty.Value).TrimEnd('\')
    if ($actualPath -cne $expectedPath) {
        throw "Evaluated $($isolatedPathProperty.Key) escaped isolation: '$actualPath' != '$expectedPath'."
    }
}

$evaluatedCompileItems = @($evaluatedGraph.Items.Compile)
$evaluatedCompileIdentities = @($evaluatedCompileItems | ForEach-Object { ([string]$_.Identity).Replace('/', '\') })
if (@(Compare-Object -ReferenceObject $expectedCompileItems -DifferenceObject $evaluatedCompileIdentities).Count -ne 0 -or
    @($evaluatedCompileIdentities | Sort-Object -Unique).Count -ne $expectedCompileItems.Count) {
    throw 'The evaluated Palette compile graph contains injected or missing source files.'
}
foreach ($compileItem in $evaluatedCompileItems) {
    if ([IO.Path]::GetFullPath([string]$compileItem.DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($projectPath)) {
        throw "Compile item '$($compileItem.Identity)' was injected by '$($compileItem.DefiningProjectFullPath)'."
    }
}
foreach ($forbiddenEvaluatedItemType in @('ProjectReference', 'Content', 'None', 'EmbeddedResource', 'Resource', 'Page', 'ApplicationDefinition', 'COMReference', 'NativeReference', 'Analyzer')) {
    if (@($evaluatedGraph.Items.($forbiddenEvaluatedItemType)).Count -ne 0) {
        throw "The evaluated Palette graph contains forbidden $forbiddenEvaluatedItemType items."
    }
}

$expectedEvaluatedReferences = @(
    'System',
    'System.Core',
    'System.Drawing',
    'System.Xaml',
    'WindowsBase',
    'PresentationCore',
    'PresentationFramework',
    'accoremgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'acdbmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'acmgd, Version=20.1.0.0, Culture=neutral, PublicKeyToken=null',
    'mscorlib'
)
$evaluatedReferences = @($evaluatedGraph.Items.Reference)
if (@(Compare-Object -ReferenceObject $expectedEvaluatedReferences -DifferenceObject @($evaluatedReferences.Identity)).Count -ne 0) {
    throw 'The evaluated Palette reference graph differs from the exact reviewed allowlist.'
}
foreach ($reference in $evaluatedReferences) {
    if ($reference.Identity -ceq 'mscorlib') {
        $expectedMscorlibTarget = Join-Path $packageCache 'microsoft.netframework.referenceassemblies.net45\1.0.3\build\Microsoft.NETFramework.ReferenceAssemblies.net45.targets'
        if ([IO.Path]::GetFullPath([string]$reference.DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($expectedMscorlibTarget)) {
            throw "The pinned net45 package did not define mscorlib from the isolated signed package cache: '$($reference.DefiningProjectFullPath)'."
        }
    }
    elseif ([IO.Path]::GetFullPath([string]$reference.DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($projectPath)) {
        throw "Reference '$($reference.Identity)' was injected by '$($reference.DefiningProjectFullPath)'."
    }
}
$evaluatedPackages = @($evaluatedGraph.Items.PackageReference)
if ($evaluatedPackages.Count -ne 1 -or
    $evaluatedPackages[0].Identity -cne 'Microsoft.NETFramework.ReferenceAssemblies.net45' -or
    $evaluatedPackages[0].Version -cne '[1.0.3]' -or
    $evaluatedPackages[0].PrivateAssets -cne 'all' -or
    $evaluatedPackages[0].IncludeAssets -cne 'runtime;build;native;contentfiles;analyzers' -or
    [IO.Path]::GetFullPath([string]$evaluatedPackages[0].DefiningProjectFullPath) -cne [IO.Path]::GetFullPath($projectPath)) {
    throw 'The evaluated Palette PackageReference graph differs from the exact reviewed allowlist.'
}
if ((Get-DirectoryManifestHash -Path $defaultIntermediatePath) -cne $defaultIntermediateManifestBefore) {
    throw 'Palette evaluated-graph inspection modified the project-local obj directory.'
}

$paletteDll = Join-Path $outputDirectory 'Codex.AutoCAD.Host.2016.Palette.dll'
if (-not (Test-Path -LiteralPath $paletteDll -PathType Leaf)) {
    throw "Palette build output is missing: $paletteDll"
}
if ((Get-PeMachine -Path $paletteDll) -ne 'x64') {
    throw 'Palette output is not an x64 PE image.'
}
$firstBuildCandidateSha256 = (Get-FileHash -LiteralPath $paletteDll -Algorithm SHA256).Hash
if ($firstBuildCandidateSha256 -cne $expectedCandidateSha256) {
    throw "Palette output changed from the frozen candidate identity: $firstBuildCandidateSha256 != $expectedCandidateSha256"
}
$paletteAssemblyIdentity = [Reflection.AssemblyName]::GetAssemblyName($paletteDll)
if ($paletteAssemblyIdentity.Name -cne $expectedAssemblyName) {
    throw "Unexpected Palette assembly identity: $($paletteAssemblyIdentity.FullName)"
}
try {
    $loadedPaletteAssembly = [Reflection.Assembly]::LoadFile((Get-Item -LiteralPath $paletteDll).FullName)
    $outputReferences = @($loadedPaletteAssembly.GetReferencedAssemblies())
}
catch {
    throw "Could not inspect Palette output assembly references: $($_.Exception.Message)"
}
$actualOutputReferences = @($outputReferences | ForEach-Object { $_.FullName })
if (@(Compare-Object -ReferenceObject $expectedOutputReferences -DifferenceObject $actualOutputReferences -CaseSensitive).Count -ne 0 -or
    @($actualOutputReferences | Sort-Object -Unique).Count -ne $expectedOutputReferences.Count) {
    throw "Palette output assembly-reference table differs from the exact allowlist:`n$($actualOutputReferences -join [Environment]::NewLine)"
}

$paletteBytes = [IO.File]::ReadAllBytes($paletteDll)
$binaryText = [Text.Encoding]::ASCII.GetString($paletteBytes)
$binaryUtf8Text = [Text.Encoding]::UTF8.GetString($paletteBytes)
$binaryUnicodeText = [Text.Encoding]::Unicode.GetString($paletteBytes)
foreach ($forbiddenBuildPath in @($verificationRoot, $repoRoot, $projectDirectory)) {
    if ($binaryUtf8Text.IndexOf($forbiddenBuildPath, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $binaryUnicodeText.IndexOf($forbiddenBuildPath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Palette output leaks a build path: $forbiddenBuildPath"
    }
}
if ($binaryUtf8Text.IndexOf('.pdb', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
    $binaryUnicodeText.IndexOf('.pdb', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw 'Palette Release output contains a PDB path marker.'
}
if ($binaryText -notmatch [regex]::Escape('.NETFramework,Version=v4.5')) {
    throw 'Palette output does not contain the .NET Framework 4.5 target framework marker.'
}

$programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
    throw 'ProgramFiles(x86) is unavailable; trusted ildasm discovery cannot continue.'
}
$ildasmSearchRoots = @(
    (Join-Path $programFilesX86 'Microsoft SDKs\Windows\v10.0A\bin'),
    (Join-Path $programFilesX86 'Windows Kits\10\bin')
)
$ildasmCandidatePaths = @(
    foreach ($searchRoot in $ildasmSearchRoots) {
        if (Test-Path -LiteralPath $searchRoot -PathType Container) {
            Get-ChildItem -LiteralPath $searchRoot -Recurse -Filter 'ildasm.exe' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '(?i)\\NETFX\s+[^\\]+\s+Tools(?:\\x64)?\\ildasm\.exe$' } |
                ForEach-Object { $_.FullName }
        }
    }
) | Sort-Object -Unique
$trustedIldasmCandidates = @(
    foreach ($candidatePath in $ildasmCandidatePaths) {
        try {
            Get-TrustedMicrosoftTool -Path $candidatePath -Label 'ildasm' -DescriptionPattern '^Microsoft \.NET Framework IL disassembler$' -MinimumMajorVersion 4 -MaximumMajorVersion 4
        }
        catch {
            # Untrusted lookalikes are ignored; only a signed Microsoft SDK candidate may be selected.
        }
    }
)
if ($trustedIldasmCandidates.Count -eq 0) {
    throw 'No Microsoft-signed .NET Framework ildasm.exe was found in a known Windows SDK directory.'
}
$ildasmEvidence = @($trustedIldasmCandidates | Sort-Object @{ Expression = { [Version]$_.Version }; Descending = $true }, Path | Select-Object -First 1)[0]
$ildasmPath = $ildasmEvidence.Path
$ilOutputPath = Join-Path $verificationRoot 'Codex.AutoCAD.Host.2016.Palette.il'
$ildasmResult = Invoke-NativeCapture -FilePath $ildasmPath -Arguments @(
    '/text', '/nobar', '/tokens', '/utf8', '/caverbal', "/out=$ilOutputPath", $paletteDll
) -WorkingDirectory $repoRoot
if ($ildasmResult.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ilOutputPath -PathType Leaf)) {
    throw "ildasm failed with exit code $($ildasmResult.ExitCode); refusing to continue the exact Palette metadata gate.`n$($ildasmResult.Text)"
}
$ilDisassembly = Read-Utf8File -Path $ilOutputPath
if ([string]::IsNullOrWhiteSpace($ilDisassembly)) {
    throw 'ildasm produced an empty Palette IL file.'
}

$methodDefinitions = @(Get-IlMethodDefinitions -IlText $ilDisassembly)
if ($methodDefinitions.Count -ne $expectedMethodDefinitions.Count -or
    @($methodDefinitions.Token | Sort-Object -Unique).Count -ne $expectedMethodDefinitions.Count) {
    throw "Palette MethodDef count/token set changed; expected exactly $($expectedMethodDefinitions.Count), got $($methodDefinitions.Count)."
}
foreach ($expectedMethod in $expectedMethodDefinitions.GetEnumerator()) {
    $actualMethods = @($methodDefinitions | Where-Object { $_.Token -ceq $expectedMethod.Key })
    $actualIdentity = if ($actualMethods.Count -eq 1) { '{0}|{1}' -f $actualMethods[0].Name, $actualMethods[0].Header } else { '<missing-or-duplicate>' }
    if ($actualIdentity -cne $expectedMethod.Value) {
        throw "Palette MethodDef $($expectedMethod.Key) changed: '$actualIdentity' != '$($expectedMethod.Value)'."
    }
}

$memberReferences = Get-IlMemberReferenceMap -IlText $ilDisassembly
if ($memberReferences.Count -ne $expectedMemberReferences.Count) {
    throw "Palette MemberRef count changed; expected exactly $($expectedMemberReferences.Count), got $($memberReferences.Count)."
}
foreach ($expectedMember in $expectedMemberReferences.GetEnumerator()) {
    if (-not $memberReferences.ContainsKey($expectedMember.Key) -or $memberReferences[$expectedMember.Key] -cne $expectedMember.Value) {
        $actualMember = if ($memberReferences.ContainsKey($expectedMember.Key)) { $memberReferences[$expectedMember.Key] } else { '<missing>' }
        throw "Palette MemberRef $($expectedMember.Key) changed: '$actualMember' != '$($expectedMember.Value)'."
    }
}
Assert-NoHighRiskMemberReferences -Signatures @($memberReferences.Values)

$semanticIl = [regex]::Replace($ilDisassembly, '//[^\r\n]*(?=\r|\n|$)', '')
$semanticIl = [regex]::Replace($semanticIl, '/\*[^*]*\*/', '')
$semanticIl = [regex]::Replace($semanticIl, '\s+', ' ').Trim()
$extensionAttributeNeedle = 'Autodesk.AutoCAD.Runtime.ExtensionApplicationAttribute::.ctor(class [mscorlib]System.Type) = {type(Codex.AutoCAD.Host2016.Palette.CodexPaletteExtension)}'
$commandClassNeedle = 'Autodesk.AutoCAD.Runtime.CommandClassAttribute::.ctor(class [mscorlib]System.Type) = {type(Codex.AutoCAD.Host2016.Palette.CodexPaletteCommands)}'
if ([regex]::Matches($semanticIl, 'Autodesk\.AutoCAD\.Runtime\.ExtensionApplicationAttribute::\.ctor\(').Count -ne 1 -or
    $semanticIl.IndexOf($extensionAttributeNeedle, [StringComparison]::Ordinal) -lt 0) {
    throw 'The real Palette assembly ExtensionApplication attribute/type differs from the exact reviewed value.'
}
if ([regex]::Matches($semanticIl, 'Autodesk\.AutoCAD\.Runtime\.CommandClassAttribute::\.ctor\(').Count -ne 1 -or
    $semanticIl.IndexOf($commandClassNeedle, [StringComparison]::Ordinal) -lt 0) {
    throw 'The real Palette assembly CommandClass attribute/type differs from the exact reviewed value.'
}
$extensionInterfaceNeedle = '.class public auto ansi sealed beforefieldinit Codex.AutoCAD.Host2016.Palette.CodexPaletteExtension extends [mscorlib]System.Object implements [Acdbmgd]Autodesk.AutoCAD.Runtime.IExtensionApplication'
if ($semanticIl.IndexOf($extensionInterfaceNeedle, [StringComparison]::Ordinal) -lt 0) {
    throw 'CodexPaletteExtension does not implement the exact AutoCAD IExtensionApplication interface.'
}
if ([regex]::Matches($semanticIl, 'Autodesk\.AutoCAD\.Runtime\.CommandMethodAttribute::\.ctor\(').Count -ne 3) {
    throw 'The real Palette assembly must contain exactly three CommandMethod attributes.'
}
foreach ($commandAttribute in $expectedCommandAttributes.GetEnumerator()) {
    $methodsForToken = @($methodDefinitions | Where-Object { $_.Token -ceq $commandAttribute.Key })
    if ($methodsForToken.Count -ne 1) {
        throw "Expected Palette command MethodDef $($commandAttribute.Key) is missing or duplicated."
    }
    $methodSemantic = [regex]::Replace($methodsForToken[0].BodyCanonical, '/\*[^*]*\*/', '')
    $methodSemantic = [regex]::Replace($methodSemantic, '\s+', ' ').Trim()
    if ($methodSemantic.IndexOf($commandAttribute.Value, [StringComparison]::Ordinal) -lt 0) {
        throw "Palette CommandMethod metadata/arguments changed on MethodDef $($commandAttribute.Key)."
    }
}
foreach ($method in @($methodDefinitions | Where-Object { $_.Token -notin @($expectedCommandAttributes.Keys) })) {
    if ($method.BodyCanonical.IndexOf('CommandMethodAttribute', [StringComparison]::Ordinal) -ge 0) {
        throw "Unexpected CommandMethod attribute on $($method.Token) $($method.Name)."
    }
}

$forbiddenBinaryTokens = @(
    'ProcessStartInfo', 'System.Diagnostics.Process', 'ShellExecute', 'CreateProcess',
    'System.IO.Pipes', 'NamedPipe', 'AnonymousPipe', 'PipeStream', 'MemoryMappedFile',
    'System.Net', 'HttpClient', 'WebRequest', 'WebClient', 'HttpListener', 'Socket', 'TcpClient', 'UdpClient',
    'System.IO.File', 'System.IO.Directory', 'FileStream', 'StreamReader', 'StreamWriter',
    'Microsoft.Win32', 'RegistryKey',
    'DocumentLock', 'StartTransaction', 'StartOpenCloseTransaction', 'ForWrite', 'ForRead', 'GetSelection', 'SelectImplied', 'SetImpliedSelection', 'GetEntity',
    'AppendEntity', 'AddNewlyCreatedDBObject', 'WblockCloneObjects', 'DeepCloneObjects', 'UpgradeOpen',
    'SaveAs', 'DwgOut', 'DxfOut', 'CloseAndSave', 'SetSystemVariable', 'SendStringToExecute', 'ExecuteInCommandContextAsync',
    'System.Security.Cryptography', 'HMAC', 'CadApprovalGate', 'IAgentBridgeClient',
    'Codex.AutoCAD.Bridge', 'Codex.AutoCAD.AgentRuntime', 'Codex.AutoCAD.Ipc', 'Codex.AutoCAD.Security',
    'DllImportAttribute', 'LoadLibrary', 'GetProcAddress', 'Activator', 'MethodInfo', 'PropertyInfo',
    'System.Threading', 'BackgroundWorker', 'DispatcherTimer', 'XamlReader', 'WebBrowser'
)
$binaryTokenHits = @(
    foreach ($token in $forbiddenBinaryTokens) {
        if ($binaryText.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $token
        }
    }
)
if ($binaryTokenHits.Count -ne 0) {
    throw "Palette output contains forbidden API metadata tokens: $($binaryTokenHits -join ', ')."
}

$outputFiles = @(Get-ChildItem -LiteralPath $outputDirectory -Recurse -File)
if ($outputFiles.Count -ne 1 -or $outputFiles[0].FullName -cne (Get-Item -LiteralPath $paletteDll).FullName) {
    throw "The isolated Palette output must contain exactly the reviewed DLL and no PDB/config/script/native payload:`n$($outputFiles.FullName -join [Environment]::NewLine)"
}
$copiedAutodeskFiles = @(
    foreach ($managedApiName in $managedApiNames) {
        $copiedPath = Join-Path $outputDirectory $managedApiName
        if (Test-Path -LiteralPath $copiedPath) { $copiedPath }
    }
)
if ($copiedAutodeskFiles.Count -ne 0) {
    throw "Autodesk managed assemblies were copied to the Palette output:`n$($copiedAutodeskFiles -join [Environment]::NewLine)"
}

$rebuildRoot = Join-Path $repoRoot ("artifacts\autocad2016-palette-rebuild-{0}" -f [Guid]::NewGuid().ToString('N'))
$rebuildOutputDirectory = Join-Path $rebuildRoot 'bin'
$rebuildBaseIntermediateDirectory = Join-Path $rebuildRoot 'obj-base'
$rebuildIntermediateDirectory = Join-Path $rebuildRoot 'obj-compile'
$rebuildProjectExtensionsDirectory = Join-Path $rebuildRoot 'obj-project-extensions'
$rebuildPackageCache = Join-Path $rebuildRoot 'packages'
foreach ($directory in @($rebuildOutputDirectory, $rebuildBaseIntermediateDirectory, $rebuildIntermediateDirectory, $rebuildProjectExtensionsDirectory, $rebuildPackageCache)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$rebuildProperties = @(
    "/p:Configuration=$Configuration",
    '/p:Platform=Any CPU',
    "/p:AutoCad2016Dir=$AutoCad2016Dir",
    "/p:RestoreConfigFile=$nuGetConfigPath",
    "/p:RestorePackagesPath=$($rebuildPackageCache.TrimEnd('\'))/",
    '/p:RestoreLockedMode=true',
    '/p:RestorePackagesWithLockFile=true',
    '/p:ImportDirectoryBuildProps=false',
    '/p:ImportDirectoryBuildTargets=false',
    "/p:OutDir=$($rebuildOutputDirectory.TrimEnd('\'))/",
    "/p:BaseIntermediateOutputPath=$($rebuildBaseIntermediateDirectory.TrimEnd('\'))/",
    "/p:IntermediateOutputPath=$($rebuildIntermediateDirectory.TrimEnd('\'))/",
    "/p:MSBuildProjectExtensionsPath=$($rebuildProjectExtensionsDirectory.TrimEnd('\'))/"
)
$rebuildArguments = @($solutionPath, '/restore', '/t:Rebuild', '/m:1') + $rebuildProperties + @('/v:minimal')
$rebuildResult = Invoke-NativeCapture -FilePath $MsBuildPath -Arguments $rebuildArguments -WorkingDirectory $repoRoot
if ($rebuildResult.ExitCode -ne 0) {
    throw "The independent deterministic Palette rebuild failed with exit code $($rebuildResult.ExitCode):`n$($rebuildResult.Text)"
}
if ((Get-DirectoryManifestHash -Path $defaultIntermediatePath) -cne $defaultIntermediateManifestBefore) {
    throw 'The independent Palette rebuild modified the project-local obj directory.'
}
$rebuildPaletteDll = Join-Path $rebuildOutputDirectory 'Codex.AutoCAD.Host.2016.Palette.dll'
$rebuildOutputFiles = @(Get-ChildItem -LiteralPath $rebuildOutputDirectory -Recurse -File)
if ($rebuildOutputFiles.Count -ne 1 -or -not (Test-Path -LiteralPath $rebuildPaletteDll -PathType Leaf)) {
    throw 'The deterministic Palette rebuild output must contain exactly one DLL.'
}
$paletteSha256 = (Get-FileHash -LiteralPath $paletteDll -Algorithm SHA256).Hash
$rebuildPaletteSha256 = (Get-FileHash -LiteralPath $rebuildPaletteDll -Algorithm SHA256).Hash
if ($paletteSha256 -cne $rebuildPaletteSha256) {
    throw "Release Palette DLL is not bit-for-bit reproducible: $paletteSha256 != $rebuildPaletteSha256"
}
if ($paletteSha256 -cne $expectedCandidateSha256) {
    throw "The deterministic Palette outputs do not match the frozen candidate hash: $paletteSha256 != $expectedCandidateSha256"
}

$memberReferenceEvidence = @(
    $memberReferences.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{ Token = $_.Name; Signature = $_.Value }
    }
)
$methodDefinitionEvidence = @(
    $methodDefinitions | Sort-Object Token | ForEach-Object {
        [pscustomobject]@{ Token = $_.Token; Name = $_.Name; Header = $_.Header }
    }
)
$customAttributeEvidence = [pscustomobject]@{
    ExtensionApplicationType = 'Codex.AutoCAD.Host2016.Palette.CodexPaletteExtension'
    CommandClassType = 'Codex.AutoCAD.Host2016.Palette.CodexPaletteCommands'
    PaletteGuid = $paletteGuidText
    Commands = @(
        [pscustomobject]@{ MethodDef = '06000004'; Method = 'ShowPalette'; GlobalName = 'CODEX16PAL'; Flags = 0 },
        [pscustomobject]@{ MethodDef = '06000005'; Method = 'ShowPaletteInfo'; GlobalName = 'CODEX16PALINFO'; Flags = 0 },
        [pscustomobject]@{ MethodDef = '06000006'; Method = 'ResetPalette'; GlobalName = 'CODEX16PALRESET'; Flags = 0 }
    )
}

[pscustomobject]@{
    Ok = $true
    Status = 'compiled-palette-candidate-not-runtime-verified-by-this-script'
    AutoCad = $acadEvidence
    ManagedApis = $managedApiEvidence
    Toolchain = [pscustomobject]@{
        MSBuild = $msbuildEvidence
        DotNetHost = $dotnetEvidence
        ResolvedSdkFromRepoRoot = $resolvedDotnetSdk
        Ildasm = $ildasmEvidence
    }
    Palette = [pscustomobject]@{
        Path = $paletteDll
        TargetFramework = '.NETFramework,Version=v4.5'
        Architecture = 'x64'
        Sha256 = $paletteSha256
        ReferencedAssemblies = $actualOutputReferences
        OutputFiles = @($outputFiles.FullName)
        AgentEnabled = $false
        SelectionCaptureEnabled = $false
        CadWriteEnabled = $false
        AutomaticSaveEnabled = $false
    }
    DeterministicRebuild = [pscustomobject]@{
        FirstPath = $paletteDll
        FirstSha256 = $paletteSha256
        SecondPath = $rebuildPaletteDll
        SecondSha256 = $rebuildPaletteSha256
        BitForBitMatch = $true
        PdbProduced = $false
    }
    BuildIsolation = [pscustomobject]@{
        VerificationRoot = $verificationRoot
        RebuildRoot = $rebuildRoot
        ProjectLocalObjManifestBefore = $defaultIntermediateManifestBefore
        ProjectLocalObjManifestAfter = (Get-DirectoryManifestHash -Path $defaultIntermediatePath)
    }
    Project = [pscustomobject]@{
        SolutionPath = $solutionPath
        SolutionSha256 = $solutionSha256
        ProjectPath = $projectPath
        ProjectSha256 = $projectSha256
        NuGetConfigSha256 = $nuGetConfigSha256
        PackageLockSha256 = $packageLockSha256
        Sources = $sourceHashEvidence
    }
    IlMetadata = [pscustomobject]@{
        MethodDefinitions = $methodDefinitionEvidence
        MemberReferences = $memberReferenceEvidence
        RegistrationAttributes = $customAttributeEvidence
    }
    VerifierSelfTests = $ruleSelfTestEvidence
    AutodeskAssembliesCopiedToOutput = $false
    DedicatedSolutionBuilt = $true
    SolutionIsolationVerified = $true
    DependencyRestoreMode = 'palette-project-local-signed-package-locked'
    EvaluatedProjectGraphAllowlistVerified = $true
    SourceHashAllowlistVerified = $true
    OutputReferenceAllowlistVerified = $true
    OutputPayloadAllowlistVerified = $true
    IlMemberReferenceAllowlistVerified = $true
    IlMethodDefinitionAllowlistVerified = $true
    AutoCadRegistrationAttributesVerified = $true
    HighRiskApiNegativeSamplesVerified = $true
    AnonymousDocumentEventAllowlistVerified = $true
    SafeSystemVariableAllowlistVerified = $true
    CadProcessStartedOrRestartedByVerification = $false
    CadCommandsSentByVerification = $false
    NetLoadVerified = $false
    RuntimeToCandidateBindingVerified = $false
} | ConvertTo-Json -Depth 8

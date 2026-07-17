[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$doctorWorkspace = Join-Path $repoRoot "artifacts\doctor-workspace"

Push-Location $repoRoot
try {
    dotnet build "Codex.AutoCAD.sln" --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }

    $specProjects = @(
        "tests\Codex.AutoCAD.Contracts.Specs\Codex.AutoCAD.Contracts.Specs.csproj",
        "tests\Codex.AutoCAD.Ipc.Specs\Codex.AutoCAD.Ipc.Specs.csproj",
        "tests\Codex.AutoCAD.Security.Specs\Codex.AutoCAD.Security.Specs.csproj",
        "tests\Codex.AutoCAD.AppServer.Specs\Codex.AutoCAD.AppServer.Specs.csproj"
    )

    foreach ($project in $specProjects) {
        dotnet run --project $project --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Specification failed: $project" }
    }

    $hostSources = Get-ChildItem "src\Codex.AutoCAD.Host.2025" -Recurse -Filter "*.cs"
    $forbidden = $hostSources | Select-String -Pattern "SendStringToExecute|SaveAs|QSAVE|NETLOAD|ARXLOAD"
    if ($forbidden) {
        throw "Forbidden command, loader, or save API found in AutoCAD host: $($forbidden.Path):$($forbidden.LineNumber)"
    }

    dotnet run `
        --project "src\Codex.AutoCAD.AgentHost\Codex.AutoCAD.AgentHost.csproj" `
        --configuration $Configuration `
        --no-build `
        -- doctor `
        --workspace $doctorWorkspace
    if ($LASTEXITCODE -ne 0) { throw "Local Codex App Server handshake failed." }

    Write-Host "Phase 1 verification passed: build, 29 specs, safety scan, and live App Server handshake."
}
finally {
    Pop-Location
}

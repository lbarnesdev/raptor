# ============================================================
# setup.ps1 — RAPTOR project directory scaffold
# Run once after cloning: .\setup.ps1
# Safe to re-run (New-Item -Force is idempotent).
# ============================================================

$ErrorActionPreference = "Stop"

Write-Host "Creating RAPTOR project directory structure..." -ForegroundColor Cyan

$dirs = @(
    # Source code
    "src\Player",
    "src\Enemies",
    "src\Boss",
    "src\Projectiles",
    "src\World",
    "src\Core",
    "src\UI",
    "src\Logic",

    # Scenes
    "scenes\player",
    "scenes\enemies",
    "scenes\boss",
    "scenes\projectiles",
    "scenes\world",
    "scenes\ui",

    # Assets: sprites
    "assets\sprites\player",
    "assets\sprites\enemies",
    "assets\sprites\boss",
    "assets\sprites\projectiles",
    "assets\sprites\terrain",
    "assets\sprites\parallax",
    "assets\sprites\explosions",
    "assets\sprites\ui",

    # Assets: audio
    "assets\audio\sfx",
    "assets\audio\music",

    # Assets: data
    "assets\data",

    # Tests
    "tests\Logic",

    # Tools
    "tools\art_gen",

    # CI
    ".github\workflows"
)

foreach ($dir in $dirs) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

# Drop .gitkeep files so empty dirs are tracked by Git
Get-ChildItem -Recurse -Directory | Where-Object {
    ($_.GetFileSystemInfos().Count -eq 0) -and
    ($_.FullName -notmatch '\\.git\\') -and
    ($_.FullName -notmatch '\\.godot\\')
} | ForEach-Object {
    New-Item -ItemType File -Path (Join-Path $_.FullName ".gitkeep") -Force | Out-Null
}

Write-Host ""
Write-Host "Done. Directory tree:" -ForegroundColor Green
Get-ChildItem -Recurse -Force |
    Where-Object { $_.FullName -notmatch '\\.git\\' -and $_.FullName -notmatch '\\.godot\\' } |
    Sort-Object FullName |
    ForEach-Object {
        $depth = ($_.FullName.Split([IO.Path]::DirectorySeparatorChar).Count -
                  $PSScriptRoot.Split([IO.Path]::DirectorySeparatorChar).Count) - 1
        $indent = "  " * $depth
        Write-Host "$indent$($_.Name)"
    }

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open Godot 4.3, import this folder as a project"
Write-Host "  2. dotnet restore raptor.sln"
Write-Host "  3. dotnet build raptor.sln"
Write-Host '  4. git init && git add -A && git commit -m "chore: TICKET-001 project scaffold"'

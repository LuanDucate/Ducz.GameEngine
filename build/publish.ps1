<#
.SYNOPSIS
  Builds the distributable release of Ducz Map Builder.

.DESCRIPTION
  Publishes the Launcher and the Map Builder (SceneEditor) as self-contained win-x64
  apps (no .NET install required on the user's machine) into one folder:

    dist/DuczMapBuilder/
      Ducz.Tools.Launcher.exe        <- run this (opens the project manager)
      Ducz.Tools.SceneEditor.exe     <- opened by the launcher (or directly with a project path)
      Branding/                      <- logos / window icons
      Prefabs/                       <- ready-made pieces (houses, streets, trees...)
      Textures/prototype/            <- the default 1 m grid textures
      LICENSE, THIRD-PARTY-NOTICES.md

  Zip that folder (or use -Zip) and attach it to a GitHub Release.

.PARAMETER Runtime   win-x64 (default). linux-x64 also works.
.PARAMETER Out       Output folder (default dist/DuczMapBuilder).
.PARAMETER Zip       Also create dist/DuczMapBuilder-<version>-<runtime>.zip
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Out = "dist/DuczMapBuilder",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = ([xml](Get-Content "$root/Directory.Build.props")).Project.PropertyGroup.Version
Write-Host "Ducz Map Builder $version -> $Out ($Runtime)" -ForegroundColor Cyan

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null

$common = @("-c", "Release", "-r", $Runtime, "--self-contained", "true", "--nologo", "-v", "q",
            "-p:PublishSingleFile=false", "-p:DebugType=none", "-p:DebugSymbols=false", "-p:UseAppHost=true")

Write-Host "  publishing Ducz.Tools.Launcher..."
dotnet publish "src/Ducz.Tools.Launcher/Ducz.Tools.Launcher.csproj" @common -o $Out
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed" }

Write-Host "  publishing Ducz.Tools.SceneEditor..."
dotnet publish "src/Ducz.Tools.SceneEditor/Ducz.Tools.SceneEditor.csproj" @common -o $Out
if ($LASTEXITCODE -ne 0) { throw "SceneEditor publish failed" }

Copy-Item "$root/LICENSE" $Out -Force
Copy-Item "$root/THIRD-PARTY-NOTICES.md" $Out -Force
Set-Content -Path "$Out/version.txt" -Value "$version ($Runtime)"

Get-ChildItem $Out -Recurse -Include *.pdb, *.xml | Remove-Item -Force -ErrorAction SilentlyContinue

$size = [math]::Round((Get-ChildItem $Out -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Done: $Out ($size MB)" -ForegroundColor Green

if ($Zip) {
    $zipPath = Join-Path (Split-Path -Parent (Resolve-Path $Out)) "DuczMapBuilder-$version-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$Out/*" -DestinationPath $zipPath
    Write-Host "Zip: $zipPath" -ForegroundColor Green
}

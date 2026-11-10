param(
    [string]$Configuration = "Release",
    [Parameter(Mandatory = $true)]
    [string]$BepInExCore,
    [string]$ValheimManaged = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$version = "1.0.2"
$assemblyName = "PoVHallsChests"
$packageName = "PoV-Halls-Chests-$version"
$releaseRoot = Join-Path $projectRoot "release"
$packageRoot = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"

dotnet msbuild (Join-Path $projectRoot "PoVHallsChests.csproj") /t:Rebuild /p:Configuration=$Configuration "/p:BepInExCore=$BepInExCore" "/p:ValheimManaged=$ValheimManaged" /v:minimal

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item (Join-Path $projectRoot "bin\\$Configuration\\$assemblyName.dll") $packageRoot
Copy-Item (Join-Path $projectRoot "manifest.json") $packageRoot
Copy-Item (Join-Path $projectRoot "README.md") $packageRoot
Copy-Item (Join-Path $projectRoot "CHANGELOG.md") $packageRoot
Copy-Item (Join-Path $projectRoot "icon.png") $packageRoot
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath

Write-Host "Created $zipPath"

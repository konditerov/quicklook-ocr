$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root "QuickLook.Plugin.ImageViewerOcr\bin\Release"
$stage = Join-Path $root "dist\QuickLook.Plugin.ImageViewerOcr"
$zip = Join-Path $root "dist\QuickLook.Plugin.ImageViewerOcr.qlplugin"

if (-not (Test-Path (Join-Path $release "QuickLook.Plugin.ImageViewerOcr.dll"))) {
    throw "Release build was not found. Run dotnet build -c Release first."
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
New-Item -ItemType Directory -Path (Join-Path $root "dist") -Force | Out-Null

Get-ChildItem $release -Filter *.dll | Where-Object { $_.Name -ne "QuickLook.Common.dll" -and $_.Name -ne "DirectML.Debug.dll" } | Copy-Item -Destination $stage
Copy-Item (Join-Path $release "QuickLook.Plugin.Metadata.config") $stage
Copy-Item (Join-Path $release "models") (Join-Path $stage "models") -Recurse
Get-ChildItem $stage -Filter "onnxruntime_providers_*.dll" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "onnxruntime_providers_shared.dll" } |
    Remove-Item -Force

if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 2)
Write-Host "Packed $zip ($sizeMb MB)"
if ($sizeMb -gt 300) {
    throw "Plugin package is $sizeMb MB, above the 300 MB budget."
}

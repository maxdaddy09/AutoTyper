# Builds the download: release\AutoTyper-<version>-win-x64.zip, holding a self-contained
# AutoTyper.exe (nothing to install), README.txt and LICENSE.txt. The version comes from
# AutoTyper\AutoTyper.csproj.
#
#   powershell -File scripts\package.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'AutoTyper\AutoTyper.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $project" }

$release = Join-Path $root 'release'
$staging = Join-Path $release 'staging'
$build = Join-Path $release 'build'
$zip = Join-Path $release "AutoTyper-$version-win-x64.zip"

Remove-Item $release -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

# Self-contained puts the .NET runtime inside the exe, so it runs on a PC without .NET installed.
# Building into release\build keeps this from touching (or being blocked by) a copy of the app
# that is running from AutoTyper\bin.
dotnet publish $project -c Release -r win-x64 `
    -p:SelfContained=true -p:PublishSingleFile=true -p:DebugType=None -p:SatelliteResourceLanguages=en `
    -p:OutputPath="$build/" -o $staging
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item (Join-Path $root 'packaging\README.txt') $staging
$license = Join-Path $root 'LICENSE'
if (Test-Path $license) { Copy-Item $license (Join-Path $staging 'LICENSE.txt') }

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging, $build -Recurse -Force

$megabytes = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Output "Created $zip ($megabytes MB)"

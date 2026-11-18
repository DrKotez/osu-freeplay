# Builds a portable copy of freeplay~ into dist\freeplay~ and zips it as dist\freeplay~.zip.
# The folder runs on any 64-bit Windows without installing .NET, keeps its settings and statistics inside itself
# (portable.ini), and finds the osu!stable install of whoever runs it.
#
# Usage:  powershell -ExecutionPolicy Bypass -File build-portable.ps1

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$output = Join-Path $dist 'freeplay~'
$zip = Join-Path $dist 'freeplay~.zip'

if (Test-Path $output) { Remove-Item -Recurse -Force $output }
if (Test-Path $zip) { Remove-Item -Force $zip }

dotnet publish (Join-Path $root 'osu.Desktop') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Keeps all data (settings, statistics, the library index) next to the executable instead of in AppData.
Set-Content -Path (Join-Path $output 'portable.ini') -Value '; freeplay~ stores its data in this folder.' -Encoding utf8

Copy-Item (Join-Path $root 'portable-readme.txt') (Join-Path $output 'README.txt')

Compress-Archive -Path $output -DestinationPath $zip

Write-Host "Built $output"
Write-Host "Zipped $zip"

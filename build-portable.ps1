# Builds a portable copy of freeplay~:
#   dist\freeplay~.zip  a clean copy (no settings or statistics) to pass on
#   dist\freeplay~      your own copy, updated in place: its settings, statistics and index are kept
# The folder runs on any 64-bit Windows without installing .NET, keeps its data inside itself (portable.ini),
# and finds the osu!stable install of whoever runs it.
#
# Usage:  powershell -ExecutionPolicy Bypass -File build-portable.ps1

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$clean = Join-Path $dist 'build\freeplay~'
$own = Join-Path $dist 'freeplay~'
$zip = Join-Path $dist 'freeplay~.zip'

if (Test-Path (Join-Path $dist 'build')) { Remove-Item -Recurse -Force (Join-Path $dist 'build') }
if (Test-Path $zip) { Remove-Item -Force $zip }

dotnet publish (Join-Path $root 'osu.Desktop') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $clean
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Keeps all data (settings, statistics, the library index) next to the executable instead of in AppData.
Set-Content -Path (Join-Path $clean 'portable.ini') -Value '; freeplay~ stores its data in this folder.' -Encoding utf8

Copy-Item (Join-Path $root 'portable-readme.txt') (Join-Path $clean 'README.txt')

Compress-Archive -Path $clean -DestinationPath $zip

# Update your own copy without touching its data: files are only added or replaced, never removed.
robocopy $clean $own /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

Write-Host "Zipped a clean copy to $zip"
Write-Host "Updated your copy in $own (settings and statistics kept)"

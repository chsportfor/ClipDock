param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'src\build.ps1')
$distPath = Join-Path $PSScriptRoot 'dist'
$packagePath = Join-Path $distPath ('ClipDock-v' + $Version + '-win-x64')
if (Test-Path -LiteralPath $packagePath) { throw 'Package directory already exists; use a new version or clean dist manually.' }
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
foreach ($file in @('ClipDock.exe','README.md','Setup-FFmpeg.ps1','Start-ClipDock.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $packagePath $file) }
$zipPath = $packagePath + '.zip'
Compress-Archive -Path (Join-Path $packagePath '*') -DestinationPath $zipPath
$exePath = Join-Path $distPath 'ClipDock.exe'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ClipDock.exe') -Destination $exePath -Force
@($zipPath,$exePath) | ForEach-Object { $hash=Get-FileHash -LiteralPath $_ -Algorithm SHA256; $hash.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) } | Set-Content (Join-Path $distPath 'SHA256SUMS.txt') -Encoding ASCII
Get-ChildItem -LiteralPath $distPath -File | Select-Object Name,Length

param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$toolsPath = Join-Path $PSScriptRoot 'tools'
$expectedHash = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$archiveUrl = 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip'
New-Item -ItemType Directory -Path $toolsPath -Force | Out-Null
$downloadPath = $null
try {
 if (!$ArchivePath) {
  $downloadPath = Join-Path $toolsPath ('.ffmpeg-' + [Guid]::NewGuid().ToString('N') + '.zip')
  Write-Host 'Downloading FFmpeg 9.0.2 from gyan.dev (first run only)...'
  Invoke-WebRequest -Uri $archiveUrl -OutFile $downloadPath -UseBasicParsing
  $ArchivePath = $downloadPath
 }
 if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'FFmpeg archive checksum mismatch. Nothing was installed.' }
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
 try {
  $wanted = @{'bin/ffmpeg.exe'='ffmpeg.exe';'bin/ffprobe.exe'='ffprobe.exe';'LICENSE'='FFmpeg-LICENSE.txt';'README.txt'='FFmpeg-README.txt'}
  foreach ($suffix in $wanted.Keys) {
   $entry = @($archive.Entries | Where-Object { $_.FullName.EndsWith('/' + $suffix, [StringComparison]::OrdinalIgnoreCase) })
   if ($entry.Count -ne 1) { throw ('Missing or ambiguous archive entry: ' + $suffix) }
   [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], (Join-Path $toolsPath $wanted[$suffix]), $true)
  }
 } finally { $archive.Dispose() }
 $versionText = & (Join-Path $toolsPath 'ffmpeg.exe') -v error -version
 if ($LASTEXITCODE -ne 0) { throw 'FFmpeg verification failed.' }
 $versionText | Select-Object -First 1
 Write-Host 'FFmpeg is ready. Run ClipDock.exe.'
} finally {
 if ($downloadPath -and (Test-Path -LiteralPath $downloadPath)) { Remove-Item -LiteralPath $downloadPath }
}

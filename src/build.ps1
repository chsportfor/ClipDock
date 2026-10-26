$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$outputPath = $PSScriptRoot
if (Test-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'tools')) { $outputPath = Split-Path $PSScriptRoot -Parent }
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', "/out:$outputPath\ClipDock.exe")
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += Join-Path $PSScriptRoot 'ClipDock.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Timeline.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Features.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Grouping.cs'
$compilerArgs += Join-Path $PSScriptRoot 'EditView.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Arrangement.cs'
$compilerArgs += Join-Path $PSScriptRoot 'LivePlayback.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Framing.cs'
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

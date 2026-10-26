$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot
if (Test-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'tools')) { $projectPath = Split-Path $PSScriptRoot -Parent }
$testPath = Join-Path $projectPath 'tests'
$ffmpeg = Join-Path $projectPath 'tools\ffmpeg.exe'
$ffprobe = Join-Path $projectPath 'tools\ffprobe.exe'
New-Item -ItemType Directory -Path $testPath -Force | Out-Null
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=640x480:rate=240:duration=3' -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=3' -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -c:a aac -shortest (Join-Path $testPath 'source-240.mp4')
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
& $ffmpeg -hide_banner -loglevel error -y -i (Join-Path $testPath 'source-240.mp4') -an -c:v copy (Join-Path $testPath '무음 영상.mp4')
if ($LASTEXITCODE -ne 0) { throw 'Silent fixture generation failed' }
$cases = @(
 @{Name='240fps-padding-reordered'; Input='source-240.mp4'; Ratio='16:9'; Mode='여백'; Fps='240'; Ranges='1.5:2.5,0:0.5'; Width=640; Height=360; Frames=360; Audio=$true; Rate=240.0; Duration=1.5},
 @{Name='60fps-vertical-crop-silent'; Input='무음 영상.mp4'; Ratio='9:16'; Mode='채우기'; Fps='60'; Ranges='0.25:1.25'; Width=360; Height=640; Frames=60; Audio=$false; Rate=60.0; Duration=1.0},
 @{Name='fractional-59.94fps'; Input='source-240.mp4'; Ratio='4:3'; Mode='여백'; Fps='59.94'; Ranges='0:2'; Width=640; Height=480; Frames=120; Audio=$true; Rate=59.94; Duration=2.0},
 @{Name='original-240fps'; Input='source-240.mp4'; Ratio='원본'; Mode='여백'; Fps='240'; Ranges='0:3'; Width=640; Height=480; Frames=720; Audio=$true; Rate=240.0; Duration=3.0}
)
$results = @()
foreach ($case in $cases) {
 $output = Join-Path $testPath ($case.Name+'.mp4')
 $argText = '--test-export "'+(Join-Path $testPath $case.Input)+'" "'+$output+'" "'+$case.Ratio+'" "'+$case.Mode+'" "'+$case.Fps+'" "'+$case.Ranges+'" "원본 크기 기준"'
 $process = Start-Process -FilePath (Join-Path $projectPath 'ClipDock.exe') -ArgumentList $argText -PassThru -WindowStyle Hidden
 $process.WaitForExit()
 if ($process.ExitCode -ne 0) { if (Test-Path ($output+'.log')) { Get-Content ($output+'.log') -Tail 20 }; throw ('Export failed: '+$case.Name) }
 $metadata = (& $ffprobe -v error -show_streams -show_format -of json $output) | ConvertFrom-Json
 $video = $metadata.streams | Where-Object codec_type -eq 'video'
 $audio = @($metadata.streams | Where-Object codec_type -eq 'audio')
 $ratio = $video.avg_frame_rate.Split('/')
 $fps = [double]$ratio[0]/[double]$ratio[1]
 if ($video.width -ne $case.Width -or $video.height -ne $case.Height) { throw ('Wrong dimensions: '+$case.Name) }
 if ([Math]::Abs($fps-$case.Rate) -gt .001) { throw ('Wrong FPS: '+$case.Name) }
 if ([int]$video.nb_frames -ne $case.Frames) { throw ('Wrong frame count: '+$case.Name+' '+$video.nb_frames) }
 if (($audio.Count -gt 0) -ne $case.Audio) { throw ('Wrong audio streams: '+$case.Name) }
 if ([Math]::Abs([double]$metadata.format.duration-$case.Duration) -gt .05) { throw ('Wrong duration: '+$case.Name) }
 & $ffmpeg -v error -i $output -f null -
 if ($LASTEXITCODE -ne 0) { throw ('Decode failed: '+$case.Name) }
 $results += [pscustomobject]@{Test=$case.Name; Size=([string]$video.width+'x'+$video.height); FPS=$fps; Frames=$video.nb_frames; Duration=$metadata.format.duration; Audio=$audio.Count; Result='PASS'}
}
$results | Format-Table -AutoSize
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testPath 'results.json') -Encoding utf8
$uiReport = Join-Path $testPath 'editview-results.txt'
$uiArgs = '--test-editview "'+(Join-Path $testPath 'source-240.mp4')+'" "'+$uiReport+'" "'+(Join-Path $testPath 'editview.png')+'"'
$uiProcess = Start-Process -FilePath (Join-Path $projectPath 'ClipDock.exe') -ArgumentList $uiArgs -PassThru -WindowStyle Hidden
$uiProcess.WaitForExit()
Get-Content $uiReport
if ($uiProcess.ExitCode -ne 0) { throw 'Output timeline interaction tests failed' }
$gapOutput = $uiReport+'.mp4'
$gapMeta = (& $ffprobe -v error -select_streams v:0 -show_entries stream=avg_frame_rate,nb_frames -of json $gapOutput) | ConvertFrom-Json
if($gapMeta.streams[0].nb_frames -ne '360' -or $gapMeta.streams[0].avg_frame_rate -ne '240/1'){throw 'Wrong gap export frame count'}
foreach($outputFile in @($gapOutput)){
 $gapLog = (& $ffmpeg -hide_banner -i $outputFile -vf 'blackdetect=d=0.2:pix_th=0.02' -af 'silencedetect=n=-60dB:d=0.2' -f null - 2>&1) -join "`n"
 if($LASTEXITCODE -ne 0 -or $gapLog -notmatch 'black_start:0.5 black_end:1' -or $gapLog -notmatch 'silence_start:\s*0.50' -or $gapLog -notmatch 'silence_end:\s*1 '){throw 'Gap is not black and silent at expected times'}
}
foreach($comparison in @(@{Source='0.25';Result='0'},@{Source='2.25';Result='1'})){
 $matchLog = & $ffmpeg -hide_banner -loglevel info -ss $comparison.Source -i (Join-Path $testPath 'source-240.mp4') -ss $comparison.Result -i $gapOutput -filter_complex '[0:v]trim=end_frame=1,setpts=PTS-STARTPTS,format=yuv420p[ref];[1:v]trim=end_frame=1,setpts=PTS-STARTPTS[test];[ref][test]psnr' -frames:v 1 -an -f null - 2>&1
 $match=[regex]::Match(($matchLog -join "`n"),'average:([0-9.]+)')
 if($LASTEXITCODE -ne 0 -or !$match.Success -or [double]$match.Groups[1].Value -lt 34){throw 'Placed clip does not match source frame'}
}
'PASS: Export preserves black/silent gap from .5 to 1 seconds; positioned scenes match source; 360 frames at 240fps'



$framingSource = Join-Path $testPath 'bordered-240.mp4'
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'color=c=black:s=640x360:r=240:d=1,drawbox=x=160:y=90:w=320:h=180:color=red:t=fill' -c:v libx264 -preset ultrafast -crf 10 -pix_fmt yuv420p $framingSource
if($LASTEXITCODE -ne 0){throw 'Framing fixture failed'}
$framingReport=Join-Path $testPath 'framing-results.txt'
$framingArgs='--test-framing "'+$framingSource+'" "'+$framingReport+'" "'+(Join-Path $testPath 'framing-final.png')+'"'
$framingProcess=Start-Process -FilePath (Join-Path $projectPath 'ClipDock.exe') -ArgumentList $framingArgs -PassThru -WindowStyle Hidden
$framingProcess.WaitForExit()
Get-Content $framingReport
if($framingProcess.ExitCode -ne 0){throw 'Preview framing or transformed export tests failed'}
$framingMeta=(& $ffprobe -v error -select_streams v:0 -show_entries stream=nb_frames,avg_frame_rate -of json ($framingReport+'.zoom.mp4')) | ConvertFrom-Json
if($framingMeta.streams[0].nb_frames -ne '60' -or $framingMeta.streams[0].avg_frame_rate -ne '240/1'){throw 'Transformed export frame count changed'}
'PASS: Transformed Full HD export has exactly 60 frames at 240fps'

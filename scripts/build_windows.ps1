$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $root

$version = 'v1.21.3'
$asset = "mediamtx_${version}_windows_amd64.zip"
$base = "https://github.com/bluenviron/mediamtx/releases/download/$version"
$tools = Join-Path $root 'tools\mediamtx'
$dist = Join-Path $root 'dist\win\engine'

if (-not (Test-Path (Join-Path $tools 'mediamtx.exe'))) {
	$work = Join-Path ([System.IO.Path]::GetTempPath()) "velador-mediamtx-$PID"
	New-Item -ItemType Directory -Force $work | Out-Null
	Invoke-WebRequest "$base/$asset" -OutFile (Join-Path $work $asset)
	Invoke-WebRequest "$base/checksums.sha256" -OutFile (Join-Path $work 'checksums.sha256')
	$line = Get-Content (Join-Path $work 'checksums.sha256') | Where-Object { $_ -like "*$asset" } | Select-Object -First 1
	$expected = ($line -split '\s+')[0].ToLower()
	$actual = (Get-FileHash (Join-Path $work $asset) -Algorithm SHA256).Hash.ToLower()
	if (-not $expected -or $expected -ne $actual) { throw 'La suma de MediaMTX no coincide.' }
	New-Item -ItemType Directory -Force $tools | Out-Null
	Expand-Archive (Join-Path $work $asset) -DestinationPath $tools -Force
	Remove-Item $work -Recurse -Force
}

$ffmpegBuild = 'autobuild-2026-10-31-12-59'
$ffmpegAsset = 'ffmpeg-n9.0.2-39-g7adfbe40dc-win64-lgpl-shared-9.0.zip'
$ffmpegTools = Join-Path $root 'tools\ffmpeg'
if (-not (Test-Path (Join-Path $ffmpegTools 'ffmpeg.exe'))) {
	$work = Join-Path ([System.IO.Path]::GetTempPath()) "velador-ffmpeg-$PID"
	New-Item -ItemType Directory -Force $work | Out-Null
	$ffmpegBase = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$ffmpegBuild"
	Invoke-WebRequest "$ffmpegBase/$ffmpegAsset" -OutFile (Join-Path $work $ffmpegAsset)
	Invoke-WebRequest "$ffmpegBase/checksums.sha256" -OutFile (Join-Path $work 'checksums.sha256')
	$line = Get-Content (Join-Path $work 'checksums.sha256') | Where-Object { $_ -like "*$ffmpegAsset" } | Select-Object -First 1
	$expected = ($line -split '\s+')[0].ToLower()
	if ($expected -ne 'e907b01fa4783913151237e37df2a10fa97d99a1598a021bf59d4ae62ffed4c0') { throw 'La suma publicada de FFmpeg cambiÃ³.' }
	if ((Get-FileHash (Join-Path $work $ffmpegAsset) -Algorithm SHA256).Hash.ToLower() -ne $expected) { throw 'La suma de FFmpeg no coincide.' }
	Expand-Archive (Join-Path $work $ffmpegAsset) -DestinationPath $work -Force
	$bin = Get-ChildItem $work -Recurse -Directory -Filter bin | Select-Object -First 1
	New-Item -ItemType Directory -Force $ffmpegTools | Out-Null
	Copy-Item (Join-Path $bin.FullName 'ffmpeg.exe') $ffmpegTools
	Get-ChildItem $bin.FullName -Filter '*.dll' | Where-Object { $_.Name -notlike 'postproc*' } | Copy-Item -Destination $ffmpegTools
	Copy-Item (Join-Path $bin.Parent.FullName 'LICENSE.txt') (Join-Path $ffmpegTools 'FFMPEG_LICENSE.txt')
	Remove-Item $work -Recurse -Force
}

$model = Join-Path $root 'tools\models\yolox_tiny.onnx'
if (-not (Test-Path $model)) {
	New-Item -ItemType Directory -Force (Split-Path $model) | Out-Null
	Invoke-WebRequest 'https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_tiny.onnx' -OutFile "$model.part"
	if ((Get-FileHash "$model.part" -Algorithm SHA256).Hash.ToLower() -ne '427cc366d34e27ff7a03e2899b5e3671425c262ea2291f88bb942bc1cc70b0f7') { Remove-Item "$model.part"; throw 'La suma del modelo no coincide.' }
	Move-Item "$model.part" $model
}

$faceModel = Join-Path $root 'tools\models\face_yunet.onnx'
if (-not (Test-Path $faceModel)) {
	Invoke-WebRequest 'https://media.githubusercontent.com/media/opencv/opencv_zoo/f12e12798e8314f7c074a6656816c048dcc95b7a/models/face_detection_yunet/face_detection_yunet_2023mar.onnx' -OutFile "$faceModel.part"
	if ((Get-FileHash "$faceModel.part" -Algorithm SHA256).Hash.ToLower() -ne '8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4') { Remove-Item "$faceModel.part"; throw 'La suma del modelo de caras no coincide.' }
	Move-Item "$faceModel.part" $faceModel
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish Velador.Engine -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o $dist
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el motor.' }
Remove-Item (Join-Path $dist 'DirectML.Debug.dll') -ErrorAction SilentlyContinue
Copy-Item (Join-Path $tools 'mediamtx.exe') $dist
Copy-Item (Join-Path $tools 'LICENSE') (Join-Path $dist 'MEDIAMTX_LICENSE.txt')
Copy-Item (Join-Path $root 'web') (Join-Path $dist 'web') -Recurse
Copy-Item $ffmpegTools (Join-Path $dist 'ffmpeg') -Recurse
Copy-Item (Split-Path $model) (Join-Path $dist 'models') -Recurse

npm ci --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw 'npm ci fallÃ³.' }
npx tauri build --config desktop/src-tauri/tauri.windows.conf.json
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar la app.' }
Get-ChildItem (Join-Path $root 'desktop\src-tauri\target\release\bundle\nsis\*.exe') | ForEach-Object { Write-Host "Instalador: $($_.FullName)" }

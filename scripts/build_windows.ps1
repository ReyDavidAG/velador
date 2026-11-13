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

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish Velador.Engine -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o $dist
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el motor.' }
Copy-Item (Join-Path $tools 'mediamtx.exe') $dist
Copy-Item (Join-Path $tools 'LICENSE') (Join-Path $dist 'MEDIAMTX_LICENSE.txt')
Copy-Item (Join-Path $root 'web') (Join-Path $dist 'web') -Recurse

npm ci --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw 'npm ci falló.' }
npx tauri build --config desktop/src-tauri/tauri.windows.conf.json
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar la app.' }
Get-ChildItem (Join-Path $root 'desktop\src-tauri\target\release\bundle\nsis\*.exe') | ForEach-Object { Write-Host "Instalador: $($_.FullName)" }

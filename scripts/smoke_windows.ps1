$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
$engine = Join-Path $root 'dist\win\engine\Velador.Engine.exe'
$env:VELADOR_HOME = Join-Path ([System.IO.Path]::GetTempPath()) "velador-smoke-$PID"
$env:VELADOR_PORT = '8452'
$api = 'http://127.0.0.1:8452'

$process = Start-Process $engine -PassThru
try {
	$token = $null
	foreach ($i in 1..30) {
		Start-Sleep -Seconds 1
		$file = Join-Path $env:VELADOR_HOME 'token'
		if (Test-Path $file) { $token = (Get-Content $file -Raw).Trim() }
		if ($token) { try { Invoke-RestMethod "$api/api/summary" -Headers @{ 'X-Velador-Token' = $token } | Out-Null; break } catch { } }
	}
	if (-not $token) { throw 'El motor no creó su token.' }
	$headers = @{ 'X-Velador-Token' = $token }
	$summary = Invoke-RestMethod "$api/api/summary" -Headers $headers
	if ($summary.state -ne 'no_camera') { throw "Estado inesperado: $($summary.state)" }
	$denied = try { Invoke-WebRequest "$api/api/summary" -UseBasicParsing; 200 } catch { $_.Exception.Response.StatusCode.value__ }
	if ($denied -ne 401) { throw "Sin token debió responder 401 y respondió $denied" }
	$page = Invoke-WebRequest "$api/" -UseBasicParsing
	if ($page.Content -notmatch 'Velador') { throw 'El motor no sirvió la interfaz.' }
	$settings = Invoke-RestMethod "$api/api/settings" -Headers $headers
	if (-not $settings.defaultDir) { throw 'Ajustes sin carpeta por omisión.' }
	$body = @{ recordingsDir = $null; retentionDays = 7; retentionPercent = 70; keepAwake = $false } | ConvertTo-Json
	$saved = Invoke-RestMethod "$api/api/settings" -Method Put -Headers $headers -ContentType 'application/json' -Body $body
	if ($saved.retentionDays -ne 7) { throw 'Los ajustes no se guardaron.' }
	$found = Invoke-RestMethod "$api/api/cameras/discover" -Headers $headers
	Write-Host "Resumen: $($summary.state) · carpeta: $($settings.defaultDir) · discos: $(@($settings.volumes).Count) · cámaras en la red: $(@($found).Count)"
} finally {
	Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}

Remove-Item Env:VELADOR_HOME, Env:VELADOR_PORT
$install = Start-Process $engine -ArgumentList 'install' -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "install terminó con $($install.ExitCode)" }
schtasks /Query /TN 'Velador\Engine' | Out-Null
$viaTask = $LASTEXITCODE -eq 0
$viaRun = (reg query 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run' /v Velador 2>$null) -ne $null
if (-not ($viaTask -or $viaRun)) { throw 'No quedó registrado el arranque con la sesión.' }
Start-Sleep -Seconds 6
$running = @(Get-Process 'Velador.Engine' -ErrorAction SilentlyContinue).Count
Write-Host "Arranque registrado (tarea: $viaTask, clave Run: $viaRun) · motores corriendo: $running"
$uninstall = Start-Process $engine -ArgumentList 'uninstall' -Wait -PassThru
Get-Process 'Velador.Engine' -ErrorAction SilentlyContinue | Stop-Process -Force
if ($uninstall.ExitCode -ne 0) { throw "uninstall terminó con $($uninstall.ExitCode)" }
Write-Host 'Prueba de humo en Windows: bien.'

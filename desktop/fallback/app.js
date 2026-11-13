const invoke = cmd => window.__TAURI_INTERNALS__.invoke(cmd)
const status = document.getElementById('status')
const start = document.getElementById('start')

let waitingSince = 0

const check = async () => {
	if (await invoke('engine_status').catch(() => false)) return
	if (waitingSince && Date.now() - waitingSince > 20000) {
		waitingSince = 0
		start.disabled = false
		status.textContent = 'El motor no respondió. Vuelve a intentar.'
	}
}

start.addEventListener('click', async () => {
	start.disabled = true
	status.textContent = 'Arrancando el motor…'
	try {
		await invoke('start_engine')
		waitingSince = Date.now()
		status.textContent = 'Esperando a que responda…'
	} catch (error) {
		start.disabled = false
		status.textContent = error === 'not-installed' ? 'El motor no está instalado. Corre scripts/install_macos.sh desde la carpeta de Velador.' : String(error)
	}
})

setInterval(check, 2000)
check()

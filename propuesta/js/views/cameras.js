import { sceneHtml, updateScene } from '../scene.js'
import { nowSec, SUMMARY } from '../store.js'
import { esc, I, toast } from '../ui.js'

const CHECKS = [
	['Video principal', 'stream1 · 1080p · H.264, se graba tal cual'],
	['Video ligero', 'stream2 · 720p, para miniaturas y vista remota'],
	['Sonido', 'G.711 de la cámara, guardado en AAC'],
	['Movimiento de la cámara', 'ONVIF en el puerto 2020'],
]

const html = () => {
	const c = SUMMARY.camera
	return `<div class="cameras">
		<article class="card cam animate-tile-in">
			<div class="thumb cam-thumb">${sceneHtml()}<span class="badge live"><i class="dot pulse"></i>EN VIVO</span></div>
			<div class="cam-body">
				<header class="cam-head"><div><h2>${esc(c.name)}</h2><p class="muted-s">${esc(c.model)} · firmware <span class="mono">${c.firmware}</span></p></div><span class="chip good"><i class="dot"></i>Conectada</span></header>
				<dl class="kv">
					<div><dt>Dirección</dt><dd class="mono">${esc(c.host)}</dd></div>
					<div><dt>Imagen</dt><dd>${esc(c.codec)}</dd></div>
					<div><dt>Peso</dt><dd><span class="mono">${c.bitrate} Mbps</span> · unos <span class="mono">${SUMMARY.perDay} GB</span> al día</dd></div>
					<div><dt>Cuenta de cámara</dt><dd class="secret">${I.lock}<span>Guardada en el llavero del sistema</span></dd></div>
				</dl>
				<ul class="checks">${CHECKS.map(([t, d], i) => `<li class="animate-row-in" style="--i:${i}"><span class="ok">${I.check}</span><span><b>${t}</b><small>${d}</small></span></li>`).join('')}</ul>
				<div class="cam-actions"><button class="btn" type="button" data-test>Probar conexión</button><a class="btn" href="#/settings">Ajustes de grabación</a><a class="btn ghost" href="#/">Ver en vivo ${I.arrow}</a></div>
			</div>
		</article>
		<a class="add-cam animate-tile-in" style="--i:1" href="#/welcome"><span class="add-ico">${I.plus}</span><b>Agregar otra cámara</b><span>Cualquier cámara con RTSP u ONVIF. Velador la busca en tu red.</span></a>
	</div>`
}

const mount = main => {
	updateScene(main.querySelector('.scene'), { hour: nowSec() / 3600 })
	const onClick = e => {
		const b = e.target.closest('[data-test]')
		if (!b) return
		b.disabled = true
		b.textContent = 'Probando…'
		setTimeout(() => {
			b.disabled = false
			b.textContent = 'Probar conexión'
			toast('La cámara respondió bien en las 4 pruebas')
		}, 1400)
	}
	main.addEventListener('click', onClick)
	return () => main.removeEventListener('click', onClick)
}

export const camerasView = { title: 'Cámaras', html, mount }

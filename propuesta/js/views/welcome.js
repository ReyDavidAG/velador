import { sceneHtml, updateScene } from '../scene.js'
import { nowSec, state, SUMMARY } from '../store.js'
import { I } from '../ui.js'

const STEPS = ['Encuentra tu cámara', 'Cuenta de cámara', 'Prueba', 'Dónde guardar']

const bodies = [
	() => `<h2>Encuentra tu cámara</h2><p class="lead">Velador busca cámaras en tu red. No sale nada a internet.</p>
		<div class="found"><button class="found-row on" type="button"><span class="ev-ico span">${I.camera}</span><span class="ev-main"><b>Tapo C500</b><small class="mono">${SUMMARY.camera.host} · ONVIF</small></span><span class="radio on"></span></button>
		<button class="found-row" type="button" disabled><span class="ev-ico">${I.plus}</span><span class="ev-main"><b>Escribir la dirección a mano</b><small>Si tu cámara no aparece en la lista</small></span></button></div>`,
	() => `<h2>Cuenta de cámara</h2><p class="lead">No es tu cuenta de TP-Link. Es la que creas en la app Tapo: Ajustes de la cámara › Avanzado › Cuenta de cámara.</p>
		<label class="field"><span>Usuario</span><input type="text" value="velador" autocomplete="off" spellcheck="false" /></label>
		<label class="field"><span>Contraseña</span><input type="password" value="ejemplo-1234" autocomplete="off" /></label>
		<p class="note">${I.lock}<span>Se guarda en el llavero de tu sistema. Nunca en un archivo ni en los registros.</span></p>`,
	() => `<h2>Prueba</h2><p class="lead">Velador revisa que todo responda antes de empezar a grabar.</p>
		<div class="test"><div class="thumb">${sceneHtml()}<span class="badge live"><i class="dot pulse"></i>EN VIVO</span></div>
		<ul class="checks">${['Video principal · 1080p', 'Video ligero · 720p', 'Sonido', 'Movimiento de la cámara'].map((t, i) => `<li class="animate-row-in" style="--i:${i * 4}"><span class="ok">${I.check}</span><span><b>${t}</b></span></li>`).join('')}</ul></div>`,
	() => `<h2>Dónde guardar</h2><p class="lead">Puedes cambiarlo después en Ajustes.</p>
		<div class="set-row"><div class="set-text"><b>Carpeta</b><small class="mono">~/Movies/Velador</small></div><span class="btn small">Cambiar</span></div>
		<div class="set-row"><div class="set-text"><b>Guardar hasta</b><small>Lo más viejo se borra primero</small></div><span class="mono strong">14 días</span></div>
		<p class="note">${I.disk}<span>Unos <b class="mono">${SUMMARY.perDay} GB</b> por día: <b class="mono">${Math.round(SUMMARY.perDay * 14)} GB</b> en total. Tienes <b class="mono">211 GB</b> libres.</span></p>`,
]

const html = () => {
	const s = state.welcome
	return `<div class="welcome">
		<aside class="wl-brand">
			<svg class="wl-eye" viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="10" fill="none" stroke="currentColor" stroke-width="2.2"/><circle class="mark-eye" cx="16" cy="16" r="4.4"/></svg>
			<h2>Velador</h2><p>El que cuida mientras duermes.</p>
			<ol class="wl-steps">${STEPS.map((t, i) => `<li class="${i === s ? 'now' : i < s ? 'done' : ''}"><span class="mono">${i < s ? '✓' : i + 1}</span>${t}</li>`).join('')}</ol>
		</aside>
		<section class="wl-body"><div class="animate-page-in" data-step>${bodies[s]()}</div>
			<footer class="wl-foot"><button class="btn ghost" type="button" data-back ${s === 0 ? 'disabled' : ''}>Atrás</button><span class="mono muted-s">${s + 1} / ${STEPS.length}</span><span class="spacer"></span><button class="btn primary" type="button" data-next>${s === STEPS.length - 1 ? 'Empezar la guardia' : 'Continuar'} ${I.arrow}</button></footer>
		</section>
	</div>`
}

const mount = main => {
	const paint = () => updateScene(main.querySelector('.scene'), { hour: nowSec() / 3600 })
	paint()
	const onClick = e => {
		const dir = e.target.closest('[data-next]') ? 1 : e.target.closest('[data-back]') ? -1 : 0
		if (!dir) return
		if (state.welcome + dir >= STEPS.length) {
			state.welcome = 0
			location.hash = '#/'
			return
		}
		state.welcome = Math.max(0, state.welcome + dir)
		main.querySelector('.page').innerHTML = html()
		paint()
	}
	main.addEventListener('click', onClick)
	return () => main.removeEventListener('click', onClick)
}

export const welcomeView = { title: 'Bienvenida', html, mount }

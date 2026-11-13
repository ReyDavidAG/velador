import { sceneHtml, updateScene } from '../scene.js'
import { guardSince, nowSec, ROUNDS } from '../store.js'
import { hm, I, pct, span } from '../ui.js'

const eye = `<svg viewBox="0 0 32 32" width="16" height="16" aria-hidden="true"><circle cx="16" cy="16" r="10" fill="none" stroke="currentColor" stroke-width="3"/><circle cx="16" cy="16" r="4.4" fill="currentColor"/></svg>`

const menu = () => `
	<div class="tray-menu">
		<div class="tm-head"><span class="dot live"></span><b>De guardia</b><span class="mono">${span(Math.floor(guardSince() / 60) * 60)}</span></div>
		<div class="tm-thumb">${sceneHtml()}<span class="mono">${hm(nowSec())}</span></div>
		<div class="tm-line">Cobertura de hoy<span class="mono">${pct(ROUNDS[0].coverage)}</span></div>
		<div class="tm-line">Último suceso<span>Persona · ${hm(ROUNDS[0].events.at(-1)?.t ?? 0)}</span></div>
		<hr />
		<div class="tm-item strong">Abrir Velador</div>
		<div class="tm-item">Ver la ronda de hoy</div>
		<div class="tm-item">Pausar la guardia 15 min</div>
		<hr />
		<div class="tm-item check">${I.check}Empezar con la sesión</div>
		<div class="tm-item">Salir de Velador</div>
	</div>`

const STATES = [
	['good', 'De guardia', 'Grabando sin huecos. Es lo que ves casi siempre.'],
	['warn', 'Reconectando', 'Sin imagen hace 12 s. Tercer intento; el hueco ya se está contando.'],
	['bad', 'Sin grabar', 'La cámara no responde desde las 03:12. Aviso del sistema a los 30 s.'],
	['idle', 'En pausa', 'La detuviste tú. Vuelve sola en 14 min y queda anotada como pausa.'],
	['warn', 'Disco casi lleno', 'Quedan 4 GB. Se borra lo más viejo antes de dejar de grabar.'],
	['bad', 'Cuenta rechazada', 'La cámara no acepta la contraseña. No se reintenta hasta que la corrijas.'],
	['idle', 'Sin cámara', 'Todavía no agregas ninguna. Te lleva a la bienvenida.'],
]

const html = () => `<div class="desktop">
	<p class="lead">Así vive Velador fuera de su ventana. Cerrar la ventana no detiene la guardia: el motor sigue y la app se queda en la barra.</p>
	<div class="os-grid">
		<section class="os mac animate-tile-in"><header class="os-tag">macOS · barra de menú</header>
			<div class="mac-bar"><span></span><span class="mac-ico on">${eye}</span><span class="mac-ico">${I.wifi}</span><span class="mono">${hm(nowSec())}</span></div>
			${menu()}
		</section>
		<section class="os win animate-tile-in" style="--i:1"><header class="os-tag">Windows 11 · bandeja</header>
			${menu()}
			<div class="win-bar"><span></span><span class="win-ico">${I.up}</span><span class="win-ico on">${eye}</span><span class="win-ico">${I.wifi}</span><span class="mono">${hm(nowSec())}</span></div>
		</section>
		<section class="os notes animate-tile-in" style="--i:2"><header class="os-tag">Avisos del sistema</header>
			<div class="notif"><span class="notif-ico">${eye}</span><div><b>Persona en el portón</b><p>Entrada · hace un momento. Toca para verla en la ronda.</p></div><span class="thumb tiny">${sceneHtml()}</span></div>
			<div class="notif warn"><span class="notif-ico">${eye}</span><div><b>Hueco en la grabación</b><p>Sin señal de la cámara desde las 03:12. Reintentando cada 30 s.</p></div></div>
			<div class="notif"><span class="notif-ico">${eye}</span><div><b>De guardia otra vez</b><p>La cámara volvió. El hueco duró 1 min 36 s.</p></div></div>
		</section>
		<section class="os fall animate-tile-in" style="--i:3"><header class="os-tag">Si el motor no está corriendo</header>
			<div class="fallback"><span class="fb-eye">${eye}</span><b>Velador no está de guardia</b><p>El motor de grabación está detenido. Nada se está guardando en este momento.</p><span class="btn primary">Iniciar la guardia</span></div>
		</section>
		<section class="os states-card animate-tile-in" style="--i:4"><header class="os-tag">Los estados de la guardia</header>
			<ul class="states">${STATES.map(([k, t, d]) => `<li><span class="state-pill ${k}"><i class="dot"></i>${t}</span><span>${d}</span></li>`).join('')}</ul>
		</section>
	</div>
</div>`

const mount = main => {
	for (const s of main.querySelectorAll('.scene')) updateScene(s, { hour: nowSec() / 3600, person: Boolean(s.closest('.notif')), x: 0.4 })
}

export const desktopView = { title: 'Escritorio', html, mount }

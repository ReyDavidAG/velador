import { state, SUMMARY } from '../store.js'
import { I, segment, toast, toggle } from '../ui.js'

const row = (title, hint, control) => `<div class="set-row"><div class="set-text"><b>${title}</b><small>${hint}</small></div><div class="set-ctl">${control}</div></div>`
const stepper = (id, value, unit) => `<div class="stepper" data-stepper="${id}"><button type="button" data-step="-1" aria-label="Menos">−</button><span class="mono"><b data-val>${value}</b> ${unit}</span><button type="button" data-step="1" aria-label="Más">+</button></div>`
const sw = (id, label) => toggle(id, state.settings[id], label)

const fits = () => {
	const d = SUMMARY.disk
	const budget = (d.total * state.settings.pct) / 100 - d.other
	return Math.max(0, Math.floor(budget / SUMMARY.perDay))
}
const storageNote = () => {
	const n = fits()
	const real = Math.min(n, state.settings.days)
	return `Con <b class="mono">${SUMMARY.perDay} GB</b> al día caben <b class="mono">${n} días</b> antes de llegar al ${state.settings.pct} % del disco. Hoy se guardarían <b class="mono">${real} días</b>${n < state.settings.days ? ', menos de los que pediste: manda el límite del disco.' : '.'}`
}

const html = () => {
	const d = SUMMARY.disk
	const free = d.total - d.velador - d.other
	return `<div class="settings">
		<section class="card animate-tile-in"><header class="card-head"><h2>${I.disk}Dónde se guarda</h2></header>
			${row('Carpeta de grabaciones', 'Un archivo cada 10 minutos, ordenados por cámara y por día.', `<span class="path mono">~/Movies/Velador</span><button class="btn small" type="button" data-pick>Cambiar</button>`)}
			${row('Guardar hasta', 'Lo más viejo se borra primero. Los recortes nunca se borran.', stepper('days', state.settings.days, 'días'))}
			${row('Sin pasar de', 'Del disco entero, contando lo que ya ocupan tus otras cosas.', stepper('pct', state.settings.pct, '%'))}
			<div class="disk wide"><div class="disk-bar"><i class="v" style="width:${(d.velador / d.total) * 100}%"></i><i class="o" style="width:${(d.other / d.total) * 100}%"></i><i class="limit" data-limit style="left:${state.settings.pct}%"></i></div>
			<p class="disk-l"><span><i class="sw v"></i>Velador <b class="mono">${d.velador} GB</b></span><span><i class="sw o"></i>Otras cosas <b class="mono">${d.other} GB</b></span><span><i class="sw f"></i>Libre <b class="mono">${free} GB</b></span></p>
			<p class="note" data-note>${I.disk}<span>${storageNote()}</span></p></div>
		</section>
		<section class="card animate-tile-in" style="--i:1"><header class="card-head"><h2>${I.shield}Grabación sin huecos</h2></header>
			${row('Mantener el equipo despierto', 'Mientras Velador esté de guardia. La pantalla sí se apaga.', sw('awake', 'Mantener el equipo despierto'))}
			${row('Empezar con la sesión', 'El motor arranca solo al iniciar tu sesión, sin abrir la ventana.', sw('autostart', 'Empezar con la sesión'))}
			${row('Seguir en la bandeja al cerrar', 'Cerrar la ventana no detiene la guardia.', sw('tray', 'Seguir en la bandeja'))}
			${row('Motor en otro equipo', 'Para cero huecos por reposo: el motor graba en un equipo siempre prendido y este sólo mira.', `<span class="chip">Graba este equipo</span><button class="btn small" type="button" data-soon>Mover el motor</button>`)}
		</section>
		<section class="card animate-tile-in" style="--i:2"><header class="card-head"><h2>${I.events}Sucesos y avisos</h2></header>
			${row('Detectar movimiento y personas', 'Primero con los avisos de la propia cámara, que no gastan procesador.', sw('detect', 'Detectar'))}
			${row('Avisar cuando haya una persona', 'Aviso del sistema con la foto del momento.', sw('notifyPerson', 'Avisar persona'))}
			${row('Avisar con cualquier movimiento', 'Puede ser mucho en exterior: hojas, sombras, lluvia.', sw('notifyMotion', 'Avisar movimiento'))}
			${row('Avisar si hay un hueco', 'Cuando pase de 30 segundos sin grabar.', sw('notifyGap', 'Avisar hueco'))}
			${row('También en el celular', 'Opcional, por ntfy. Es lo único que sale de tu red: sólo el texto del aviso, sin imagen.', sw('ntfy', 'Avisar al celular'))}
		</section>
		<section class="card animate-tile-in" style="--i:3"><header class="card-head"><h2>${I.phone}Verla fuera de casa</h2></header>
			${row('Acceso por Tailscale', 'Una red privada entre tus equipos. No se abre ningún puerto del router.', `<span class="chip">Sin configurar</span><button class="btn small" type="button" data-soon>Cómo se hace</button>`)}
			${row('Apariencia', 'El tema oscuro cansa menos junto al video.', segment('theme', [['dark', 'Oscuro'], ['light', 'Claro']], state.theme, 'Apariencia'))}
			${row('Cámaras', 'Conexión, cuenta de cámara y pruebas.', `<a class="btn small" href="#/cameras">Abrir ${I.arrow}</a>`)}
		</section>
	</div>`
}

const mount = main => {
	const onClick = e => {
		const step = e.target.closest('[data-step]')
		if (step) {
			const box = step.closest('[data-stepper]')
			const id = box.dataset.stepper
			const [min, max, by] = id === 'days' ? [1, 90, 1] : [50, 95, 5]
			state.settings[id] = Math.min(max, Math.max(min, state.settings[id] + Number(step.dataset.step) * by))
			box.querySelector('[data-val]').textContent = state.settings[id]
			main.querySelector('[data-limit]').style.left = `${state.settings.pct}%`
			main.querySelector('[data-note] span').innerHTML = storageNote()
		}
		if (e.target.closest('[data-pick]')) toast('En la app, esto abre el selector de carpetas del sistema')
		if (e.target.closest('[data-soon]')) toast('Todavía no está en esta propuesta')
	}
	const onSwitch = e => {
		state.settings[e.detail.id] = e.detail.on
	}
	const onSeg = e => {
		if (e.detail.id === 'theme' && e.detail.value !== state.theme) document.querySelector('[data-theme-toggle]').click()
	}
	main.addEventListener('click', onClick)
	main.addEventListener('switch', onSwitch)
	main.addEventListener('segment', onSeg)
	return () => {
		main.removeEventListener('click', onClick)
		main.removeEventListener('switch', onSwitch)
		main.removeEventListener('segment', onSeg)
	}
}

export const settingsView = { title: 'Ajustes', html, mount }

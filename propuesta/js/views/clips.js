import { sceneHtml, updateScene } from '../scene.js'
import { CLIPS, ROUNDS, state } from '../store.js'
import { dateLabel, esc, hms, I, span, toast } from '../ui.js'

const row = (c, i) => {
	const day = c.date.toLocaleDateString('es-MX', { weekday: 'long' })
	return `<article class="clip-row animate-row-in" style="--i:${i}" data-id="${c.id}" data-hour="${c.t / 3600}">
		<span class="thumb small">${sceneHtml()}<span class="thumb-t mono">${span(c.dur)}</span></span>
		<span class="clip-main"><b>${esc(c.name)}</b><small><span class="mono">${c.id}</span> · ${esc(day)} ${esc(dateLabel(c.date))} · <span class="mono">${hms(c.t)}</span></small></span>
		<span class="mono clip-size">${c.size.toFixed(1)} MB</span>
		<span class="row-actions"><a class="btn small" href="#/round" data-open="${c.id}">Ver en la ronda</a><button class="btn small" type="button" data-folder>${I.folder}<span>Mostrar archivo</span></button><button class="icon-btn danger" type="button" data-del="${c.id}" title="Borrar recorte">${I.trash}<span class="sr-only">Borrar recorte</span></button></span>
	</article>`
}

const list = () =>
	CLIPS.length
		? CLIPS.map(row).join('')
		: `<div class="empty"><span class="empty-ico">${I.clips}</span><b>Todavía no hay recortes</b><p>En la Ronda, presiona Recortar, ajusta las asas y guarda. Aquí quedan a salvo del borrado de lo viejo.</p><a class="btn primary" href="#/round">Ir a la ronda</a></div>`

const html = () => `<div class="clips"><p class="lead">Pedazos de grabación guardados aparte, en MP4 que abre cualquier reproductor. Nunca se borran con lo viejo.</p><div class="card list" data-list>${list()}</div></div>`

const mount = main => {
	const paint = () => {
		for (const c of main.querySelectorAll('.clip-row')) updateScene(c.querySelector('.scene'), { hour: Number(c.dataset.hour), person: true, x: 0.4 })
	}
	paint()
	const onClick = e => {
		const del = e.target.closest('[data-del]')
		if (del) {
			const at = CLIPS.findIndex(c => c.id === del.dataset.del)
			if (at >= 0) CLIPS.splice(at, 1)
			main.querySelector('[data-list]').innerHTML = list()
			paint()
			toast('Recorte borrado')
		}
		if (e.target.closest('[data-folder]')) toast('En la app, esto abre la carpeta de recortes')
		const open = e.target.closest('[data-open]')
		if (open) {
			const c = CLIPS.find(x => x.id === open.dataset.open)
			const at = ROUNDS.findIndex(r => r.date.getTime() === c.date.getTime())
			if (at < 0) return
			state.day = at
			state.t = c.t
			state.playing = true
		}
	}
	main.addEventListener('click', onClick)
	return () => main.removeEventListener('click', onClick)
}

export const clipsView = { title: 'Recortes', html, mount }

import { api } from '../services/api.js'
import { state } from '../store.js'
import { bytes, confirmOnce, esc, I, span, toast } from '../ui.js'

const when = ms => new Date(ms).toLocaleString('es-MX', { weekday: 'long', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })

const row = (c, i) => `<article class="clip-row animate-row-in" style="--i:${i}" data-id="${c.id}">
	<span class="thumb small">${c.available ? `<video muted playsinline preload="metadata" class="${state.summary?.cameras.find(x => x.id === c.cameraId)?.rotation === 180 ? 'rot' : ''}" src="${api.clipUrl(c.id)}#t=0.5"></video>` : ''}<span class="thumb-t mono">${span((c.endMs - c.startMs) / 1000)}</span></span>
	<span class="clip-main"><b>${esc(c.title)}</b><small>${esc(when(c.startMs))}${c.available ? '' : ' · el archivo no está: ¿el disco externo está conectado?'}</small></span>
	<span class="mono clip-size">${bytes(c.bytes)}</span>
	<span class="row-actions"><button class="btn small" type="button" data-play="${c.id}" ${c.available ? '' : 'disabled'}>${I.play}<span>Ver</span></button><button class="btn small" type="button" data-reveal="${c.id}" ${c.available ? '' : 'disabled'}>${I.folder}<span>Mostrar archivo</span></button><button class="icon-btn danger" type="button" data-del="${c.id}" title="Borrar recorte">${I.trash}<span class="sr-only">Borrar recorte</span></button></span>
</article>`

const html = () => `<div class="clips"><p class="lead">Pedazos de grabación guardados aparte, en MP4. Nunca se borran con lo viejo.</p>
	<section class="stage" data-player hidden style="margin-bottom:14px;max-height:60vh"><video controls playsinline></video></section>
	<div class="card list" data-list><p class="empty-s">Cargando…</p></div></div>`

const mount = main => {
	const list = main.querySelector('[data-list]')
	const player = main.querySelector('[data-player]')
	const draw = async () => {
		const clips = await api.clips()
		list.innerHTML = clips.length
			? clips.map(row).join('')
			: `<div class="empty"><span class="empty-ico">${I.clips}</span><b>Todavía no hay recortes</b><p>En la Ronda, presiona Recortar, ajusta las asas y guarda. O en En vivo, guarda los últimos 30 segundos.</p><a class="btn primary" href="#/round">Ir a la ronda</a></div>`
	}
	const onClick = async e => {
		const play = e.target.closest('[data-play]')
		if (play) {
			player.hidden = false
			const video = player.querySelector('video')
			video.src = api.clipUrl(play.dataset.play)
			video.play().catch(() => {})
			player.scrollIntoView({ block: 'nearest' })
		}
		const reveal = e.target.closest('[data-reveal]')
		if (reveal) api.revealClip(reveal.dataset.reveal).catch(err => toast(err.message))
		const del = e.target.closest('[data-del]')
		if (del && confirmOnce(del, 'Borrar')) {
			await api.removeClip(del.dataset.del).catch(err => toast(err.message))
			player.hidden = true
			draw()
		}
	}
	main.addEventListener('click', onClick)
	draw().catch(() => (list.innerHTML = '<p class="empty-s">No se pudieron leer los recortes.</p>'))
	return () => main.removeEventListener('click', onClick)
}

export const clipsView = { title: 'Recortes', html, mount }

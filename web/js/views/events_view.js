import { api } from '../services/api.js'
import { loadWeek, state } from '../store.js'
import { dateLabel, dayLabel, esc, I, KIND, segment, span } from '../ui.js'

const card = (r, e, i) =>
	`<a class="ev-card animate-tile-in" style="--i:${i}" href="#/round" data-day="${r.i}" data-t="${e.t}">
		<span class="thumb"><video muted playsinline preload="none" class="${state.camera.rotation === 180 ? 'rot' : ''}" data-src="${api.mediaUrl(state.camera.id, r.ms(e.t), 2000, 'minor')}#t=0.5"></video><span class="badge kind ${e.kind}">${I[KIND[e.kind]?.icon ?? 'motion']}${esc(KIND[e.kind]?.label ?? 'Suceso')}</span><span class="thumb-t mono">${span(e.dur)}</span><span class="thumb-play">${I.play}</span></span>
		<span class="ev-card-b"><b class="mono">${r.clock(e.t)}</b><small class="mono">S-${e.id}</small></span>
	</a>`

const groups = rounds =>
	rounds
		.filter(Boolean)
		.map(r => ({ r, list: r.events.filter(e => state.filter === 'all' || e.kind === state.filter).reverse() }))
		.filter(g => g.list.length)
		.map(g => `<section class="ev-group"><header class="group-head"><h2>${esc(dayLabel(g.r.date, g.r.i))}</h2><span class="mono">${esc(dateLabel(g.r.date))}</span><span class="count">${g.list.length}</span></header><div class="ev-grid">${g.list.map((e, i) => card(g.r, e, i)).join('')}</div></section>`)
		.join('') ||
	`<div class="empty"><span class="empty-ico">${I.events}</span><b>Sin sucesos en los últimos 7 días</b><p>Los sucesos vienen de la detección de la propia cámara: actívala en la app de la cámara (movimiento y personas) y aparecerán aquí y marcados en la ronda.</p></div>`

const html = () => `<div class="events">
	<div class="toolbar">${segment('filter', [['all', 'Todos'], ['person', 'Personas'], ['motion', 'Movimiento']], state.filter, 'Tipo de suceso')}</div>
	<div data-groups><p class="empty-s">Cargando…</p></div>
</div>`

const mount = main => {
	let rounds = []
	const watcher = new IntersectionObserver(
		entries => {
			for (const entry of entries) {
				if (!entry.isIntersecting) continue
				entry.target.src = entry.target.dataset.src
				watcher.unobserve(entry.target)
			}
		},
		{ rootMargin: '200px' }
	)
	const draw = () => {
		main.querySelector('[data-groups]').innerHTML = groups(rounds)
		for (const v of main.querySelectorAll('video[data-src]')) watcher.observe(v)
	}
	const onSeg = e => {
		if (e.detail.id !== 'filter') return
		state.filter = e.detail.value
		draw()
	}
	const onClick = e => {
		const c = e.target.closest('.ev-card')
		if (!c) return
		state.day = Number(c.dataset.day)
		state.t = Math.max(0, Number(c.dataset.t) - 3)
		state.playing = true
	}
	main.addEventListener('segment', onSeg)
	main.addEventListener('click', onClick)
	loadWeek().then(w => {
		rounds = w
		draw()
	})
	return () => {
		watcher.disconnect()
		main.removeEventListener('segment', onSeg)
		main.removeEventListener('click', onClick)
	}
}

export const eventsView = { title: 'Sucesos', html, mount }

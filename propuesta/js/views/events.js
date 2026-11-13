import { sceneHtml, updateScene } from '../scene.js'
import { ROUNDS, state } from '../store.js'
import { dateLabel, dayLabel, esc, hms, I, KIND, segment, span } from '../ui.js'

let shown = 2

const card = (e, i) =>
	`<a class="ev-card animate-tile-in" style="--i:${i}" href="#/round" data-day="${e.day}" data-t="${e.t}" data-kind="${e.kind}" data-hour="${e.t / 3600}" data-x="${e.x}">
		<span class="thumb">${sceneHtml()}<span class="badge kind ${e.kind}">${I[KIND[e.kind].icon]}${KIND[e.kind].label}</span><span class="thumb-t mono">${span(e.dur)}</span><span class="thumb-play">${I.play}</span></span>
		<span class="ev-card-b"><b class="mono">${hms(e.t)}</b><span>${esc(e.zone)}</span><small class="mono">${e.id}</small></span>
	</a>`

const groups = () =>
	ROUNDS.slice(0, shown)
		.map(r => {
			const list = r.events.filter(e => state.filter === 'all' || e.kind === state.filter).reverse()
			return `<section class="ev-group"><header class="group-head"><h2>${esc(dayLabel(r.date, r.i))}</h2><span class="mono">${esc(dateLabel(r.date))}</span><span class="count">${list.length}</span></header>
			${list.length ? `<div class="ev-grid">${list.map(card).join('')}</div>` : '<p class="empty-s">Nada con este filtro.</p>'}</section>`
		})
		.join('')

const html = () => `<div class="events">
	<div class="toolbar">${segment('filter', [['all', 'Todos'], ['person', 'Personas'], ['motion', 'Movimiento']], state.filter, 'Tipo de suceso')}<span class="spacer"></span><span class="muted-s">Las miniaturas salen del video ligero</span></div>
	<div data-groups>${groups()}</div>
	<div class="more">${shown < ROUNDS.length ? `<button class="btn" type="button" data-more>Cargar días anteriores</button>` : ''}</div>
</div>`

const paint = main => {
	for (const c of main.querySelectorAll('.ev-card')) updateScene(c.querySelector('.scene'), { hour: Number(c.dataset.hour), person: c.dataset.kind === 'person', motion: c.dataset.kind === 'motion', x: Number(c.dataset.x) })
}

const mount = main => {
	paint(main)
	const redraw = () => {
		main.querySelector('[data-groups]').innerHTML = groups()
		paint(main)
	}
	const onSeg = e => {
		if (e.detail.id !== 'filter') return
		state.filter = e.detail.value
		redraw()
	}
	const onClick = e => {
		const more = e.target.closest('[data-more]')
		if (more) {
			shown = Math.min(ROUNDS.length, shown + 2)
			if (shown >= ROUNDS.length) more.remove()
			redraw()
		}
		const c = e.target.closest('.ev-card')
		if (c) {
			state.day = Number(c.dataset.day)
			state.t = Math.max(0, Number(c.dataset.t) - 3)
			state.playing = true
		}
	}
	main.addEventListener('segment', onSeg)
	main.addEventListener('click', onClick)
	return () => {
		main.removeEventListener('segment', onSeg)
		main.removeEventListener('click', onClick)
	}
}

export const eventsView = { title: 'Sucesos', html, mount }

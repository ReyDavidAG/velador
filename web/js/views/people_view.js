import { api } from '../services/api.js'
import { DAYS, dateKey, dayAgo, state } from '../store.js'
import { clockAt, confirmOnce, dateLabel, dayLabel, esc, I, segment, toast } from '../ui.js'

const shot = (s, cls = '') => `<img class="${cls}" src="${api.sightingUrl(s.id)}" alt="" loading="lazy" decoding="async" />`
const face = (s, cls = '') => `<img class="${cls}" src="${api.faceUrl(s.id)}" alt="" loading="lazy" decoding="async" />`
const best = s => (s.face ? face(s, 'face') : shot(s))

const card = (p, i) => {
	const body = p.sightings.find(s => s.id === p.coverId) ?? p.sightings[0]
	const portrait = p.sightings.find(s => s.id === p.faceId)
	const times = p.sightings.length
	return `<article class="person-card animate-tile-in" style="--i:${i}">
		<button class="person-cover${portrait ? ' has-face' : ''}" type="button" data-at="${body.startMs}" title="Ver en la ronda">${shot(body)}${portrait ? face(portrait, 'cover-face') : ''}<span class="badge kind person">${I.person}${times === 1 ? 'Una vez' : `${times} veces`}</span></button>
		<div class="person-b">
			<b class="mono">${clockAt(p.firstMs)}${p.lastMs - p.firstMs > 60000 ? ` – ${clockAt(p.lastMs)}` : ''}</b>
			<div class="person-strip">${p.sightings.map(s => `<button type="button" data-at="${s.startMs}" title="${clockAt(s.startMs)} · ver en la ronda">${best(s)}<span class="mono">${clockAt(s.startMs)}</span></button>`).join('')}</div>
			<button class="btn small ghost" type="button" data-forget="${p.id}">${I.close}<span>Olvidar</span></button>
		</div>
	</article>`
}

const body = (people, day) =>
	people.length
		? `<div class="people-grid">${people.map(card).join('')}</div>`
		: `<div class="empty"><span class="empty-ico">${I.person}</span><b>${day === 0 ? 'Nadie todavía hoy' : 'Nadie ese día'}</b><p>Cuando Velador ve a una persona guarda su mejor foto de cuerpo y, si se le ve, de la cara, y la agrupa con las que se le parecen por la ropa. Sirve para el mismo día; no identifica a nadie por su cara.</p></div>`

const days = () => Array.from({ length: DAYS }, (_, i) => [String(i), i < 2 ? dayLabel(dayAgo(i), i) : dateLabel(dayAgo(i))])

const html = () => `<div class="people">
	<div class="toolbar">${segment('peopleDay', days(), state.peopleDay ?? 0, 'Día')}</div>
	<p class="muted-s people-note">${I.lock}<span>Las fotos se quedan en este equipo y se borran con la misma retención que el video.</span></p>
	<div data-people><p class="empty-s">Cargando…</p></div>
</div>`

const mount = main => {
	let alive = true
	const day = () => state.peopleDay ?? 0
	const load = async () => {
		try {
			const people = await api.people(state.camera.id, dateKey(dayAgo(day())))
			if (alive) main.querySelector('[data-people]').innerHTML = body(people, day())
		} catch {
			if (alive) main.querySelector('[data-people]').innerHTML = '<p class="empty-s">No se pudieron leer las personas.</p>'
		}
	}
	const onSeg = e => {
		if (e.detail.id !== 'peopleDay') return
		state.peopleDay = Number(e.detail.value)
		load()
	}
	const onClick = async e => {
		const at = e.target.closest('[data-at]')
		if (at) {
			const ms = Number(at.dataset.at)
			const midnight = new Date(ms)
			midnight.setHours(0, 0, 0, 0)
			state.day = day()
			state.t = Math.max(0, (ms - midnight.getTime()) / 1000 - 2)
			state.playing = true
			location.hash = '#/round'
			return
		}
		const forget = e.target.closest('[data-forget]')
		if (forget && confirmOnce(forget, '¿Borrar sus fotos?')) {
			await api.forgetPerson(forget.dataset.forget).catch(() => {})
			toast('Fotos borradas')
			load()
		}
	}
	main.addEventListener('segment', onSeg)
	main.addEventListener('click', onClick)
	load()
	const poll = setInterval(() => day() === 0 && load(), 30000)
	return () => {
		alive = false
		clearInterval(poll)
		main.removeEventListener('segment', onSeg)
		main.removeEventListener('click', onClick)
	}
}

export const peopleView = { title: 'Personas', html, mount }

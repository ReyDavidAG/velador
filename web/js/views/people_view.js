import { api } from '../services/api.js'
import { DAYS, dateKey, dayAgo, state } from '../store.js'
import { clockAt, confirmOnce, dateLabel, dayLabel, esc, I, segment, span, toast } from '../ui.js'

const KINDS = { body: 'Cuerpo', face: 'Cara', fused: 'Combinada' }
const srcOf = (s, kind) => (kind === 'face' ? api.faceUrl(s.id) : kind === 'fused' ? api.fusedUrl(s.id) : api.sightingUrl(s.id))
const kindsOf = s => ['body', ...(s.face ? ['face'] : []), ...(s.fused ? ['fused'] : [])]

const tile = (s, i) => `<button class="shot-tile animate-tile-in${s.face ? ' has-face' : ''}" style="--i:${Math.min(i, 24)}" type="button" data-open="${s.id}" title="${clockAt(s.startMs)} · ver en grande">
	<img src="${srcOf(s, s.face ? 'face' : 'body')}" alt="" loading="lazy" decoding="async" />
	${s.face ? `<img class="shot-body" src="${api.sightingUrl(s.id)}" alt="" loading="lazy" decoding="async" />` : ''}
	<span class="shot-meta"><b class="mono">${clockAt(s.startMs)}</b>${s.face ? `<span class="chip">${I.person}${s.facePx ? `cara ${s.facePx} px` : 'cara'}</span>` : ''}</span>
</button>`

const body = (shots, day, only) =>
	shots.length
		? `<div class="shot-grid">${shots.map(tile).join('')}</div>`
		: `<div class="empty"><span class="empty-ico">${I.person}</span><b>${only === 'face' ? 'Ninguna cara ese día' : day === 0 ? 'Nadie todavía hoy' : 'Nadie ese día'}</b><p>Cuando Velador ve a una persona guarda su mejor foto de cuerpo y, si se le ve de frente, la cara recortada de la grabación en alta. No identifica a nadie por su cara.</p></div>`

const days = () => Array.from({ length: DAYS }, (_, i) => [String(i), i < 2 ? dayLabel(dayAgo(i), i) : dateLabel(dayAgo(i))])

const html = () => `<div class="people">
	<div class="toolbar">${segment('peopleDay', days(), state.peopleDay ?? 0, 'Día')}<span data-filter></span></div>
	<p class="muted-s people-note">${I.lock}<span>Las fotos se quedan en este equipo y se borran con la misma retención que el video. Toca una para verla en grande y acercarla.</span></p>
	<div data-people><p class="empty-s">Cargando…</p></div>
	<div class="lightbox" data-box hidden role="dialog" aria-modal="true" aria-label="Foto de la persona">
		<div class="lb-view" data-lb-view><img data-lb-img alt="" draggable="false" /></div>
		<aside class="lb-side">
			<header class="lb-head"><b class="mono lb-time" data-lb-time></b><button class="icon-btn" type="button" data-lb-close title="Cerrar (Esc)">${I.close}<span class="sr-only">Cerrar</span></button></header>
			<p class="muted-s" data-lb-info></p>
			<div data-lb-kinds></div>
			<div class="lb-zoom"><button class="btn small" type="button" data-lb-zoom="out" title="Alejar (-)">−</button><button class="btn small mono" type="button" data-lb-zoom="reset" title="Tamaño completo (0)">1×</button><button class="btn small" type="button" data-lb-zoom="in" title="Acercar (+)">+</button><span class="muted-s">Rueda del ratón o pellizco para acercar; arrastra para moverte.</span></div>
			<div class="lb-nav"><button class="btn" type="button" data-lb-step="-1">${I.left}<span>Anterior</span></button><button class="btn" type="button" data-lb-step="1"><span>Siguiente</span>${I.right}</button><span class="mono muted-s" data-lb-count></span></div>
			<div class="lb-actions"><button class="btn primary" type="button" data-lb-round>${I.round}<span>Ver ese momento en la ronda</span></button><button class="btn ghost" type="button" data-lb-delete>${I.trash}<span>Borrar esta foto</span></button></div>
		</aside>
	</div>
</div>`

const mount = main => {
	let alive = true
	let all = []
	let shown = []
	let only = 'all'
	let current = -1
	let kind = 'body'
	let lens = { k: 1, x: 0, y: 0 }
	const box = main.querySelector('[data-box]')
	const view = box.querySelector('[data-lb-view]')
	const img = box.querySelector('[data-lb-img]')
	document.body.append(box)
	const day = () => state.peopleDay ?? 0

	const draw = () => {
		shown = only === 'face' ? all.filter(s => s.face) : all
		const faces = all.filter(s => s.face).length
		main.querySelector('[data-filter]').innerHTML = all.length ? segment('peopleOnly', [['all', `Todas · ${all.length}`], ['face', `Con cara · ${faces}`]], only, 'Qué mostrar') : ''
		main.querySelector('[data-people]').innerHTML = body(shown, day(), only)
	}
	const load = async () => {
		try {
			const people = await api.people(state.camera.id, dateKey(dayAgo(day())))
			if (!alive) return
			all = people.flatMap(p => p.sightings).sort((a, b) => b.startMs - a.startMs)
			draw()
		} catch {
			if (alive) main.querySelector('[data-people]').innerHTML = '<p class="empty-s">No se pudieron leer las personas.</p>'
		}
	}

	const applyLens = () => {
		const b = view.getBoundingClientRect()
		const maxX = (b.width * (lens.k - 1)) / 2
		const maxY = (b.height * (lens.k - 1)) / 2
		lens.x = Math.max(-maxX, Math.min(maxX, lens.x))
		lens.y = Math.max(-maxY, Math.min(maxY, lens.y))
		img.style.transform = `translate(${lens.x}px, ${lens.y}px) scale(${lens.k})`
		view.classList.toggle('zoomed', lens.k > 1)
		box.querySelector('[data-lb-zoom="reset"]').textContent = `${lens.k.toFixed(lens.k % 1 ? 1 : 0)}×`
	}
	const zoomAt = (k, cx, cy) => {
		const b = view.getBoundingClientRect()
		const px = (cx ?? b.left + b.width / 2) - b.left - b.width / 2
		const py = (cy ?? b.top + b.height / 2) - b.top - b.height / 2
		const next = Math.max(1, Math.min(8, k))
		lens.x = px - ((px - lens.x) * next) / lens.k
		lens.y = py - ((py - lens.y) * next) / lens.k
		lens.k = next
		applyLens()
	}
	const show = () => {
		const s = shown[current]
		if (!s) return close()
		if (!kindsOf(s).includes(kind)) kind = s.face ? 'face' : 'body'
		img.src = srcOf(s, kind)
		lens = { k: 1, x: 0, y: 0 }
		applyLens()
		box.querySelector('[data-lb-time]').textContent = clockAt(s.startMs)
		box.querySelector('[data-lb-info]').textContent =
			`A la vista ${span(Math.max(1, (s.endMs - s.startMs) / 1000))}. ` +
			(kind === 'fused' ? 'Varios cuadros de la misma cara, alineados y combinados para quitar ruido; no se inventa nada.' : kind === 'face' ? (s.facePx ? `El mejor cuadro de la cara, recortado de la grabación en alta (${s.facePx} px de ancho).` : 'La cara, recortada de la foto de cuerpo.') : s.face ? 'La mejor toma de cuerpo completo.' : 'La mejor toma de cuerpo completo. No se le vio la cara de frente.')
		box.querySelector('[data-lb-kinds]').innerHTML = kindsOf(s).length > 1 ? segment('lbKind', kindsOf(s).map(k => [k, KINDS[k]]), kind, 'Qué foto') : ''
		box.querySelector('[data-lb-count]').textContent = `${current + 1} de ${shown.length}`
		box.querySelector('[data-lb-step="-1"]').disabled = current <= 0
		box.querySelector('[data-lb-step="1"]').disabled = current >= shown.length - 1
	}
	const open = id => {
		current = shown.findIndex(s => String(s.id) === String(id))
		if (current < 0) return
		kind = shown[current].face ? 'face' : 'body'
		box.hidden = false
		show()
		box.querySelector('[data-lb-close]').focus()
	}
	const close = () => {
		box.hidden = true
		current = -1
	}
	const toRound = s => {
		const midnight = new Date(s.startMs)
		midnight.setHours(0, 0, 0, 0)
		state.day = day()
		state.t = Math.max(0, (s.startMs - midnight.getTime()) / 1000 - 2)
		state.playing = true
		location.hash = '#/round'
	}

	const onSeg = e => {
		if (e.detail.id === 'peopleDay') {
			state.peopleDay = Number(e.detail.value)
			close()
			load()
		}
		if (e.detail.id === 'peopleOnly') {
			only = e.detail.value
			draw()
		}
		if (e.detail.id === 'lbKind') {
			kind = e.detail.value
			show()
		}
	}
	const onClick = async e => {
		const tileEl = e.target.closest('[data-open]')
		if (tileEl) return open(tileEl.dataset.open)
		if (e.target.closest('[data-lb-close]') || e.target === box) return close()
		const step = e.target.closest('[data-lb-step]')
		if (step) {
			current = Math.max(0, Math.min(shown.length - 1, current + Number(step.dataset.lbStep)))
			return show()
		}
		const z = e.target.closest('[data-lb-zoom]')
		if (z) return z.dataset.lbZoom === 'reset' ? zoomAt(1) : zoomAt(lens.k * (z.dataset.lbZoom === 'in' ? 1.5 : 1 / 1.5))
		if (e.target.closest('[data-lb-round]') && shown[current]) return toRound(shown[current])
		const del = e.target.closest('[data-lb-delete]')
		if (del && shown[current] && confirmOnce(del, 'Sí, borrarla')) {
			const s = shown[current]
			try {
				await api.removeSighting(s.id)
				all = all.filter(x => x.id !== s.id)
				draw()
				current = Math.min(current, shown.length - 1)
				toast('Foto borrada')
				show()
			} catch (err) {
				toast(err.message)
			}
		}
	}
	const onKey = e => {
		if (box.hidden) return
		if (e.key === 'Escape') return close()
		if (e.target.closest('[role="radio"]')) return
		const step = { ArrowLeft: -1, ArrowRight: 1 }[e.key]
		if (step) {
			e.preventDefault()
			current = Math.max(0, Math.min(shown.length - 1, current + step))
			show()
		}
		if (e.key === '+' || e.key === '=') zoomAt(lens.k * 1.5)
		if (e.key === '-') zoomAt(lens.k / 1.5)
		if (e.key === '0') zoomAt(1)
	}
	const onWheel = e => {
		e.preventDefault()
		zoomAt(lens.k * (e.deltaY < 0 ? 1.25 : 0.8), e.clientX, e.clientY)
	}
	const pointers = new Map()
	let pinch = 0
	const onDown = e => {
		pointers.set(e.pointerId, { x: e.clientX, y: e.clientY })
		view.setPointerCapture(e.pointerId)
		if (pointers.size === 2) {
			const [a, b] = [...pointers.values()]
			pinch = Math.hypot(a.x - b.x, a.y - b.y) / lens.k
		}
	}
	const onMove = e => {
		const before = pointers.get(e.pointerId)
		if (!before) return
		pointers.set(e.pointerId, { x: e.clientX, y: e.clientY })
		if (pointers.size === 2 && pinch) {
			const [a, b] = [...pointers.values()]
			return zoomAt(Math.hypot(a.x - b.x, a.y - b.y) / pinch, (a.x + b.x) / 2, (a.y + b.y) / 2)
		}
		if (lens.k <= 1) return
		lens.x += e.clientX - before.x
		lens.y += e.clientY - before.y
		applyLens()
	}
	const onUp = e => {
		pointers.delete(e.pointerId)
		pinch = 0
	}
	const onDbl = e => zoomAt(lens.k > 1 ? 1 : 3, e.clientX, e.clientY)

	main.addEventListener('segment', onSeg)
	main.addEventListener('click', onClick)
	box.addEventListener('segment', onSeg)
	box.addEventListener('click', onClick)
	document.addEventListener('keydown', onKey)
	view.addEventListener('wheel', onWheel, { passive: false })
	view.addEventListener('pointerdown', onDown)
	view.addEventListener('pointermove', onMove)
	view.addEventListener('pointerup', onUp)
	view.addEventListener('pointercancel', onUp)
	view.addEventListener('dblclick', onDbl)
	load()
	const poll = setInterval(() => day() === 0 && box.hidden && load(), 30000)
	return () => {
		alive = false
		clearInterval(poll)
		box.remove()
		main.removeEventListener('segment', onSeg)
		main.removeEventListener('click', onClick)
		document.removeEventListener('keydown', onKey)
	}
}

export const peopleView = { title: 'Personas', html, mount }

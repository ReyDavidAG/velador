import { api } from '../services/api.js'
import { I } from '../ui.js'

const FULL = { minX: -1, maxX: 1, minY: -1, maxY: 1 }
const MIN_SPAN = 0.06
const pct = v => `${((v + 1) / 2) * 100}%`
const size = v => `${(v / 2) * 100}%`
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v))
const round = v => Math.round(v * 1000) / 1000

export const routeOf = z => {
	const h = z.maxY - z.minY
	const side = z.minY + h * 0.35
	return {
		left: { x: z.minX, y: clamp(z.routeLeftY ?? side, z.minY, z.maxY) },
		mid: { x: clamp(z.routeMidX ?? (z.minX + z.maxX) / 2, z.minX + 0.02, z.maxX - 0.02), y: clamp(z.routeMidY ?? z.maxY - h * 0.2, z.minY, z.maxY) },
		right: { x: z.maxX, y: clamp(z.routeRightY ?? side, z.minY, z.maxY) },
	}
}

const routeY = (r, x) =>
	(r.left.y * (x - r.mid.x) * (x - r.right.x)) / ((r.left.x - r.mid.x) * (r.left.x - r.right.x)) +
	(r.mid.y * (x - r.left.x) * (x - r.right.x)) / ((r.mid.x - r.left.x) * (r.mid.x - r.right.x)) +
	(r.right.y * (x - r.left.x) * (x - r.mid.x)) / ((r.right.x - r.left.x) * (r.right.x - r.mid.x))

export const defaultZone = position => ({ ...FULL, homeX: round(position?.x ?? 0), homeY: round(position?.y ?? 0) })

export const zoneText = zone => {
	if (!zone) return 'Sin límites: la cámara puede apuntar a todo su recorrido.'
	const wide = Math.round(((zone.maxX - zone.minX) / 2) * 100)
	const tall = Math.round(((zone.maxY - zone.minY) / 2) * 100)
	return `Usa el ${wide} % del giro y el ${tall} % de la inclinación.`
}

const EDGES = [
	['minX', 'Límite izquierdo', 'v'],
	['maxX', 'Límite derecho', 'v'],
	['minY', 'Límite superior', 'h'],
	['maxY', 'Límite inferior', 'h'],
]

export const zoneMapHtml = (mini = false) => `<div class="zm-scroll"><div class="zm${mini ? ' mini' : ''}" data-zm>
	<div class="zm-tiles" data-zm-tiles></div>
	<i class="zm-veil" data-veil="l"></i><i class="zm-veil" data-veil="r"></i><i class="zm-veil" data-veil="t"></i><i class="zm-veil" data-veil="b"></i>
	<div class="zm-seen" data-zm-seen></div>
	<svg class="zm-route" data-zm-route viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true"><polyline class="under" points="" /><polyline points="" /></svg>
	${mini ? '' : ['left', 'mid', 'right'].map(k => `<button type="button" class="zm-knot" data-knot="${k}" title="Ruta de la calle: ${k === 'mid' ? 'punto más cercano' : k === 'left' ? 'extremo izquierdo' : 'extremo derecho'}"><span class="sr-only">Punto de la ruta</span></button>`).join('')}
	<div class="zm-zone" data-zm-zone>${mini ? '' : EDGES.map(([key, label, axis]) => `<button type="button" class="zm-edge ${axis} ${key}" data-edge="${key}" role="slider" aria-label="${label}" aria-valuemin="-100" aria-valuemax="100"><i></i></button>`).join('')}</div>
	<button type="button" class="zm-home" data-zm-home title="Reposo: adonde regresa la cámara" ${mini ? 'tabindex="-1"' : ''}>${I.home}<span class="sr-only">Posición de reposo</span></button>
	<div class="zm-now" data-zm-now hidden><i></i></div>
	<p class="zm-empty" data-zm-empty hidden>Aún no hay mapa. «Medir cobertura» toma una foto de cada dirección.</p>
</div></div>`

export const mountZoneMap = (root, { cameraId, editable = false, onChange = () => {}, onGo = () => {} }) => {
	const map = root.querySelector('[data-zm]')
	const tiles = map.querySelector('[data-zm-tiles]')
	const zoneEl = map.querySelector('[data-zm-zone]')
	const routeEl = map.querySelector('[data-zm-route]')
	const seen = map.querySelector('[data-zm-seen]')
	const home = map.querySelector('[data-zm-home]')
	const now = map.querySelector('[data-zm-now]')
	const veils = Object.fromEntries([...map.querySelectorAll('[data-veil]')].map(v => [v.dataset.veil, v]))
	let zone = null
	let view = { w: 0.44, h: 0.92 }
	let surveyAt = -1
	let surveyDone = -1
	let drag = null

	const place = () => {
		const z = zone ?? FULL
		zoneEl.style.cssText = `left:${pct(z.minX)};top:${pct(z.minY)};width:${size(z.maxX - z.minX)};height:${size(z.maxY - z.minY)}`
		zoneEl.classList.toggle('none', !zone)
		const sx0 = clamp(z.minX - view.w / 2, -1, 1)
		const sx1 = clamp(z.maxX + view.w / 2, -1, 1)
		const sy0 = clamp(z.minY - view.h / 2, -1, 1)
		const sy1 = clamp(z.maxY + view.h / 2, -1, 1)
		seen.style.cssText = `left:${pct(sx0)};top:${pct(sy0)};width:${size(sx1 - sx0)};height:${size(sy1 - sy0)}`
		seen.hidden = !zone
		veils.l.style.cssText = `left:0;top:0;bottom:0;width:${pct(sx0)}`
		veils.r.style.cssText = `right:0;top:0;bottom:0;width:${size(1 - sx1)}`
		veils.t.style.cssText = `left:${pct(sx0)};width:${size(sx1 - sx0)};top:0;height:${pct(sy0)}`
		veils.b.style.cssText = `left:${pct(sx0)};width:${size(sx1 - sx0)};bottom:0;height:${size(1 - sy1)}`
		for (const v of Object.values(veils)) v.hidden = !zone
		home.hidden = !zone
		if (zone) home.style.cssText = `left:${pct(zone.homeX)};top:${pct(zone.homeY)}`
		for (const b of zoneEl.querySelectorAll('[data-edge]')) b.setAttribute('aria-valuenow', Math.round(z[b.dataset.edge] * 100))
		routeEl.hidden = !zone
		for (const k of map.querySelectorAll('[data-knot]')) k.hidden = !zone
		if (!zone) return
		const r = routeOf(zone)
		const points = Array.from({ length: 41 }, (_, i) => {
			const x = zone.minX + ((zone.maxX - zone.minX) * i) / 40
			return `${((x + 1) / 2) * 100},${((clamp(routeY(r, x), zone.minY, zone.maxY) + 1) / 2) * 100}`
		}).join(' ')
		for (const line of routeEl.querySelectorAll('polyline')) line.setAttribute('points', points)
		for (const k of map.querySelectorAll('[data-knot]')) k.style.cssText = `left:${pct(r[k.dataset.knot].x)};top:${pct(r[k.dataset.knot].y)}`
	}

	const setView = v => {
		view = v
		map.style.aspectRatio = String(clamp((v.h / v.w) * (16 / 9), 2.4, 3.6))
	}

	const setSurvey = survey => {
		map.querySelector('[data-zm-empty]').hidden = Boolean(survey?.tiles.length)
		if (!survey || (survey.atMs === surveyAt && survey.done === surveyDone)) return
		surveyAt = survey.atMs
		surveyDone = survey.done
		tiles.innerHTML = survey.tiles
			.map(t => `<img alt="" draggable="false" src="/api/cameras/${cameraId}/zone/survey/${t.index}?v=${survey.atMs}" style="left:${pct(t.x - survey.tileW / 2)};top:${pct(t.y - survey.tileH / 2)};width:${size(survey.tileW)};height:${size(survey.tileH)}" />`)
			.join('')
	}

	const setPosition = (position, inside = true) => {
		now.hidden = !position
		if (!position) return
		now.style.cssText = `left:${pct(position.x - view.w / 2)};top:${pct(position.y - view.h / 2)};width:${size(view.w)};height:${size(view.h)}`
		now.classList.toggle('out', !inside)
	}

	const at = e => {
		const b = map.getBoundingClientRect()
		return { x: clamp(((e.clientX - b.left) / b.width) * 2 - 1, -1, 1), y: clamp(((e.clientY - b.top) / b.height) * 2 - 1, -1, 1) }
	}

	const move = (key, value) => {
		const z = { ...zone }
		if (key === 'minX') z.minX = clamp(value, -1, z.maxX - MIN_SPAN)
		if (key === 'maxX') z.maxX = clamp(value, z.minX + MIN_SPAN, 1)
		if (key === 'minY') z.minY = clamp(value, -1, z.maxY - MIN_SPAN)
		if (key === 'maxY') z.maxY = clamp(value, z.minY + MIN_SPAN, 1)
		if (key === 'home') {
			z.homeX = value.x
			z.homeY = value.y
		}
		if (key === 'left') z.routeLeftY = value.y
		if (key === 'right') z.routeRightY = value.y
		if (key === 'mid') {
			z.routeMidX = value.x
			z.routeMidY = value.y
		}
		const r = routeOf(z)
		if (z.routeLeftY != null) z.routeLeftY = round(r.left.y)
		if (z.routeRightY != null) z.routeRightY = round(r.right.y)
		if (z.routeMidX != null) {
			z.routeMidX = round(r.mid.x)
			z.routeMidY = round(r.mid.y)
		}
		z.homeX = round(clamp(z.homeX, z.minX, z.maxX))
		z.homeY = round(clamp(z.homeY, z.minY, z.maxY))
		for (const k of ['minX', 'maxX', 'minY', 'maxY']) z[k] = round(z[k])
		zone = z
		place()
		onChange(zone)
	}

	const onDown = e => {
		if (e.button !== 0) return
		const edge = e.target.closest('[data-edge]')
		const pin = e.target.closest('[data-zm-home]')
		const knot = e.target.closest('[data-knot]')
		if (editable && zone && (edge || pin || knot)) {
			e.preventDefault()
			drag = edge ? edge.dataset.edge : knot ? knot.dataset.knot : 'home'
			map.setPointerCapture(e.pointerId)
			map.classList.add('dragging')
			return
		}
		const p = at(e)
		onGo(round(p.x), round(p.y))
	}
	const onMove = e => {
		if (!drag) return
		const p = at(e)
		move(drag, ['home', 'left', 'mid', 'right'].includes(drag) ? p : drag.endsWith('X') ? p.x : p.y)
	}
	const onUp = () => {
		drag = null
		map.classList.remove('dragging')
	}
	const onKey = e => {
		const edge = e.target.closest('[data-edge]')
		const pin = e.target.closest('[data-zm-home]')
		const knot = e.target.closest('[data-knot]')
		const d = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key]
		if (!editable || !zone || !d || !(edge || pin || knot)) return
		e.preventDefault()
		e.stopPropagation()
		const step = e.shiftKey ? 0.05 : 0.01
		if (pin) return move('home', { x: zone.homeX + d[0] * step, y: zone.homeY + d[1] * step })
		if (knot) {
			const at = routeOf(zone)[knot.dataset.knot]
			return move(knot.dataset.knot, { x: at.x + d[0] * step, y: at.y + d[1] * step })
		}
		const key = edge.dataset.edge
		move(key, zone[key] + (key.endsWith('X') ? d[0] : d[1]) * step)
	}
	map.addEventListener('pointerdown', onDown)
	map.addEventListener('pointermove', onMove)
	map.addEventListener('pointerup', onUp)
	map.addEventListener('pointercancel', onUp)
	map.addEventListener('keydown', onKey)

	return {
		get zone() {
			return zone
		},
		setZone: z => {
			zone = z ? { ...z } : null
			place()
		},
		show: data => {
			setView(data.view)
			setSurvey(data.survey)
			place()
			setPosition(data.position, data.inside)
		},
		setEdge: (key, value) => zone && move(key, value),
		destroy: () => {
			map.removeEventListener('pointerdown', onDown)
			map.removeEventListener('pointermove', onMove)
			map.removeEventListener('pointerup', onUp)
			map.removeEventListener('pointercancel', onUp)
			map.removeEventListener('keydown', onKey)
		},
	}
}

export const loadZone = cameraId => api.zone(cameraId)

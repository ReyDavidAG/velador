import { CAUSE, esc, KIND, span } from '../ui.js'

export const eventAt = (r, t) => r.events.find(e => t >= e.t && t <= e.t + e.dur)
export const gapAt = (r, t) => r.gaps.find(g => t >= g.start && t < g.end)

const pos = (t, w) => ((t - w.from) / (w.to - w.from)) * 100
const clampT = (t, r) => Math.min(Math.max(0, t), Math.max(0, r.end - 1))

const ticks = w => {
	const len = w.to - w.from
	const base = len > 12 * 3600 ? 3 * 3600 : len > 4 * 3600 ? 3600 : len > 3600 ? 900 : len > 900 ? 300 : 60
	const step = base * (window.innerWidth < 640 ? 2 : 1)
	const out = []
	for (let t = Math.ceil(w.from / step) * step; t <= w.to; t += step) out.push(t)
	return out
}

export const roundHtml = (r, w = { from: 0, to: r.len }, { mini = false, clip = null } = {}) => {
	const inWin = (a, b) => b > w.from && a < w.to
	const block = (cls, a, b, extra = '') => `<i class="${cls}" style="left:${pos(Math.max(a, w.from), w)}%;width:${pos(Math.min(b, w.to), w) - pos(Math.max(a, w.from), w)}%" ${extra}></i>`
	const spans = r.spans.filter(s => inWin(s.start, s.end)).map(s => block('rt-span', s.start, s.end)).join('')
	const gaps = r.gaps.filter(g => inWin(g.start, g.end)).map(g => block(`rt-gap`, g.start, g.end)).join('')
	const future = r.end < w.to ? block('rt-future', Math.max(r.end, w.from), w.to) : ''
	const events = r.events
		.filter(e => inWin(e.t, e.t + e.dur))
		.map(e => `<i class="rt-ev ${e.kind}" style="left:${pos(e.t, w)}%;width:max(3px, ${((e.dur / (w.to - w.from)) * 100).toFixed(3)}%)"></i>`)
		.join('')
	const hours = mini
		? [0, 6, 12, 18, 24].map(h => `<span style="left:${(h / 24) * 100}%">${String(h).padStart(2, '0')}</span>`).join('')
		: ticks(w).map(t => `<span style="left:${pos(t, w)}%">${r.clock(t).slice(0, 5)}</span>`).join('')
	const sel = clip ? `<div class="rt-sel" style="left:${pos(clip.from, w)}%;width:${pos(clip.to, w) - pos(clip.from, w)}%"><b data-handle="from" tabindex="0" role="slider" aria-label="Inicio del recorte"></b><b data-handle="to" tabindex="0" role="slider" aria-label="Fin del recorte"></b></div>` : ''
	return `<div class="rt ${mini ? 'mini' : ''}" ${mini ? 'role="img" aria-label="Ronda de hoy"' : `tabindex="0" role="slider" aria-label="Ronda del día" aria-valuemin="0" aria-valuemax="${Math.round(r.len)}" aria-valuenow="0"`}>
		<div class="rt-events">${events}</div>
		<div class="rt-track">${spans}${gaps}${future}${sel}</div>
		<div class="rt-hours">${hours}</div>
		<div class="rt-hover" hidden><div class="rt-peek">${mini ? '' : '<video class="rt-peek-v" muted playsinline preload="auto"></video>'}<span class="rt-peek-t"></span><span class="rt-peek-l"></span></div></div>
		<div class="rt-cursor" hidden><span class="rt-flag"></span></div>
	</div>`
}

export const setCursor = (el, t, w, r) => {
	const c = el?.querySelector('.rt-cursor')
	if (!c) return
	const p = pos(t, w)
	c.hidden = p < 0 || p > 100
	c.style.left = `${p}%`
	c.querySelector('.rt-flag').textContent = r.clock(t)
	c.classList.toggle('flip', p > 88)
	if (el.getAttribute('role') !== 'slider') return
	el.setAttribute('aria-valuenow', Math.round(t))
	el.setAttribute('aria-valuetext', r.clock(t))
}

export const bindRound = (el, { r, win, onSeek, onZoom, onClip, getClip }) => {
	const track = el.querySelector('.rt-track')
	const hover = el.querySelector('.rt-hover')
	const at = e => {
		const b = track.getBoundingClientRect()
		const w = win()
		return w.from + (Math.min(Math.max(0, e.clientX - b.left), b.width) / b.width) * (w.to - w.from)
	}
	let drag = null
	const peek = e => {
		const t = at(e)
		const w = win()
		hover.hidden = false
		hover.style.left = `${pos(t, w)}%`
		hover.classList.toggle('edge-l', pos(t, w) < 12)
		hover.classList.toggle('edge-r', pos(t, w) > 88)
		const ev = eventAt(r, t) ?? r.events.find(x => Math.abs(x.t - t) < (w.to - w.from) / 260)
		const gap = gapAt(r, t)
		hover.querySelector('.rt-peek-t').textContent = r.clock(t)
		const label = hover.querySelector('.rt-peek-l')
		label.className = `rt-peek-l ${gap ? 'gap' : ev ? ev.kind : t >= r.end ? 'gap' : ''}`
		label.textContent = gap ? `${CAUSE[gap.cause].label} · ${span(gap.end - gap.start)}` : t >= r.end ? 'Todavía no pasa' : ev ? (KIND[ev.kind]?.label ?? 'Suceso') : 'Grabado'
		hover.classList.toggle('is-gap', Boolean(gap) || t >= r.end)
	}
	el.addEventListener('pointerdown', e => {
		const handle = e.target.closest('[data-handle]')
		drag = handle ? handle.dataset.handle : 'seek'
		el.setPointerCapture(e.pointerId)
		el.classList.add('dragging')
		if (drag === 'seek') onSeek(clampT(at(e), r))
	})
	el.addEventListener('pointermove', e => {
		peek(e)
		if (!drag) return
		const t = clampT(at(e), r)
		if (drag === 'seek') return onSeek(t)
		const c = { ...getClip() }
		if (drag === 'from') c.from = Math.min(t, c.to - 5)
		else c.to = Math.max(t, c.from + 5)
		onClip(c)
	})
	const stop = () => {
		drag = null
		el.classList.remove('dragging')
	}
	el.addEventListener('pointerup', stop)
	el.addEventListener('pointercancel', stop)
	el.addEventListener('pointerleave', () => (hover.hidden = true))
	el.addEventListener('pointerup', e => e.pointerType !== 'mouse' && (hover.hidden = true))
	el.addEventListener('keydown', e => {
		const handle = e.target.closest('[data-handle]')?.dataset.handle
		if (handle) {
			const d = { ArrowLeft: -1, ArrowRight: 1 }[e.key]
			if (!d) return
			e.preventDefault()
			e.stopPropagation()
			const c = { ...getClip() }
			if (handle === 'from') c.from = Math.max(0, Math.min(c.from + d, c.to - 5))
			else c.to = Math.min(r.end - 1, Math.max(c.to + d, c.from + 5))
			return onClip(c)
		}
		if (e.key === 'Home' || e.key === 'End') {
			e.preventDefault()
			return onSeek(e.key === 'Home' ? 0 : clampT(r.end, r))
		}
		const step = { ArrowLeft: -10, ArrowRight: 10, PageDown: -300, PageUp: 300 }[e.key]
		if (!step) return
		e.preventDefault()
		e.stopPropagation()
		onSeek(clampT(Number(el.getAttribute('aria-valuenow')) + step, r))
	})
	if (onZoom)
		el.addEventListener(
			'wheel',
			e => {
				e.preventDefault()
				if (e.deltaY) onZoom(Math.exp(Math.max(-60, Math.min(60, e.deltaY)) * 0.004), at(e))
			},
			{ passive: false }
		)
}

export const gapChip = g => `<span class="chip gap">${esc(CAUSE[g.cause].label)} · ${span(g.end - g.start)}</span>`

export const setSel = (el, clip, w) => {
	const s = el?.querySelector('.rt-sel')
	if (!s || !clip) return
	s.style.left = `${pos(clip.from, w)}%`
	s.style.width = `${pos(clip.to, w) - pos(clip.from, w)}%`
}

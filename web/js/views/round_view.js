import { bindRound, gapAt, roundHtml, setCursor } from '../components/round_timeline.js'
import { api } from '../services/api.js'
import { loadRound, state } from '../store.js'
import { CAUSE, dateLabel, dayLabel, esc, I, pct, segment, span, toast, toggle } from '../ui.js'

const DAYS = 7
const CHUNK = 120
const ZOOMS = [['day', '24 h'], ['21600', '6 h'], ['3600', '1 h'], ['600', '10 min']]
const SPEEDS = [['1', '1×'], ['2', '2×'], ['8', '8×']]

const dayBtn = (r, i) =>
	`<button class="day ${i === state.day ? 'on' : ''}" type="button" data-day="${i}" aria-pressed="${i === state.day}" ${r ? '' : 'disabled'}>
		<span class="day-name">${esc(dayLabel(r?.date ?? new Date(), i))}</span><span class="day-date mono">${r ? esc(dateLabel(r.date)) : '…'}</span>
		<span class="day-bar">${(r?.spans ?? []).map(s => `<i style="left:${(s.start / r.len) * 100}%;width:${((s.end - s.start) / r.len) * 100}%"></i>`).join('')}</span>
		<span class="day-meta"><b class="mono ${r?.gaps.length ? 'warn' : ''}">${r && r.end > r.from ? pct(r.coverage) : '—'}</b><span>${!r ? '' : r.end <= r.from ? 'sin guardia' : `${r.gaps.length} ${r.gaps.length === 1 ? 'hueco' : 'huecos'}`}</span></span>
	</button>`

const sideHtml = r => `
	<header class="card-head"><h2>${esc(dayLabel(r.date, r.i))} · ${esc(dateLabel(r.date))}</h2><span class="chip ${r.gaps.length ? 'warn' : 'good'}">${r.gaps.length ? `${r.gaps.length} ${r.gaps.length === 1 ? 'hueco' : 'huecos'}` : 'Sin huecos'}</span></header>
	<div class="ev-list scroll">${
		r.gaps
			.map(g => `<button class="gap-row" type="button" data-seek="${g.start + 1}"><span class="ev-ico gap">${I[CAUSE[g.cause]?.icon ?? 'alert']}</span><span class="ev-main"><b>${esc(CAUSE[g.cause]?.label ?? 'Sin grabación')}</b><small>${span(g.end - g.start)} sin grabar</small></span><span class="mono ev-time">${r.clock(g.start).slice(0, 5)}</span></button>`)
			.join('') || `<p class="empty-s">${r.spans.length ? 'Sin huecos desde que empezó la guardia.' : 'Este día Velador todavía no vigilaba.'}</p>`
	}</div>`

const html = () => `<div class="round">
	<div class="days" role="group" aria-label="Día" data-days>${Array.from({ length: DAYS }, (_, k) => dayBtn(null, DAYS - 1 - k)).join('')}</div>
	<div class="round-main">
		<section class="stage" data-stage>
			<video playsinline muted preload="auto"></video>
			<div class="osd tl"><span class="badge rec" data-mode>GRABADO</span><span class="osd-name">${esc(state.camera.name)}</span></div>
			<div class="osd tr mono" data-clock></div>
			<div class="gap-veil" data-veil hidden><span class="veil-ico" data-veil-ico></span><b data-veil-title></b><span data-veil-sub></span><button class="btn small" type="button" data-skip>Saltar el hueco ${I.next}</button></div>
		</section>
		<aside class="card rail-card" data-side><p class="empty-s">Cargando la ronda…</p></aside>
	</div>
	<section class="card deck">
		<div class="transport">
			<div class="tr-group">
				<button class="t-btn" type="button" data-nudge="-10" title="Atrás 10 s">${I.back10}<span class="sr-only">Atrás 10 segundos</span></button>
				<button class="t-btn play" type="button" data-play title="Reproducir (espacio)"><span data-play-ico>${I.play}</span><span class="sr-only">Reproducir o pausar</span></button>
				<button class="t-btn" type="button" data-nudge="10" title="Adelante 10 s">${I.fwd10}<span class="sr-only">Adelante 10 segundos</span></button>
			</div>
			<div class="timecode mono" data-tc>--:--:--</div>
			${segment('speed', SPEEDS, state.speed, 'Velocidad')}
			<span class="inline-switch">${toggle('skip', state.skipGaps, 'Saltar huecos')}<span aria-hidden="true">Saltar huecos</span></span>
			<span class="spacer"></span>
			${segment('zoom', ZOOMS, 'day', 'Acercamiento')}
			<button class="btn small" type="button" data-mute>${I.mute}<span>Sonido</span></button>
		</div>
		<div data-rt></div>
		<p class="hint">Arrastra para moverte · rueda del ratón para acercar · <kbd>espacio</kbd> reproduce · <kbd>←</kbd> <kbd>→</kbd> 10 s</p>
	</section>
</div>`

const mount = main => {
	const video = main.querySelector('video')
	const veil = main.querySelector('[data-veil]')
	const rtHost = main.querySelector('[data-rt]')
	const rounds = Array(DAYS).fill(null)
	let r = null
	let rt = null
	let chunk = null
	let loadTimer = 0
	let raf = 0
	let alive = true

	const spanAt = t => r.spans.find(s => t >= s.start && t < s.end)

	const frame = () => {
		if (!r) return
		const t = state.t
		const gap = gapAt(r, t) ?? (spanAt(t) ? null : { start: t, end: t, cause: 'unknown', none: true })
		setCursor(rt, t, state.win, r)
		main.querySelector('[data-tc]').textContent = r.clock(t)
		main.querySelector('[data-clock]').innerHTML = `<span class="osd-date">${r.raw.date} </span>${r.clock(t)}`
		veil.hidden = !gap
		main.querySelector('[data-mode]').textContent = gap ? 'HUECO' : 'GRABADO'
		main.querySelector('[data-mode]').classList.toggle('gap', Boolean(gap))
		if (gap) {
			const before = t < r.from
			main.querySelector('[data-veil-ico]').innerHTML = I[CAUSE[gap.cause]?.icon ?? 'alert']
			main.querySelector('[data-veil-title]').textContent = before ? 'Velador todavía no vigilaba' : (CAUSE[gap.cause]?.label ?? 'Sin grabación')
			main.querySelector('[data-veil-sub]').textContent = gap.none ? (before ? `La guardia empezó a las ${r.clock(r.from)}` : 'No hay grabación en este momento') : `Sin grabación de ${r.clock(gap.start)} a ${r.clock(gap.end)} · ${span(gap.end - gap.start)}`
			main.querySelector('[data-skip]').hidden = Boolean(gap.none) && !before
		}
		main.querySelector('[data-play-ico]').innerHTML = state.playing ? I.pause : I.play
	}

	const load = () => {
		clearTimeout(loadTimer)
		const s = spanAt(state.t)
		if (!s) {
			chunk = null
			video.removeAttribute('src')
			video.load()
			return
		}
		const len = Math.min(CHUNK, s.end - state.t - (r.i === 0 && s.end >= r.end - 3 ? 2 : 0))
		if (len < 1) return
		chunk = { start: state.t, len }
		video.src = api.mediaUrl(state.camera.id, r.ms(state.t), len * 1000)
		video.playbackRate = state.speed
		video.muted = state.muted
		if (state.playing) video.play().catch(() => {})
	}

	const drawRt = () => {
		rtHost.innerHTML = roundHtml(r, state.win)
		rt = rtHost.querySelector('.rt')
		bindRound(rt, { r, win: () => state.win, onSeek: t => seek(t, true), onZoom: (f, at) => zoom((state.win.to - state.win.from) * f, at), getClip: () => null, onClip: () => {} })
		const len = Math.round(state.win.to - state.win.from)
		for (const b of main.querySelectorAll('[data-segment="zoom"] [data-value]')) b.setAttribute('aria-checked', b.dataset.value === 'day' ? len >= Math.round(r.len) : Number(b.dataset.value) === len)
		frame()
	}

	const zoom = (len, center = state.t) => {
		len = Math.min(r.len, Math.max(300, len))
		const ratio = (center - state.win.from) / (state.win.to - state.win.from)
		const from = Math.min(Math.max(0, center - len * ratio), r.len - len)
		state.win = { from, to: from + len }
		drawRt()
	}

	const follow = () => {
		const len = state.win.to - state.win.from
		if (len >= r.len || (state.t >= state.win.from && state.t <= state.win.to)) return
		const from = Math.min(Math.max(0, state.t - len * 0.15), r.len - len)
		state.win = { from, to: from + len }
		drawRt()
	}

	function seek(t, lazy = false) {
		state.t = Math.max(Math.min(r.from, r.end - 1), Math.min(t, r.end - 1))
		if (!rt?.classList.contains('dragging')) follow()
		frame()
		clearTimeout(loadTimer)
		if (chunk && state.t >= chunk.start && state.t < chunk.start + chunk.len - 1 && video.readyState >= 1) {
			video.currentTime = state.t - chunk.start
			return
		}
		if (lazy) loadTimer = setTimeout(load, 160)
		else load()
	}

	const advance = () => {
		if (!alive) return
		raf = requestAnimationFrame(advance)
		if (!r || !state.playing || rt?.classList.contains('dragging')) return
		if (chunk && !video.seeking && video.readyState >= 2) {
			const t = chunk.start + video.currentTime
			if (Math.abs(t - state.t) > 0.05) {
				state.t = Math.min(t, r.end - 1)
				follow()
				frame()
			}
			return
		}
		if (chunk) return
		const gap = gapAt(r, state.t)
		const next = r.spans.find(s => s.start > state.t)
		if (state.skipGaps && (gap || next)) return seek(gap ? gap.end + 0.05 : next.start + 0.05)
		play(false)
		toast(r.i === 0 ? 'Llegaste al presente' : 'Fin de la grabación de este día')
	}

	const play = on => {
		state.playing = on
		if (on && !chunk) load()
		if (on) video.play().catch(() => {})
		else video.pause()
		frame()
	}

	const onEnded = () => {
		if (!chunk) return
		const end = chunk.start + chunk.len
		chunk = null
		if (end >= r.end - 3) {
			state.t = Math.min(end, r.end - 1)
			return
		}
		seek(end)
	}

	const show = i => {
		r = rounds[i]
		state.day = i
		if (state.t === null || state.t > r.end - 1) state.t = Math.max(r.from, r.end - 60)
		if (state.t < r.from && r.spans[0]) state.t = r.spans[0].start
		state.win = { from: 0, to: r.len }
		for (const b of main.querySelectorAll('[data-day]')) {
			b.classList.toggle('on', Number(b.dataset.day) === i)
			b.setAttribute('aria-pressed', Number(b.dataset.day) === i)
		}
		main.querySelector('[data-side]').innerHTML = sideHtml(r)
		drawRt()
		load()
	}

	const refreshDays = () => {
		main.querySelector('[data-days]').innerHTML = Array.from({ length: DAYS }, (_, k) => dayBtn(rounds[DAYS - 1 - k], DAYS - 1 - k)).join('')
		main.querySelector('.day.on')?.scrollIntoView({ block: 'nearest', inline: 'end' })
	}

	const boot = async () => {
		await Promise.all(
			rounds.map(async (_, i) => {
				try {
					rounds[i] = await loadRound(i)
				} catch {
					rounds[i] = null
				}
			})
		)
		if (!alive) return
		refreshDays()
		if (rounds[state.day]) show(state.day)
		else main.querySelector('[data-side]').innerHTML = '<p class="empty-s">No se pudo leer la ronda. ¿El motor sigue corriendo?</p>'
	}

	const poll = setInterval(async () => {
		if (!r || r.i !== 0 || rt?.classList.contains('dragging')) return
		try {
			const fresh = await loadRound(0)
			if (!alive || state.day !== 0) return
			rounds[0] = fresh
			r = fresh
			refreshDays()
			main.querySelector('[data-side]').innerHTML = sideHtml(r)
			drawRt()
		} catch {}
	}, 15000)

	const onClick = e => {
		const day = e.target.closest('[data-day]')
		if (day && rounds[Number(day.dataset.day)]) {
			play(false)
			state.t = null
			show(Number(day.dataset.day))
		}
		const s = e.target.closest('[data-seek]')
		if (s) seek(Number(s.dataset.seek))
		if (e.target.closest('[data-play]')) play(!state.playing)
		const n = e.target.closest('[data-nudge]')
		if (n) seek(state.t + Number(n.dataset.nudge))
		if (e.target.closest('[data-skip]')) {
			const g = gapAt(r, state.t)
			const next = r.spans.find(x => x.start > state.t)
			if (g) seek(g.end + 0.05)
			else if (next) seek(next.start + 0.05)
		}
		const mute = e.target.closest('[data-mute]')
		if (mute) {
			state.muted = !state.muted
			video.muted = state.muted
			mute.innerHTML = `${state.muted ? I.mute : I.sound}<span>Sonido</span>`
		}
	}
	const onSeg = e => {
		if (e.detail.id === 'speed') {
			state.speed = Number(e.detail.value)
			video.playbackRate = state.speed
		}
		if (e.detail.id === 'zoom' && r) zoom(e.detail.value === 'day' ? r.len : Number(e.detail.value))
	}
	const onSwitch = e => {
		if (e.detail.id === 'skip') state.skipGaps = e.detail.on
	}
	const onKey = e => {
		if (!r || e.repeat || e.target.closest('input, button, a')) return
		if (e.code === 'Space') {
			e.preventDefault()
			play(!state.playing)
		}
		if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') seek(state.t + (e.key === 'ArrowLeft' ? -10 : 10))
	}
	video.addEventListener('ended', onEnded)
	main.addEventListener('click', onClick)
	main.addEventListener('segment', onSeg)
	main.addEventListener('switch', onSwitch)
	document.addEventListener('keydown', onKey)
	boot()
	raf = requestAnimationFrame(advance)
	return () => {
		alive = false
		cancelAnimationFrame(raf)
		clearInterval(poll)
		clearTimeout(loadTimer)
		state.playing = false
		video.removeAttribute('src')
		video.load()
		main.removeEventListener('click', onClick)
		main.removeEventListener('segment', onSeg)
		main.removeEventListener('switch', onSwitch)
		document.removeEventListener('keydown', onKey)
	}
}

export const roundView = { title: 'Ronda', html, mount }

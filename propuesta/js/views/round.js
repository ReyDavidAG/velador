import { sceneHtml, updateScene } from '../scene.js'
import { CLIPS, DAY, eventAt, gapAt, nextClipId, recorded, round, ROUNDS, state, SUMMARY } from '../store.js'
import { bindRound, roundHtml, setCursor, setSel } from '../timeline.js'
import { CAUSE, dateLabel, dayLabel, esc, hm, hms, I, KIND, pct, segment, span, toast, toggle } from '../ui.js'

const ZOOMS = [['86400', '24 h'], ['21600', '6 h'], ['3600', '1 h'], ['600', '10 min']]
const SPEEDS = [['1', '1×'], ['2', '2×'], ['8', '8×'], ['60', '60×']]

const dayBtn = r =>
	`<button class="day ${r.i === state.day ? 'on' : ''}" type="button" data-day="${r.i}" aria-pressed="${r.i === state.day}">
		<span class="day-name">${esc(dayLabel(r.date, r.i))}</span><span class="day-date mono">${esc(dateLabel(r.date))}</span>
		<span class="day-bar">${r.spans.map(s => `<i style="left:${(s.start / DAY) * 100}%;width:${((s.end - s.start) / DAY) * 100}%"></i>`).join('')}</span>
		<span class="day-meta"><b class="mono ${r.gaps.length ? 'warn' : ''}">${pct(r.coverage)}</b><span>${r.events.length} sucesos</span></span>
	</button>`

const sideHtml = r => `
	<header class="card-head"><h2>${esc(dayLabel(r.date, r.i))} · ${esc(dateLabel(r.date))}</h2><span class="chip ${r.gaps.length ? 'warn' : 'good'}">${r.gaps.length ? `${r.gaps.length} ${r.gaps.length === 1 ? 'hueco' : 'huecos'}` : 'Sin huecos'}</span></header>
	${r.gaps.map(g => `<button class="gap-row" type="button" data-seek="${g.start + 1}"><span class="ev-ico gap">${I[CAUSE[g.cause].icon]}</span><span class="ev-main"><b>${CAUSE[g.cause].label}</b><small>${span(g.end - g.start)} sin grabar</small></span><span class="mono ev-time">${hm(g.start)}</span></button>`).join('')}
	<div class="ev-list scroll">${
		r.events
			.map(e => `<button class="ev-row" type="button" data-seek="${e.t - 2}" data-ev="${e.id}"><span class="ev-ico ${e.kind}">${I[KIND[e.kind].icon]}</span><span class="ev-main"><b>${KIND[e.kind].label}</b><small>${esc(e.zone)} · ${span(e.dur)}</small></span><span class="mono ev-time">${hm(e.t)}</span></button>`)
			.join('') || '<p class="empty-s">Sin sucesos este día.</p>'
	}</div>`

const html = () => {
	const r = round()
	return `<div class="round">
		<div class="days" role="group" aria-label="Día">${[...ROUNDS].reverse().map(dayBtn).join('')}</div>
		<div class="round-main">
			<section class="stage" data-stage>
				${sceneHtml('', 'meet')}
				<div class="osd tl"><span class="badge rec" data-mode>GRABADO</span><span class="osd-name">${esc(SUMMARY.camera.name)}</span><span class="badge ghost" data-ir hidden>IR</span></div>
				<div class="osd tr mono" data-clock></div>
				<div class="osd bl"><span class="alert-chip" data-alert hidden></span></div>
				<div class="gap-veil" data-veil hidden><span class="veil-ico" data-veil-ico></span><b data-veil-title></b><span data-veil-sub></span><button class="btn small" type="button" data-skip>Saltar el hueco ${I.next}</button></div>
			</section>
			<aside class="card rail-card" data-side>${sideHtml(r)}</aside>
		</div>
		<section class="card deck">
			<div class="transport">
				<div class="tr-group">
					<button class="t-btn" type="button" data-jump="prev" title="Suceso anterior">${I.prev}<span class="sr-only">Suceso anterior</span></button>
					<button class="t-btn" type="button" data-nudge="-10" title="Atrás 10 s">${I.back10}<span class="sr-only">Atrás 10 segundos</span></button>
					<button class="t-btn play" type="button" data-play title="Reproducir (espacio)"><span data-play-ico></span><span class="sr-only">Reproducir o pausar</span></button>
					<button class="t-btn" type="button" data-nudge="10" title="Adelante 10 s">${I.fwd10}<span class="sr-only">Adelante 10 segundos</span></button>
					<button class="t-btn" type="button" data-jump="next" title="Suceso siguiente">${I.next}<span class="sr-only">Suceso siguiente</span></button>
				</div>
				<div class="timecode mono" data-tc></div>
				${segment('speed', SPEEDS, state.speed, 'Velocidad')}
				<span class="inline-switch">${toggle('skip', state.skipGaps, 'Saltar huecos')}<span aria-hidden="true">Saltar huecos</span></span>
				<span class="spacer"></span>
				${segment('zoom', ZOOMS, state.win.to - state.win.from, 'Acercamiento')}
				<button class="btn ${state.clip ? 'on' : ''}" type="button" data-clip>${I.clips}<span>Recortar</span></button>
			</div>
			<div data-rt></div>
			<div class="clip-bar" data-clipbar hidden></div>
			<p class="hint">Arrastra para moverte · rueda del ratón para acercar · <kbd>espacio</kbd> reproduce · <kbd>←</kbd> <kbd>→</kbd> 10 s</p>
		</section>
	</div>`
}

const mount = main => {
	let r = round()
	const svg = main.querySelector('.stage .scene')
	const veil = main.querySelector('[data-veil]')
	const alert = main.querySelector('[data-alert]')
	const rtHost = main.querySelector('[data-rt]')
	const clipBar = main.querySelector('[data-clipbar]')
	let rt = null
	let raf = 0
	let last = 0
	let lastEv = null

	const clipInfo = () => {
		clipBar.hidden = !state.clip
		if (!state.clip) return
		const len = recorded(r, state.clip.from, state.clip.to)
		const holes = len < state.clip.to - state.clip.from - 0.5
		clipBar.innerHTML = `<span class="clip-len mono">${hms(len)}</span><span>de <b class="mono">${hms(state.clip.from)}</b> a <b class="mono">${hms(state.clip.to)}</b> · unos <b class="mono">${((len * SUMMARY.camera.bitrate) / 8).toFixed(1)} MB</b>${holes ? ' · incluye un hueco, que no cuenta' : ', el video sale tal cual se grabó'}</span><span class="spacer"></span><button class="btn ghost" type="button" data-clip-cancel>Cancelar</button><button class="btn primary" type="button" data-clip-save ${len < 1 ? 'disabled' : ''}>Guardar recorte</button>`
	}

	const frame = () => {
		const t = state.t
		const gap = gapAt(r, t)
		const ev = gap ? null : eventAt(r, t)
		setCursor(rt, t, state.win)
		main.querySelector('[data-tc]').textContent = hms(t)
		main.querySelector('[data-clock]').innerHTML = `<span class="osd-date">${r.date.toLocaleDateString('sv-SE')} </span>${hms(t)}`
		updateScene(svg, { hour: t / 3600, person: ev?.kind === 'person', motion: ev?.kind === 'motion', x: ev ? ev.x * 0.5 + ((t - ev.t) / ev.dur) * 0.5 : 0 })
		main.querySelector('[data-ir]').hidden = !svg.classList.contains('ir')
		veil.hidden = !gap
		main.querySelector('[data-mode]').textContent = gap ? 'HUECO' : 'GRABADO'
		main.querySelector('[data-mode]').classList.toggle('gap', Boolean(gap))
		if (gap) {
			main.querySelector('[data-veil-ico]').innerHTML = I[CAUSE[gap.cause].icon]
			main.querySelector('[data-veil-title]').textContent = CAUSE[gap.cause].label
			main.querySelector('[data-veil-sub]').textContent = `Sin grabación de ${hms(gap.start)} a ${hms(gap.end)} · ${span(gap.end - gap.start)}`
		}
		if (ev?.id !== lastEv) {
			lastEv = ev?.id ?? null
			alert.hidden = !ev
			if (ev) alert.innerHTML = `${I[KIND[ev.kind].icon]}<span><b>${KIND[ev.kind].label}</b> · ${esc(ev.zone)}</span>`
			for (const b of main.querySelectorAll('[data-ev]')) b.classList.toggle('now', b.dataset.ev === lastEv)
			main.querySelector('[data-ev].now')?.scrollIntoView({ block: 'nearest' })
		}
		main.querySelector('[data-play-ico]').innerHTML = state.playing ? I.pause : I.play
	}

	const drawRt = () => {
		rtHost.innerHTML = roundHtml(r, state.win, { clip: state.clip })
		rt = rtHost.querySelector('.rt')
		bindRound(rt, {
			r,
			win: () => state.win,
			onSeek: seek,
			onZoom: (f, at) => zoom((state.win.to - state.win.from) * f, at),
			getClip: () => state.clip,
			onClip: c => {
				state.clip = c
				setSel(rt, c, state.win)
				clipInfo()
			},
		})
		for (const b of main.querySelectorAll('[data-segment="zoom"] [data-value]')) b.setAttribute('aria-checked', Number(b.dataset.value) === Math.round(state.win.to - state.win.from))
		if (!main.querySelector('[data-segment="zoom"] [tabindex="0"]')) main.querySelector('[data-segment="zoom"] [data-value]').tabIndex = 0
		frame()
	}

	const zoom = (len, center = state.t) => {
		len = Math.min(DAY, Math.max(300, len))
		const ratio = (center - state.win.from) / (state.win.to - state.win.from)
		let from = center - len * ratio
		from = Math.min(Math.max(0, from), DAY - len)
		state.win = { from, to: from + len }
		drawRt()
	}

	const follow = () => {
		const len = state.win.to - state.win.from
		if (len >= DAY || (state.t >= state.win.from && state.t <= state.win.to)) return
		const from = Math.min(Math.max(0, state.t - len * 0.15), DAY - len)
		state.win = { from, to: from + len }
		drawRt()
	}

	function seek(t) {
		state.t = Math.max(0, Math.min(t, r.end - 1))
		if (!rt?.classList.contains('dragging')) follow()
		frame()
	}

	const step = now => {
		if (!state.playing) return
		const dt = Math.min(0.25, (now - last) / 1000)
		last = now
		let t = state.t + dt * state.speed
		const gap = gapAt(r, t)
		if (gap && state.skipGaps) t = gap.end
		if (t >= r.end - 1) {
			state.playing = false
			t = r.end - 1
			toast(r.i === 0 ? 'Llegaste al presente' : 'Fin del día')
		}
		state.t = t
		if (!rt?.classList.contains('dragging')) follow()
		frame()
		raf = requestAnimationFrame(step)
	}
	const play = on => {
		state.playing = on
		cancelAnimationFrame(raf)
		if (on) {
			last = performance.now()
			raf = requestAnimationFrame(step)
		}
		frame()
	}

	const onClick = e => {
		const day = e.target.closest('[data-day]')
		if (day) {
			state.day = Number(day.dataset.day)
			r = round()
			state.t = Math.max(0, Math.min(state.t, r.end - 1))
			state.clip = null
			main.querySelector('[data-clip]').classList.remove('on')
			for (const b of main.querySelectorAll('[data-day]')) {
				b.classList.toggle('on', b === day)
				b.setAttribute('aria-pressed', b === day)
			}
			main.querySelector('[data-side]').innerHTML = sideHtml(r)
			lastEv = null
			clipInfo()
			drawRt()
		}
		const s = e.target.closest('[data-seek]')
		if (s) seek(Number(s.dataset.seek))
		if (e.target.closest('[data-play]')) play(!state.playing)
		const n = e.target.closest('[data-nudge]')
		if (n) seek(state.t + Number(n.dataset.nudge))
		const j = e.target.closest('[data-jump]')?.dataset.jump
		if (j) {
			const target = j === 'next' ? r.events.find(x => x.t > state.t + 2) : [...r.events].reverse().find(x => x.t < state.t - 4)
			if (target) seek(target.t - 2)
			else toast(j === 'next' ? 'No hay más sucesos este día' : 'No hay sucesos antes')
		}
		if (e.target.closest('[data-skip]')) {
			const g = gapAt(r, state.t)
			if (g) seek(g.end)
		}
		if (e.target.closest('[data-clip]')) {
			state.clip = state.clip ? null : { from: Math.max(0, state.t - 15), to: Math.min(r.end - 1, state.t + 15) }
			e.target.closest('[data-clip]').classList.toggle('on', Boolean(state.clip))
			if (state.clip && state.win.to - state.win.from > 3600) zoom(600, state.t)
			else drawRt()
			clipInfo()
		}
		if (e.target.closest('[data-clip-cancel]')) {
			state.clip = null
			main.querySelector('[data-clip]').classList.remove('on')
			clipInfo()
			drawRt()
		}
		if (e.target.closest('[data-clip-save]')) {
			const len = recorded(r, state.clip.from, state.clip.to)
			CLIPS.unshift({ id: nextClipId(), name: `Recorte de las ${hm(state.clip.from)}`, date: r.date, t: state.clip.from, dur: len, size: (len * SUMMARY.camera.bitrate) / 8 })
			state.clip = null
			main.querySelector('[data-clip]').classList.remove('on')
			clipInfo()
			drawRt()
			toast('Recorte guardado · no se borra con lo viejo')
		}
	}
	const onSeg = e => {
		if (e.detail.id === 'speed') state.speed = Number(e.detail.value)
		if (e.detail.id === 'zoom') zoom(Number(e.detail.value))
	}
	const onSwitch = e => {
		if (e.detail.id === 'skip') state.skipGaps = e.detail.on
	}
	const onKey = e => {
		if (e.repeat || e.target.closest('input, button, a')) return
		if (e.code === 'Space') {
			e.preventDefault()
			play(!state.playing)
		}
		if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') seek(state.t + (e.key === 'ArrowLeft' ? -10 : 10))
	}
	main.addEventListener('click', onClick)
	main.addEventListener('segment', onSeg)
	main.addEventListener('switch', onSwitch)
	document.addEventListener('keydown', onKey)
	state.t = Math.max(0, Math.min(state.t, r.end - 1))
	clipInfo()
	drawRt()
	follow()
	main.querySelector('.day.on')?.scrollIntoView({ block: 'nearest', inline: 'end' })
	if (state.playing) play(true)
	return () => {
		cancelAnimationFrame(raf)
		state.playing = false
		main.removeEventListener('click', onClick)
		main.removeEventListener('segment', onSeg)
		main.removeEventListener('switch', onSwitch)
		document.removeEventListener('keydown', onKey)
	}
}

export const roundView = { title: 'Ronda', html, mount }

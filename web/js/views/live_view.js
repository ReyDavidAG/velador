import { roundHtml, setCursor } from '../components/round_timeline.js'
import { api } from '../services/api.js'
import { watchLive } from '../services/live.js'
import { loadRound, loadSummary, state } from '../store.js'
import { CAUSE, esc, I, pct, span, toast } from '../ui.js'

const GB = 1024 ** 3
const LEGEND = `<span class="legend"><span><i class="sw span"></i>Grabado</span><span><i class="sw gapp"></i>Hueco</span><span><i class="sw person"></i>Persona</span><span><i class="sw motion"></i>Movimiento</span></span>`
const PAD = `<header class="card-head"><h2>Mover la cámara</h2></header>
	<div class="ptz"><div class="pad">
		<button type="button" data-ptz="0,1" class="u" title="Arriba">${I.up}<span class="sr-only">Arriba</span></button>
		<button type="button" data-ptz="-1,0" class="l" title="Izquierda">${I.left}<span class="sr-only">Izquierda</span></button>
		<span class="pad-c" aria-hidden="true"><i></i></span>
		<button type="button" data-ptz="1,0" class="r" title="Derecha">${I.right}<span class="sr-only">Derecha</span></button>
		<button type="button" data-ptz="0,-1" class="d" title="Abajo">${I.down}<span class="sr-only">Abajo</span></button>
	</div><p class="muted-s">Cada toque gira un poco. También con las flechas del teclado.</p></div>`
const STATE = { on_guard: ['good', 'De guardia'], reconnecting: ['warn', 'Reconectando'], gap: ['warn', 'Sin grabar'] }

const guardHtml = () => {
	const s = state.summary
	const cam = state.camera
	const [cls, label] = STATE[cam.state] ?? ['', cam.state]
	const since = cam.state === 'on_guard' && cam.guardSinceMs ? span(s.nowMs - cam.guardSinceMs < 60000 ? (s.nowMs - cam.guardSinceMs) / 1000 : Math.floor((s.nowMs - cam.guardSinceMs) / 60000) * 60) : '—'
	const used = s.disk ? ((s.disk.totalBytes - s.disk.freeBytes) / s.disk.totalBytes) * 100 : 0
	return `<header class="card-head"><h2>Guardia</h2><span class="chip ${cls}"><i class="dot"></i>${label}</span></header>
		<div class="stat-row">
			<div class="stat"><span class="mono stat-n">${since}</span><span class="stat-l">sin huecos</span></div>
			<div class="stat"><span class="mono stat-n" data-cov>—</span><span class="stat-l">cobertura de hoy</span></div>
		</div>
		${cam.gap ? `<p class="note">${I[CAUSE[cam.gap.cause]?.icon ?? 'alert']}<span><b>${esc(CAUSE[cam.gap.cause]?.label ?? 'Sin grabación')}</b> desde hace ${span((s.nowMs - cam.gap.startMs) / 1000)}.</span></p>` : ''}
		${s.disk ? `<div class="disk"><div class="disk-bar"><i class="o" style="width:${used}%"></i><i class="limit" style="left:${s.retention.percent}%"></i></div>
		<p class="disk-l"><span><i class="sw o"></i>Ocupado <b class="mono">${Math.round((s.disk.totalBytes - s.disk.freeBytes) / GB)} GB</b></span><span><i class="sw f"></i>Libre <b class="mono">${Math.round(s.disk.freeBytes / GB)} GB</b></span><span>Guarda <b class="mono">${s.retention.days} días</b></span></p></div>` : ''}`
}

const html = () => `<div class="live-grid">
	<div class="live-main">
		<section class="stage" data-stage>
			<video autoplay playsinline muted class="${state.camera.rotation === 180 ? 'rot' : ''}"></video>
			<div class="osd tl"><span class="badge live" data-badge><i class="dot pulse"></i>EN VIVO</span><span class="osd-name">${esc(state.camera.name)}</span></div>
			<div class="osd tr mono" data-clock></div>
			<div class="stage-note" data-note><b>Conectando con la cámara…</b></div>
			<div class="stage-bar">
				<button class="bar-btn" type="button" data-mute title="Sonido">${I.mute}<span class="sr-only">Sonido</span></button>
				<span class="spacer"></span>
				<button class="bar-btn text" type="button" data-save>${I.clips}<span>Guardar los últimos 30 s</span></button>
				<button class="bar-btn" type="button" data-full title="Pantalla completa">${I.full}<span class="sr-only">Pantalla completa</span></button>
			</div>
		</section>
		<section class="card today">
			<header class="card-head"><h2>Ronda de hoy</h2>${LEGEND}<a class="link" href="#/round">Abrir la ronda ${I.arrow}</a></header>
			<div data-mini></div>
		</section>
	</div>
	<aside class="rail"><section class="card" data-guard>${guardHtml()}</section><section class="card">${PAD}</section></aside>
</div>`

const mount = main => {
	const video = main.querySelector('video')
	const note = main.querySelector('[data-note]')
	const stage = main.querySelector('[data-stage]')
	let round = null
	let alive = true
	const stopLive = watchLive(state.camera.id, video, s => {
		note.hidden = s === 'live'
		note.innerHTML = s === 'reconnecting' ? '<b>Sin imagen</b><span>Reintentando la conexión…</span>' : '<b>Conectando con la cámara…</b>'
		main.querySelector('[data-badge]').hidden = s !== 'live'
	})
	const clock = () => {
		const d = new Date()
		main.querySelector('[data-clock]').textContent = d.toLocaleTimeString('es-MX', { hour12: false })
	}
	const refresh = async () => {
		try {
			await loadSummary()
			round = await loadRound(0)
		} catch {
			return
		}
		if (!alive) return
		main.querySelector('[data-guard]').innerHTML = guardHtml()
		main.querySelector('[data-cov]').textContent = pct(round.coverage)
		main.querySelector('[data-mini]').innerHTML = roundHtml(round, undefined, { mini: true })
		setCursor(main.querySelector('[data-mini] .rt'), round.end, { from: 0, to: round.len }, round)
	}
	clock()
	refresh()
	const tick = setInterval(clock, 1000)
	const poll = setInterval(refresh, 5000)
	let moving = false
	const move = async (x, y) => {
		if (moving) return
		moving = true
		try {
			await api.move(state.camera.id, x * 0.08, y * 0.08)
		} catch {
			toast('La cámara no respondió al movimiento')
		}
		moving = false
	}
	const onKey = e => {
		const dir = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, 1], ArrowDown: [0, -1] }[e.key]
		if (!dir || e.target.closest('input, [role="radio"]')) return
		e.preventDefault()
		move(...dir)
	}
	document.addEventListener('keydown', onKey)
	const onClick = async e => {
		const save = e.target.closest('[data-save]')
		if (save) {
			save.disabled = true
			try {
				const end = Date.now() - 3000
				await api.addClip(state.camera.id, { startMs: end - 30000, endMs: end, title: `${state.camera.name} · últimos 30 s` })
				toast('Recorte de 30 s guardado en Recortes')
			} catch (err) {
				toast(err.message)
			}
			save.disabled = false
		}
		const ptz = e.target.closest('[data-ptz]')
		if (ptz) move(...ptz.dataset.ptz.split(',').map(Number))
		if (e.target.closest('[data-mute]')) {
			state.muted = !state.muted
			video.muted = state.muted
			e.target.closest('[data-mute]').innerHTML = `${state.muted ? I.mute : I.sound}<span class="sr-only">Sonido</span>`
		}
		if (e.target.closest('[data-full]')) (document.fullscreenElement ? document.exitFullscreen() : stage.requestFullscreen?.())?.catch?.(() => {})
		const track = e.target.closest('[data-mini] .rt-track')
		if (track && round) {
			const b = track.getBoundingClientRect()
			state.day = 0
			state.t = Math.max(round.from, Math.min(round.end - 5, ((e.clientX - b.left) / b.width) * round.len))
			location.hash = '#/round'
		}
	}
	main.addEventListener('click', onClick)
	return () => {
		alive = false
		clearInterval(tick)
		clearInterval(poll)
		stopLive()
		document.removeEventListener('keydown', onKey)
		main.removeEventListener('click', onClick)
	}
}

export const liveView = { title: 'En vivo', html, mount }

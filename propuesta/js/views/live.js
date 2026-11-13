import { sceneHtml, updateScene } from '../scene.js'
import { CLIPS, guardSince, nextClipId, nowSec, ROUNDS, state, SUMMARY } from '../store.js'
import { roundHtml, setCursor } from '../timeline.js'
import { esc, hm, hms, I, KIND, pct, segment, span, toast } from '../ui.js'

const PRESETS = [
	['Portón', 0, 0],
	['Cochera', 0.9, 0.5],
	['Calle', -0.7, -0.5],
	['Jardín', -1, 0.6],
]

const eventRow = (e, i, fresh = false) =>
	`<a class="ev-row ${fresh ? 'fresh' : ''} animate-row-in" style="--i:${i}" href="#/round" data-goto="${e.t}"><span class="ev-ico ${e.kind}">${I[KIND[e.kind].icon]}</span><span class="ev-main"><b>${KIND[e.kind].label}</b><small>${esc(e.zone)} · ${span(e.dur)}</small></span><span class="mono ev-time">${fresh ? 'ahora' : hm(e.t)}</span></a>`

const stamp = () => {
	const d = new Date()
	const p = n => String(n).padStart(2, '0')
	return `<span class="osd-date">${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} </span>${hms(nowSec())}`
}

const html = () => {
	const r = ROUNDS[0]
	const free = SUMMARY.disk.total - SUMMARY.disk.velador - SUMMARY.disk.other
	return `<div class="live-grid">
		<div class="live-main">
		<section class="stage" data-stage>
			${sceneHtml('walk-live', 'meet')}
			<div class="osd tl"><span class="badge live"><i class="dot pulse"></i>EN VIVO</span><span class="osd-name">${esc(SUMMARY.camera.name)}</span><span class="badge ghost" data-ir hidden>IR</span></div>
			<div class="osd tr mono" data-clock></div>
			<div class="osd bl"><span class="alert-chip" data-alert hidden></span></div>
			<div class="stage-bar">
				<button class="bar-btn" type="button" data-mute title="Sonido">${I.mute}<span class="sr-only">Sonido</span></button>
				${segment('quality', [['hd', '1080p'], ['sd', '720p']], state.quality, 'Calidad')}
				<span class="spacer"></span>
				<button class="bar-btn text" type="button" data-save>${I.clips}<span>Guardar los últimos 30 s</span></button>
				<button class="bar-btn" type="button" data-shot title="Tomar foto">${I.shot}<span class="sr-only">Tomar foto</span></button>
				<button class="bar-btn" type="button" data-full title="Pantalla completa">${I.full}<span class="sr-only">Pantalla completa</span></button>
			</div>
		</section>
		<section class="card today animate-tile-in" style="--i:4">
			<header class="card-head"><h2>Ronda de hoy</h2><span class="legend"><span><i class="sw span"></i>Grabado</span><span><i class="sw gapp"></i>Hueco</span><span><i class="sw person"></i>Persona</span><span><i class="sw motion"></i>Movimiento</span></span><a class="link" href="#/round">Abrir la ronda ${I.arrow}</a></header>
			<div data-mini>${roundHtml(r, undefined, { mini: true })}</div>
		</section>
		</div>
		<aside class="rail">
			<section class="card animate-tile-in" style="--i:1">
				<header class="card-head"><h2>Guardia</h2><span class="chip good"><i class="dot"></i>De guardia</span></header>
				<div class="stat-row">
					<div class="stat"><span class="mono stat-n" data-since>${span(Math.floor(guardSince() / 60) * 60)}</span><span class="stat-l">sin huecos</span></div>
					<div class="stat"><span class="mono stat-n">${pct(r.coverage)}</span><span class="stat-l">cobertura de hoy</span></div>
				</div>
				<div class="disk"><div class="disk-bar"><i class="v" style="width:${(SUMMARY.disk.velador / SUMMARY.disk.total) * 100}%"></i><i class="o" style="width:${(SUMMARY.disk.other / SUMMARY.disk.total) * 100}%"></i></div>
				<p class="disk-l"><span><i class="sw v"></i>Velador <b class="mono">${SUMMARY.disk.velador} GB</b></span><span><i class="sw f"></i>Libre <b class="mono">${free} GB</b></span><span>Guarda <b class="mono">${SUMMARY.retentionDays} días</b></span></p></div>
			</section>
			<section class="card animate-tile-in" style="--i:2">
				<header class="card-head"><h2>Mover la cámara</h2><button class="link" type="button" data-ptz="home">Centrar</button></header>
				<div class="ptz">
					<div class="pad">
						<button type="button" data-ptz="up" class="u" title="Arriba">${I.up}<span class="sr-only">Arriba</span></button>
						<button type="button" data-ptz="left" class="l" title="Izquierda">${I.left}<span class="sr-only">Izquierda</span></button>
						<span class="pad-c" aria-hidden="true"><i data-pad-dot></i></span>
						<button type="button" data-ptz="right" class="r" title="Derecha">${I.right}<span class="sr-only">Derecha</span></button>
						<button type="button" data-ptz="down" class="d" title="Abajo">${I.down}<span class="sr-only">Abajo</span></button>
					</div>
					<div class="presets">${PRESETS.map(([n], i) => `<button class="preset" type="button" data-preset="${i}"><span class="mono">${i + 1}</span>${n}</button>`).join('')}</div>
				</div>
			</section>
			<section class="card grow animate-tile-in" style="--i:3">
				<header class="card-head"><h2>Sucesos de hoy</h2><a class="link" href="#/events">Ver todos</a></header>
				<div class="ev-list" data-events>${r.events.slice(-5).reverse().map((e, i) => eventRow(e, i)).join('') || '<p class="empty-s">Sin sucesos todavía.</p>'}</div>
			</section>
		</aside>
	</div>`
}

const mount = main => {
	const svg = main.querySelector('.stage .scene')
	const stage = main.querySelector('[data-stage]')
	const alert = main.querySelector('[data-alert]')
	const mini = main.querySelector('[data-mini] .rt')
	const padDot = main.querySelector('[data-pad-dot]')
	let tick = 14
	let alive = true
	let walker = null
	const frame = () => {
		const t = nowSec()
		main.querySelector('[data-clock]').innerHTML = stamp()
		main.querySelector('[data-since]').textContent = span(Math.floor(guardSince() / 60) * 60)
		updateScene(svg, { hour: t / 3600, person: Boolean(walker), x: walker?.x ?? 0, pan: state.pan, tilt: state.tilt })
		main.querySelector('[data-ir]').hidden = !svg.classList.contains('ir')
		setCursor(mini, t, { from: 0, to: 86400 })
		padDot.style.transform = `translate(${state.pan * 9}px, ${state.tilt * 9}px)`
	}
	const loop = setInterval(() => {
		tick++
		const phase = tick % 36
		if (phase === 16) {
			walker = { x: 0.05, kind: 'person', zone: 'Portón', t: nowSec() }
			frame()
			requestAnimationFrame(() => requestAnimationFrame(() => alive && walker && ((walker.x = 0.95), frame())))
			alert.innerHTML = `${I.person}<span><b>Persona</b> en el portón</span>`
			alert.hidden = false
			alert.classList.remove('animate-panel-in')
			void alert.offsetWidth
			alert.classList.add('animate-panel-in')
			const list = main.querySelector('[data-events]')
			list.insertAdjacentHTML('afterbegin', eventRow({ ...walker, dur: 9 }, 0, true))
			if (list.children.length > 5) list.lastElementChild.remove()
		}
		if (phase === 26) {
			walker = null
			alert.hidden = true
		}
		frame()
	}, 1000)
	frame()
	const onClick = e => {
		const ptz = e.target.closest('[data-ptz]')?.dataset.ptz
		if (ptz) {
			const step = 0.34
			if (ptz === 'home') [state.pan, state.tilt] = [0, 0]
			if (ptz === 'left') state.pan = Math.max(-1, state.pan - step)
			if (ptz === 'right') state.pan = Math.min(1, state.pan + step)
			if (ptz === 'up') state.tilt = Math.max(-1, state.tilt - step)
			if (ptz === 'down') state.tilt = Math.min(1, state.tilt + step)
			frame()
		}
		const preset = e.target.closest('[data-preset]')
		if (preset) {
			const [name, pan, tilt] = PRESETS[preset.dataset.preset]
			;[state.pan, state.tilt] = [pan, tilt]
			frame()
			toast(`Cámara en ${name}`)
		}
		if (e.target.closest('[data-mute]')) {
			state.muted = !state.muted
			e.target.closest('[data-mute]').innerHTML = `${state.muted ? I.mute : I.sound}<span class="sr-only">Sonido</span>`
		}
		if (e.target.closest('[data-save]')) {
			const t = Math.max(0, nowSec() - 30)
			CLIPS.unshift({ id: nextClipId(), name: `Últimos 30 s · ${hm(t)}`, date: ROUNDS[0].date, t, dur: 30, size: (30 * SUMMARY.camera.bitrate) / 8 })
			toast('Recorte de 30 s guardado en Recortes')
		}
		if (e.target.closest('[data-shot]')) {
			stage.classList.remove('flash')
			void stage.offsetWidth
			stage.classList.add('flash')
			toast('Foto guardada')
		}
		if (e.target.closest('[data-full]')) (document.fullscreenElement ? document.exitFullscreen() : stage.requestFullscreen?.())?.catch?.(() => {})
		const go = e.target.closest('[data-goto]')
		if (go) {
			state.day = 0
			state.t = Math.max(0, Number(go.dataset.goto) - 3)
			state.playing = true
		}
	}
	main.addEventListener('click', onClick)
	const onSeg = e => {
		if (e.detail.id !== 'quality') return
		state.quality = e.detail.value
		stage.classList.toggle('sd', state.quality === 'sd')
	}
	main.addEventListener('segment', onSeg)
	const onMini = e => {
		const b = mini.querySelector('.rt-track').getBoundingClientRect()
		state.day = 0
		state.t = Math.min(ROUNDS[0].end - 5, Math.max(0, ((e.clientX - b.left) / b.width) * 86400))
		state.playing = false
		location.hash = '#/round'
	}
	mini.addEventListener('click', onMini)
	return () => {
		alive = false
		clearInterval(loop)
		main.removeEventListener('click', onClick)
		main.removeEventListener('segment', onSeg)
	}
}

export const liveView = { title: 'En vivo', html, mount }

import { mascot, setMood, watchPointer } from './components/mascot.js'
import { loadSummary, state } from './store.js'
import { CAUSE, clockAt, esc, HERE, I, NAV, span } from './ui.js'
import { camerasView } from './views/cameras_view.js'
import { clipsView } from './views/clips_view.js'
import { continuityView } from './views/continuity_view.js'
import { eventsView } from './views/events_view.js'
import { liveView } from './views/live_view.js'
import { peopleView } from './views/people_view.js'
import { roundView } from './views/round_view.js'
import { settingsView } from './views/settings_view.js'
import { welcomeView } from './views/welcome_view.js'

const ROUTES = {
	'/': liveView,
	'/round': roundView,
	'/events': eventsView,
	'/people': peopleView,
	'/clips': clipsView,
	'/cameras': camerasView,
	'/continuity': continuityView,
	'/settings': settingsView,
}

const path = () => location.hash.slice(1).split('?')[0] || '/'

const links = mobile =>
	NAV.filter(n => !(mobile && n.desktop))
		.map(n => `${!mobile && n.group ? `<span class="nav-group">${n.group}</span>` : ''}<a href="${n.href}" data-path="${n.href.slice(1)}">${I[n.icon]}<span>${esc(n.label)}</span></a>`)
		.join('')

const brand = cls => `<a class="brand ${cls}" href="#/" title="Sereno, el velador">${mascot()}<span>Velador</span></a>`

const shell = () => `
	<div class="shell">
		<aside class="sidebar">
			${brand('')}
			<nav class="nav" aria-label="Secciones"><i class="nav-pill" aria-hidden="true"></i>${links(false)}</nav>
			<div class="guard">
				<span class="guard-top" data-guard-top><span class="dot live ping"></span>De guardia</span>
				<span class="guard-time mono" data-guard>—</span>
				<span class="guard-sub" data-guard-sub>sin huecos</span>
			</div>
		</aside>
		<div class="body">
			<header class="topbar">
				${brand('small')}
				<h1 data-title></h1>
				<span class="say" role="status"><i></i><span data-say>Todo en calma</span></span>
				<span class="spacer"></span>
				<button class="icon-btn key" type="button" data-theme-toggle title="Cambiar entre día y noche"><span class="sr-only">Cambiar entre día y noche</span><span data-theme-icon></span></button>
				<span class="top-clock" aria-hidden="true"><b data-top-clock></b><small data-top-date></small></span>
			</header>
			<main id="main" tabindex="-1"></main>
		</div>
		<footer class="tabbar"><nav class="nav" aria-label="Secciones"><i class="nav-pill" aria-hidden="true"></i>${links(true)}</nav></footer>
		<div class="toast" role="status"></div>
	</div>`

let cleanup = null
let loadedWeb = null

const applyTheme = () => {
	document.documentElement.dataset.theme = state.theme
	const el = document.querySelector('[data-theme-icon]')
	if (el) el.innerHTML = state.theme === 'dark' ? I.moon : I.sun
}

const calm = matchMedia('(prefers-reduced-motion: reduce)')
const veil = document.querySelector('[data-splash]')

const flipTheme = () => {
	const flip = () => {
		state.theme = state.theme === 'dark' ? 'light' : 'dark'
		localStorage.setItem('velador-theme', state.theme)
		applyTheme()
	}
	if (calm.matches || !veil) return flip()
	veil.classList.add('on')
	setTimeout(flip, 360)
	setTimeout(() => veil.classList.remove('on'), 880)
}

const movePills = () => {
	for (const nav of document.querySelectorAll('.nav')) {
		const pill = nav.querySelector('.nav-pill')
		const now = nav.querySelector('a[aria-current]')
		pill.hidden = !now || !now.offsetWidth
		if (pill.hidden) continue
		pill.style.width = `${now.offsetWidth}px`
		pill.style.height = `${now.offsetHeight}px`
		pill.style.transform = `translate(${now.offsetLeft}px, ${now.offsetTop}px)`
	}
}

const tickClock = () => {
	const d = new Date()
	const h = d.getHours()
	document.querySelector('[data-top-clock]').textContent = `${h % 12 || 12}:${String(d.getMinutes()).padStart(2, '0')} ${h < 12 ? 'AM' : 'PM'}`
	document.querySelector('[data-top-date]').textContent = d.toLocaleDateString('es-MX', { weekday: 'short', day: 'numeric', month: 'short' }).replaceAll('.', '')
}

const MOOD = {
	on_guard: () => ['calm', 'Todo en calma'],
	reconnecting: () => ['off', 'Reconectando con la cámara'],
	gap: cam => ['off', CAUSE[cam.gap?.cause]?.label ?? 'Sin grabar'],
	paused: () => ['sleep', 'Grabación en pausa'],
}

const since = ms => span(ms < 60000 ? ms / 1000 : Math.floor(ms / 60000) * 60)
const LABEL = { on_guard: 'De guardia', reconnecting: 'Reconectando', gap: 'Sin grabar', paused: 'En pausa' }

const tickGuard = async () => {
	try {
		await loadSummary()
	} catch (e) {
		if (e.message === 'unauthorized') return gate('Abre Velador desde la app o con el enlace de inicio para entrar.')
		document.querySelector('[data-guard-top]').lastChild.textContent = 'Motor sin respuesta'
		setMood('off', 'El motor no responde')
		return
	}
	if (state.summary.web && loadedWeb && state.summary.web !== loadedWeb && !document.fullscreenElement) return location.reload()
	loadedWeb ??= state.summary.web
	const cam = state.camera
	if (!cam) return setMood('calm', 'Listo para conectar tu cámara')
	setMood(...(MOOD[cam.state]?.(cam) ?? ['calm', 'Todo en calma']))
	if (state.summary.storage?.onFallback) document.querySelector('[data-guard-sub]').dataset.fallback = '1'
	else delete document.querySelector('[data-guard-sub]').dataset.fallback
	document.querySelector('[data-guard-top]').lastChild.textContent = LABEL[cam.state] ?? cam.state
	document.querySelector('[data-guard]').textContent = cam.state === 'on_guard' && cam.guardSinceMs ? since(state.summary.nowMs - cam.guardSinceMs) : '—'
	document.querySelector('[data-guard-sub]').textContent = cam.state === 'paused' ? (cam.pausedUntilMs ? `reanuda a las ${clockAt(cam.pausedUntilMs)}` : 'hasta que la reanudes') : cam.state !== 'on_guard' ? 'la grabación está detenida' : state.summary.storage?.onFallback ? `sin huecos · en ${HERE}, falta el disco` : 'sin huecos'
}

const gate = message => {
	veil?.classList.remove('on', 'boot')
	document.getElementById('main').innerHTML = `<div class="page"><div class="empty"><span class="empty-ico">${I.lock}</span><b>Velador</b><p>${esc(message)}</p></div></div>`
}

const swap = () => {
	cleanup?.()
	cleanup = null
	const p = path()
	const view = state.camera ? (ROUTES[p] ?? liveView) : welcomeView
	document.querySelector('.shell').classList.toggle('bare', !state.camera)
	const main = document.getElementById('main')
	main.innerHTML = `<div class="page animate-page-in">${view.html()}</div>`
	cleanup = view.mount?.(main) ?? null
	document.querySelector('[data-title]').textContent = view.title
	document.title = `${view.title} · Velador`
	for (const a of document.querySelectorAll('.nav a')) a.toggleAttribute('aria-current', a.dataset.path === (ROUTES[p] ? p : '/'))
	main.scrollTop = 0
	movePills()
	veil?.classList.remove('on', 'boot')
}

export const render = () => {
	if (!state.summary) return
	if (document.startViewTransition && !calm.matches && !document.hidden && !document.fullscreenElement) document.startViewTransition(swap)
	else swap()
}

document.getElementById('app').innerHTML = shell()
applyTheme()
watchPointer()
tickClock()
setInterval(tickClock, 1000)
window.addEventListener('resize', movePills)
document.fonts?.ready.then(movePills)
setInterval(tickGuard, 5000)
window.addEventListener('hashchange', () => {
	render()
	document.getElementById('main').focus({ preventScroll: true })
})
document.addEventListener('keydown', e => {
	const opt = e.target.closest?.('[data-segment] [data-value]')
	const dir = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key]
	if (!opt || !dir) return
	e.preventDefault()
	const all = [...opt.parentElement.querySelectorAll('[data-value]')]
	const next = all[(all.indexOf(opt) + dir + all.length) % all.length]
	next.focus()
	next.click()
})
document.addEventListener('click', e => {
	if (e.target.closest('[data-theme-toggle]')) flipTheme()
	const sw = e.target.closest('[data-switch]')
	if (sw) {
		const on = sw.getAttribute('aria-checked') !== 'true'
		sw.setAttribute('aria-checked', on)
		sw.dispatchEvent(new CustomEvent('switch', { bubbles: true, detail: { id: sw.dataset.switch, on } }))
	}
	const opt = e.target.closest('[data-segment] [data-value]')
	if (opt) {
		const group = opt.closest('[data-segment]')
		for (const b of group.querySelectorAll('[data-value]')) {
			b.setAttribute('aria-checked', b === opt)
			b.tabIndex = b === opt ? 0 : -1
		}
		group.dispatchEvent(new CustomEvent('segment', { bubbles: true, detail: { id: group.dataset.segment, value: opt.dataset.value } }))
	}
})
tickGuard().then(render)

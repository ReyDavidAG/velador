import { guardSince, nowSec, ROUNDS, state } from './store.js'
import { esc, I, NAV, pct, span } from './ui.js'
import { camerasView } from './views/cameras.js'
import { clipsView } from './views/clips.js'
import { continuityView } from './views/continuity.js'
import { desktopView } from './views/desktop.js'
import { eventsView } from './views/events.js'
import { liveView } from './views/live.js'
import { roundView } from './views/round.js'
import { settingsView } from './views/settings.js'
import { welcomeView } from './views/welcome.js'

const ROUTES = {
	'/': liveView,
	'/round': roundView,
	'/events': eventsView,
	'/clips': clipsView,
	'/cameras': camerasView,
	'/continuity': continuityView,
	'/settings': settingsView,
	'/desktop': desktopView,
	'/welcome': welcomeView,
}

const path = () => location.hash.slice(1).split('?')[0] || '/'

const links = mobile =>
	NAV.filter(n => !(mobile && n.desktop))
		.map(n => `${!mobile && n.group ? `<span class="nav-group">${n.group}</span>` : ''}<a href="${n.href}" data-path="${n.href.slice(1)}">${I[n.icon]}<span>${esc(n.label)}</span></a>`)
		.join('')

const mark = `<svg class="mark" viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="16" r="10" fill="none" stroke="currentColor" stroke-width="2.6"/><circle class="mark-eye" cx="16" cy="16" r="4.4"/></svg>`

const shell = () => `
	<div class="shell">
		<aside class="sidebar">
			<a class="brand" href="#/">${mark}<span>Velador</span></a>
			<nav class="nav" aria-label="Secciones">${links(false)}</nav>
			<a class="guard" href="#/continuity">
				<span class="guard-top"><span class="dot live ping"></span>De guardia</span>
				<span class="guard-time mono" data-guard></span>
				<span class="guard-sub">sin huecos · hoy <b data-cov></b></span>
			</a>
		</aside>
		<div class="body">
			<header class="topbar">
				<a class="brand small" href="#/">${mark}<span>Velador</span></a>
				<h1 data-title></h1>
				<span class="spacer"></span>
				<span class="demo" title="Nada de lo que ves viene de una cámara real">Datos de ejemplo</span>
				<button class="icon-btn" type="button" data-theme-toggle title="Cambiar tema"><span class="sr-only">Cambiar tema</span><span data-theme-icon></span></button>
			</header>
			<main id="main" tabindex="-1"></main>
		</div>
		<footer class="tabbar"><nav class="nav" aria-label="Secciones">${links(true)}</nav></footer>
		<div class="toast" role="status"></div>
	</div>`

let cleanup = null

const applyTheme = () => {
	document.documentElement.dataset.theme = state.theme
	const el = document.querySelector('[data-theme-icon]')
	if (el) el.innerHTML = state.theme === 'dark' ? I.sun : I.moon
}

const tickGuard = () => {
	const g = document.querySelector('[data-guard]')
	if (!g) return
	ROUNDS[0].end = Math.max(ROUNDS[0].end, nowSec())
	ROUNDS[0].spans.at(-1).end = ROUNDS[0].end
	g.textContent = span(Math.floor(guardSince() / 60) * 60)
	document.querySelector('[data-cov]').textContent = pct(ROUNDS[0].coverage)
}

export const render = () => {
	cleanup?.()
	cleanup = null
	const p = path()
	const view = ROUTES[p] ?? liveView
	const main = document.getElementById('main')
	main.innerHTML = `<div class="page animate-page-in">${view.html()}</div>`
	cleanup = view.mount?.(main) ?? null
	document.querySelector('[data-title]').textContent = view.title
	document.title = `${view.title} · Velador`
	for (const a of document.querySelectorAll('.nav a')) a.toggleAttribute('aria-current', a.dataset.path === (ROUTES[p] ? p : '/'))
	main.scrollTop = 0
}

document.getElementById('app').innerHTML = shell()
applyTheme()
tickGuard()
setInterval(tickGuard, 1000)
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
	if (e.target.closest('[data-theme-toggle]')) {
		state.theme = state.theme === 'dark' ? 'light' : 'dark'
		localStorage.setItem('velador-theme', state.theme)
		applyTheme()
	}
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
render()

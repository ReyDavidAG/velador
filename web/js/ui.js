export const esc = v => String(v).replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`)

const svg = d => `<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${d}</svg>`

export const I = {
	live: svg('<path d="M3 8.5A2.5 2.5 0 0 1 5.5 6h8A2.5 2.5 0 0 1 16 8.5v7a2.5 2.5 0 0 1-2.5 2.5h-8A2.5 2.5 0 0 1 3 15.5z"/><path d="m16 10.5 5-3v9l-5-3"/>'),
	round: svg('<path d="M3 12h18"/><path d="M6 8v8M10 6v12M14 9v6M18 7v10"/>'),
	events: svg('<path d="M13 3 5 13.5h6L10 21l8-10.5h-6z"/>'),
	clips: svg('<circle cx="6" cy="7" r="2.5"/><circle cx="6" cy="17" r="2.5"/><path d="M8.2 8.3 20 16M8.2 15.7 20 8"/>'),
	camera: svg('<circle cx="12" cy="11" r="7"/><circle cx="12" cy="11" r="2.5"/><path d="M7 21h10M12 18v3"/>'),
	shield: svg('<path d="M12 3 4.5 6v5.5c0 4.6 3.1 8 7.5 9.5 4.4-1.5 7.5-4.9 7.5-9.5V6z"/><path d="m9 12 2.2 2.2L15.2 10"/>'),
	settings: svg('<path d="M4 7h10M18 7h2M4 17h2M10 17h10"/><circle cx="16" cy="7" r="2"/><circle cx="8" cy="17" r="2"/>'),
	desktop: svg('<rect x="3" y="4" width="18" height="13" rx="2"/><path d="M8 21h8M12 17v4"/>'),
	welcome: svg('<path d="M12 3v3M5.6 5.6l2.1 2.1M3 12h3M18 12h3M16.3 7.7l2.1-2.1"/><path d="M7 21a5 5 0 0 1 10 0z"/><circle cx="12" cy="12" r="3"/>'),
	moon: svg('<path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/>'),
	sun: svg('<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>'),
	play: `<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor" aria-hidden="true"><path d="M8 5.2v13.6a1 1 0 0 0 1.5.9l11-6.8a1 1 0 0 0 0-1.8l-11-6.8a1 1 0 0 0-1.5.9z"/></svg>`,
	pause: `<svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor" aria-hidden="true"><rect x="6" y="5" width="4.5" height="14" rx="1.2"/><rect x="13.5" y="5" width="4.5" height="14" rx="1.2"/></svg>`,
	back10: svg('<path d="M4 12a8 8 0 1 0 2.6-5.9L4 8.5"/><path d="M4 4v4.5h4.5"/><path d="M9.5 10v5M12.5 11.2c0-.7.6-1.2 1.4-1.2s1.4.5 1.4 1.2v2.6c0 .7-.6 1.2-1.4 1.2s-1.4-.5-1.4-1.2z" stroke-width="1.4"/>'),
	fwd10: svg('<path d="M20 12a8 8 0 1 1-2.6-5.9L20 8.5"/><path d="M20 4v4.5h-4.5"/><path d="M8.5 10v5M11.5 11.2c0-.7.6-1.2 1.4-1.2s1.4.5 1.4 1.2v2.6c0 .7-.6 1.2-1.4 1.2s-1.4-.5-1.4-1.2z" stroke-width="1.4"/>'),
	prev: svg('<path d="M18 6 9 12l9 6zM6 6v12"/>'),
	next: svg('<path d="m6 6 9 6-9 6zM18 6v12"/>'),
	sound: svg('<path d="M4 9.5h3l5-4v13l-5-4H4z"/><path d="M16 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11"/>'),
	mute: svg('<path d="M4 9.5h3l5-4v13l-5-4H4z"/><path d="m16 9.5 5 5M21 9.5l-5 5"/>'),
	shot: svg('<path d="M4 8.5A1.5 1.5 0 0 1 5.5 7H8l1.5-2h5L16 7h2.5A1.5 1.5 0 0 1 20 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5z"/><circle cx="12" cy="13" r="3.2"/>'),
	full: svg('<path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/>'),
	shrink: svg('<path d="M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5"/>'),
	up: svg('<path d="m6 14 6-6 6 6"/>'),
	down: svg('<path d="m6 10 6 6 6-6"/>'),
	left: svg('<path d="m14 6-6 6 6 6"/>'),
	right: svg('<path d="m10 6 6 6-6 6"/>'),
	home: svg('<circle cx="12" cy="12" r="2.5"/><path d="M12 3v3M12 18v3M3 12h3M18 12h3"/>'),
	check: svg('<path d="m5 12 4.5 4.5L19 7"/>'),
	alert: svg('<path d="M12 4 2.8 19.5h18.4z"/><path d="M12 10v4M12 17h.01"/>'),
	person: svg('<circle cx="12" cy="7" r="3.2"/><path d="M5.5 20.5a6.5 6.5 0 0 1 13 0"/>'),
	motion: svg('<path d="M3 12h3l2.5-6 4 12 3-9 1.5 3h4"/>'),
	folder: svg('<path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2h7.5A2.5 2.5 0 0 1 21 9.5v8a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 17.5z"/>'),
	trash: svg('<path d="M4 7h16M9 7V4.5h6V7M6.5 7l1 13h9l1-13M10 11v5.5M14 11v5.5"/>'),
	lock: svg('<rect x="5" y="10" width="14" height="10" rx="2"/><path d="M8 10V7a4 4 0 1 1 8 0v3"/>'),
	plus: svg('<path d="M12 5v14M5 12h14"/>'),
	zoomIn: svg('<circle cx="11" cy="11" r="6.5"/><path d="m20 20-4-4M11 8.5v5M8.5 11h5"/>'),
	bolt: svg('<path d="M13 3 5 13.5h6L10 21l8-10.5h-6z"/>'),
	disk: svg('<rect x="3" y="6" width="18" height="12" rx="3"/><path d="M7 14h.01M11 14h6"/>'),
	phone: svg('<rect x="7" y="3" width="10" height="18" rx="2.5"/><path d="M11 18h2"/>'),
	relay: svg('<path d="M4 8h11l-3-3M20 16H9l3 3"/>'),
	wifi: svg('<path d="M3 9.5a13 13 0 0 1 18 0M6 13a8.5 8.5 0 0 1 12 0M9 16.5a4.2 4.2 0 0 1 6 0"/><path d="M12 19.5h.01"/>'),
	sleep: svg('<path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/><path d="M14 4h4l-4 4h4"/>'),
	power: svg('<path d="M12 3v8"/><path d="M6.3 6.8a8 8 0 1 0 11.4 0"/>'),
	engine: svg('<rect x="5" y="5" width="14" height="14" rx="2"/><path d="M9 9h6v6H9zM9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/>'),
	bulb: svg('<path d="M9 18h6M10 21h4"/><path d="M12 3a6 6 0 0 0-4 10.5c.7.7 1 1.5 1 2.5h6c0-1 .3-1.800 1-2.5A6 6 0 0 0 12 3z"/>'),
	close: svg('<path d="M6 6l12 12M18 6 6 18"/>'),
	arrow: svg('<path d="M5 12h14M13 6l6 6-6 6"/>'),
}

export const NAV = [
	{ href: '#/', label: 'En vivo', icon: 'live' },
	{ href: '#/round', label: 'Ronda', icon: 'round' },
	{ href: '#/events', label: 'Sucesos', icon: 'events' },
	{ href: '#/people', label: 'Personas', icon: 'person', desktop: true },
	{ href: '#/clips', label: 'Recortes', icon: 'clips' },
	{ href: '#/cameras', label: 'Cámaras', icon: 'camera', group: 'Sistema', desktop: true },
	{ href: '#/continuity', label: 'Continuidad', icon: 'shield', desktop: true },
	{ href: '#/settings', label: 'Ajustes', icon: 'settings' },
]

const p2 = n => String(Math.floor(n)).padStart(2, '0')
export const hms = s => `${p2(s / 3600)}:${p2((s % 3600) / 60)}:${p2(s % 60)}`
export const hm = s => `${p2(s / 3600)}:${p2((s % 3600) / 60)}`
export const span = s => {
	s = Math.round(s)
	if (s < 60) return `${s} s`
	if (s < 3600) return `${Math.floor(s / 60)} min${s % 60 ? ` ${s % 60} s` : ''}`
	return `${Math.floor(s / 3600)} h${Math.floor((s % 3600) / 60) ? ` ${Math.floor((s % 3600) / 60)} min` : ''}`
}
export const HERE = /Windows/.test(navigator.userAgent) ? 'esta PC' : 'esta Mac'
export const THERE = /Windows/.test(navigator.userAgent) ? 'la PC' : 'la Mac'
export const clockAt = ms => new Date(ms).toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit', hour12: false })
export const pct = v => `${v >= 100 ? '100' : Math.min(99.9, v).toFixed(1)} %`
export const gb = v => `${v.toFixed(v < 10 ? 1 : 0)} GB`
const cap = t => t.charAt(0).toUpperCase() + t.slice(1)
export const dayLabel = (d, i) => (i === 0 ? 'Hoy' : i === 1 ? 'Ayer' : cap(d.toLocaleDateString('es-MX', { weekday: 'long' })))
export const dateLabel = d => d.toLocaleDateString('es-MX', { day: 'numeric', month: 'short' }).replace('.', '')

export const KIND = {
	person: { label: 'Persona', icon: 'person' },
	motion: { label: 'Movimiento', icon: 'motion' },
	tamper: { label: 'Cámara manipulada', icon: 'alert' },
}
export const CAUSE = {
	sleep: { label: 'Equipo en reposo', icon: 'sleep' },
	camera: { label: 'Sin señal de la cámara', icon: 'wifi' },
	network: { label: 'Red caída', icon: 'wifi' },
	engine: { label: 'Motor reiniciado', icon: 'engine' },
	power: { label: 'Corte de luz', icon: 'power' },
	update: { label: 'Actualización', icon: 'engine' },
	disk: { label: 'Disco lleno', icon: 'disk' },
	paused: { label: 'Guardia en pausa', icon: 'power' },
	privacy: { label: 'Modo privacidad', icon: 'lock' },
	credentials: { label: 'Cuenta rechazada', icon: 'lock' },
	unknown: { label: 'Sin grabación', icon: 'alert' },
}

let toastTimer
export const toast = msg => {
	const el = document.querySelector('.toast')
	if (!el) return
	el.innerHTML = `${I.check}<span>${esc(msg)}</span>`
	el.classList.add('show')
	clearTimeout(toastTimer)
	toastTimer = setTimeout(() => el.classList.remove('show'), 2600)
}

export const toggle = (id, on, label) =>
	`<button class="switch" type="button" role="switch" aria-checked="${on}" data-switch="${id}"><span class="sr-only">${esc(label)}</span><span class="knob"></span></button>`

export const segment = (id, options, value, label = '') =>
	`<div class="segment" role="radiogroup" aria-label="${esc(label)}" data-segment="${id}">${options
		.map(([v, l]) => `<button type="button" role="radio" aria-checked="${String(v) === String(value)}" tabindex="${String(v) === String(value) ? 0 : -1}" data-value="${v}">${esc(l)}</button>`)
		.join('')}</div>`

export const bytes = n => (n >= 1024 ** 3 ? `${(n / 1024 ** 3).toFixed(n >= 10 * 1024 ** 3 ? 0 : 1)} GB` : `${(n / 1024 ** 2).toFixed(1)} MB`)

export const confirmOnce = (button, label = '¿Seguro?') => {
	if (button.dataset.armed) return true
	button.dataset.armed = '1'
	const before = button.innerHTML
	button.innerHTML = `<span>${label}</span>`
	button.classList.add('danger-armed')
	setTimeout(() => {
		delete button.dataset.armed
		button.classList.remove('danger-armed')
		button.innerHTML = before
	}, 3000)
	return false
}

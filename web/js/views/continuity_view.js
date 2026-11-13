import { api } from '../services/api.js'
import { loadWeek, state } from '../store.js'
import { CAUSE, dateLabel, dayLabel, esc, I, pct, span } from '../ui.js'

const layers = (settings, summary) => [
	['engine', 'El motor no depende de la ventana', 'Graba en segundo plano mientras tu sesión esté abierta. Cerrar la ventana no corta nada.', true],
	['sleep', 'El equipo no se duerme solo', 'Evita el reposo por inactividad. Cerrar la tapa o quedarse sin batería sí lo duerme.', settings.keepAwake],
	['wifi', 'Vigía de señal', 'Si pasan 5 s sin imagen nueva, reconecta solo y anota la causa.', true],
	['disk', 'Se guarda cada segundo', 'Si se va la luz o se cierra a la fuerza, se pierde a lo mucho 1 s.', true],
	['folder', 'Disco externo con respaldo', summary.storage.external ? (summary.storage.onFallback ? 'El disco no está: se graba en esta Mac y se pasará al volver.' : 'Grabando en el disco externo. Si lo desconectas, sigue en esta Mac.') : 'Se graba en esta Mac. Puedes elegir un disco externo en Ajustes.', summary.storage.external && !summary.storage.onFallback],
]

const body = (rounds, settings, summary) => {
	const live = rounds.filter(r => r && r.end > r.from)
	const watched = live.reduce((s, r) => s + (r.end - r.from), 0)
	const gaps = live.flatMap(r => r.gaps.map(g => ({ ...g, r })))
	const lost = gaps.reduce((s, g) => s + g.end - g.start, 0)
	const cam = state.camera
	return `
	<section class="card hero"><div class="hero-main"><span class="eyebrow"><span class="dot live ${cam.state === 'on_guard' ? 'ping' : ''}"></span>${cam.state === 'on_guard' ? 'De guardia' : 'Sin grabar'}</span><p class="hero-n mono">${watched > 0 ? pct(((watched - lost) / watched) * 100) : '—'}</p><p class="hero-l">grabado desde que Velador vigila, en los últimos 7 días</p></div>
		<div class="hero-stats"><div><span class="mono">${cam.state === 'on_guard' && cam.guardSinceMs ? span(Math.max(0, (summary.nowMs - cam.guardSinceMs) / 1000)) : '—'}</span><small>sin huecos ahora</small></div><div><span class="mono">${gaps.length}</span><small>huecos en la semana</small></div><div><span class="mono">${span(lost)}</span><small>sin grabar en total</small></div></div></section>
	<section class="card"><header class="card-head"><h2>La semana</h2><span class="legend"><span><i class="sw span"></i>Grabado</span><span><i class="sw gapp"></i>Hueco</span></span></header>
		<div class="week">${rounds.filter(Boolean).map(r => `<a class="week-row" href="#/round" data-day="${r.i}"><span class="week-d"><b>${esc(dayLabel(r.date, r.i))}</b><small class="mono">${esc(dateLabel(r.date))}</small></span><span class="week-bar">${r.spans.map(s => `<i class="s" style="left:${(s.start / r.len) * 100}%;width:${((s.end - s.start) / r.len) * 100}%"></i>`).join('')}${r.gaps.map(g => `<i class="g" style="left:${(g.start / r.len) * 100}%;width:max(3px, ${((g.end - g.start) / r.len) * 100}%)"></i>`).join('')}</span><span class="mono week-p ${r.gaps.length ? 'warn' : ''}">${r.end > r.from ? pct(r.coverage) : '—'}</span></a>`).join('')}</div></section>
	<div class="two">
		<section class="card"><header class="card-head"><h2>Qué protege la grabación</h2></header><ul class="layers">${layers(settings, summary).map(([ico, t, d, on]) => `<li class="${on ? 'on' : 'off'}"><span class="layer-ico">${I[ico]}</span><span class="layer-b"><b>${t}</b><small>${d}</small></span>${on ? `<span class="chip good">${I.check}Activa</span>` : `<a class="btn small" href="#/settings">Ajustes</a>`}</li>`).join('')}</ul>
			<p class="note">${I.shield}<span>Lo que ningún programa evita: la tapa cerrada, la batería agotada, el equipo apagado y que la cámara pierda su WiFi. Cada uno queda anotado como hueco con su causa.</span></p></section>
		<section class="card"><header class="card-head"><h2>Huecos de la semana</h2><span class="count">${gaps.length}</span></header><div class="gap-table">${gaps.reverse().map(g => `<a class="gap-row" href="#/round" data-day="${g.r.i}" data-t="${g.start + 1}"><span class="ev-ico gap">${I[CAUSE[g.cause]?.icon ?? 'alert']}</span><span class="ev-main"><b>${esc(CAUSE[g.cause]?.label ?? 'Sin grabación')}</b><small>${esc(dayLabel(g.r.date, g.r.i))} · <span class="mono">${g.r.clock(g.start)}</span></small></span><span class="mono ev-time">${span(g.end - g.start)}</span></a>`).join('') || '<p class="empty-s">Ninguno. Así debe verse.</p>'}</div></section>
	</div>`
}

const html = () => `<div class="continuity" data-root><p class="empty-s">Cargando…</p></div>`

const mount = main => {
	const root = main.querySelector('[data-root]')
	const onClick = e => {
		const a = e.target.closest('[data-day]')
		if (!a) return
		state.day = Number(a.dataset.day)
		state.t = a.dataset.t ? Number(a.dataset.t) : null
		state.playing = false
	}
	main.addEventListener('click', onClick)
	Promise.all([loadWeek(), api.settings(), api.summary()])
		.then(([rounds, settings, summary]) => (root.innerHTML = body(rounds, settings, summary)))
		.catch(() => (root.innerHTML = '<p class="empty-s">No se pudo leer la continuidad.</p>'))
	return () => main.removeEventListener('click', onClick)
}

export const continuityView = { title: 'Continuidad', html, mount }

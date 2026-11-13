import { allGaps, DAY, guardSince, ROUNDS, state } from '../store.js'
import { CAUSE, dateLabel, dayLabel, esc, hms, I, pct, span } from '../ui.js'

const LAYERS = [
	['engine', 'El motor no depende de la ventana', 'Graba en segundo plano mientras tu sesión esté abierta. Cerrar la ventana no corta nada.', 'on'],
	['sleep', 'El equipo no se duerme solo', 'Evita el reposo por inactividad. Cerrar la tapa o quedarse sin batería sí lo duerme.', 'on'],
	['wifi', 'Vigía de señal', 'Si pasan 5 s sin imagen nueva, reconecta solo y anota la causa.', 'on'],
	['disk', 'Se guarda cada segundo', 'Si se va la luz o se cierra a la fuerza, se pierde a lo mucho 1 s.', 'on'],
	['relay', 'Motor en un equipo siempre prendido', 'Lo único que quita los huecos por reposo: el motor graba allá y este equipo sólo mira.', 'off'],
]

const html = () => {
	const gaps = allGaps()
	const total = ROUNDS.reduce((s, r) => s + r.end, 0)
	const lost = gaps.reduce((s, g) => s + g.end - g.start, 0)
	const worst = gaps.filter(g => g.cause === 'sleep' || g.cause === 'engine').sort((a, b) => b.end - b.start - (a.end - a.start))[0]
	return `<div class="continuity">
		<section class="card hero animate-tile-in">
			<div class="hero-main"><span class="eyebrow"><span class="dot live ping"></span>De guardia</span><p class="hero-n mono">${pct(((total - lost) / total) * 100)}</p><p class="hero-l">de los últimos 7 días quedó grabado</p></div>
			<div class="hero-stats">
				<div><span class="mono">${span(Math.floor(guardSince() / 60) * 60)}</span><small>sin huecos ahora</small></div>
				<div><span class="mono">${gaps.length}</span><small>huecos en la semana</small></div>
				<div><span class="mono">${span(lost)}</span><small>sin grabar en total</small></div>
			</div>
		</section>
		<section class="card animate-tile-in" style="--i:1">
			<header class="card-head"><h2>La semana</h2><span class="legend"><span><i class="sw span"></i>Grabado</span><span><i class="sw gapp"></i>Hueco</span></span></header>
			<div class="week">${ROUNDS.map(
				r => `<a class="week-row" href="#/round" data-day="${r.i}"><span class="week-d"><b>${esc(dayLabel(r.date, r.i))}</b><small class="mono">${esc(dateLabel(r.date))}</small></span>
				<span class="week-bar">${r.spans.map(s => `<i class="s" style="left:${(s.start / DAY) * 100}%;width:${((s.end - s.start) / DAY) * 100}%"></i>`).join('')}${r.gaps.map(g => `<i class="g" style="left:${(g.start / DAY) * 100}%;width:max(3px, ${((g.end - g.start) / DAY) * 100}%)"></i>`).join('')}</span>
				<span class="mono week-p ${r.gaps.length ? 'warn' : ''}">${pct(r.coverage)}</span></a>`
			).join('')}</div>
		</section>
		<div class="two">
			<section class="card animate-tile-in" style="--i:2">
				<header class="card-head"><h2>Qué protege la grabación</h2></header>
				<ul class="layers">${LAYERS.map(([ico, t, d, s], i) => `<li class="animate-row-in ${s}" style="--i:${i}"><span class="layer-ico">${I[ico]}</span><span class="layer-b"><b>${t}</b><small>${d}</small></span>${s === 'on' ? `<span class="chip good">${I.check}Activa</span>` : `<a class="btn small" href="#/settings">Ver cómo</a>`}</li>`).join('')}</ul>
				${worst ? `<p class="note">${I.relay}<span>Con el motor en un equipo siempre prendido, el hueco de <b>${span(worst.end - worst.start)}</b> del ${esc(dayLabel(worst.date, worst.day).toLowerCase())} no habría existido. Los de la cámara y la luz no los evita ningún programa.</span></p>` : ''}
			</section>
			<section class="card animate-tile-in" style="--i:3">
				<header class="card-head"><h2>Huecos de la semana</h2><span class="count">${gaps.length}</span></header>
				<div class="gap-table">${gaps
					.map((g, i) => `<a class="gap-row animate-row-in" style="--i:${i}" href="#/round" data-day="${g.day}" data-t="${g.start + 1}"><span class="ev-ico gap">${I[CAUSE[g.cause].icon]}</span><span class="ev-main"><b>${CAUSE[g.cause].label}</b><small>${esc(dayLabel(g.date, g.day))} · <span class="mono">${hms(g.start)}</span></small></span><span class="mono ev-time">${span(g.end - g.start)}</span></a>`)
					.join('')}</div>
			</section>
		</div>
	</div>`
}

const mount = main => {
	const onClick = e => {
		const a = e.target.closest('[data-day]')
		if (!a) return
		state.day = Number(a.dataset.day)
		if (a.dataset.t) state.t = Math.max(0, Number(a.dataset.t))
		state.playing = false
	}
	main.addEventListener('click', onClick)
	return () => main.removeEventListener('click', onClick)
}

export const continuityView = { title: 'Continuidad', html, mount }

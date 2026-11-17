import { defaultZone, mountZoneMap, zoneMapHtml, zoneText } from '../components/zone_map.js'
import { api } from '../services/api.js'
import { watchLive } from '../services/live.js'
import { confirmOnce, esc, I, toast } from '../ui.js'

const PAD = `<button type="button" data-step="0,1" class="u" title="Arriba">${I.up}<span class="sr-only">Arriba</span></button>
	<button type="button" data-step="-1,0" class="l" title="Izquierda">${I.left}<span class="sr-only">Izquierda</span></button>
	<span class="pad-c" aria-hidden="true"><i></i></span>
	<button type="button" data-step="1,0" class="r" title="Derecha">${I.right}<span class="sr-only">Derecha</span></button>
	<button type="button" data-step="0,-1" class="d" title="Abajo">${I.down}<span class="sr-only">Abajo</span></button>`

const SETS = [
	['minX', 'Límite izquierdo', 'left'],
	['maxX', 'Límite derecho', 'right'],
	['minY', 'Límite superior', 'up'],
	['maxY', 'Límite inferior', 'down'],
]

export const zoneSummaryHtml = id => `<div class="nets zone-sum" data-zone-sum="${esc(id)}">
	<div class="form-row"><b>Zona de vigilancia</b><span class="muted-s" data-zone-text>Consultando…</span><span class="spacer"></span><button class="btn small" type="button" data-zone-open="${esc(id)}">${I.settings}<span>Ajustar zona</span></button></div>
</div>`

export const zoneEditorHtml = camera => `<section class="card zone-ed" data-zone-ed="${esc(camera.id)}">
	<header class="card-head"><h2>${I.camera}Zona de vigilancia · ${esc(camera.name)}</h2><span class="chip" data-zone-chip>…</span><button class="btn small ghost" type="button" data-zone-close>${I.close}<span>Cerrar</span></button></header>
	<p class="lead">Lleva la cámara hasta donde deja de verse algo útil y fija ahí cada límite. El seguimiento no sale de esa zona, acompaña a la gente sobre la ruta de la calle y al terminar regresa al reposo.</p>
	<div class="zone-grid">
		<div class="zone-live">
			<div class="stage zone-stage" data-zone-stage><video autoplay playsinline muted class="${camera.rotation === 180 ? 'rot' : ''}"></video><div class="osd tl"><span class="badge live"><i class="dot pulse"></i>EN VIVO</span></div><div class="stage-note" data-zone-note><b>Conectando con la cámara…</b></div><div class="pad zone-pad" role="group" aria-label="Mover la cámara">${PAD}</div></div>
			<p class="muted-s">Mueve con el control, con las flechas del teclado o tocando el mapa.</p>
		</div>
		<div class="zone-side">
			${zoneMapHtml()}
			<ul class="zm-legend"><li><i class="k zone"></i>Hasta dónde apunta</li><li><i class="k seen"></i>Lo que alcanza a verse</li><li><i class="k now"></i>Lo que ve ahora</li><li><i class="k home"></i>Reposo</li><li><i class="k route"></i>Ruta de la calle</li></ul>
			<div class="zone-set" role="group" aria-label="Fijar con la posición actual">
				${SETS.map(([key, label, icon]) => `<button class="btn small" type="button" data-set="${key}">${I[icon]}<span>${label} aquí</span></button>`).join('')}
				<button class="btn small" type="button" data-set="home">${I.home}<span>Reposo aquí</span></button>
			</div>
			<div class="zone-set" role="group" aria-label="Ruta de la calle con la posición actual">
				<button class="btn small" type="button" data-set="left">${I.round}<span>Ruta: extremo izquierdo aquí</span></button>
				<button class="btn small" type="button" data-set="mid">${I.round}<span>Ruta: punto más cercano aquí</span></button>
				<button class="btn small" type="button" data-set="right">${I.round}<span>Ruta: extremo derecho aquí</span></button>
			</div>
			<p class="muted-s">La ruta es la curva por la que la cámara acompaña a quien camina: apunta a donde pisa la gente en cada extremo y en el punto más cercano, o arrastra los tres puntos blancos del mapa.</p>
			<p class="muted-s mono" data-zone-read></p>
			<div class="survey" data-survey hidden><div class="disk-bar"><i class="v" data-survey-bar style="width:0"></i></div><span class="muted-s" data-survey-text></span><button class="btn small ghost" type="button" data-survey-stop>Cancelar</button></div>
			<div class="form-row zone-actions">
				<button class="btn primary" type="button" data-zone-save>Guardar zona</button>
				<button class="btn" type="button" data-zone-home>${I.home}<span>Ir al reposo</span></button>
				<button class="btn" type="button" data-survey-start title="Toma una foto de cada dirección para dibujar el mapa. Tarda unos 3 minutos.">Medir cobertura</button>
				<button class="btn ghost" type="button" data-zone-clear>Quitar límites</button>
			</div>
		</div>
	</div>
</section>`

export const showZoneSummary = async (root, id) => {
	const box = root.querySelector(`[data-zone-sum="${id}"] [data-zone-text]`)
	if (!box) return
	try {
		box.textContent = zoneText((await api.zone(id)).zone)
	} catch {
		box.textContent = 'No se pudo leer la zona.'
	}
}

export const mountZoneEditor = (root, camera, onClose) => {
	const el = root.querySelector(`[data-zone-ed="${camera.id}"]`)
	const chip = el.querySelector('[data-zone-chip]')
	const read = el.querySelector('[data-zone-read]')
	const note = el.querySelector('[data-zone-note]')
	const surveyBox = el.querySelector('[data-survey]')
	let alive = true
	let dirty = false
	let position = null
	let busy = false
	let saved = null
	const map = mountZoneMap(el, {
		cameraId: camera.id,
		editable: true,
		onChange: () => {
			dirty = true
			describe()
		},
		onGo: (x, y) => go(x, y),
	})
	const stopLive = watchLive(camera.id, el.querySelector('video'), s => {
		note.hidden = s === 'live'
		note.innerHTML = s === 'reconnecting' ? '<b>Sin imagen</b><span>Reintentando la conexión…</span>' : '<b>Conectando con la cámara…</b>'
	}, { stream: 'minor' })

	const describe = () => {
		const z = map.zone
		read.textContent = z ? `giro ${z.minX.toFixed(2)} a ${z.maxX.toFixed(2)} · inclinación ${z.minY.toFixed(2)} a ${z.maxY.toFixed(2)} · reposo ${z.homeX.toFixed(2)}, ${z.homeY.toFixed(2)}` : 'Sin límites'
		el.querySelector('[data-zone-save]').disabled = !dirty || !z
		el.querySelector('[data-zone-clear]').hidden = !saved
		el.querySelector('[data-zone-home]').disabled = !z
	}

	const refresh = async first => {
		let data
		try {
			data = await api.zone(camera.id)
		} catch {
			return
		}
		if (!alive) return
		position = data.position
		busy = data.busy
		saved = data.zone
		if (first || (!dirty && JSON.stringify(map.zone) !== JSON.stringify(data.zone ?? map.zone))) map.setZone(data.zone ?? defaultZone(position))
		const draft = map.zone
		const inside = !position || !draft || (position.x >= draft.minX - 0.01 && position.x <= draft.maxX + 0.01 && position.y >= draft.minY - 0.01 && position.y <= draft.maxY + 0.01)
		map.show({ ...data, inside })
		chip.className = `chip ${busy ? '' : inside ? 'good' : 'warn'}`
		chip.innerHTML = `<i class="dot"></i>${busy ? 'Midiendo…' : inside ? 'Dentro de la zona' : 'Fuera de la zona'}`
		const s = data.survey
		surveyBox.hidden = !s?.running
		if (s?.running) {
			el.querySelector('[data-survey-bar]').style.width = `${(s.done / Math.max(1, s.total)) * 100}%`
			el.querySelector('[data-survey-text]').textContent = `Midiendo cobertura: ${s.done} de ${s.total} direcciones`
		}
		el.querySelector('[data-survey-start]').disabled = busy
		for (const b of el.querySelectorAll('[data-set], [data-step]')) b.disabled = busy
		describe()
	}

	const go = async (x, y) => {
		if (busy) return toast('La cámara está midiendo; espera a que termine')
		try {
			await api.goTo(camera.id, x, y)
		} catch (err) {
			toast(err.message.startsWith('http') ? 'La cámara no respondió al movimiento' : err.message)
		}
	}

	let held = null
	const hold = async dir => {
		held = dir
		while (held === dir && alive) {
			try {
				await api.move(camera.id, dir[0] * 0.06, dir[1] * 0.06)
			} catch {
				toast('La cámara no respondió al movimiento')
				break
			}
			if (held !== dir) break
			await new Promise(r => setTimeout(r, 120))
		}
		if (held === dir) held = null
	}
	const release = () => {
		held = null
		el.querySelectorAll('[data-step].held').forEach(b => b.classList.remove('held'))
	}
	const KEYS = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, 1], ArrowDown: [0, -1] }
	const onKey = e => {
		const dir = KEYS[e.key]
		if (!dir || busy || e.target.closest('input, textarea, [role="slider"], [role="radio"], [data-zm-home]')) return
		e.preventDefault()
		if (!e.repeat) hold(dir)
	}
	const onKeyUp = e => KEYS[e.key] && release()
	const onDown = e => {
		const b = e.target.closest('[data-step]')
		if (!b || e.button !== 0 || busy) return
		e.preventDefault()
		b.classList.add('held')
		hold(b.dataset.step.split(',').map(Number))
	}

	const onClick = async e => {
		const set = e.target.closest('[data-set]')
		if (set) {
			if (!position) return toast('Aún no se sabe hacia dónde apunta la cámara')
			if (!map.zone) map.setZone(defaultZone(position))
			const key = set.dataset.set
			map.setEdge(key, ['home', 'left', 'mid', 'right'].includes(key) ? { x: position.x, y: position.y } : key.endsWith('X') ? position.x : position.y)
			return
		}
		const step = e.target.closest('[data-step]')
		if (step && e.detail === 0 && !busy) return api.move(camera.id, ...step.dataset.step.split(',').map(n => Number(n) * 0.06)).catch(() => toast('La cámara no respondió al movimiento'))
		if (e.target.closest('[data-zone-save]')) {
			try {
				saved = await api.saveZone(camera.id, map.zone)
				dirty = false
				toast('Zona guardada: el seguimiento se queda dentro')
				describe()
			} catch (err) {
				toast(err.message)
			}
			return
		}
		if (e.target.closest('[data-zone-home]') && map.zone) return go(map.zone.homeX, map.zone.homeY)
		const clear = e.target.closest('[data-zone-clear]')
		if (clear && confirmOnce(clear, 'Sí, quitar los límites')) {
			await api.clearZone(camera.id).catch(err => toast(err.message))
			dirty = false
			saved = null
			map.setZone(defaultZone(position))
			toast('Sin límites: la cámara usa todo su recorrido')
			return describe()
		}
		if (e.target.closest('[data-survey-start]')) {
			try {
				await api.startSurvey(camera.id)
				toast('Midiendo: la cámara va a recorrer todo y regresar')
				refresh()
			} catch (err) {
				toast(err.message)
			}
			return
		}
		if (e.target.closest('[data-survey-stop]')) return api.stopSurvey(camera.id).catch(() => {})
		if (e.target.closest('[data-zone-close]')) onClose()
	}

	el.addEventListener('click', onClick)
	el.addEventListener('pointerdown', onDown)
	document.addEventListener('keydown', onKey)
	document.addEventListener('keyup', onKeyUp)
	window.addEventListener('pointerup', release)
	window.addEventListener('blur', release)
	refresh(true)
	const poll = setInterval(refresh, 800)
	return () => {
		alive = false
		held = null
		clearInterval(poll)
		stopLive()
		map.destroy()
		el.removeEventListener('click', onClick)
		el.removeEventListener('pointerdown', onDown)
		document.removeEventListener('keydown', onKey)
		document.removeEventListener('keyup', onKeyUp)
		window.removeEventListener('pointerup', release)
		window.removeEventListener('blur', release)
	}
}

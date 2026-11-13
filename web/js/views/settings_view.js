import { api } from '../services/api.js'
import { state } from '../store.js'
import { bytes, esc, HERE, I, segment, THERE, toast, toggle } from '../ui.js'

const row = (title, hint, control) => `<div class="set-row"><div class="set-text"><b>${title}</b><small>${hint}</small></div><div class="set-ctl">${control}</div></div>`
const stepper = (id, value, unit) => `<div class="stepper" data-stepper="${id}"><button type="button" data-step="-1" aria-label="Menos">−</button><span class="mono"><b data-val>${value}</b> ${unit}</span><button type="button" data-step="1" aria-label="Más">+</button></div>`

const place = (s, path, title, sub) => `<button class="vol ${((s.recordingsDir ?? s.defaultDir) === path ? 'on' : '')}" type="button" data-dir="${esc(path)}"><span class="ev-ico ${(s.recordingsDir ?? s.defaultDir) === path ? 'span' : ''}">${I.disk}</span><span><b>${esc(title)}</b><small>${sub}</small></span></button>`

const when = ms => new Date(ms).toLocaleString('es-MX', { weekday: 'long', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', hour12: false }).replace('.', '')
const days = d => (d >= 10 ? Math.floor(d) : Math.floor(d * 10) / 10).toLocaleString('es-MX')

const outlookHtml = s => {
	const o = s.outlook
	if (!o) return ''
	const facts = [
		o.bytesPerDay ? `Se graban unos <b class="mono">${bytes(o.bytesPerDay)}</b> al día (medido en lo último grabado).` : 'Todavía no hay grabación suficiente para medir cuánto ocupa un día.',
		o.daysThatFit != null ? (o.limitedBy === 'disk' ? `El disco alcanza para <b class="mono">${days(o.daysThatFit)} días</b>, menos de los ${s.retentionDays} que pediste.` : `Con estos límites se guardan <b class="mono">${s.retentionDays} días</b>.`) : '',
		o.oldestMs ? `Hay grabación desde el <b>${when(o.oldestMs)}</b> (<span class="mono">${bytes(o.recordedBytes)}</span> en total).` : '',
		o.nextDeletionMs ? `Lo más viejo se empieza a borrar el <b>${when(o.nextDeletionMs)}</b>.` : '',
		'Velador nunca deja de grabar por falta de espacio: borra lo más viejo primero y nunca los recortes.',
	].filter(Boolean)
	return `<div class="outlook">${I.disk}<div>${facts.map(f => `<p>${f}</p>`).join('')}</div></div>`
}

const body = s => {
	const d = s.disk
	const used = d ? ((d.totalBytes - d.freeBytes) / d.totalBytes) * 100 : 0
	const external = s.recordingsDir && s.recordingsDir !== s.defaultDir
	return `
	${s.onFallback ? `<div class="banner info">${I.alert}<span>${s.released ? `El disco externo está liberado: ya puedes expulsarlo. Mientras, Velador graba en ${HERE}.` : `El disco externo no está conectado. Velador graba en ${HERE} y pasará todo al disco cuando vuelva.`}</span><span class="spacer"></span>${s.released ? '<button class="btn small" type="button" data-reclaim>Volver a usarlo</button>' : ''}</div>` : ''}
	<section class="card"><header class="card-head"><h2>${I.disk}Dónde se guarda</h2></header>
		<div class="vol-list">
			${place(s, s.defaultDir, `En ${HERE}`, `<span class="mono">${esc(s.defaultDir)}</span>`)}
			${s.volumes.map(v => place(s, v.suggested, `Disco externo «${esc(v.name)}»`, `${bytes(v.freeBytes)} libres de ${bytes(v.totalBytes)} · <span class="mono">${esc(v.suggested)}</span>`)).join('')}
			${external && !s.volumes.some(v => v.suggested === s.recordingsDir) ? place(s, s.recordingsDir, 'Carpeta elegida', `<span class="mono">${esc(s.recordingsDir)}</span>${s.primaryAvailable ? '' : ' · no está conectada'}`) : ''}
		</div>
		${s.volumes.length ? '' : '<p class="muted-s">Conecta un disco externo y aparecerá aquí para elegirlo.</p>'}
		<form class="form-row" data-custom style="margin-top:10px"><label class="field" style="flex:1;margin:0"><span>Otra carpeta (ruta completa)</span><input name="dir" type="text" placeholder="/Volumes/MiDisco/Velador" spellcheck="false" autocomplete="off" /></label><button class="btn" type="submit">Usar esta</button></form>
		<p class="form-error" data-error role="alert"></p>
		${external ? `<div class="set-row"><div class="set-text"><b>Expulsar el disco sin perder nada</b><small>Velador pasa a grabar en ${HERE} durante 10 minutos para que puedas desconectarlo. Al volver a conectarlo, lo de ${THERE} se pasa al disco.</small></div><div class="set-ctl"><button class="btn small" type="button" data-release ${s.onFallback ? 'disabled' : ''}>Preparar para expulsar</button></div></div>` : ''}
		${row('Guardar hasta', 'Lo más viejo se borra primero. Los recortes nunca se borran.', stepper('retentionDays', s.retentionDays, 'días'))}
		${row('Sin pasar de', 'Del disco donde se graba, contando lo que ya tiene.', stepper('retentionPercent', s.retentionPercent, '%'))}
		${outlookHtml(s)}
		${d ? `<div class="disk wide"><div class="disk-bar"><i class="o" style="width:${used}%"></i><i class="limit" style="left:${s.retentionPercent}%"></i></div>
		<p class="disk-l"><span><i class="sw o"></i>Ocupado <b class="mono">${bytes(d.totalBytes - d.freeBytes)}</b></span><span><i class="sw f"></i>Libre <b class="mono">${bytes(d.freeBytes)}</b></span><span>Grabando en <b class="mono">${esc(s.activeDir)}</b></span></p></div>` : ''}
	</section>
	<section class="card"><header class="card-head"><h2>${I.shield}Grabación sin huecos</h2></header>
		${row('Mantener el equipo despierto', 'Mientras Velador esté de guardia. La pantalla sí se apaga. Con la tapa cerrada o sin batería el equipo se duerme igual.', toggle('keepAwake', s.keepAwake, 'Mantener el equipo despierto'))}
	</section>
	<section class="card"><header class="card-head"><h2>${I.settings}Apariencia</h2></header>
		${row('Tema', 'El oscuro cansa menos junto al video.', segment('theme', [['dark', 'Oscuro'], ['light', 'Claro']], state.theme, 'Tema'))}
		${row('Cámaras', 'Conexión, cuenta de cámara y giro de la imagen.', `<a class="btn small" href="#/cameras">Abrir ${I.arrow}</a>`)}
	</section>`
}

const html = () => `<div class="settings" data-root><p class="empty-s">Cargando…</p></div>`

const mount = main => {
	const root = main.querySelector('[data-root]')
	let s = null
	const draw = () => (root.innerHTML = body(s))
	const save = async patch => {
		try {
			s = await api.saveSettings({ recordingsDir: s.recordingsDir, retentionDays: s.retentionDays, retentionPercent: s.retentionPercent, keepAwake: s.keepAwake, ...patch })
			draw()
			return true
		} catch (err) {
			draw()
			root.querySelector('[data-error]').textContent = err.message
			return false
		}
	}
	const refresh = async () => {
		s = await api.settings()
		draw()
	}
	const onClick = async e => {
		const dir = e.target.closest('[data-dir]')
		if (dir && !dir.classList.contains('on')) {
			const path = dir.dataset.dir
			if (await save({ recordingsDir: path === s.defaultDir ? null : path })) toast('Listo: las grabaciones nuevas van ahí')
		}
		const step = e.target.closest('[data-step]')
		if (step) {
			const id = step.closest('[data-stepper]').dataset.stepper
			const [min, max, by] = id === 'retentionDays' ? [1, 365, 1] : [10, 98, 5]
			await save({ [id]: Math.min(max, Math.max(min, s[id] + Number(step.dataset.step) * by)) })
		}
		if (e.target.closest('[data-release]')) {
			await api.releaseDisk()
			toast('En unos segundos podrás expulsar el disco')
			setTimeout(refresh, 4000)
		}
		if (e.target.closest('[data-reclaim]')) {
			await api.reclaimDisk()
			setTimeout(refresh, 4000)
		}
	}
	const onSubmit = async e => {
		if (!e.target.closest('[data-custom]')) return
		e.preventDefault()
		const dir = new FormData(e.target).get('dir').trim()
		if (dir && (await save({ recordingsDir: dir }))) toast('Listo: las grabaciones nuevas van ahí')
	}
	const onSwitch = e => {
		if (e.detail.id === 'keepAwake') save({ keepAwake: e.detail.on })
	}
	const onSeg = e => {
		if (e.detail.id === 'theme' && e.detail.value !== state.theme) document.querySelector('[data-theme-toggle]').click()
	}
	main.addEventListener('click', onClick)
	main.addEventListener('submit', onSubmit)
	main.addEventListener('switch', onSwitch)
	main.addEventListener('segment', onSeg)
	refresh().catch(() => (root.innerHTML = '<p class="empty-s">No se pudieron leer los ajustes.</p>'))
	const poll = setInterval(() => !root.contains(document.activeElement) && refresh().catch(() => {}), 10000)
	return () => {
		clearInterval(poll)
		main.removeEventListener('click', onClick)
		main.removeEventListener('submit', onSubmit)
		main.removeEventListener('switch', onSwitch)
		main.removeEventListener('segment', onSeg)
	}
}

export const settingsView = { title: 'Ajustes', html, mount }

import { api } from '../services/api.js'
import { loadSummary, state } from '../store.js'
import { confirmOnce, esc, I, toast } from '../ui.js'
import { bindCameraForm, cameraFormHtml } from './camera_form.js'

const STATE = { on_guard: ['good', 'De guardia'], reconnecting: ['warn', 'Reconectando'], gap: ['warn', 'Sin grabar'] }

const card = (c, live) => {
	const [cls, label] = STATE[live?.state] ?? ['', 'Sin datos']
	return `<article class="card cam" data-cam="${esc(c.id)}">
		<div class="thumb cam-thumb"><video muted playsinline preload="metadata" class="${c.rotation === 180 ? 'rot' : ''}" src="${api.mediaUrl(c.id, Date.now() - 20000, 2000, 'minor')}#t=0.1"></video></div>
		<div class="cam-body">
			<header class="cam-head"><div><h2>${esc(c.name)}</h2><p class="muted-s mono">${esc(c.host)}</p></div><span class="chip ${cls}"><i class="dot"></i>${label}</span></header>
			<dl class="kv"><div><dt>Cuenta de cámara</dt><dd class="secret">${I.lock}<span>${esc(c.user)} · contraseña en el llavero</span></dd></div><div><dt>Imagen</dt><dd>${c.rotation === 180 ? 'Girada 180°' : 'Sin girar'}</dd></div></dl>
			<div class="nets"><div class="form-row"><b>Redes conocidas</b><span class="muted-s">${c.networks.length} de 3 · ${c.followsAddress ? 'se reconecta sola aunque cambie su dirección' : 'aún no se identifica la cámara para seguirla'}</span></div>
				${c.networks.map(n => `<div class="net ${n.current ? 'on' : ''}"><span class="ev-ico ${n.current ? 'span' : ''}">${I.wifi}</span><span class="ev-main"><b>${esc(n.name)}</b><small class="mono">${esc(n.host)}${n.current ? ' · conectado ahora' : ''}</small></span>${n.current ? '' : `<button class="btn small" type="button" data-use="${esc(n.id)}" data-of="${esc(c.id)}">Usar</button>`}<button class="icon-btn danger" type="button" data-forget="${esc(n.id)}" data-of="${esc(c.id)}" title="Olvidar esta red">${I.trash}<span class="sr-only">Olvidar esta red</span></button></div>`).join('') || '<p class="muted-s">Se llenan solas: cada red donde Velador vea esta cámara queda guardada.</p>'}
			</div>
			<ul class="results" data-results hidden></ul>
			<div class="cam-actions"><button class="btn" type="button" data-test="${esc(c.id)}">Probar conexión</button><button class="btn" type="button" data-edit="${esc(c.id)}">Editar</button><button class="btn ghost" type="button" data-remove="${esc(c.id)}">${I.trash}<span>Quitar</span></button></div>
		</div>
	</article>`
}

const html = () => `<div class="cameras" data-root><p class="empty-s">Cargando…</p></div>`

const mount = main => {
	const root = main.querySelector('[data-root]')
	let unbind = () => {}
	let cameras = []
	const draw = async (editing = null) => {
		unbind()
		cameras = await api.cameras()
		await loadSummary().catch(() => {})
		if (editing !== null) {
			const camera = cameras.find(c => c.id === editing) ?? null
			root.innerHTML = `<section class="card"><header class="card-head"><h2>${camera ? `Editar «${esc(camera.name)}»` : 'Agregar una cámara'}</h2></header>${cameraFormHtml(camera)}${camera ? '' : '<div class="form-row" style="margin-top:10px"><button class="btn ghost" type="button" data-back>Volver</button></div>'}</section>`
			unbind = bindCameraForm(root, {
				onSaved: () => {
					toast('Cámara guardada')
					draw()
				},
				onCancel: () => draw(),
			})
			return
		}
		root.innerHTML =
			cameras.map(c => card(c, state.summary?.cameras.find(x => x.id === c.id))).join('') +
			`<button class="add-cam" type="button" data-add><span class="add-ico">${I.plus}</span><b>Agregar otra cámara</b><span>Cualquier cámara con RTSP y ONVIF. Velador la busca en tu red.</span></button>`
	}
	const onClick = async e => {
		if (e.target.closest('[data-add]')) return draw('')
		if (e.target.closest('[data-back]')) return draw()
		const edit = e.target.closest('[data-edit]')
		if (edit) return draw(edit.dataset.edit)
		const test = e.target.closest('[data-test]')
		if (test && !test.closest('form')) {
			const c = cameras.find(x => x.id === test.dataset.test)
			const list = test.closest('.cam').querySelector('[data-results]')
			test.disabled = true
			try {
				const r = await api.testCamera({ host: c.host, user: c.user, password: null, id: c.id })
				list.hidden = false
				list.innerHTML = `<li><span class="${r.video ? 'ok' : 'no'}">${r.video ? I.check : I.close}</span><span>${r.video ? 'Responde en la red' : 'No responde en esa dirección'}</span></li><li><span class="${r.account ? 'ok' : 'no'}">${r.account ? I.check : I.close}</span><span>${r.account ? `La cuenta entra${r.model ? ` · ${esc(r.model)}` : ''}` : 'La cuenta fue rechazada'}</span></li>`
			} catch (err) {
				toast(err.message)
			}
			test.disabled = false
		}
		const use = e.target.closest('[data-use]')
		if (use) {
			await api.useNetwork(use.dataset.of, use.dataset.use).catch(err => toast(err.message))
			toast('Cambiando a la dirección de esa red…')
			setTimeout(() => draw(), 3000)
		}
		const forget = e.target.closest('[data-forget]')
		if (forget && confirmOnce(forget, 'Olvidar')) {
			await api.forgetNetwork(forget.dataset.of, forget.dataset.forget).catch(err => toast(err.message))
			draw()
		}
		const remove = e.target.closest('[data-remove]')
		if (remove && confirmOnce(remove, 'Quitar y dejar de grabarla')) {
			await api.removeCamera(remove.dataset.remove).catch(err => toast(err.message))
			await loadSummary().catch(() => {})
			if (!state.camera) location.reload()
			else draw()
		}
	}
	main.addEventListener('click', onClick)
	draw().catch(() => (root.innerHTML = '<p class="empty-s">No se pudieron leer las cámaras.</p>'))
	return () => {
		unbind()
		main.removeEventListener('click', onClick)
	}
}

export const camerasView = { title: 'Cámaras', html, mount }

import { api } from '../services/api.js'
import { esc, I, toggle } from '../ui.js'

const slug = name =>
	name
		.normalize('NFD')
		.replace(/[^a-zA-Z0-9]/g, '')
		.toLowerCase()
		.slice(0, 24) || 'camara'

export const cameraFormHtml = (camera = null) => `
	<form class="form" data-camera-form data-id="${camera?.id ?? ''}" novalidate>
		${camera ? '' : `<div><div class="form-row"><b>Cámaras en tu red</b><button class="btn small" type="button" data-discover>Buscar otra vez</button></div><div class="found" data-found><p class="muted-s">Buscando…</p></div></div>`}
		<label class="field"><span>Nombre</span><input name="name" type="text" value="${esc(camera?.name ?? '')}" placeholder="Entrada" autocomplete="off" required /></label>
		<label class="field"><span>Dirección de la cámara</span><input name="host" type="text" value="${esc(camera?.host ?? '')}" placeholder="192.168.1.20" autocomplete="off" spellcheck="false" required /></label>
		<label class="field"><span>Usuario de la cuenta de cámara</span><input name="user" type="text" value="${esc(camera?.user ?? '')}" autocomplete="off" spellcheck="false" required /></label>
		<label class="field"><span>Contraseña${camera ? ' (déjala vacía para no cambiarla)' : ''}</span><input name="password" type="password" autocomplete="new-password" /></label>
		<div class="set-row"><div class="set-text"><b>Girar la imagen 180°</b><small>Si la cámara está montada de cabeza.</small></div>${toggle('rotation', camera?.rotation === 180, 'Girar la imagen')}</div>
		<p class="note">${I.lock}<span>La contraseña se guarda en el llavero de tu sistema, nunca en un archivo. En la app Tapo se crea en Ajustes de la cámara › Avanzado › Cuenta de cámara.</span></p>
		<ul class="results" data-results hidden></ul>
		<p class="form-error" data-error role="alert"></p>
		<div class="form-row"><button class="btn" type="button" data-test>Probar conexión</button><span class="spacer"></span>${camera ? '<button class="btn ghost" type="button" data-cancel>Cancelar</button>' : ''}<button class="btn primary" type="submit">${camera ? 'Guardar cambios' : 'Empezar la guardia'}</button></div>
	</form>`

const result = (ok, text) => `<li><span class="${ok ? 'ok' : 'no'}">${ok ? I.check : I.close}</span><span>${esc(text)}</span></li>`

export const bindCameraForm = (root, { onSaved, onCancel }) => {
	const form = root.querySelector('[data-camera-form]')
	if (!form) return () => {}
	const error = form.querySelector('[data-error]')
	const results = form.querySelector('[data-results]')
	let rotation = form.querySelector('[data-switch="rotation"]').getAttribute('aria-checked') === 'true'
	const values = () => Object.fromEntries(new FormData(form))

	const discover = async () => {
		const box = form.querySelector('[data-found]')
		if (!box) return
		box.innerHTML = '<p class="muted-s">Buscando…</p>'
		try {
			const found = await api.discover()
			box.innerHTML = found.length
				? found.map(f => `<button class="found-row" type="button" data-host="${esc(f.host)}"><span class="ev-ico span">${I.camera}</span><span class="ev-main"><b class="mono">${esc(f.host)}</b><small>${f.known ? 'Ya está en Velador' : 'Responde por ONVIF'}</small></span></button>`).join('')
				: '<p class="muted-s">No apareció ninguna. Escribe su dirección abajo; la ves en la app de la cámara o en tu router.</p>'
		} catch {
			box.innerHTML = '<p class="muted-s">No se pudo buscar. Escribe la dirección abajo.</p>'
		}
	}

	const onClick = async e => {
		const host = e.target.closest('[data-host]')
		if (host) {
			form.elements.host.value = host.dataset.host
			for (const b of form.querySelectorAll('[data-host]')) b.classList.toggle('on', b === host)
			form.elements.user.focus()
		}
		if (e.target.closest('[data-discover]')) discover()
		if (e.target.closest('[data-cancel]')) onCancel?.()
		const test = e.target.closest('[data-test]')
		if (test) {
			const v = values()
			test.disabled = true
			test.textContent = 'Probando…'
			error.textContent = ''
			try {
				const r = await api.testCamera({ host: v.host, user: v.user, password: v.password || null, id: form.dataset.id || null })
				results.hidden = false
				results.innerHTML =
					result(r.video, r.video ? 'La cámara responde en la red' : 'La cámara no responde en esa dirección') +
					result(r.account, r.account ? `La cuenta entra${r.model ? ` · ${r.model}` : ''}` : 'La cuenta fue rechazada o la cámara no tiene ONVIF activo')
			} catch (err) {
				error.textContent = err.message
			}
			test.disabled = false
			test.textContent = 'Probar conexión'
		}
	}
	const onSwitch = e => {
		if (e.detail.id === 'rotation') rotation = e.detail.on
	}
	const onSubmit = async e => {
		e.preventDefault()
		const v = values()
		error.textContent = ''
		const submit = form.querySelector('[type="submit"]')
		submit.disabled = true
		try {
			const id = form.dataset.id || slug(v.name)
			await api.saveCamera(id, { name: v.name, host: v.host, user: v.user, password: v.password || null, rotation: rotation ? 180 : 0 })
			onSaved?.(id)
		} catch (err) {
			error.textContent = err.message
			submit.disabled = false
		}
	}
	form.addEventListener('click', onClick)
	form.addEventListener('switch', onSwitch)
	form.addEventListener('submit', onSubmit)
	discover()
	return () => {
		form.removeEventListener('click', onClick)
		form.removeEventListener('switch', onSwitch)
		form.removeEventListener('submit', onSubmit)
	}
}

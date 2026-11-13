const send = async (method, path, body) => {
	const response = await fetch(`/api${path}`, {
		method,
		credentials: 'same-origin',
		headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
		body: body === undefined ? undefined : JSON.stringify(body),
	})
	if (response.status === 401) throw new Error('unauthorized')
	const data = response.status === 204 ? null : await response.json().catch(() => null)
	if (!response.ok) throw new Error(data?.error ?? `http ${response.status}`)
	return data
}

export const zone = Intl.DateTimeFormat().resolvedOptions().timeZone

export const api = {
	summary: () => send('GET', '/summary'),
	round: (cameraId, date) => send('GET', `/cameras/${cameraId}/rounds/${date}?tz=${encodeURIComponent(zone)}`),
	recording: (cameraId, paused, minutes) => send('POST', `/cameras/${cameraId}/recording`, { paused, minutes }),
	move: (cameraId, x, y) => send('POST', `/cameras/${cameraId}/ptz`, { x, y }),
	mediaUrl: (cameraId, startMs, durationMs, stream = 'main') => `/api/cameras/${cameraId}/media?startMs=${Math.round(startMs)}&durationMs=${Math.round(durationMs)}&stream=${stream}`,
	settings: () => send('GET', '/settings'),
	saveSettings: body => send('PUT', '/settings', body),
	releaseDisk: () => send('POST', '/storage/release'),
	reclaimDisk: () => send('POST', '/storage/reclaim'),
	cameras: () => send('GET', '/cameras'),
	discover: () => send('GET', '/cameras/discover'),
	testCamera: body => send('POST', '/cameras/test', body),
	saveCamera: (id, body) => send('PUT', `/cameras/${id}`, body),
	removeCamera: id => send('DELETE', `/cameras/${id}`),
	useNetwork: (id, networkId) => send('POST', `/cameras/${id}/networks/${encodeURIComponent(networkId)}/use`),
	forgetNetwork: (id, networkId) => send('DELETE', `/cameras/${id}/networks/${encodeURIComponent(networkId)}`),
	extras: id => send('GET', `/cameras/${id}/extras`),
	light: (id, on) => send('POST', `/cameras/${id}/light`, { on }),
	night: (id, mode) => send('PUT', `/cameras/${id}/night`, { mode }),
	flip: (id, on) => send('PUT', `/cameras/${id}/flip`, { on }),
	wifi: id => send('GET', `/cameras/${id}/wifi`),
	joinWifi: (id, ssid, password) => send('POST', `/cameras/${id}/wifi`, { ssid, password }),
	clips: () => send('GET', '/clips'),
	addClip: (cameraId, body) => send('POST', `/cameras/${cameraId}/clips`, body),
	removeClip: id => send('DELETE', `/clips/${id}`),
	revealClip: id => send('POST', `/clips/${id}/reveal`),
	clipUrl: id => `/api/clips/${id}/file`,
	vision: cameraId => send('GET', `/cameras/${cameraId}/vision`),
	follow: (cameraId, follow) => send('PUT', `/cameras/${cameraId}/vision`, { follow }),
	calibrate: cameraId => send('POST', `/cameras/${cameraId}/vision/calibrate`),
	people: (cameraId, date) => send('GET', `/cameras/${cameraId}/people?date=${date}`),
	sightingUrl: id => `/api/sightings/${id}/image`,
	faceUrl: id => `/api/sightings/${id}/face`,
	forgetPerson: id => send('DELETE', `/people/${id}`),
}

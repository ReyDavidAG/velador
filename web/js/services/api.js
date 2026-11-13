const get = async path => {
	const response = await fetch(`/api${path}`, { credentials: 'same-origin' })
	if (response.status === 401) throw new Error('unauthorized')
	if (!response.ok) throw new Error(`http ${response.status}`)
	return response.json()
}

export const zone = Intl.DateTimeFormat().resolvedOptions().timeZone

export const api = {
	summary: () => get('/summary'),
	round: (cameraId, date) => get(`/cameras/${cameraId}/rounds/${date}?tz=${encodeURIComponent(zone)}`),
	move: async (cameraId, x, y) => {
		const response = await fetch(`/api/cameras/${cameraId}/ptz`, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ x, y }) })
		if (!response.ok) throw new Error(`http ${response.status}`)
	},
	mediaUrl: (cameraId, startMs, durationMs) => `/api/cameras/${cameraId}/media?startMs=${Math.round(startMs)}&durationMs=${Math.round(durationMs)}`,
}

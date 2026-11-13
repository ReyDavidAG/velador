const gathered = pc =>
	new Promise(resolve => {
		if (pc.iceGatheringState === 'complete') return resolve()
		const done = () => pc.iceGatheringState === 'complete' && (pc.removeEventListener('icegatheringstatechange', done), resolve())
		pc.addEventListener('icegatheringstatechange', done)
		setTimeout(resolve, 2000)
	})

export const watchLive = (cameraId, video, onState) => {
	let pc = null
	let session = null
	let retry = 0
	let closed = false

	const stop = () => {
		if (session) fetch(session, { method: 'DELETE', credentials: 'same-origin' }).catch(() => {})
		session = null
		pc?.close()
		pc = null
	}

	const again = () => {
		if (closed) return
		stop()
		onState('reconnecting')
		clearTimeout(retry)
		retry = setTimeout(connect, 2000)
	}

	async function connect() {
		if (closed) return
		try {
			pc = new RTCPeerConnection()
			pc.addTransceiver('video', { direction: 'recvonly' })
			pc.addTransceiver('audio', { direction: 'recvonly' })
			pc.addEventListener('track', e => {
				if (video.srcObject !== e.streams[0]) video.srcObject = e.streams[0]
			})
			pc.addEventListener('connectionstatechange', () => {
				if (pc?.connectionState === 'connected') onState('live')
				if (pc && ['failed', 'disconnected', 'closed'].includes(pc.connectionState)) again()
			})
			await pc.setLocalDescription(await pc.createOffer())
			await gathered(pc)
			const response = await fetch(`/api/cameras/${cameraId}/live`, {
				method: 'POST',
				credentials: 'same-origin',
				headers: { 'Content-Type': 'application/sdp' },
				body: pc.localDescription.sdp,
			})
			if (!response.ok) throw new Error(`whep ${response.status}`)
			session = response.headers.get('Location')
			await pc.setRemoteDescription({ type: 'answer', sdp: await response.text() })
		} catch {
			again()
		}
	}

	onState('connecting')
	connect()
	return () => {
		closed = true
		clearTimeout(retry)
		stop()
		video.srcObject = null
	}
}

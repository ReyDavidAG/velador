const gathered = pc =>
	new Promise(resolve => {
		if (pc.iceGatheringState === 'complete') return resolve()
		const done = () => pc.iceGatheringState === 'complete' && (pc.removeEventListener('icegatheringstatechange', done), resolve())
		pc.addEventListener('icegatheringstatechange', done)
		setTimeout(resolve, 2000)
	})

const received = async pc => {
	let bytes = 0
	for (const report of (await pc.getStats()).values()) if (report.type === 'inbound-rtp') bytes += report.bytesReceived ?? 0
	return bytes
}

export const watchLive = (cameraId, video, onState) => {
	let pc = null
	let session = null
	let retry = 0
	let closed = false
	let lastBytes = -1
	let stalls = 0
	let hiddenAt = 0

	const stop = () => {
		if (session) fetch(session, { method: 'DELETE', credentials: 'same-origin' }).catch(() => {})
		session = null
		pc?.close()
		pc = null
		lastBytes = -1
		stalls = 0
	}

	const again = () => {
		if (closed) return
		stop()
		onState('reconnecting')
		clearTimeout(retry)
		retry = setTimeout(connect, 2000)
	}

	async function connect() {
		if (closed || pc || document.visibilityState === 'hidden') return
		const mine = new RTCPeerConnection()
		pc = mine
		try {
			mine.addTransceiver('video', { direction: 'recvonly' })
			mine.addTransceiver('audio', { direction: 'recvonly' })
			mine.addEventListener('track', e => {
				if (pc === mine && video.srcObject !== e.streams[0]) video.srcObject = e.streams[0]
			})
			mine.addEventListener('connectionstatechange', () => {
				if (pc !== mine) return
				if (mine.connectionState === 'connected') onState('live')
				if (['failed', 'disconnected', 'closed'].includes(mine.connectionState)) again()
			})
			await mine.setLocalDescription(await mine.createOffer())
			await gathered(mine)
			const response = await fetch(`/api/cameras/${cameraId}/live`, {
				method: 'POST',
				credentials: 'same-origin',
				headers: { 'Content-Type': 'application/sdp' },
				body: mine.localDescription.sdp,
			})
			if (!response.ok) throw new Error(`whep ${response.status}`)
			if (pc !== mine) return
			session = response.headers.get('Location')
			await mine.setRemoteDescription({ type: 'answer', sdp: await response.text() })
		} catch {
			if (pc === mine) again()
		}
	}

	const pulse = setInterval(async () => {
		if (closed || !pc || pc.connectionState !== 'connected') return
		const mine = pc
		const bytes = await received(mine).catch(() => -1)
		if (pc !== mine) return
		stalls = bytes > lastBytes ? 0 : stalls + 1
		lastBytes = bytes
		if (stalls >= 2) again()
	}, 3000)

	const onVisible = () => {
		if (closed) return
		if (document.visibilityState === 'hidden') {
			hiddenAt = Date.now()
			return
		}
		if (!pc) connect()
	}
	const idle = setInterval(() => {
		if (!closed && pc && document.visibilityState === 'hidden' && Date.now() - hiddenAt > 60000) {
			stop()
			onState('connecting')
		}
	}, 10000)
	document.addEventListener('visibilitychange', onVisible)

	onState('connecting')
	connect()
	return () => {
		closed = true
		clearTimeout(retry)
		clearInterval(pulse)
		clearInterval(idle)
		document.removeEventListener('visibilitychange', onVisible)
		stop()
		video.srcObject = null
	}
}

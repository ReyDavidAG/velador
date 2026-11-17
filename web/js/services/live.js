const gathered = pc =>
	new Promise(resolve => {
		if (pc.iceGatheringState === 'complete') return resolve()
		const done = () => pc.iceGatheringState === 'complete' && (pc.removeEventListener('icegatheringstatechange', done), resolve())
		pc.addEventListener('icegatheringstatechange', done)
		setTimeout(resolve, 2000)
	})

const received = async pc => {
	let bytes = 0
	let shape = null
	for (const report of (await pc.getStats()).values()) {
		if (report.type !== 'inbound-rtp') continue
		bytes += report.bytesReceived ?? 0
		if (report.kind === 'video') shape = { width: report.frameWidth ?? 0, height: report.frameHeight ?? 0, fps: report.framesPerSecond ?? 0, videoBytes: report.bytesReceived ?? 0 }
	}
	return { bytes, shape }
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms))

const settled = (buffer, act) =>
	new Promise((resolve, reject) => {
		buffer.addEventListener('updateend', resolve, { once: true })
		buffer.addEventListener('error', reject, { once: true })
		act()
	})

const watchHls = (cameraId, video, onState, stream, onStats) => {
	const base = `/api/cameras/${cameraId}/hls/${stream}/`
	let closed = false
	let run = 0
	let retry = 0
	let lastTime = -1
	let stalls = 0
	let url = null

	const get = async (path, as) => {
		const response = await fetch(base + path, { credentials: 'same-origin', cache: 'no-store' })
		if (!response.ok) throw new Error(`hls ${response.status}`)
		return response[as]()
	}

	const release = () => {
		video.removeAttribute('src')
		video.load()
		if (url) URL.revokeObjectURL(url)
		url = null
	}

	async function play(mine) {
		const index = await get('index.m3u8', 'text')
		const variant = index.split('\n').find(line => line && !line.startsWith('#'))
		const codecs = index.match(/CODECS="([^"]+)"/)?.[1] ?? 'avc1.640028'
		const source = new MediaSource()
		url = URL.createObjectURL(source)
		video.src = url
		await new Promise(resolve => source.addEventListener('sourceopen', resolve, { once: true }))
		const buffer = source.addSourceBuffer(`video/mp4; codecs="${codecs}"`)
		const done = new Set()
		let init = null
		while (mine === run) {
			const list = await get(variant, 'text')
			const map = list.match(/#EXT-X-MAP:URI="([^"]+)"/)?.[1]
			if (map && map !== init) {
				const data = await get(map, 'arrayBuffer')
				await settled(buffer, () => buffer.appendBuffer(data))
				init = map
			}
			const parts = [...list.matchAll(/#EXT-X-PART:([^\n]*)/g)].map(m => ({ uri: m[1].match(/URI="([^"]+)"/)?.[1], key: m[1].includes('INDEPENDENT=YES') }))
			let fresh = parts.filter(p => p.uri && !done.has(p.uri))
			if (!done.size) fresh = fresh.slice(Math.max(0, fresh.findLastIndex(p => p.key)))
			if (!done.size && !fresh[0]?.key) fresh = []
			for (const part of fresh) {
				const data = await get(part.uri, 'arrayBuffer')
				if (mine !== run) return
				await settled(buffer, () => buffer.appendBuffer(data))
				done.add(part.uri)
			}
			if (done.size > 200) for (const old of [...done].slice(0, done.size - 100)) done.delete(old)
			const end = buffer.buffered.length ? buffer.buffered.end(buffer.buffered.length - 1) : 0
			if (end && end - video.currentTime > 2) video.currentTime = end - 0.3
			if (end && video.paused) video.play().catch(() => {})
			const start = buffer.buffered.length ? buffer.buffered.start(0) : 0
			if (end - start > 30) await settled(buffer, () => buffer.remove(start, end - 10))
			await sleep(300)
		}
	}

	const begin = () => {
		if (closed) return
		const mine = ++run
		play(mine).catch(() => mine === run && again())
	}
	const again = () => {
		if (closed) return
		run++
		release()
		onState('reconnecting')
		clearTimeout(retry)
		retry = setTimeout(begin, 2000)
	}
	const playing = () => onState('live')
	video.addEventListener('playing', playing)
	const pulse = setInterval(() => {
		if (closed || video.paused) return
		onStats({ width: 0, height: video.videoHeight, fps: 0, kbps: 0 })
		stalls = video.currentTime > lastTime ? 0 : stalls + 1
		lastTime = video.currentTime
		if (stalls >= 3) {
			stalls = 0
			again()
		}
	}, 3000)

	onState('connecting')
	begin()
	return () => {
		closed = true
		run++
		clearTimeout(retry)
		clearInterval(pulse)
		video.removeEventListener('playing', playing)
		release()
	}
}

export const watchLive = (cameraId, video, onState, { stream = 'main', onStats = () => {} } = {}) => {
	if (typeof RTCPeerConnection === 'undefined') return watchHls(cameraId, video, onState, stream, onStats)
	let pc = null
	let session = null
	let retry = 0
	let closed = false
	let lastBytes = -1
	let stalls = 0
	let hiddenAt = 0
	let lastVideo = null

	const stop = () => {
		if (session) fetch(session, { method: 'DELETE', credentials: 'same-origin' }).catch(() => {})
		session = null
		pc?.close()
		pc = null
		lastBytes = -1
		stalls = 0
		lastVideo = null
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
			const response = await fetch(`/api/cameras/${cameraId}/live?stream=${stream}`, {
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
		const { bytes, shape } = await received(mine).catch(() => ({ bytes: -1, shape: null }))
		if (pc !== mine) return
		if (shape) {
			const now = performance.now()
			const kbps = lastVideo ? ((shape.videoBytes - lastVideo.bytes) * 8) / (now - lastVideo.at) : 0
			lastVideo = { bytes: shape.videoBytes, at: now }
			onStats({ width: shape.width, height: shape.height, fps: shape.fps, kbps })
		}
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

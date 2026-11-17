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

const settled = (buffer, act) =>
	new Promise((resolve, reject) => {
		buffer.addEventListener('updateend', resolve, { once: true })
		buffer.addEventListener('error', reject, { once: true })
		act()
	})

const boxAt = (bytes, at) => {
	if (bytes.length < at + 8) return null
	const size = ((bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3]) >>> 0
	return { size, type: String.fromCharCode(bytes[at + 4], bytes[at + 5], bytes[at + 6], bytes[at + 7]) }
}

const codecOf = init => {
	for (let i = 0; i + 8 < init.length; i++)
		if (init[i] === 0x61 && init[i + 1] === 0x76 && init[i + 2] === 0x63 && init[i + 3] === 0x43)
			return `avc1.${[init[i + 5], init[i + 6], init[i + 7]].map(b => b.toString(16).padStart(2, '0')).join('')}`
	return 'avc1.640028'
}

const EDGE = 0.2
const BEHIND = 0.8

const watchMse = (cameraId, video, onState, stream, onStats) => {
	let closed = false
	let run = 0
	let retry = 0
	let abort = null
	let url = null
	let bytes = 0
	let frames = 0
	let last = null
	let stalls = 0
	let hiddenAt = 0
	let behind = BEHIND
	let soughtAt = 0

	const release = () => {
		abort?.abort()
		abort = null
		video.removeAttribute('src')
		video.load()
		if (url) URL.revokeObjectURL(url)
		url = null
		last = null
		stalls = 0
	}

	const chase = buffer => {
		const ranges = buffer.buffered
		if (!ranges.length) return null
		const start = ranges.start(0)
		const end = ranges.end(ranges.length - 1)
		if (end - video.currentTime > behind || video.currentTime < start) {
			const now = performance.now()
			behind = now - soughtAt < 5000 ? Math.min(behind * 2, 6) : BEHIND
			soughtAt = now
			video.currentTime = Math.max(start, end - EDGE)
		}
		if (video.paused) video.play().catch(() => {})
		return end - start > 20 ? [start, end - 8] : null
	}

	async function play(mine) {
		const signal = (abort = new AbortController()).signal
		const response = await fetch(`/api/cameras/${cameraId}/live.mp4?stream=${stream}`, { credentials: 'same-origin', cache: 'no-store', signal })
		if (!response.ok || !response.body) throw new Error(`live ${response.status}`)
		const reader = response.body.getReader()
		let pending = new Uint8Array(0)
		let buffer = null
		for (;;) {
			const { done, value } = await reader.read()
			if (mine !== run) return
			if (done) throw new Error('live ended')
			bytes += value.length
			const joined = new Uint8Array(pending.length + value.length)
			joined.set(pending)
			joined.set(value, pending.length)
			let at = 0
			let ready = 0
			for (let box = boxAt(joined, at); box && box.size >= 8 && at + box.size <= joined.length; box = boxAt(joined, at)) {
				at += box.size
				if (box.type === 'moof') frames++
				if (box.type === 'moov' || box.type === 'mdat') ready = at
			}
			pending = joined.slice(ready)
			if (!ready) continue
			const chunk = joined.subarray(0, ready)
			if (!buffer) {
				const source = new MediaSource()
				url = URL.createObjectURL(source)
				video.src = url
				await new Promise(resolve => source.addEventListener('sourceopen', resolve, { once: true }))
				if (mine !== run) return
				buffer = source.addSourceBuffer(`video/mp4; codecs="${codecOf(chunk)}"`)
			}
			await settled(buffer, () => buffer.appendBuffer(chunk))
			if (mine !== run) return
			const old = chase(buffer)
			if (old) await settled(buffer, () => buffer.remove(old[0], old[1]))
		}
	}

	const begin = () => {
		if (closed || document.visibilityState === 'hidden') return
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
		if (closed || !abort) return
		const now = performance.now()
		if (last) {
			const seconds = (now - last.at) / 1000
			onStats({ width: video.videoWidth, height: video.videoHeight, fps: (frames - last.frames) / seconds, kbps: ((bytes - last.bytes) * 8) / (seconds * 1000) })
			stalls = bytes > last.bytes ? 0 : stalls + 1
		}
		last = { at: now, bytes, frames }
		if (stalls >= 2) again()
	}, 3000)

	const onVisible = () => {
		if (closed) return
		if (document.visibilityState === 'hidden') {
			hiddenAt = Date.now()
			return
		}
		if (!abort) begin()
	}
	const idle = setInterval(() => {
		if (!closed && abort && document.visibilityState === 'hidden' && Date.now() - hiddenAt > 60000) {
			run++
			release()
			onState('connecting')
		}
	}, 10000)
	document.addEventListener('visibilitychange', onVisible)

	onState('connecting')
	begin()
	return () => {
		closed = true
		run++
		clearTimeout(retry)
		clearInterval(pulse)
		clearInterval(idle)
		document.removeEventListener('visibilitychange', onVisible)
		video.removeEventListener('playing', playing)
		release()
	}
}

export const watchLive = (cameraId, video, onState, { stream = 'main', onStats = () => {} } = {}) => {
	if (typeof RTCPeerConnection === 'undefined' && typeof MediaSource !== 'undefined') return watchMse(cameraId, video, onState, stream, onStats)
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

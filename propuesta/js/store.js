export const DAY = 86400

const rng = seed => () => {
	seed = (seed * 1664525 + 1013904223) % 4294967296
	return seed / 4294967296
}

const GAPS = {
	0: [[3 * 3600 + 728, 96, 'camera']],
	1: [[14 * 3600 + 300, 1572, 'sleep'], [21 * 3600 + 2403, 41, 'network']],
	2: [],
	3: [[7 * 3600 + 3480, 270, 'engine']],
	4: [[1 * 3600 + 1200, 5 * 3600 + 120, 'sleep']],
	5: [[18 * 3600 + 120, 760, 'power']],
	6: [],
}
const ZONES = ['Portón', 'Cochera', 'Banqueta', 'Jardín']

export const nowSec = () => {
	const d = new Date()
	return d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds()
}

const buildRound = i => {
	const date = new Date()
	date.setHours(0, 0, 0, 0)
	date.setDate(date.getDate() - i)
	const end = i === 0 ? nowSec() : DAY
	const gaps = (GAPS[i] ?? []).filter(([start]) => start < end).map(([start, len, cause]) => ({ start, end: Math.min(start + len, end), cause }))
	const spans = []
	let at = 0
	for (const g of gaps) {
		spans.push({ start: at, end: g.start })
		at = g.end
	}
	spans.push({ start: at, end })
	const r = rng(977 + i * 131)
	const events = []
	const n = 16 + Math.floor(r() * 12)
	for (let k = 0; k < n; k++) {
		const night = r() < 0.14
		const t = Math.floor(night ? r() * 6 * 3600 : 6 * 3600 + r() * 17.6 * 3600)
		const dur = 6 + Math.floor(r() * 44)
		const kind = r() < 0.38 ? 'person' : 'motion'
		const zone = ZONES[Math.floor(r() * ZONES.length)]
		const x = r()
		if (t + dur >= end || gaps.some(g => t < g.end && t + dur > g.start)) continue
		events.push({ id: `S-${String(date.getDate()).padStart(2, '0')}${String(k).padStart(2, '0')}`, t, dur, kind, zone, x, day: i })
	}
	events.sort((a, b) => a.t - b.t)
	return {
		i,
		date,
		end,
		spans,
		gaps,
		events,
		get coverage() {
			const lost = this.gaps.reduce((s, g) => s + g.end - g.start, 0)
			return this.end > 0 ? ((this.end - lost) / this.end) * 100 : 100
		},
	}
}

export const ROUNDS = Array.from({ length: 7 }, (_, i) => buildRound(i))

export const SUMMARY = {
	camera: { id: 'entrada', name: 'Entrada', model: 'Tapo C500', host: '192.168.0.•••', firmware: '1.3.5', codec: 'H.264 · 1080p · 15 cuadros/s', audio: 'G.711', bitrate: 1.17 },
	disk: { name: 'Disco interno', total: 494, velador: 142, other: 141 },
	retentionDays: 14,
	retentionPct: 80,
	perDay: 12.7,
	engine: { version: '0.1.0', cpu: 2.1, ram: 96 },
}

export const CLIPS = [
	{ id: 'R-0007', name: 'Paquetería en el portón', date: ROUNDS[1].date, t: 11 * 3600 + 1520, dur: 48, size: 7.4 },
	{ id: 'R-0006', name: 'Gato en la cochera', date: ROUNDS[3].date, t: 22 * 3600 + 640, dur: 31, size: 4.8 },
	{ id: 'R-0005', name: 'Camión de la basura', date: ROUNDS[5].date, t: 7 * 3600 + 905, dur: 74, size: 11.5 },
]

export const state = {
	theme: localStorage.getItem('velador-theme') ?? 'dark',
	day: 0,
	t: Math.max(0, nowSec() - 900),
	playing: false,
	speed: 1,
	skipGaps: true,
	win: { from: 0, to: DAY },
	clip: null,
	muted: true,
	quality: 'hd',
	pan: 0,
	tilt: 0,
	filter: 'all',
	settings: { awake: true, autostart: true, tray: true, notifyPerson: true, notifyMotion: false, notifyGap: true, ntfy: false, detect: true, days: 14, pct: 80, part: 1, segment: 10 },
	welcome: 0,
}

export const round = () => ROUNDS[state.day]
export const eventAt = (r, t) => r.events.find(e => t >= e.t && t <= e.t + e.dur)
export const gapAt = (r, t) => r.gaps.find(g => t >= g.start && t < g.end)
export const guardSince = () => {
	let total = 0
	for (const r of ROUNDS) {
		const last = r.gaps.at(-1)
		total += r.end - (last ? last.end : 0)
		if (last) break
	}
	return total
}
let clipSeq = 7
export const nextClipId = () => `R-${String(++clipSeq).padStart(4, '0')}`
export const recorded = (r, from, to) => to - from - r.gaps.reduce((s, g) => s + Math.max(0, Math.min(to, g.end) - Math.max(from, g.start)), 0)
export const allGaps = () => ROUNDS.flatMap(r => r.gaps.map(g => ({ ...g, day: r.i, date: r.date })))

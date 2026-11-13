import { api } from './services/api.js'

const p2 = n => String(n).padStart(2, '0')
export const dateKey = d => `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())}`
export const dayAgo = i => {
	const d = new Date()
	d.setHours(12, 0, 0, 0)
	d.setDate(d.getDate() - i)
	return d
}

export const state = {
	theme: localStorage.getItem('velador-theme') ?? 'dark',
	summary: null,
	camera: null,
	day: 0,
	t: null,
	playing: false,
	speed: 1,
	skipGaps: true,
	win: null,
	muted: true,
}

export const toRound = (raw, i) => {
	const sec = ms => (ms - raw.startMs) / 1000
	return {
		i,
		date: dayAgo(i),
		raw,
		len: sec(raw.endMs),
		end: sec(raw.nowMs),
		from: sec(raw.watchedFromMs),
		spans: raw.spans.map(s => ({ start: sec(s.startMs), end: sec(s.endMs) })),
		gaps: raw.gaps.map(g => ({ start: sec(g.startMs), end: sec(g.endMs), cause: g.cause })),
		events: raw.events.map(e => ({ id: e.id, t: sec(e.startMs), dur: Math.max(1, (e.endMs - e.startMs) / 1000), kind: e.kind })),
		coverage: raw.coverage,
		ms: t => raw.startMs + t * 1000,
		clock: t => {
			const d = new Date(raw.startMs + t * 1000)
			return `${p2(d.getHours())}:${p2(d.getMinutes())}:${p2(d.getSeconds())}`
		},
	}
}

export const loadSummary = async () => {
	state.summary = await api.summary()
	state.camera = state.summary.cameras[0] ?? null
	return state.summary
}

export const loadRound = async i => toRound(await api.round(state.camera.id, dateKey(dayAgo(i))), i)

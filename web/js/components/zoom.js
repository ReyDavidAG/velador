const MAX = 6
const STEP = 1.25

export const attachZoom = (stage, layers, { rotated = () => false, onChange = () => {}, ignore = 'button, a, input, .pad, .rec-menu, .stage-bar, .zoom-ctl' } = {}) => {
	let scale = 1
	let x = 0
	let y = 0
	const pointers = new Map()
	let pinch = null
	let drag = null

	const clamp = () => {
		const { width, height } = stage.getBoundingClientRect()
		const maxX = (width * (scale - 1)) / 2
		const maxY = (height * (scale - 1)) / 2
		x = Math.max(-maxX, Math.min(maxX, x))
		y = Math.max(-maxY, Math.min(maxY, y))
	}

	const apply = () => {
		clamp()
		const base = `translate(${x}px, ${y}px) scale(${scale})`
		for (const el of layers()) {
			if (!el) continue
			el.style.transform = el.tagName === 'VIDEO' && rotated() ? `${base} rotate(180deg)` : scale === 1 ? '' : base
		}
		stage.classList.toggle('zoomed', scale > 1)
		onChange(scale)
	}

	const zoomAt = (next, cx, cy) => {
		const box = stage.getBoundingClientRect()
		const px = cx - box.left - box.width / 2
		const py = cy - box.top - box.height / 2
		const target = Math.max(1, Math.min(MAX, next))
		const k = target / scale
		x = px - (px - x) * k
		y = py - (py - y) * k
		scale = target
		if (scale === 1) x = y = 0
		apply()
	}

	const center = () => {
		const box = stage.getBoundingClientRect()
		return [box.left + box.width / 2, box.top + box.height / 2]
	}

	const onWheel = e => {
		if (e.target.closest(ignore)) return
		e.preventDefault()
		zoomAt(scale * (e.deltaY < 0 ? STEP : 1 / STEP), e.clientX, e.clientY)
	}

	const onDown = e => {
		if (e.target.closest(ignore)) return
		pointers.set(e.pointerId, { x: e.clientX, y: e.clientY })
		if (pointers.size === 2) {
			const [a, b] = [...pointers.values()]
			pinch = { distance: Math.hypot(a.x - b.x, a.y - b.y), scale, cx: (a.x + b.x) / 2, cy: (a.y + b.y) / 2 }
			drag = null
		} else if (scale > 1) {
			drag = { x: e.clientX - x, y: e.clientY - y }
			stage.setPointerCapture?.(e.pointerId)
			stage.classList.add('panning')
		}
	}

	const onMove = e => {
		if (!pointers.has(e.pointerId)) return
		pointers.set(e.pointerId, { x: e.clientX, y: e.clientY })
		if (pinch && pointers.size === 2) {
			const [a, b] = [...pointers.values()]
			zoomAt((pinch.scale * Math.hypot(a.x - b.x, a.y - b.y)) / pinch.distance, pinch.cx, pinch.cy)
			return
		}
		if (drag) {
			x = e.clientX - drag.x
			y = e.clientY - drag.y
			apply()
		}
	}

	const onUp = e => {
		pointers.delete(e.pointerId)
		if (pointers.size < 2) pinch = null
		if (pointers.size === 0) {
			drag = null
			stage.classList.remove('panning')
		}
	}

	const onKey = e => {
		if (e.target.closest('input, textarea')) return
		if (e.key === '+' || e.key === '=') zoomAt(scale * STEP, ...center())
		else if (e.key === '-' || e.key === '_') zoomAt(scale / STEP, ...center())
		else if (e.key === '0') zoomAt(1, ...center())
	}

	const onResize = () => apply()

	stage.addEventListener('wheel', onWheel, { passive: false })
	stage.addEventListener('pointerdown', onDown)
	window.addEventListener('pointermove', onMove)
	window.addEventListener('pointerup', onUp)
	window.addEventListener('pointercancel', onUp)
	document.addEventListener('keydown', onKey)
	window.addEventListener('resize', onResize)
	document.addEventListener('fullscreenchange', onResize)

	return {
		in: () => zoomAt(scale * STEP, ...center()),
		out: () => zoomAt(scale / STEP, ...center()),
		reset: () => zoomAt(1, ...center()),
		refresh: apply,
		get scale() {
			return scale
		},
		destroy: () => {
			stage.removeEventListener('wheel', onWheel)
			stage.removeEventListener('pointerdown', onDown)
			window.removeEventListener('pointermove', onMove)
			window.removeEventListener('pointerup', onUp)
			window.removeEventListener('pointercancel', onUp)
			document.removeEventListener('keydown', onKey)
			window.removeEventListener('resize', onResize)
			document.removeEventListener('fullscreenchange', onResize)
		},
	}
}

export const zoomControls = () =>
	`<div class="zoom-ctl" role="group" aria-label="Acercamiento"><button type="button" data-zoom="out" title="Alejar (−)" aria-label="Alejar">−</button><button type="button" class="mono" data-zoom="reset" title="Tamaño normal (0)" aria-label="Tamaño normal"><span data-zoom-level>1×</span></button><button type="button" data-zoom="in" title="Acercar (+)" aria-label="Acercar">+</button></div>`

export const zoomClick = (e, zoom) => {
	const btn = e.target.closest('[data-zoom]')
	if (!btn) return false
	zoom[btn.dataset.zoom]()
	return true
}

export const zoomLabel = (root, scale) => {
	for (const el of root.querySelectorAll('[data-zoom-level]')) el.textContent = `${scale < 1.05 ? 1 : scale.toFixed(1)}×`
}

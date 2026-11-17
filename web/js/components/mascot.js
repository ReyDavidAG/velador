const root = document.documentElement

export const mascot = (cls = '') => `<svg class="sereno ${cls}" viewBox="0 0 120 120" aria-hidden="true">
	<path class="sereno-ear l" d="M30 46 15 16c-1-3 2-5 5-4l28 16z"/><path class="sereno-ear" d="M90 46l15-30c1-3-2-5-5-4L72 28z"/>
	<rect x="19" y="30" width="82" height="60" rx="27" fill="var(--fur)"/>
	<path d="M19 66a41 26 0 0 0 82 0v-4H19z" fill="var(--fur-2)" opacity=".28"/>
	<path d="M29 36Q60 4 91 36z" fill="var(--cap)"/><path d="M24 35h72a4 4 0 0 1 4 4v3H20v-3a4 4 0 0 1 4-4z" fill="#14131f"/>
	<path d="M60 14l3 6 6 .8-4.4 4.2 1 6.2-5.6-2.9-5.6 2.9 1-6.2L51 20.8l6-.8z" fill="#ffc857"/>
	<g class="sereno-lids"><circle cx="43" cy="59" r="11.5" fill="#0c0b12"/><circle cx="77" cy="59" r="11.5" fill="#0c0b12"/>
	<g class="sereno-look"><circle class="sereno-halo" cx="43" cy="59" r="7.5"/><circle class="sereno-halo" cx="77" cy="59" r="7.5"/><circle class="sereno-pup" cx="43" cy="59" r="4.6"/><circle class="sereno-pup" cx="77" cy="59" r="4.6"/></g></g>
	<circle cx="25" cy="72" r="2.2" fill="#14131f" opacity=".5"/><circle cx="95" cy="72" r="2.2" fill="#14131f" opacity=".5"/>
	<g class="sereno-jaw"><rect x="37" y="86" width="46" height="15" rx="7.5" fill="var(--fur-2)"/><path d="M43 86h6v5h-6zM52 86h6v5h-6zM62 86h6v5h-6zM71 86h6v5h-6z" fill="#fff8e6"/></g>
	<rect x="40" y="67" width="40" height="22" rx="11" fill="var(--muzzle)"/>
	<path d="M53 71h14c2 0 3 2 1.6 3.6L62 81c-1 1-3 1-4 0l-6.6-6.4C50 73 51 71 53 71z" fill="#14131f"/>
</svg>`

let base = ['calm', 'Todo en calma']
let alert = null

const apply = () => {
	const [mood, text] = alert ? ['person', alert] : base
	const say = document.querySelector('[data-say]')
	if (root.dataset.mood === mood && say?.textContent === text) return
	root.dataset.mood = mood
	if (!say) return
	say.textContent = text
	say.classList.remove('swap')
	void say.offsetWidth
	say.classList.add('swap')
}

export const setMood = (mood, text) => {
	base = [mood, text]
	apply()
}

export const setAlert = text => {
	alert = text
	apply()
}

export const watchPointer = () => {
	let x = 0
	let y = 0
	let queued = false
	window.addEventListener('pointermove', e => {
		x = e.clientX
		y = e.clientY
		if (queued) return
		queued = true
		requestAnimationFrame(() => {
			queued = false
			root.style.setProperty('--mx', `${x}px`)
			root.style.setProperty('--my', `${y}px`)
			for (const look of document.querySelectorAll('.sereno-look')) {
				const r = look.ownerSVGElement.getBoundingClientRect()
				if (!r.width) continue
				const dx = x - (r.left + r.width / 2)
				const dy = y - (r.top + r.height / 2)
				const d = Math.hypot(dx, dy) || 1
				const k = Math.min(1, d / 160) * 4.2
				look.style.transform = `translate(${(dx / d) * k}px, ${(dy / d) * k}px)`
			}
		})
	})
}

const hex = c => [1, 3, 5].map(i => parseInt(c.slice(i, i + 2), 16))
const mix = (a, b, t) => {
	const [x, y] = [hex(a), hex(b)]
	return `rgb(${x.map((v, i) => Math.round(v + (y[i] - v) * t)).join(' ')})`
}
const smooth = (a, b, x) => {
	const t = Math.min(1, Math.max(0, (x - a) / (b - a)))
	return t * t * (3 - 2 * t)
}
const bump = (x, c, w) => Math.max(0, 1 - Math.abs(x - c) / w)

export const light = h => Math.min(smooth(5.6, 7.4, h), 1 - smooth(18.2, 19.8, h))

const PALETTE = {
	sky: ['#0a0f1f', '#8ec5ee'],
	far: ['#0e1322', '#a3b6c4'],
	wall: ['#1b1e26', '#dccfb8'],
	shade: ['#14161c', '#bcae96'],
	drive: ['#1c1f27', '#a8a7a2'],
	walk: ['#23272f', '#cbc8c0'],
	lawn: ['#0f1a15', '#74a05a'],
	tree: ['#0c1512', '#3f7c47'],
	tree2: ['#0f1b16', '#559458'],
	trunk: ['#15110e', '#5e4632'],
	car: ['#232831', '#b63a2f'],
	glass: ['#0c0f14', '#2c3d4d'],
	gate: ['#0b0d11', '#2b2e35'],
	body: ['#06070a', '#24364a'],
	skin: ['#0a0b0e', '#c79a7a'],
}

let uid = 0

export const sceneHtml = (cls = '', fit = 'slice') => {
	const id = `sc${++uid}`
	const f = k => `style="fill:var(--c-${k})"`
	const bars = Array.from({ length: 13 }, (_, i) => `<rect x="${372 + i * 11}" y="158" width="3" height="62" ${f('gate')}/>`).join('')
	const stars = [[40, 30], [120, 62], [210, 24], [300, 50], [388, 18], [470, 44], [540, 28], [600, 70], [80, 96], [260, 88], [430, 84]]
		.map(([x, y], i) => `<circle cx="${x}" cy="${y}" r="${i % 3 ? 0.9 : 1.4}" fill="#fff"/>`)
		.join('')
	return `<svg class="scene ${cls}" viewBox="0 0 640 360" preserveAspectRatio="xMidYMid ${fit}" aria-hidden="true">
		<defs>
			<radialGradient id="${id}v" cx="50%" cy="46%" r="72%"><stop offset="55%" stop-color="#000" stop-opacity="0"/><stop offset="100%" stop-color="#000" stop-opacity="0.55"/></radialGradient>
			<radialGradient id="${id}g" cx="50%" cy="50%" r="50%"><stop offset="0%" stop-color="#ffe2a3" stop-opacity="0.95"/><stop offset="100%" stop-color="#ffb648" stop-opacity="0"/></radialGradient>
			<linearGradient id="${id}w" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#ff9a4d" stop-opacity="0"/><stop offset="100%" stop-color="#ff9a4d" stop-opacity="0.75"/></linearGradient>
			<clipPath id="${id}c"><rect width="640" height="360"/></clipPath>
		</defs>
		<g clip-path="url(#${id}c)"><g class="scene-pan">
			<rect x="-80" y="-60" width="800" height="250" ${f('sky')}/>
			<rect class="scene-warm" x="-80" y="20" width="800" height="150" fill="url(#${id}w)"/>
			<g class="scene-stars">${stars}</g>
			<g class="scene-clouds"><g class="scene-cloud"><ellipse cx="0" cy="52" rx="46" ry="11" fill="#fff" opacity="0.8"/><ellipse cx="30" cy="44" rx="30" ry="12" fill="#fff" opacity="0.8"/></g></g>
			<path d="M-80 150 V118 h70 v-18 h54 v24 h60 v-30 l32 -20 l32 20 v30 h70 v-16 h64 v22 h90 v-34 h58 v28 h70 v-20 l26 -16 l26 16 v24 h90 v-12 h50 V150z" ${f('far')}/>
			<rect x="-80" y="148" width="800" height="76" ${f('wall')}/>
			<rect x="-80" y="148" width="800" height="7" ${f('shade')}/>
			<rect x="-80" y="216" width="800" height="8" ${f('shade')}/>
			<rect x="364" y="150" width="156" height="74" ${f('walk')}/>
			<rect x="360" y="146" width="8" height="78" ${f('shade')}/><rect x="516" y="146" width="8" height="78" ${f('shade')}/>
			${bars}<rect x="366" y="170" width="152" height="3" ${f('gate')}/>
			<rect x="-80" y="224" width="800" height="200" ${f('lawn')}/>
			<path d="M330 224 H560 L760 424 H180z" ${f('drive')}/>
			<path d="M330 224 H560 L574 238 H318z" ${f('walk')}/>
			<g class="scene-motion"><rect x="372" y="150" width="140" height="92" rx="3"/><g class="scene-tag"><rect x="372" y="133" width="74" height="15" rx="3"/><text x="377" y="144">Movimiento</text></g></g>
			<g class="scene-person"><g class="scene-walker">
				<ellipse cx="0" cy="62" rx="15" ry="4" fill="#000" opacity="0.28"/>
				<rect x="-7" y="30" width="6" height="32" rx="3" ${f('body')}/><rect x="1" y="30" width="6" height="32" rx="3" ${f('body')}/>
				<rect x="-10" y="-2" width="20" height="38" rx="8" ${f('body')}/>
				<circle cx="0" cy="-12" r="8" ${f('skin')}/>
				<rect class="scene-box" x="-22" y="-28" width="44" height="98" rx="3"/>
				<g class="scene-tag"><rect x="-22" y="-45" width="58" height="15" rx="3"/><text x="-17" y="-34">Persona</text></g>
			</g></g>
			<g transform="translate(486 262)">
				<ellipse cx="60" cy="52" rx="92" ry="9" fill="#000" opacity="0.25"/>
				<path d="M-22 44 V18 q2 -10 14 -12 l22 -4 q8 -22 30 -24 h52 q20 2 30 22 l20 6 q10 3 10 14 v24z" ${f('car')}/>
				<path d="M22 0 q6 -16 22 -17 h20 v17z M72 -17 h20 q14 1 22 17 H72z" ${f('glass')}/>
				<circle cx="12" cy="45" r="13" fill="#0a0b0e"/><circle cx="118" cy="45" r="13" fill="#0a0b0e"/>
				<circle cx="12" cy="45" r="5" ${f('shade')}/><circle cx="118" cy="45" r="5" ${f('shade')}/>
			</g>
			<rect x="92" y="150" width="14" height="110" rx="4" ${f('trunk')}/>
			<g class="scene-tree"><circle cx="100" cy="120" r="54" ${f('tree')}/><circle cx="62" cy="146" r="34" ${f('tree2')}/><circle cx="140" cy="140" r="38" ${f('tree2')}/><circle cx="104" cy="88" r="32" ${f('tree2')}/></g>
			<rect x="590" y="70" width="5" height="160" ${f('gate')}/><rect x="566" y="68" width="30" height="5" rx="2" ${f('gate')}/>
			<g class="scene-lamp"><circle cx="570" cy="80" r="70" fill="url(#${id}g)"/><ellipse cx="548" cy="250" rx="120" ry="34" fill="url(#${id}g)" opacity="0.5"/><circle cx="570" cy="76" r="5" fill="#fff3d1"/></g>
		</g></g>
		<rect width="640" height="360" fill="url(#${id}v)"/>
	</svg>`
}

export const updateScene = (svg, { hour, person = false, motion = false, x = 0.5, pan = 0, tilt = 0 }) => {
	if (!svg) return
	const l = light(hour)
	const s = svg.style
	for (const [k, [n, d]] of Object.entries(PALETTE)) s.setProperty(`--c-${k}`, mix(n, d, l))
	s.setProperty('--warm', (Math.max(bump(hour, 6.7, 1.2), bump(hour, 19, 1.2)) * 0.9).toFixed(2))
	s.setProperty('--night', (1 - l).toFixed(2))
	s.setProperty('--day', l.toFixed(2))
	s.setProperty('--px', `${300 + x * 190}px`)
	s.setProperty('--pan', `${-pan * 26}px`)
	s.setProperty('--tilt', `${tilt * 18}px`)
	svg.classList.toggle('ir', l < 0.1)
	svg.classList.toggle('has-person', person)
	svg.classList.toggle('has-motion', motion)
	s.setProperty('--mx', `${(x - 0.5) * 120}px`)
}

import { mascot } from '../components/mascot.js'
import { loadSummary } from '../store.js'
import { bindCameraForm, cameraFormHtml } from './camera_form.js'

const html = () => `<div class="welcome welcome-one">
	<aside class="wl-brand">
		${mascot('wl-eye')}
		<h2>Velador</h2><p>Sereno cuida mientras duermes.</p>
		<ol class="wl-steps"><li class="now"><span class="mono">1</span>Conecta tu cámara</li><li><span class="mono">2</span>Velador empieza a grabar</li><li><span class="mono">3</span>Elige dónde guardar, en Ajustes</li></ol>
	</aside>
	<section class="wl-body"><h2>Conecta tu cámara</h2><p class="lead">Todo se queda en tu equipo: nada sale a internet.</p>${cameraFormHtml()}</section>
</div>`

const mount = main =>
	bindCameraForm(main, {
		onSaved: async () => {
			await loadSummary().catch(() => {})
			location.hash = '#/'
			location.reload()
		},
	})

export const welcomeView = { title: 'Bienvenida', html, mount }

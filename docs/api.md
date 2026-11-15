# API del motor

Todo bajo `http://127.0.0.1:8352/api`, con sesión (cookie `velador_session` o cabecera `X-Velador-Token`). Tiempos en milisegundos UTC. Los errores `400` traen `{ "error": "…" }` en español, listo para mostrarse.

| Método y ruta | Qué hace |
|---|---|
| `GET /summary` | Estado de la guardia por cámara (`on_guard`, `reconnecting`, `gap` con causa, `paused` con `pausedUntilMs`), último suceso, disco y almacenamiento activo. |
| `POST /cameras/{id}/recording` `{paused, minutes?}` | Pausa la grabación (de 1 minuto a 24 horas, o hasta reanudar) o la reanuda. El vivo sigue; queda un hueco `paused`. |
| `GET /cameras/{id}/rounds/{AAAA-MM-DD}?tz=` | La ronda del día local: `spans`, `gaps` (con causa), `events`, `coverage`, `watchedFromMs`. |
| `GET /cameras/{id}/media?startMs&durationMs&stream=main\|minor` | Video grabado por rango, con soporte de rangos HTTP. Hasta 10 minutos por petición. |
| `POST /cameras/{id}/live?stream=main\|minor` · `PATCH/DELETE /cameras/{id}/live/{sesión}?stream=` | Señalización WHEP del vivo, reenviada a MediaMTX; `minor` es el stream ligero (720p). |
| `GET /cameras/{id}/ptz` · `POST /cameras/{id}/ptz` `{x,y}` | Posición y giro relativo (−1 a 1; `x` a la derecha y `y` hacia arriba de la imagen tal como se ve, una vez calibrada). Suspende el seguimiento 10 s. |
| `GET /cameras/{id}/vision` | Detección de personas: estado, `device` (`gpu`/`cpu`), cuadros por segundo, recuadros (`tracks`, en fracciones de la imagen), objetivo, si sigue o busca (`coasting`) y la calibración. |
| `PUT /cameras/{id}/vision` `{follow}` | Enciende o apaga «Seguir personas». La primera vez calibra antes (unos 30 s). |
| `POST /cameras/{id}/vision/calibrate` | Mide con la posición real de la cámara cuánto se mueve la imagen por unidad de posición, la velocidad a tres niveles de orden (no es proporcional), el sentido de cada eje y el retraso del video; la regresa a donde estaba. Cerca de un minuto. Devuelve `view` y `steps` con cada medición. |
| `GET /cameras/{id}/zone` · `PUT /cameras/{id}/zone` `{minX,maxX,minY,maxY,homeX,homeY}` · `DELETE /cameras/{id}/zone` | Zona de vigilancia en unidades de posición ONVIF (−1 a 1): límites para el centro de la imagen y reposo. El `GET` trae también la posición actual, si está dentro, el tamaño de la imagen en esas unidades y el mapa de cobertura. |
| `POST /cameras/{id}/ptz/goto` `{x,y}` | Lleva la cámara a una posición absoluta. |
| `POST /cameras/{id}/zone/survey` · `DELETE …/survey` · `GET …/survey/{n}` | Mide la cobertura: recorre todo el alcance, guarda una foto por dirección (PNG de 320×180, privadas) y regresa. Cancelar y leer cada foto. |
| `GET /cameras/{id}/image` · `PUT /cameras/{id}/image` `{name,value}` | Ajustes de imagen de la cámara por el protocolo de Tapo (`sharpness`, `brightness`, `contrast`, `saturation` de 0 a 100; `wdr`, `hlc`, `dehaze`, `ldc` en `on`/`off`) y el video principal (sólo lectura en la C500). |
| `GET /cameras/{id}/people?date=AAAA-MM-DD` · `GET /sightings/{id}/image` · `…/face` · `…/fused` · `DELETE /people/{id}` | Personas del día agrupadas por apariencia, con sus fotos (PNG) y horas. `face` es el mejor cuadro de la cara, recortado de la grabación principal; `fused`, la combinación de varios cuadros. Olvidar a una persona borra sus fotos. |
| `GET /cameras` · `PUT /cameras/{id}` · `DELETE /cameras/{id}` | Alta, edición y baja. La contraseña sólo entra; nunca sale. |
| `POST /cameras/{id}/networks/{red}/use` · `DELETE /cameras/{id}/networks/{red}` | Usar la dirección recordada de una red, u olvidarla. Hasta 3 redes por cámara. |
| `GET /cameras/{id}/extras` | Luz blanca, modo de noche y volteo, leídos por el protocolo de Tapo. |
| `POST /cameras/{id}/light` `{on}` · `PUT /cameras/{id}/night` `{mode}` · `PUT /cameras/{id}/flip` `{on}` | Encender la luz, elegir `infrared`, `white` o `smart`, y voltear la imagen en la cámara. |
| `GET /cameras/{id}/wifi` · `POST /cameras/{id}/wifi` `{ssid,password}` | Redes que ve la cámara, y pasarla a otra. |
| `GET /cameras/discover` | Cámaras ONVIF que responden en la red local. |
| `POST /cameras/test` `{host,user,password\|id}` | Comprueba red y cuenta sin guardar nada. |
| `GET /settings` · `PUT /settings` | Carpeta de grabación, retención, no dormir, `detectPeople`; lista de discos externos y `outlook` (cuánto pesa un día, cuántos días caben, cuándo empieza a borrar). |
| `POST /storage/release` · `POST /storage/reclaim` | Suelta el disco externo 10 minutos para expulsarlo, o vuelve a usarlo. |
| `GET /clips` · `POST /cameras/{id}/clips` `{startMs,endMs,title}` | Recortes guardados. De 1 segundo a 30 minutos. |
| `GET /clips/{id}/file` · `POST /clips/{id}/reveal` · `DELETE /clips/{id}` | Reproducir, mostrar en el Finder y borrar. |

# API del motor

Todo bajo `http://127.0.0.1:8352/api`, con sesión (cookie `velador_session` o cabecera `X-Velador-Token`). Tiempos en milisegundos UTC. Los errores `400` traen `{ "error": "…" }` en español, listo para mostrarse.

| Método y ruta | Qué hace |
|---|---|
| `GET /summary` | Estado de la guardia por cámara (`on_guard`, `reconnecting`, `gap` con causa), último suceso, disco y almacenamiento activo. |
| `GET /cameras/{id}/rounds/{AAAA-MM-DD}?tz=` | La ronda del día local: `spans`, `gaps` (con causa), `events`, `coverage`, `watchedFromMs`. |
| `GET /cameras/{id}/media?startMs&durationMs&stream=main\|minor` | Video grabado por rango, con soporte de rangos HTTP. Hasta 10 minutos por petición. |
| `POST /cameras/{id}/live` · `PATCH/DELETE /cameras/{id}/live/{sesión}` | Señalización WHEP del vivo, reenviada a MediaMTX. |
| `GET /cameras/{id}/ptz` · `POST /cameras/{id}/ptz` `{x,y}` | Posición y giro relativo (−1 a 1). |
| `GET /cameras` · `PUT /cameras/{id}` · `DELETE /cameras/{id}` | Alta, edición y baja. La contraseña sólo entra; nunca sale. |
| `POST /cameras/{id}/networks/{red}/use` · `DELETE /cameras/{id}/networks/{red}` | Usar la dirección recordada de una red, u olvidarla. Hasta 3 redes por cámara. |
| `GET /cameras/{id}/extras` | Luz blanca, modo de noche y volteo, leídos por el protocolo de Tapo. |
| `POST /cameras/{id}/light` `{on}` · `PUT /cameras/{id}/night` `{mode}` · `PUT /cameras/{id}/flip` `{on}` | Encender la luz, elegir `infrared`, `white` o `smart`, y voltear la imagen en la cámara. |
| `GET /cameras/{id}/wifi` · `POST /cameras/{id}/wifi` `{ssid,password}` | Redes que ve la cámara, y pasarla a otra. |
| `GET /cameras/discover` | Cámaras ONVIF que responden en la red local. |
| `POST /cameras/test` `{host,user,password\|id}` | Comprueba red y cuenta sin guardar nada. |
| `GET /settings` · `PUT /settings` | Carpeta de grabación, retención, no dormir; lista de discos externos. |
| `POST /storage/release` · `POST /storage/reclaim` | Suelta el disco externo 10 minutos para expulsarlo, o vuelve a usarlo. |
| `GET /clips` · `POST /cameras/{id}/clips` `{startMs,endMs,title}` | Recortes guardados. De 1 segundo a 30 minutos. |
| `GET /clips/{id}/file` · `POST /clips/{id}/reveal` · `DELETE /clips/{id}` | Reproducir, mostrar en el Finder y borrar. |

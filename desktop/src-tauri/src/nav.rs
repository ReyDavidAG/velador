use tauri::Url;

use crate::platform;

pub fn port() -> u16 {
    std::env::var("VELADOR_PORT")
        .ok()
        .and_then(|p| p.parse().ok())
        .unwrap_or(8352)
}

pub fn dashboard() -> Url {
    Url::parse(&format!("http://127.0.0.1:{}/", port())).expect("dashboard url")
}

pub fn entry(token: &str) -> Url {
    Url::parse(&format!("http://127.0.0.1:{}/?t={token}", port())).expect("entry url")
}

pub fn is_dashboard(url: &Url, port: u16) -> bool {
    url.scheme() == "http"
        && matches!(url.host_str(), Some("127.0.0.1" | "localhost"))
        && url.port() == Some(port)
        && url.username().is_empty()
        && url.password().is_none()
}

fn is_bundled(url: &Url) -> bool {
    match url.scheme() {
        "tauri" => url.host_str() == Some("localhost"),
        "http" => url.host_str() == Some("tauri.localhost"),
        "about" => url.as_str() == "about:blank",
        _ => false,
    }
}

pub fn allowed(url: &Url, port: u16) -> bool {
    is_dashboard(url, port) || is_bundled(url)
}

pub fn opens_outside(url: &Url) -> bool {
    matches!(url.scheme(), "http" | "https" | "mailto")
}

pub fn guard(url: &Url) -> bool {
    if allowed(url, port()) {
        return true;
    }
    if opens_outside(url) {
        platform::open_external(url.as_str());
    }
    #[cfg(debug_assertions)]
    eprintln!("[velador] navegación bloqueada: {url}");
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    fn url(s: &str) -> Url {
        Url::parse(s).unwrap()
    }

    #[test]
    fn permite_el_dashboard_local() {
        assert!(allowed(&url("http://127.0.0.1:8352/"), 8352));
        assert!(allowed(&url("http://localhost:8352/clean?x=1"), 8352));
        assert!(allowed(&url("tauri://localhost/index.html"), 8352));
    }

    #[test]
    fn rechaza_otros_puertos_esquemas_y_hosts() {
        assert!(!allowed(&url("http://127.0.0.1:8353/"), 8352));
        assert!(!allowed(&url("https://127.0.0.1:8352/"), 8352));
        assert!(!allowed(&url("http://evil.com/"), 8352));
        assert!(!allowed(&url("http://127.0.0.1.evil.com:8352/"), 8352));
        assert!(!allowed(&url("file:///etc/passwd"), 8352));
        assert!(!allowed(&url("tauri://evil/"), 8352));
    }

    #[test]
    fn rechaza_credenciales_disfrazadas_de_host() {
        assert!(!allowed(&url("http://127.0.0.1:8352@evil.com/"), 8352));
        assert!(!allowed(&url("http://user:pw@127.0.0.1:8352/"), 8352));
    }

    #[test]
    fn solo_abre_afuera_web_y_correo() {
        assert!(opens_outside(&url("https://github.com/ReyDavidAG/velador")));
        assert!(opens_outside(&url("mailto:hola@example.com")));
        assert!(!opens_outside(&url("file:///Applications")));
        assert!(!opens_outside(&url("x-apple.systempreferences:")));
    }
}

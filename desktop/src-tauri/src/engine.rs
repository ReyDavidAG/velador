use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

use serde::Deserialize;

use crate::nav;

const LABEL: &str = "com.reydavidag.velador.engine";
pub const NOT_INSTALLED: &str = "not-installed";

#[derive(Debug, Clone, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct OpenGap {
    pub start_ms: i64,
    pub cause: String,
}

#[derive(Debug, Clone, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Camera {
    pub id: String,
    pub name: String,
    pub state: String,
    pub gap: Option<OpenGap>,
    pub guard_since_ms: Option<i64>,
}

#[derive(Debug, Clone, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Summary {
    pub now_ms: i64,
    pub state: String,
    pub cameras: Vec<Camera>,
}

fn home() -> PathBuf {
    std::env::var_os("HOME")
        .map(PathBuf::from)
        .unwrap_or_default()
}

pub fn state_dir() -> PathBuf {
    std::env::var_os("VELADOR_HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|| home().join("Library/Application Support/Velador"))
}

pub fn read_token(dir: &Path) -> Result<String, String> {
    let raw = fs::read_to_string(dir.join("token"))
        .map_err(|_| "Falta el token de Velador".to_string())?;
    let token = raw.trim();
    if token.len() < 32 || !token.bytes().all(|b| b.is_ascii_hexdigit()) {
        return Err("El token de Velador no es válido".into());
    }
    Ok(token.to_string())
}

fn client(timeout: Duration) -> ureq::Agent {
    ureq::Agent::new_with_config(
        ureq::Agent::config_builder()
            .timeout_global(Some(timeout))
            .proxy(None)
            .build(),
    )
}

pub fn summary() -> Result<Summary, String> {
    let token = read_token(&state_dir())?;
    client(Duration::from_secs(3))
        .get(format!("{}api/summary", nav::dashboard()))
        .header("x-velador-token", &token)
        .call()
        .map_err(|e| e.to_string())?
        .body_mut()
        .read_json::<Summary>()
        .map_err(|e| e.to_string())
}

pub fn alive() -> bool {
    summary().is_ok()
}

pub fn agent_plist() -> PathBuf {
    home()
        .join("Library/LaunchAgents")
        .join(format!("{LABEL}.plist"))
}

fn launchctl(args: &[&str]) -> bool {
    Command::new("/bin/launchctl")
        .args(args)
        .output()
        .map(|o| o.status.success())
        .unwrap_or(false)
}

pub fn start() -> Result<(), String> {
    let plist = agent_plist();
    if !plist.exists() {
        return Err(NOT_INSTALLED.into());
    }
    let domain = format!("gui/{}", unsafe { libc::getuid() });
    let service = format!("{domain}/{LABEL}");
    if launchctl(&["kickstart", &service])
        || launchctl(&["bootstrap", &domain, &plist.to_string_lossy()])
    {
        return Ok(());
    }
    Err("launchctl no pudo arrancar el motor".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn dir_with(token: Option<&str>) -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "velador-token-{}-{}",
            std::process::id(),
            token.map(|t| t.len()).unwrap_or(0)
        ));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        if let Some(t) = token {
            fs::write(dir.join("token"), t).unwrap();
        }
        dir
    }

    #[test]
    fn lee_el_token_y_quita_espacios() {
        let token = "A1".repeat(32);
        let dir = dir_with(Some(&format!("{token}\n")));
        assert_eq!(read_token(&dir).unwrap(), token);
    }

    #[test]
    fn falla_sin_token_o_con_token_invalido() {
        assert!(read_token(&dir_with(None)).is_err());
        assert!(read_token(&dir_with(Some("   "))).is_err());
        assert!(read_token(&dir_with(Some(&"z<".repeat(24)))).is_err());
    }

    #[test]
    fn interpreta_el_resumen_del_motor() {
        let json = r#"{"nowMs":1794539154653,"state":"running","cameras":[{"id":"entrada","name":"Entrada","state":"gap","gap":{"startMs":1794539100000,"cause":"camera"},"guardSinceMs":null}],"disk":{"totalBytes":1,"freeBytes":1},"retention":{"days":14,"percent":80}}"#;
        let s: Summary = serde_json::from_str(json).unwrap();
        assert_eq!(s.cameras[0].gap.as_ref().unwrap().cause, "camera");
        assert_eq!(s.cameras[0].guard_since_ms, None);
    }
}

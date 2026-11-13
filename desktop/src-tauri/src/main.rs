mod alerts;
mod engine;
mod nav;
mod platform;
mod text;
mod tray;

use std::sync::Mutex;
use std::thread;
use std::time::Duration;

use tauri::WindowEvent;
use tauri::async_runtime::spawn_blocking;
use tauri::webview::NewWindowResponse;
use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindow, WebviewWindowBuilder};
use tauri_plugin_window_state::{AppHandleExt, StateFlags};

fn entry() -> Option<tauri::Url> {
    if !engine::alive() {
        return None;
    }
    engine::read_token(&engine::state_dir())
        .ok()
        .map(|token| nav::entry(&token))
}

#[tauri::command]
async fn engine_status(window: WebviewWindow) -> bool {
    match spawn_blocking(entry).await.ok().flatten() {
        Some(url) => {
            let _ = window.navigate(url);
            true
        }
        None => false,
    }
}

#[tauri::command]
async fn start_engine(app: AppHandle) -> Result<(), String> {
    let resources = app.path().resource_dir().ok();
    spawn_blocking(move || engine::start(resources.as_deref()))
        .await
        .map_err(|e| e.to_string())?
}

fn open_window(app: &AppHandle) -> tauri::Result<WebviewWindow> {
    if let Some(window) = app.get_webview_window("main") {
        return Ok(window);
    }
    let url = match entry() {
        Some(url) => WebviewUrl::External(url),
        None => WebviewUrl::App("index.html".into()),
    };
    WebviewWindowBuilder::new(app, "main", url)
        .title("Velador")
        .inner_size(1280.0, 820.0)
        .min_inner_size(960.0, 600.0)
        .on_navigation(nav::guard)
        .on_new_window(|url, _| {
            nav::guard(&url);
            NewWindowResponse::Deny
        })
        .build()
}

const LAUNCHER: &str = "com.reydavidag.velador.launcher";
const HIDDEN: &str = "--hidden";

fn state_flags() -> StateFlags {
    StateFlags::all() & !StateFlags::VISIBLE
}

pub fn show_window(app: &AppHandle) {
    platform::dock(app, true);
    #[cfg(target_os = "macos")]
    let _ = app.show();
    if let Ok(window) = open_window(app) {
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
    }
}

fn watch(app: AppHandle) {
    thread::spawn(move || {
        loop {
            let summary = engine::summary();
            tray::update(&app, &summary);
            if let Ok(summary) = &summary {
                alerts::check(&app, summary);
            }
            thread::sleep(Duration::from_secs(10));
        }
    });
}

fn hide_window(window: &tauri::Window) {
    let app = window.app_handle();
    let _ = app.save_window_state(state_flags());
    let _ = window.hide();
    platform::dock(app, false);
}

fn main() {
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_notification::init())
        .plugin(
            tauri_plugin_window_state::Builder::default()
                .with_state_flags(state_flags())
                .build(),
        )
        .plugin(
            tauri_plugin_autostart::Builder::new()
                .app_name(LAUNCHER)
                .arg(HIDDEN)
                .build(),
        )
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                hide_window(window);
            }
        })
        .invoke_handler(tauri::generate_handler![engine_status, start_engine])
        .setup(|app| {
            app.manage(alerts::Alerts(Mutex::new(alerts::GapWatch::default())));
            tray::build(app.handle())?;
            if std::env::args().any(|a| a == HIDDEN) {
                platform::dock(app.handle(), false);
            } else {
                show_window(app.handle());
            }
            watch(app.handle().clone());
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building Velador");
    app.run(|app, event| {
        #[cfg(target_os = "macos")]
        if let tauri::RunEvent::Reopen { .. } = event {
            show_window(app);
        }
        let _ = (app, &event);
    });
}

use std::sync::Mutex;
use std::thread;

use tauri::image::Image;
use tauri::menu::{CheckMenuItem, Menu, MenuEvent, MenuItem, PredefinedMenuItem};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Manager, Wry};
use tauri_plugin_autostart::ManagerExt;

use crate::engine::Summary;
use crate::{nav, platform, text};

pub struct Tray {
    status: MenuItem<Wry>,
    pause: MenuItem<Wry>,
    login: CheckMenuItem<Wry>,
    camera: Mutex<Option<(String, bool)>>,
}

pub fn build(app: &AppHandle) -> tauri::Result<()> {
    let status = MenuItem::with_id(
        app,
        "status",
        "Consultando la guardia…",
        false,
        None::<&str>,
    )?;
    let pause = MenuItem::with_id(app, "pause", text::pause_label(false), false, None::<&str>)?;
    let open = MenuItem::with_id(app, "open", "Abrir Velador", true, None::<&str>)?;
    let browser = MenuItem::with_id(app, "browser", "Abrir en el navegador", true, None::<&str>)?;
    let login = CheckMenuItem::with_id(
        app,
        "login",
        "Abrir al iniciar sesión",
        true,
        app.autolaunch().is_enabled().unwrap_or(false),
        None::<&str>,
    )?;
    let quit = MenuItem::with_id(app, "quit", "Salir de Velador", true, None::<&str>)?;
    let menu = Menu::with_items(
        app,
        &[
            &status,
            &pause,
            &PredefinedMenuItem::separator(app)?,
            &open,
            &browser,
            &PredefinedMenuItem::separator(app)?,
            &login,
            &PredefinedMenuItem::separator(app)?,
            &quit,
        ],
    )?;
    TrayIconBuilder::with_id("velador")
        .icon(tray_icon()?)
        .icon_as_template(cfg!(target_os = "macos"))
        .tooltip("Velador")
        .menu(&menu)
        .show_menu_on_left_click(true)
        .on_menu_event(on_menu)
        .build(app)?;
    app.manage(Tray {
        status,
        pause,
        login,
        camera: Mutex::new(None),
    });
    Ok(())
}

fn tray_icon() -> tauri::Result<Image<'static>> {
    #[cfg(target_os = "macos")]
    let bytes: &[u8] = include_bytes!("../icons/tray.png");
    #[cfg(not(target_os = "macos"))]
    let bytes: &[u8] = include_bytes!("../icons/32x32.png");
    Image::from_bytes(bytes)
}

fn on_menu(app: &AppHandle, event: MenuEvent) {
    match event.id().as_ref() {
        "open" => crate::show_window(app),
        "browser" => {
            if let Ok(token) = crate::engine::read_token(&crate::engine::state_dir()) {
                platform::open_external(nav::entry(&token).as_str());
            }
        }
        "login" => {
            let launcher = app.autolaunch();
            let on = launcher.is_enabled().unwrap_or(false);
            let _ = if on {
                launcher.disable()
            } else {
                launcher.enable()
            };
            if let Some(tray) = app.try_state::<Tray>() {
                let _ = tray.login.set_checked(launcher.is_enabled().unwrap_or(!on));
            }
        }
        "pause" => {
            let current = app
                .try_state::<Tray>()
                .and_then(|tray| tray.camera.lock().ok().and_then(|c| c.clone()));
            if let Some((id, paused)) = current {
                let app = app.clone();
                thread::spawn(move || {
                    if crate::engine::set_recording(&id, !paused).is_ok() {
                        update(&app, &crate::engine::summary());
                    }
                });
            }
        }
        "quit" => app.exit(0),
        _ => {}
    }
}

pub fn update(app: &AppHandle, summary: &Result<Summary, String>) {
    let label = text::status(summary);
    if let Some(tray) = app.try_state::<Tray>() {
        let _ = tray.status.set_text(&label);
        let camera = summary
            .as_ref()
            .ok()
            .and_then(|s| s.cameras.first())
            .map(|c| (c.id.clone(), c.state == "paused"));
        let _ = tray.pause.set_enabled(camera.is_some());
        if let Some((_, paused)) = &camera {
            let _ = tray.pause.set_text(text::pause_label(*paused));
        }
        if let Ok(mut current) = tray.camera.lock() {
            *current = camera;
        }
    }
    if let Some(icon) = app.tray_by_id("velador") {
        let _ = icon.set_tooltip(Some(format!("Velador · {label}")));
    }
}

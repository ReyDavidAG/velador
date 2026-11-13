use std::process::Command;

use tauri::image::Image;
use tauri::menu::{CheckMenuItem, Menu, MenuEvent, MenuItem, PredefinedMenuItem};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Manager, Wry};
use tauri_plugin_autostart::ManagerExt;

use crate::engine::Summary;
use crate::{nav, text};

pub struct Tray {
    status: MenuItem<Wry>,
    login: CheckMenuItem<Wry>,
}

pub fn build(app: &AppHandle) -> tauri::Result<()> {
    let status = MenuItem::with_id(
        app,
        "status",
        "Consultando la guardia…",
        false,
        None::<&str>,
    )?;
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
        .icon(Image::from_bytes(include_bytes!("../icons/tray.png"))?)
        .icon_as_template(true)
        .tooltip("Velador")
        .menu(&menu)
        .show_menu_on_left_click(true)
        .on_menu_event(on_menu)
        .build(app)?;
    app.manage(Tray { status, login });
    Ok(())
}

fn on_menu(app: &AppHandle, event: MenuEvent) {
    match event.id().as_ref() {
        "open" => crate::show_window(app),
        "browser" => {
            if let Ok(token) = crate::engine::read_token(&crate::engine::state_dir()) {
                let _ = Command::new("/usr/bin/open")
                    .arg(nav::entry(&token).as_str())
                    .spawn();
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
        "quit" => app.exit(0),
        _ => {}
    }
}

pub fn update(app: &AppHandle, summary: &Result<Summary, String>) {
    let label = text::status(summary);
    if let Some(tray) = app.try_state::<Tray>() {
        let _ = tray.status.set_text(&label);
    }
    if let Some(icon) = app.tray_by_id("velador") {
        let _ = icon.set_tooltip(Some(format!("Velador · {label}")));
    }
}

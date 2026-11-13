use std::process::Command;

use tauri::AppHandle;

pub fn quiet(program: &str) -> Command {
    #[allow(unused_mut)]
    let mut command = Command::new(program);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x0800_0000);
    }
    command
}

pub fn open_external(target: &str) {
    #[cfg(target_os = "macos")]
    let _ = Command::new("/usr/bin/open").arg(target).spawn();
    #[cfg(windows)]
    let _ = quiet("explorer.exe").arg(target).spawn();
    #[cfg(not(any(target_os = "macos", windows)))]
    let _ = Command::new("xdg-open").arg(target).spawn();
}

pub fn dock(app: &AppHandle, visible: bool) {
    #[cfg(target_os = "macos")]
    {
        let policy = if visible {
            tauri::ActivationPolicy::Regular
        } else {
            tauri::ActivationPolicy::Accessory
        };
        let _ = app.set_activation_policy(policy);
    }
    #[cfg(not(target_os = "macos"))]
    let _ = (app, visible);
}

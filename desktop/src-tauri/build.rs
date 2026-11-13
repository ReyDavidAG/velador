fn main() {
    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(
        tauri_build::AppManifest::new().commands(&["engine_status", "start_engine"]),
    ))
    .expect("tauri build");
}

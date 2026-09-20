// Orbit for macOS: a thin Tauri shell around orbit-core. The UI is the same web front end
// the Windows app shows (dist/), so all behaviour lives in the core crate.
use orbit_core::{Emit, Host};
use serde_json::Value;
use std::path::PathBuf;
use std::sync::Arc;
use tauri::{Manager, State, WebviewUrl, WebviewWindowBuilder};

struct AppState {
    host: Arc<Host>,
}

fn call(host: &Arc<Host>, message: &Value) -> Result<Value, String> {
    let method = message.get("method").and_then(Value::as_str).unwrap_or("");
    let args = message.get("args").cloned().unwrap_or(Value::Null);
    host.call(method, &args)
}

/// Calls that can take a while (disk, processes). Runs off the main thread.
#[tauri::command]
async fn orbit_rpc(state: State<'_, AppState>, msg: Value) -> Result<Value, String> {
    let host = state.host.clone();
    tauri::async_runtime::spawn_blocking(move || call(&host, &msg)).await.map_err(|e| e.to_string())?
}

/// Cheap calls whose order matters (keystrokes, output acknowledgements). A plain command runs
/// on the main thread in arrival order and only hands work to a queue, so it never blocks.
#[tauri::command]
fn orbit_ordered(state: State<'_, AppState>, msg: Value) -> Result<Value, String> {
    call(&state.host, &msg)
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![orbit_rpc, orbit_ordered])
        .setup(|app| {
            let handle = app.handle().clone();
            let emit: Emit = Arc::new(move |event: Value| {
                if let Some(window) = handle.get_webview_window("main") {
                    let _ = window.eval(format!("window.__orbitEvent && window.__orbitEvent({})", event));
                }
            });
            let home = std::env::var("HOME").unwrap_or_else(|_| "/".to_string());
            let data_root = PathBuf::from(&home).join("Library/Application Support/OrbitAgentDesktop");
            app.manage(AppState { host: Host::new(data_root, home, env!("CARGO_PKG_VERSION").to_string(), emit) });

            WebviewWindowBuilder::new(app, "main", WebviewUrl::App("index.html".into()))
                .title("Orbit")
                .inner_size(1360.0, 860.0)
                .min_inner_size(900.0, 600.0)
                .initialization_script(include_str!("shim.js"))
                .build()?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Orbit could not start");
}

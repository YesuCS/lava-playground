use std::collections::HashMap;
use std::sync::Mutex;

use tauri::{Manager, RunEvent};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;

/// Handles to the backend sidecar processes so we can shut them down on exit.
#[derive(Default)]
struct Sidecars(Mutex<Vec<CommandChild>>);

/// Spawn one bundled sidecar binary, draining its output so it never blocks.
fn spawn_sidecar(
    app: &tauri::AppHandle,
    name: &str,
    env: HashMap<String, String>,
) -> Option<CommandChild> {
    let command = match app.shell().sidecar(name) {
        Ok(cmd) => cmd.envs(env),
        Err(err) => {
            eprintln!("[lava] cannot resolve sidecar {name}: {err}");
            return None;
        }
    };

    match command.spawn() {
        Ok((mut rx, child)) => {
            let label = name.to_string();
            tauri::async_runtime::spawn(async move {
                while let Some(event) = rx.recv().await {
                    if let CommandEvent::Terminated(payload) = event {
                        eprintln!("[lava] sidecar {label} exited: {:?}", payload.code);
                        break;
                    }
                }
            });
            Some(child)
        }
        Err(err) => {
            eprintln!("[lava] failed to spawn sidecar {name}: {err}");
            None
        }
    }
}

/// Open an external http(s) URL in the user's default browser. Used by the
/// "newer version available" banner's "View on GitHub" button.
#[tauri::command]
fn open_url(url: String) {
    if url.starts_with("https://") || url.starts_with("http://") {
        let _ = std::process::Command::new("open").arg(url).spawn();
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .invoke_handler(tauri::generate_handler![open_url])
        .manage(Sidecars::default())
        .setup(|app| {
            let handle = app.handle().clone();
            let mut children = Vec::new();

            // Sidecars watch this PID and self-exit if we die (see the watchdogs
            // in Program.cs / desktop_launcher.py) — no orphans on force-quit.
            let host_pid = std::process::id().to_string();

            // C# render API — pin it to the port the frontend expects.
            let mut api_env = HashMap::new();
            api_env.insert("ASPNETCORE_URLS".into(), "http://127.0.0.1:5133".into());
            api_env.insert("ASPNETCORE_ENVIRONMENT".into(), "Production".into());
            api_env.insert("LAVA_HOST_PID".into(), host_pid.clone());
            if let Some(child) = spawn_sidecar(&handle, "lava-api", api_env) {
                children.push(child);
            }

            // Python linter — its port (8000) is baked into desktop_launcher.py.
            let mut lint_env = HashMap::new();
            lint_env.insert("LAVA_HOST_PID".into(), host_pid);
            if let Some(child) = spawn_sidecar(&handle, "lava-linter", lint_env) {
                children.push(child);
            }

            *app.state::<Sidecars>().0.lock().unwrap() = children;
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building Lava Playground")
        .run(|app, event| {
            if let RunEvent::ExitRequested { .. } = event {
                if let Some(state) = app.try_state::<Sidecars>() {
                    for child in state.0.lock().unwrap().drain(..) {
                        let _ = child.kill();
                    }
                }
            }
        });
}

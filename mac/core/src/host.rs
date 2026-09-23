//! The host side of the UI's `call(method, args)` contract (see src/bridge.js and
//! native/Program.cs). The UI is unchanged; this answers the same method names.
use crate::{files, history, pty::{Pty, SpawnOptions}};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

/// Sends an event (`{"type": ...}`) to the UI.
pub type Emit = Arc<dyn Fn(Value) + Send + Sync>;

const MAX_TERMINALS: usize = 8;
const NOT_YET: &str = "macOS 버전에서는 아직 지원하지 않는 기능입니다.";

pub struct Host {
    data_root: PathBuf,
    home: String,
    version: String,
    emit: Emit,
    sessions: Mutex<HashMap<String, Arc<Pty>>>,
    saved: Mutex<HashMap<String, history::Saved>>,
}

fn text(args: &Value, key: &str) -> String {
    match args.get(key) {
        Some(Value::String(value)) => value.clone(),
        Some(Value::Bool(value)) => value.to_string(),
        Some(Value::Number(value)) => value.to_string(),
        _ => String::new(),
    }
}

fn text_or(args: &Value, key: &str, fallback: &str) -> String {
    let value = text(args, key);
    if value.is_empty() { fallback.to_string() } else { value }
}

fn flag(args: &Value, key: &str) -> bool {
    matches!(args.get(key), Some(Value::Bool(true))) || text(args, key).eq_ignore_ascii_case("true")
}

fn number(args: &Value, key: &str, fallback: i64) -> i64 {
    args.get(key).and_then(Value::as_i64).unwrap_or(fallback)
}

/// Single-quoted for a POSIX shell.
fn quote(value: &str) -> String {
    format!("'{}'", value.replace('\'', "'\\''"))
}

fn is_url(value: &str) -> bool {
    let lower = value.to_ascii_lowercase();
    lower.starts_with("https://") || lower.starts_with("http://")
}

impl Host {
    pub fn new(data_root: PathBuf, home: String, version: String, emit: Emit) -> Arc<Host> {
        Arc::new(Host { data_root, home, version, emit, sessions: Mutex::new(HashMap::new()), saved: Mutex::new(HashMap::new()) })
    }

    fn settings_file(&self) -> PathBuf {
        self.data_root.join("settings.json")
    }

    fn load_settings(&self) -> Value {
        std::fs::read_to_string(self.settings_file()).ok().and_then(|raw| serde_json::from_str(&raw).ok()).unwrap_or(Value::Null)
    }

    fn session(&self, id: &str) -> Result<Arc<Pty>, String> {
        self.sessions.lock().unwrap_or_else(|e| e.into_inner()).get(id).cloned().ok_or_else(|| "터미널을 찾을 수 없습니다.".to_string())
    }

    pub fn call(self: &Arc<Self>, method: &str, args: &Value) -> Result<Value, String> {
        let cwd = text_or(args, "cwd", &self.home);
        match method {
            "init" => Ok(json!({ "folder": self.home, "version": self.version, "settings": self.load_settings(), "testMode": false, "platform": "mac" })),
            "settings" => {
                std::fs::create_dir_all(&self.data_root).map_err(|e| e.to_string())?;
                std::fs::write(self.settings_file(), args.to_string()).map_err(|e| e.to_string())?;
                Ok(Value::Null)
            }
            "theme" | "dirty" | "windowMinimize" | "windowMaximize" | "windowClose" => Ok(Value::Null),
            "pickerPlaces" => Ok(files::picker_places(&self.home)),
            "browse" => files::browse(&text_or(args, "path", &self.home), flag(args, "hidden"), flag(args, "directoriesOnly")),
            "createFile" => files::create_file(&text(args, "path")),
            "list" => files::list(&text(args, "path"), flag(args, "hidden")),
            "move" => files::move_into(&text(args, "path"), &text(args, "target")),
            "read" => files::read(&files::full_path(&text(args, "path"), &cwd)),
            "save" => {
                let revision = match args.get("revision") {
                    Some(Value::String(value)) => Some(value.as_str()),
                    _ => None,
                };
                files::save(&text(args, "path"), &text(args, "content"), &text(args, "encoding"), revision)
            }
            "stat" => Ok(files::stat(&files::full_path(&text(args, "path"), &cwd))),
            "external" => self.open_external(&text(args, "path"), &cwd),
            "clipboard" => {
                let value = text(args, "text");
                if !value.is_empty() {
                    pipe("pbcopy", &value)?;
                }
                Ok(Value::Null)
            }
            "clipboardRead" => Ok(Value::String(capture("pbpaste"))),
            "savedSessions" => {
                let found = history::list(&cwd, flag(args, "allWorkspaces"));
                let mut cache = self.saved.lock().unwrap_or_else(|e| e.into_inner());
                cache.clear();
                for item in &found {
                    cache.insert(format!("{}:{}", item.provider, item.id), item.clone());
                }
                Ok(history::to_json(&found))
            }
            "createTerminal" => self.create_terminal(args),
            "write" => self.session(&text(args, "session"))?.write(&text(args, "data")).map(|_| Value::Null),
            "ack" => {
                if let Ok(terminal) = self.session(&text(args, "session")) {
                    terminal.acknowledge();
                }
                Ok(Value::Null)
            }
            "resize" => {
                let (cols, rows) = (number(args, "cols", 80).clamp(1, 1000) as u16, number(args, "rows", 24).clamp(1, 1000) as u16);
                self.session(&text(args, "session"))?.resize(cols, rows).map(|_| Value::Null)
            }
            "closeTerminal" => {
                let removed = self.sessions.lock().unwrap_or_else(|e| e.into_inner()).remove(&text(args, "session"));
                if let Some(terminal) = removed {
                    terminal.close();
                }
                Ok(Value::Null)
            }
            "terminalName" => Ok(Value::Null),
            "metrics" => Ok(self.metrics()),
            // Status queries the UI sends at start-up; there is no remote access on macOS yet.
            "remoteStatus" | "accountStatus" | "remoteTunnelStatus" | "remoteTailscaleStatus" => Ok(json!({ "enabled": false, "supported": false })),
            _ => Err(NOT_YET.to_string()),
        }
    }

    fn open_external(&self, path: &str, cwd: &str) -> Result<Value, String> {
        let target = if is_url(path) {
            path.to_string()
        } else {
            let full = files::full_path(path, cwd);
            if !full.exists() {
                return Err("파일 또는 폴더를 찾을 수 없습니다.".to_string());
            }
            full.to_string_lossy().into_owned()
        };
        Command::new("open").arg(&target).stdout(Stdio::null()).stderr(Stdio::null()).spawn().map_err(|e| e.to_string())?;
        Ok(Value::Null)
    }

    fn metrics(&self) -> Value {
        let rss = capture_args("ps", &["-o", "rss=", "-p", &std::process::id().to_string()]).trim().parse::<f64>().unwrap_or(0.0);
        json!({ "hostMemoryMb": (rss / 1024.0 * 10.0).round() / 10.0, "hostCpu": 0, "note": "앱 호스트만 측정 · 웹 화면과 터미널 프로세스는 별도입니다." })
    }

    fn create_terminal(self: &Arc<Self>, args: &Value) -> Result<Value, String> {
        let id = text(args, "session");
        if id.trim().is_empty() {
            return Err("터미널 식별자가 없습니다.".to_string());
        }
        let profile = text_or(args, "profile", "powershell");
        if !["codex", "claude", "powershell", "cmd"].contains(&profile.as_str()) {
            return Err("Unsupported terminal profile.".to_string());
        }
        let resume = text(args, "resumeId");
        let mut cwd = text_or(args, "cwd", &self.home);
        if !resume.is_empty() {
            if (profile != "codex" && profile != "claude") || !history::is_id(&resume) {
                return Err("Invalid saved session.".to_string());
            }
            let cache = self.saved.lock().unwrap_or_else(|e| e.into_inner());
            let saved = cache.get(&format!("{}:{}", profile, resume)).ok_or("Refresh saved sessions before resuming.")?;
            cwd = saved.cwd.clone();
        }
        if !Path::new(&cwd).is_dir() {
            return Err("작업 폴더를 찾을 수 없습니다.".to_string());
        }

        let shell = login_shell();
        let script = match profile.as_str() {
            "claude" | "codex" => {
                let launch = match (profile.as_str(), resume.is_empty()) {
                    ("claude", true) => "claude".to_string(),
                    ("claude", false) => format!("claude --resume {}", resume),
                    (_, true) => "codex".to_string(),
                    (_, false) => format!("codex -C {} resume {}", quote(&cwd), resume),
                };
                // The shell stays open afterwards, like the Windows version's -NoExit.
                Some(format!(
                    "if command -v {name} >/dev/null 2>&1; then {launch}; else echo '{name} CLI is not installed or is not on PATH.'; fi; exec {shell} -l",
                    name = profile,
                    launch = launch,
                    shell = quote(&shell)
                ))
            }
            _ => None,
        };
        let mut shell_args = vec!["-l".to_string()];
        if let Some(script) = script {
            shell_args = vec!["-l".to_string(), "-i".to_string(), "-c".to_string(), script];
        }
        let mut env = vec![("TERM".to_string(), "xterm-256color".to_string()), ("COLORTERM".to_string(), "truecolor".to_string()), ("SHELL".to_string(), shell.clone())];
        // An app started from Finder has no locale; without one many tools print garbage for non-ASCII text.
        if std::env::var("LANG").map(|v| v.is_empty()).unwrap_or(true) {
            env.push(("LANG".to_string(), "en_US.UTF-8".to_string()));
        }
        let options = SpawnOptions {
            program: shell,
            args: shell_args,
            cwd,
            env,
            cols: number(args, "cols", 100).clamp(10, 500) as u16,
            rows: number(args, "rows", 30).clamp(3, 300) as u16,
        };

        // Held until the terminal is registered, so a program that exits at once cannot report
        // its exit before it exists in the table.
        let mut table = self.sessions.lock().unwrap_or_else(|e| e.into_inner());
        if table.contains_key(&id) {
            return Err("이미 생성 중인 터미널입니다.".to_string());
        }
        if table.len() >= MAX_TERMINALS {
            return Err("동시에 8개까지 터미널을 열 수 있습니다.".to_string());
        }
        let sequence = Arc::new(AtomicU64::new(0));
        let (emit, key) = (self.emit.clone(), id.clone());
        let on_data = move |chunk: String| {
            let seq = sequence.fetch_add(1, Ordering::SeqCst) + 1;
            emit(json!({ "type": "output", "session": key, "data": chunk, "seq": seq }));
        };
        let (host, key) = (self.clone(), id.clone());
        let on_exit = move |code: i32| {
            // A terminal the user closed is already gone; only an unexpected end is reported.
            let ended = host.sessions.lock().unwrap_or_else(|e| e.into_inner()).remove(&key).is_some();
            if ended {
                (host.emit)(json!({ "type": "exit", "session": key, "code": code }));
            }
        };
        let terminal = Pty::spawn(&id, options, on_data, on_exit)?;
        let pid = terminal.pid;
        table.insert(id, terminal);
        Ok(json!({ "pid": pid }))
    }
}

fn login_shell() -> String {
    match std::env::var("SHELL") {
        Ok(value) if Path::new(&value).exists() => value,
        _ if Path::new("/bin/zsh").exists() => "/bin/zsh".to_string(),
        _ => "/bin/sh".to_string(),
    }
}

fn capture(program: &str) -> String {
    capture_args(program, &[])
}

fn capture_args(program: &str, args: &[&str]) -> String {
    Command::new(program).args(args).stderr(Stdio::null()).output().map(|o| String::from_utf8_lossy(&o.stdout).into_owned()).unwrap_or_default()
}

fn pipe(program: &str, input: &str) -> Result<(), String> {
    use std::io::Write;
    let mut child = Command::new(program).stdin(Stdio::piped()).stdout(Stdio::null()).stderr(Stdio::null()).spawn().map_err(|e| e.to_string())?;
    if let Some(stdin) = child.stdin.as_mut() {
        stdin.write_all(input.as_bytes()).map_err(|e| e.to_string())?;
    }
    child.wait().map(|_| ()).map_err(|e| e.to_string())
}

//! File explorer and editor backend. Mirrors native/Files.cs so both hosts behave the same.
use base64::Engine;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::fs;
use std::io::Write;
use std::path::{Component, Path, PathBuf};
use std::sync::Mutex;

pub const MAX_BYTES: u64 = 4 * 1024 * 1024;
const MAX_ENTRIES: usize = 1500;
static SAVE_LOCK: Mutex<()> = Mutex::new(());

fn io_msg(error: std::io::Error) -> String {
    match error.kind() {
        std::io::ErrorKind::NotFound => "파일 또는 폴더를 찾을 수 없습니다.".to_string(),
        std::io::ErrorKind::PermissionDenied => "접근 권한이 없습니다. 시스템 설정에서 Orbit의 파일 접근을 허용해 주세요.".to_string(),
        _ => error.to_string(),
    }
}

/// Lexical normalisation like .NET's Path.GetFullPath: no symlink resolution.
pub fn normalize(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::ParentDir => {
                out.pop();
            }
            Component::CurDir => {}
            other => out.push(other.as_os_str()),
        }
    }
    if out.as_os_str().is_empty() {
        out.push(std::path::MAIN_SEPARATOR_STR);
    }
    out
}

pub fn full_path(path: &str, cwd: &str) -> PathBuf {
    // Terminals print "~/notes.md"; the shell would expand it, so do the same.
    let expanded;
    let path = match (path.strip_prefix("~/"), std::env::var("HOME")) {
        (Some(rest), Ok(home)) if !home.is_empty() => {
            expanded = format!("{}/{}", home.trim_end_matches('/'), rest);
            expanded.as_str()
        }
        _ => path,
    };
    let given = Path::new(path);
    normalize(&if given.is_absolute() { given.to_path_buf() } else { Path::new(cwd).join(given) })
}

fn text(path: &Path) -> String {
    path.to_string_lossy().into_owned()
}

fn shown(name: &str, hidden: bool) -> bool {
    hidden || !matches!(name, ".git" | "node_modules" | ".tools" | ".DS_Store")
}

fn is_dir(entry: &fs::DirEntry) -> bool {
    match entry.file_type() {
        Ok(kind) if kind.is_symlink() => fs::metadata(entry.path()).map(|m| m.is_dir()).unwrap_or(false),
        Ok(kind) => kind.is_dir(),
        Err(_) => false,
    }
}

fn entries(dir: &Path, hidden: bool, directories_only: bool) -> Result<(Vec<Value>, bool), String> {
    let mut items: Vec<(bool, String, String)> = Vec::new();
    for entry in fs::read_dir(dir).map_err(io_msg)?.flatten() {
        let name = entry.file_name().to_string_lossy().into_owned();
        if !shown(&name, hidden) {
            continue;
        }
        let directory = is_dir(&entry);
        if directories_only && !directory {
            continue;
        }
        items.push((directory, name, text(&entry.path())));
        if items.len() > MAX_ENTRIES {
            break;
        }
    }
    items.sort_by(|a, b| b.0.cmp(&a.0).then_with(|| a.1.to_lowercase().cmp(&b.1.to_lowercase())));
    let truncated = items.len() > MAX_ENTRIES;
    items.truncate(MAX_ENTRIES);
    let list = items.into_iter().map(|(directory, name, path)| json!({ "name": name, "path": path, "directory": directory })).collect();
    Ok((list, truncated))
}

pub fn list(path: &str, hidden: bool) -> Result<Value, String> {
    let (list, truncated) = entries(Path::new(path), hidden, false)?;
    Ok(json!({ "entries": list, "truncated": truncated }))
}

pub fn browse(path: &str, hidden: bool, directories_only: bool) -> Result<Value, String> {
    let full = normalize(Path::new(path));
    if !full.is_dir() {
        return Err("폴더를 찾을 수 없습니다.".to_string());
    }
    let (list, truncated) = entries(&full, hidden, directories_only)?;
    let parent = full.parent().map(text);
    Ok(json!({ "path": text(&full), "parent": parent, "entries": list, "truncated": truncated }))
}

pub fn picker_places(home: &str) -> Value {
    let home_path = Path::new(home);
    let mut drives = vec![json!({ "name": "/", "path": "/" })];
    if let Ok(volumes) = fs::read_dir("/Volumes") {
        let mut names: Vec<String> = volumes.flatten().map(|e| e.file_name().to_string_lossy().into_owned()).collect();
        names.sort();
        for name in names {
            drives.push(json!({ "name": name, "path": format!("/Volumes/{}", name) }));
        }
    }
    json!({
        "home": home,
        "desktop": text(&home_path.join("Desktop")),
        "documents": text(&home_path.join("Documents")),
        "downloads": text(&home_path.join("Downloads")),
        "drives": drives
    })
}

pub fn stat(path: &Path) -> Value {
    json!({ "path": text(path), "directory": path.is_dir(), "exists": path.exists() })
}

pub fn create_file(path: &str) -> Result<Value, String> {
    let full = normalize(Path::new(path));
    let parent = full.parent().filter(|p| p.is_dir()).ok_or("대상 폴더를 찾을 수 없습니다.")?;
    let name = full.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    if name.trim().is_empty() || name == "." || name == ".." || name.contains('/') || name.contains('\0') {
        return Err("유효한 새 파일 이름을 입력해 주세요.".to_string());
    }
    fs::OpenOptions::new().write(true).create_new(true).open(parent.join(&name)).map_err(|e| {
        if e.kind() == std::io::ErrorKind::AlreadyExists {
            "이미 같은 이름의 파일이 있습니다.".to_string()
        } else {
            io_msg(e)
        }
    })?;
    Ok(json!({ "path": text(&full) }))
}

fn hash(bytes: &[u8]) -> String {
    base64::engine::general_purpose::STANDARD.encode(Sha256::digest(bytes))
}

fn decode(bytes: &[u8]) -> Result<(String, &'static str), String> {
    let fail = || "파일을 이 인코딩으로 읽을 수 없습니다.".to_string();
    if bytes.starts_with(&[0xEF, 0xBB, 0xBF]) {
        return std::str::from_utf8(&bytes[3..]).map(|s| (s.to_string(), "UTF-8 BOM")).map_err(|_| fail());
    }
    if bytes.starts_with(&[0xFF, 0xFE]) {
        return encoding_rs::UTF_16LE
            .decode_without_bom_handling_and_without_replacement(&bytes[2..])
            .map(|s| (s.into_owned(), "UTF-16 LE"))
            .ok_or_else(fail);
    }
    if bytes.starts_with(&[0xFE, 0xFF]) {
        return encoding_rs::UTF_16BE
            .decode_without_bom_handling_and_without_replacement(&bytes[2..])
            .map(|s| (s.into_owned(), "UTF-16 BE"))
            .ok_or_else(fail);
    }
    if bytes.iter().take(8192).any(|&b| b == 0) {
        return Err("바이너리 파일은 기본 앱에서 열어 주세요.".to_string());
    }
    match std::str::from_utf8(bytes) {
        Ok(s) => Ok((s.to_string(), "UTF-8")),
        Err(_) => encoding_rs::EUC_KR
            .decode_without_bom_handling_and_without_replacement(bytes)
            .map(|s| (s.into_owned(), "CP949"))
            .ok_or_else(fail),
    }
}

pub fn read(path: &Path) -> Result<Value, String> {
    let meta = fs::metadata(path).map_err(io_msg)?;
    if meta.len() > MAX_BYTES {
        return Err("가벼운 편집을 위해 4MB 이하의 텍스트 파일만 열 수 있습니다. 기본 앱에서 열어 주세요.".to_string());
    }
    let bytes = fs::read(path).map_err(io_msg)?;
    let (content, label) = decode(&bytes)?;
    let newline = if content.contains("\r\n") { "CRLF" } else { "LF" };
    let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    Ok(json!({ "path": text(path), "name": name, "content": content, "encoding": label, "revision": hash(&bytes), "newline": newline }))
}

fn encode(content: &str, encoding: &str) -> Result<Vec<u8>, String> {
    Ok(match encoding {
        "UTF-8 BOM" => [&[0xEF, 0xBB, 0xBF][..], content.as_bytes()].concat(),
        "UTF-16 LE" => {
            let mut out = vec![0xFF, 0xFE];
            out.extend(content.encode_utf16().flat_map(|unit| unit.to_le_bytes()));
            out
        }
        "UTF-16 BE" => {
            let mut out = vec![0xFE, 0xFF];
            out.extend(content.encode_utf16().flat_map(|unit| unit.to_be_bytes()));
            out
        }
        "CP949" => {
            let (bytes, _, unmappable) = encoding_rs::EUC_KR.encode(content);
            if unmappable {
                return Err("CP949로 표현할 수 없는 문자가 있어 저장하지 못했습니다.".to_string());
            }
            bytes.into_owned()
        }
        _ => content.as_bytes().to_vec(),
    })
}

const CHANGED: &str = "다른 프로그램이 파일을 변경했습니다. 변경 내용을 복사한 뒤 파일을 다시 열어 주세요.";

pub fn save(path: &str, content: &str, encoding: &str, revision: Option<&str>) -> Result<Value, String> {
    let full = normalize(Path::new(path));
    let _guard = SAVE_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let bytes = encode(content, encoding)?;
    if bytes.len() as u64 > MAX_BYTES {
        return Err("파일이 4MB를 초과합니다.".to_string());
    }
    let exists = full.is_file();
    let unchanged = |expected: Option<&str>| -> bool {
        match (fs::metadata(&full), fs::read(&full), expected) {
            (Ok(meta), Ok(current), Some(rev)) => meta.len() <= MAX_BYTES && hash(&current) == rev,
            _ => false,
        }
    };
    if exists && !unchanged(revision) {
        return Err(CHANGED.to_string());
    }
    if !exists && revision.is_some() {
        return Err("파일이 이동 또는 삭제되었습니다. 새 파일로 저장해 주세요.".to_string());
    }
    let dir = full.parent().ok_or("저장할 폴더를 찾을 수 없습니다.")?;
    let stamp = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or(0);
    let temp = dir.join(format!(".orbit-{:x}-{:x}.tmp", stamp, std::process::id()));
    let result = (|| -> Result<(), String> {
        let mut file = fs::OpenOptions::new().write(true).create_new(true).open(&temp).map_err(io_msg)?;
        file.write_all(&bytes).map_err(io_msg)?;
        file.sync_all().map_err(io_msg)?;
        drop(file);
        if exists {
            // Keep the original mode (an executable script must stay executable).
            if let Ok(meta) = fs::metadata(&full) {
                let _ = fs::set_permissions(&temp, meta.permissions());
            }
            // Check again right before replacing, narrowing the window for an external edit.
            if !unchanged(revision) {
                return Err(CHANGED.to_string());
            }
        }
        fs::rename(&temp, &full).map_err(io_msg)
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temp);
    }
    result?;
    Ok(json!({ "revision": hash(&bytes) }))
}

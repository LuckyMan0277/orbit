//! Reads the small local metadata needed to resume a Claude or Codex conversation. Like
//! native/SessionHistory.cs it never watches or indexes the history folders: it looks at the
//! newest few files when asked.
use serde_json::{json, Value};
use std::collections::HashMap;
use std::fs;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

const MAX_FILES: usize = 80;
const MAX_BYTES: u64 = 256 * 1024;

#[derive(Clone, Debug)]
pub struct Saved {
    pub provider: &'static str,
    pub id: String,
    pub title: String,
    pub cwd: String,
    pub updated: SystemTime,
    rank: u8,
}

pub fn is_id(value: &str) -> bool {
    let parts: Vec<&str> = value.split('-').collect();
    parts.len() == 5
        && [8, 4, 4, 4, 12].iter().zip(&parts).all(|(len, part)| part.len() == *len && part.bytes().all(|b| b.is_ascii_hexdigit()))
}

fn home_dir(variable: &str, fallback: &str) -> PathBuf {
    match std::env::var(variable) {
        Ok(value) if !value.is_empty() => PathBuf::from(value),
        _ => PathBuf::from(std::env::var("HOME").unwrap_or_default()).join(fallback),
    }
}

fn modified(path: &Path) -> SystemTime {
    fs::metadata(path).and_then(|m| m.modified()).unwrap_or(UNIX_EPOCH)
}

/// Up to MAX_BYTES of a file, as parsed JSON lines. A cut-off last line is simply skipped.
fn lines(path: &Path) -> Vec<Value> {
    let mut text = String::new();
    if let Ok(file) = fs::File::open(path) {
        let mut bytes = Vec::new();
        if file.take(MAX_BYTES).read_to_end(&mut bytes).is_ok() {
            text = String::from_utf8_lossy(&bytes).into_owned();
        }
    }
    text.lines().filter_map(|line| serde_json::from_str::<Value>(line).ok()).collect()
}

fn text(row: &Value, key: &str) -> String {
    row.get(key).and_then(Value::as_str).unwrap_or("").trim().to_string()
}

fn limit(value: &str) -> String {
    let flat: String = value.split_whitespace().collect::<Vec<_>>().join(" ");
    if flat.chars().count() > 90 {
        format!("{}…", flat.chars().take(89).collect::<String>())
    } else {
        flat
    }
}

/// (title, rank): a custom title beats a summary, which beats a name, which beats the first prompt.
fn title(row: &Value) -> (String, u8) {
    for key in ["customTitle", "custom_title", "custom-title"] {
        let value = text(row, key);
        if !value.is_empty() {
            return (limit(&value), 4);
        }
    }
    let summary = text(row, "summary");
    if !summary.is_empty() {
        return (limit(&summary), 3);
    }
    for key in ["thread_name", "title"] {
        let value = text(row, key);
        if !value.is_empty() {
            return (limit(&value), 2);
        }
    }
    let prompt = user_text(row);
    if prompt.is_empty() { (String::new(), 0) } else { (limit(&prompt), 1) }
}

fn user_text(row: &Value) -> String {
    if row.get("type").and_then(Value::as_str) != Some("user") {
        return String::new();
    }
    let content = &row["message"]["content"];
    if let Some(value) = content.as_str() {
        return value.trim().to_string();
    }
    content
        .as_array()
        .and_then(|parts| parts.iter().find_map(|part| part.get("text").and_then(Value::as_str)))
        .unwrap_or("")
        .trim()
        .to_string()
}

fn allowed(cwd: &str, workspace: &str, all: bool) -> bool {
    if cwd.trim().is_empty() || !Path::new(cwd).is_dir() {
        return false;
    }
    all || cwd.trim_end_matches('/').eq_ignore_ascii_case(workspace.trim_end_matches('/'))
}

fn newest(mut files: Vec<PathBuf>) -> Vec<PathBuf> {
    files.sort_by_key(|f| std::cmp::Reverse(modified(f)));
    files.truncate(MAX_FILES);
    files
}

fn jsonl_in(dir: &Path) -> Vec<PathBuf> {
    fs::read_dir(dir)
        .map(|rd| rd.flatten().map(|e| e.path()).filter(|p| p.extension().map(|x| x == "jsonl").unwrap_or(false)).collect())
        .unwrap_or_default()
}

fn subdirs(dir: &Path) -> Vec<PathBuf> {
    fs::read_dir(dir).map(|rd| rd.flatten().map(|e| e.path()).filter(|p| p.is_dir()).collect()).unwrap_or_default()
}

fn read_codex(home: &Path, workspace: &str, all: bool) -> Vec<Saved> {
    let mut names: HashMap<String, String> = HashMap::new();
    for row in lines(&home.join("session_index.jsonl")) {
        let (id, name) = (text(&row, "id"), title(&row).0);
        if is_id(&id) && !name.is_empty() {
            names.insert(id, name);
        }
    }
    // sessions/YYYY/MM/DD/*.jsonl
    let mut files = Vec::new();
    for year in subdirs(&home.join("sessions")) {
        for month in subdirs(&year) {
            for day in subdirs(&month) {
                files.extend(jsonl_in(&day));
            }
        }
    }
    let mut found: HashMap<String, Saved> = HashMap::new();
    for file in newest(files) {
        for row in lines(&file) {
            let payload = row.get("payload").cloned().unwrap_or(Value::Null);
            let (id, cwd) = (text(&payload, "id"), text(&payload, "cwd"));
            if !is_id(&id) || !allowed(&cwd, workspace, all) {
                continue;
            }
            let entry = found.entry(id.clone()).or_insert_with(|| Saved { provider: "codex", id, title: String::new(), cwd, updated: modified(&file), rank: 0 });
            let (candidate, rank) = title(&payload);
            if !candidate.is_empty() && rank >= entry.rank {
                entry.title = candidate;
                entry.rank = rank;
            }
        }
    }
    found
        .into_values()
        .map(|mut item| {
            if let Some(name) = names.get(&item.id) {
                item.title = name.clone();
            }
            if item.title.is_empty() {
                item.title = format!("Codex {}", &item.id[..8]);
            }
            item
        })
        .collect()
}

fn read_claude(home: &Path, workspace: &str, all: bool) -> Vec<Saved> {
    let mut files = Vec::new();
    for project in subdirs(&home.join("projects")) {
        files.extend(jsonl_in(&project));
    }
    let mut found: HashMap<String, Saved> = HashMap::new();
    for file in newest(files) {
        for row in lines(&file) {
            let (id, cwd) = (text(&row, "sessionId"), text(&row, "cwd"));
            if !is_id(&id) || row.get("isSidechain").and_then(Value::as_bool).unwrap_or(false) {
                continue;
            }
            if !found.contains_key(&id) {
                if !allowed(&cwd, workspace, all) {
                    continue;
                }
                found.insert(id.clone(), Saved { provider: "claude", id: id.clone(), title: String::new(), cwd, updated: modified(&file), rank: 0 });
            }
            let entry = found.get_mut(&id).expect("just inserted");
            let (candidate, rank) = title(&row);
            if !candidate.is_empty() && (rank > entry.rank || (rank > 1 && rank == entry.rank)) {
                entry.title = candidate;
                entry.rank = rank;
            }
        }
    }
    found
        .into_values()
        .map(|mut item| {
            if item.title.is_empty() {
                item.title = format!("Claude {}", &item.id[..8]);
            }
            item
        })
        .collect()
}

pub fn list(workspace: &str, all: bool) -> Vec<Saved> {
    let mut result = read_codex(&home_dir("CODEX_HOME", ".codex"), workspace, all);
    result.extend(read_claude(&home_dir("CLAUDE_CONFIG_DIR", ".claude"), workspace, all));
    result.sort_by_key(|item| std::cmp::Reverse(item.updated));
    result.truncate(MAX_FILES * 2);
    result
}

/// RFC 3339 in UTC, without a date-time dependency (days-from-civil algorithm).
pub fn iso(time: SystemTime) -> String {
    let seconds = time.duration_since(UNIX_EPOCH).map(|d| d.as_secs() as i64).unwrap_or(0);
    let (days, rest) = (seconds.div_euclid(86_400), seconds.rem_euclid(86_400));
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = yoe + era * 400 + i64::from(month <= 2);
    format!("{:04}-{:02}-{:02}T{:02}:{:02}:{:02}Z", year, month, day, rest / 3600, rest % 3600 / 60, rest % 60)
}

pub fn to_json(items: &[Saved]) -> Value {
    let list: Vec<Value> = items
        .iter()
        .map(|x| json!({ "provider": x.provider, "id": x.id, "title": x.title, "cwd": x.cwd, "updated": iso(x.updated) }))
        .collect();
    json!({ "sessions": list })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ids_are_canonical_guids() {
        assert!(is_id("123e4567-e89b-12d3-a456-426614174000"));
        assert!(!is_id("123e4567e89b12d3a456426614174000"));
        assert!(!is_id("../../etc/passwd"));
    }

    #[test]
    fn iso_formats_known_instants() {
        assert_eq!(iso(UNIX_EPOCH), "1970-01-01T00:00:00Z");
        assert_eq!(iso(UNIX_EPOCH + std::time::Duration::from_secs(1_800_000_000)), "2027-01-15T08:00:00Z");
    }
}

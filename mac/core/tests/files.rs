use orbit_core::files;
use serde_json::Value;
use std::fs;
use std::path::PathBuf;

fn scratch(name: &str) -> PathBuf {
    let dir = std::env::temp_dir().join(format!("orbit-core-{}-{}", name, std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    dir
}

fn field<'a>(value: &'a Value, key: &str) -> &'a str {
    value[key].as_str().unwrap_or_else(|| panic!("missing {} in {}", key, value))
}

#[test]
fn reads_and_saves_with_the_same_encoding_and_newlines() {
    let dir = scratch("encodings");
    for (encoding, bytes) in [
        ("UTF-8", "안녕 line1\r\nline2".as_bytes().to_vec()),
        ("UTF-8 BOM", [&[0xEF, 0xBB, 0xBF][..], "한글".as_bytes()].concat()),
        ("UTF-16 LE", [&[0xFF, 0xFE][..], &"한글\r\n".encode_utf16().flat_map(|u| u.to_le_bytes()).collect::<Vec<u8>>()[..]].concat()),
        ("CP949", encoding_rs::EUC_KR.encode("한글 파일").0.into_owned()),
    ] {
        let path = dir.join(format!("{}.txt", encoding.replace(' ', "-")));
        fs::write(&path, &bytes).unwrap();
        let read = files::read(&path).unwrap();
        assert_eq!(field(&read, "encoding"), encoding);
        let content = field(&read, "content").to_string();
        let saved = files::save(path.to_str().unwrap(), &content, encoding, Some(field(&read, "revision"))).unwrap();
        assert_eq!(fs::read(&path).unwrap(), bytes, "{} must round-trip byte for byte", encoding);
        assert!(saved["revision"].is_string());
    }
    let crlf = dir.join("crlf.txt");
    fs::write(&crlf, "a\r\nb").unwrap();
    assert_eq!(field(&files::read(&crlf).unwrap(), "newline"), "CRLF");
}

#[test]
fn refuses_to_overwrite_a_file_changed_elsewhere() {
    let dir = scratch("conflict");
    let path = dir.join("a.txt");
    fs::write(&path, "one").unwrap();
    let read = files::read(&path).unwrap();
    fs::write(&path, "changed by an agent").unwrap();
    let error = files::save(path.to_str().unwrap(), "mine", "UTF-8", Some(field(&read, "revision"))).unwrap_err();
    assert!(error.contains("다른 프로그램이"), "{}", error);
    assert_eq!(fs::read_to_string(&path).unwrap(), "changed by an agent");
    assert!(files::save(path.to_str().unwrap(), "x", "UTF-8", None).is_err(), "an existing file needs a revision");
}

#[cfg(unix)]
#[test]
fn saving_keeps_the_executable_bit() {
    use std::os::unix::fs::PermissionsExt;
    let dir = scratch("mode");
    let path = dir.join("run.sh");
    fs::write(&path, "#!/bin/sh\n").unwrap();
    fs::set_permissions(&path, fs::Permissions::from_mode(0o755)).unwrap();
    let read = files::read(&path).unwrap();
    files::save(path.to_str().unwrap(), "#!/bin/sh\necho hi\n", "UTF-8", Some(field(&read, "revision"))).unwrap();
    assert_eq!(fs::metadata(&path).unwrap().permissions().mode() & 0o777, 0o755);
}

#[test]
fn binary_and_large_files_are_not_opened() {
    let dir = scratch("binary");
    let binary = dir.join("a.bin");
    fs::write(&binary, [0u8, 1, 2, 3]).unwrap();
    assert!(files::read(&binary).unwrap_err().contains("바이너리"));
    let big = dir.join("big.txt");
    fs::write(&big, vec![b'a'; (files::MAX_BYTES + 1) as usize]).unwrap();
    assert!(files::read(&big).unwrap_err().contains("4MB"));
}

#[test]
fn browse_sorts_folders_first_and_hides_noise() {
    let dir = scratch("browse");
    for folder in ["zeta", ".git", "node_modules", "Alpha"] {
        fs::create_dir(dir.join(folder)).unwrap();
    }
    fs::write(dir.join("b.txt"), "").unwrap();
    fs::write(dir.join(".env"), "").unwrap();
    let listing = files::browse(dir.to_str().unwrap(), false, false).unwrap();
    let names: Vec<&str> = listing["entries"].as_array().unwrap().iter().map(|e| e["name"].as_str().unwrap()).collect();
    assert_eq!(names, ["Alpha", "zeta", ".env", "b.txt"]);
    let only = files::browse(dir.to_str().unwrap(), true, true).unwrap();
    assert_eq!(only["entries"].as_array().unwrap().len(), 4);
    assert!(files::browse(dir.join("missing").to_str().unwrap(), false, false).is_err());
}

#[test]
fn creating_a_file_rejects_bad_names_and_duplicates() {
    let dir = scratch("create");
    let path = dir.join("new.md");
    files::create_file(path.to_str().unwrap()).unwrap();
    assert!(path.exists());
    assert!(files::create_file(path.to_str().unwrap()).unwrap_err().contains("이미"));
    assert!(files::create_file(dir.join("no-such-dir/x.md").to_str().unwrap()).is_err());
}

#[test]
fn relative_paths_resolve_against_the_project_folder() {
    assert_eq!(files::full_path("src/../a.txt", "/work/proj"), PathBuf::from("/work/proj/a.txt"));
    assert_eq!(files::full_path("/etc/hosts", "/work/proj"), PathBuf::from("/etc/hosts"));
}

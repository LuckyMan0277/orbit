//! Real terminals: these spawn actual shells, so they only run on Unix (the macOS CI runner).
#![cfg(unix)]

use orbit_core::{Emit, Host};
use serde_json::{json, Value};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

struct Rig {
    host: Arc<Host>,
    events: Arc<Mutex<Vec<Value>>>,
}

fn rig() -> Rig {
    let events: Arc<Mutex<Vec<Value>>> = Arc::new(Mutex::new(Vec::new()));
    let sink = events.clone();
    let emit: Emit = Arc::new(move |event| sink.lock().unwrap().push(event));
    let root = std::env::temp_dir().join(format!("orbit-host-{}", std::process::id()));
    let home = std::env::temp_dir().to_string_lossy().into_owned();
    Rig { host: Host::new(root, home, "0.0.0".into(), emit), events }
}

impl Rig {
    /// Everything the terminal printed so far, acknowledging chunks the way the UI does.
    fn output(&self, session: &str) -> String {
        let events = self.events.lock().unwrap();
        events.iter().filter(|e| e["type"] == "output" && e["session"] == session).filter_map(|e| e["data"].as_str()).collect()
    }
    fn wait_for(&self, session: &str, what: &str) {
        let start = Instant::now();
        while start.elapsed() < Duration::from_secs(20) {
            if self.output(session).contains(what) {
                return;
            }
            let _ = self.host.call("ack", &json!({ "session": session }));
            std::thread::sleep(Duration::from_millis(25));
        }
        panic!("timed out waiting for {:?}; got {:?}", what, self.output(session));
    }
    fn exit_code(&self, session: &str) -> Option<i64> {
        self.events.lock().unwrap().iter().find(|e| e["type"] == "exit" && e["session"] == session).and_then(|e| e["code"].as_i64())
    }
}

fn create(rig: &Rig, session: &str, profile: &str) -> Value {
    rig.host.call("createTerminal", &json!({ "session": session, "profile": profile, "cols": 100, "rows": 30 })).unwrap()
}

#[test]
fn a_shell_runs_commands_and_prints_unicode() {
    let rig = rig();
    let made = create(&rig, "t1", "powershell");
    assert!(made["pid"].as_u64().unwrap() > 0);
    rig.host.call("write", &json!({ "session": "t1", "data": "echo 안녕-$((6*7))\r" })).unwrap();
    rig.wait_for("t1", "안녕-42");
    rig.host.call("closeTerminal", &json!({ "session": "t1" })).unwrap();
}

#[test]
fn input_keeps_its_order_and_resize_reaches_the_program() {
    let rig = rig();
    create(&rig, "t2", "cmd");
    rig.host.call("resize", &json!({ "session": "t2", "cols": 123, "rows": 45 })).unwrap();
    rig.host.call("write", &json!({ "session": "t2", "data": "echo AB" })).unwrap();
    rig.host.call("write", &json!({ "session": "t2", "data": "CD; stty size\r" })).unwrap();
    rig.wait_for("t2", "ABCD");
    rig.wait_for("t2", "45 123");
    rig.host.call("closeTerminal", &json!({ "session": "t2" })).unwrap();
}

#[test]
fn exiting_reports_the_exit_code_once() {
    let rig = rig();
    create(&rig, "t3", "cmd");
    rig.host.call("write", &json!({ "session": "t3", "data": "exit 7\r" })).unwrap();
    let start = Instant::now();
    while rig.exit_code("t3").is_none() && start.elapsed() < Duration::from_secs(20) {
        let _ = rig.host.call("ack", &json!({ "session": "t3" }));
        std::thread::sleep(Duration::from_millis(25));
    }
    assert_eq!(rig.exit_code("t3"), Some(7));
    assert!(rig.host.call("write", &json!({ "session": "t3", "data": "x" })).is_err(), "a finished terminal is gone");
}

#[test]
fn closing_does_not_report_an_exit() {
    let rig = rig();
    create(&rig, "t4", "cmd");
    rig.host.call("closeTerminal", &json!({ "session": "t4" })).unwrap();
    std::thread::sleep(Duration::from_millis(800));
    assert!(rig.exit_code("t4").is_none());
}

#[test]
fn terminal_limits_and_validation() {
    let rig = rig();
    assert!(rig.host.call("createTerminal", &json!({ "session": "x", "profile": "bash" })).is_err());
    assert!(rig.host.call("createTerminal", &json!({ "session": "x", "cwd": "/no/such/folder" })).is_err());
    assert!(rig.host.call("createTerminal", &json!({ "session": "x", "profile": "claude", "resumeId": "../../x" })).is_err());
    for n in 0..8 {
        create(&rig, &format!("many{}", n), "cmd");
    }
    let error = rig.host.call("createTerminal", &json!({ "session": "many8" })).unwrap_err();
    assert!(error.contains("8개"), "{}", error);
    assert!(rig.host.call("createTerminal", &json!({ "session": "many0" })).is_err(), "a duplicate id is refused");
    for n in 0..8 {
        rig.host.call("closeTerminal", &json!({ "session": format!("many{}", n) })).unwrap();
    }
}

#[test]
fn settings_and_init_round_trip() {
    let rig = rig();
    rig.host.call("settings", &json!({ "theme": "light", "fontSize": 15 })).unwrap();
    let init = rig.host.call("init", &Value::Null).unwrap();
    assert_eq!(init["settings"]["theme"], "light");
    assert_eq!(init["platform"], "mac");
    assert!(rig.host.call("secretSet", &json!({})).is_err(), "unsupported features fail with a message, not a crash");
}

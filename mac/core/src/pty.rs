//! One pseudo terminal. Behaves like native/ConPty.cs: output is sent to the UI one chunk at a
//! time and the reader waits for the UI's acknowledgement before it reads more, so a busy
//! program cannot flood the renderer.
use portable_pty::{native_pty_system, ChildKiller, CommandBuilder, MasterPty, PtySize};
use std::io::{Read, Write};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::mpsc;
use std::sync::{Arc, Condvar, Mutex};
use std::thread;

const CHUNK: usize = 8192;
const INPUT_LIMIT: usize = 1024 * 1024;

pub struct SpawnOptions {
    pub program: String,
    pub args: Vec<String>,
    pub cwd: String,
    pub env: Vec<(String, String)>,
    pub cols: u16,
    pub rows: u16,
}

struct Shared {
    ack: Mutex<bool>,
    wake: Condvar,
    closed: AtomicBool,
}

impl Shared {
    fn wait_ack(&self) {
        let mut ready = self.ack.lock().unwrap_or_else(|e| e.into_inner());
        while !*ready {
            ready = self.wake.wait(ready).unwrap_or_else(|e| e.into_inner());
        }
        *ready = false;
    }
    fn signal(&self) {
        *self.ack.lock().unwrap_or_else(|e| e.into_inner()) = true;
        self.wake.notify_all();
    }
}

pub struct Pty {
    pub id: String,
    pub pid: Option<u32>,
    shared: Arc<Shared>,
    master: Mutex<Option<Box<dyn MasterPty + Send>>>,
    input: Mutex<Option<mpsc::Sender<Vec<u8>>>>,
    queued: Arc<AtomicUsize>,
    killer: Mutex<Box<dyn ChildKiller + Send + Sync>>,
}

/// Byte length of the leading part of `bytes` that ends on a character boundary.
fn complete_prefix(bytes: &[u8]) -> usize {
    match std::str::from_utf8(bytes) {
        Ok(_) => bytes.len(),
        Err(error) if error.error_len().is_none() => error.valid_up_to(),
        Err(_) => bytes.len(),
    }
}

fn clamp(value: u16, low: u16, high: u16) -> u16 {
    value.max(low).min(high)
}

impl Pty {
    pub fn spawn<D, X>(id: &str, options: SpawnOptions, on_data: D, on_exit: X) -> Result<Arc<Pty>, String>
    where
        D: Fn(String) + Send + 'static,
        X: FnOnce(i32) + Send + 'static,
    {
        let size = PtySize { rows: clamp(options.rows, 3, 300), cols: clamp(options.cols, 10, 500), pixel_width: 0, pixel_height: 0 };
        let pair = native_pty_system().openpty(size).map_err(|e| e.to_string())?;
        let mut command = CommandBuilder::new(&options.program);
        command.args(&options.args);
        command.cwd(&options.cwd);
        for (key, value) in &options.env {
            command.env(key, value);
        }
        let mut child = pair.slave.spawn_command(command).map_err(|e| e.to_string())?;
        // Without this the master never sees EOF when the child exits.
        drop(pair.slave);
        let pid = child.process_id();
        let killer = child.clone_killer();
        let mut reader = pair.master.try_clone_reader().map_err(|e| e.to_string())?;
        let mut writer = pair.master.take_writer().map_err(|e| e.to_string())?;

        let shared = Arc::new(Shared { ack: Mutex::new(false), wake: Condvar::new(), closed: AtomicBool::new(false) });
        let queued = Arc::new(AtomicUsize::new(0));
        let (sender, receiver) = mpsc::channel::<Vec<u8>>();

        let written = queued.clone();
        thread::Builder::new().name("orbit-input".into()).spawn(move || {
            while let Ok(bytes) = receiver.recv() {
                written.fetch_sub(bytes.len(), Ordering::SeqCst);
                if writer.write_all(&bytes).is_err() || writer.flush().is_err() {
                    break;
                }
            }
        }).map_err(|e| e.to_string())?;

        let watch = shared.clone();
        thread::Builder::new().name("orbit-output".into()).spawn(move || {
            let mut buffer = [0u8; CHUNK];
            let mut pending: Vec<u8> = Vec::new();
            loop {
                let count = match reader.read(&mut buffer) {
                    Ok(0) | Err(_) => break,
                    Ok(count) => count,
                };
                if watch.closed.load(Ordering::SeqCst) {
                    continue; // closed by the user: drain without waiting for the renderer
                }
                pending.extend_from_slice(&buffer[..count]);
                let cut = complete_prefix(&pending);
                if cut == 0 {
                    continue;
                }
                let chunk = String::from_utf8_lossy(&pending[..cut]).into_owned();
                pending.drain(..cut);
                on_data(chunk);
                watch.wait_ack();
            }
            if !pending.is_empty() && !watch.closed.load(Ordering::SeqCst) {
                on_data(String::from_utf8_lossy(&pending).into_owned());
            }
            let code = child.wait().map(|status| status.exit_code() as i32).unwrap_or(-1);
            on_exit(code);
        }).map_err(|e| e.to_string())?;

        Ok(Arc::new(Pty {
            id: id.to_string(),
            pid,
            shared,
            master: Mutex::new(Some(pair.master)),
            input: Mutex::new(Some(sender)),
            queued,
            killer: Mutex::new(killer),
        }))
    }

    pub fn write(&self, text: &str) -> Result<(), String> {
        const CLOSED: &str = "종료된 터미널입니다.";
        if self.shared.closed.load(Ordering::SeqCst) {
            return Err(CLOSED.to_string());
        }
        let bytes = text.as_bytes().to_vec();
        let total = self.queued.fetch_add(bytes.len(), Ordering::SeqCst) + bytes.len();
        if total > INPUT_LIMIT {
            self.queued.fetch_sub(bytes.len(), Ordering::SeqCst);
            return Err("입력이 너무 큽니다. 나누어 붙여넣어 주세요.".to_string());
        }
        match &*self.input.lock().unwrap_or_else(|e| e.into_inner()) {
            Some(sender) => sender.send(bytes).map_err(|_| CLOSED.to_string()),
            None => Err(CLOSED.to_string()),
        }
    }

    pub fn acknowledge(&self) {
        self.shared.signal();
    }

    pub fn resize(&self, cols: u16, rows: u16) -> Result<(), String> {
        let guard = self.master.lock().unwrap_or_else(|e| e.into_inner());
        match &*guard {
            Some(master) => master
                .resize(PtySize { rows: clamp(rows, 3, 300), cols: clamp(cols, 10, 500), pixel_width: 0, pixel_height: 0 })
                .map_err(|e| e.to_string()),
            None => Ok(()),
        }
    }

    pub fn close(&self) {
        if self.shared.closed.swap(true, Ordering::SeqCst) {
            return;
        }
        self.shared.signal();
        self.input.lock().unwrap_or_else(|e| e.into_inner()).take();
        let _ = self.killer.lock().unwrap_or_else(|e| e.into_inner()).kill();
        self.master.lock().unwrap_or_else(|e| e.into_inner()).take();
    }
}

impl Drop for Pty {
    fn drop(&mut self) {
        self.close();
    }
}

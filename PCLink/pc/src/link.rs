//! One authenticated connection to a phone: request/response matching,
//! events, and file transfers in both directions.

use crate::proto::{self, MsgExt, CHUNK};
use anyhow::{anyhow, bail, Context, Result};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::net::SocketAddr;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncSeekExt, AsyncWriteExt};
use tokio::sync::{mpsc, oneshot, watch};

/// Result of a finished incoming transfer.
pub type XferResult = std::result::Result<PathBuf, String>;

struct Incoming {
    file: Option<tokio::fs::File>,
    part_path: PathBuf,
    final_path: Option<PathBuf>,
    received: u64,
    expected: Option<u64>,
    done: Option<oneshot::Sender<XferResult>>,
}

pub struct Link {
    pub device_id: String,
    pub device_name: String,
    pub peer: SocketAddr,
    tx: mpsc::Sender<Vec<u8>>,
    pending: Mutex<HashMap<u64, oneshot::Sender<std::result::Result<Value, String>>>>,
    next_id: AtomicU64,
    /// The PC allocates odd transfer ids, the phone even ones.
    next_xfer: AtomicU32,
    incoming: Mutex<HashMap<u32, Incoming>>,
    finished: Mutex<HashMap<u32, XferResult>>,
    closed: AtomicBool,
    close_tx: watch::Sender<bool>,
}

impl Link {
    pub fn new(
        device_id: String,
        device_name: String,
        peer: SocketAddr,
        tx: mpsc::Sender<Vec<u8>>,
    ) -> Self {
        let (close_tx, _) = watch::channel(false);
        Self {
            device_id,
            device_name,
            peer,
            tx,
            pending: Mutex::new(HashMap::new()),
            next_id: AtomicU64::new(1),
            next_xfer: AtomicU32::new(1),
            incoming: Mutex::new(HashMap::new()),
            finished: Mutex::new(HashMap::new()),
            closed: AtomicBool::new(false),
            close_tx,
        }
    }

    pub fn is_closed(&self) -> bool {
        self.closed.load(Ordering::SeqCst)
    }

    pub fn close_signal(&self) -> watch::Receiver<bool> {
        self.close_tx.subscribe()
    }

    /// Tears the link down: fails pending requests and stops the IO tasks.
    pub fn close(&self) {
        if self.closed.swap(true, Ordering::SeqCst) {
            return;
        }
        let _ = self.close_tx.send(true);
        for (_, tx) in self.pending.lock().unwrap().drain() {
            let _ = tx.send(Err("phone disconnected".into()));
        }
        for (_, mut inc) in self.incoming.lock().unwrap().drain() {
            if let Some(done) = inc.done.take() {
                let _ = done.send(Err("phone disconnected".into()));
            }
            let _ = std::fs::remove_file(&inc.part_path);
        }
    }

    pub async fn send_json(&self, v: Value) -> Result<()> {
        if self.is_closed() {
            bail!("phone disconnected");
        }
        self.tx
            .send(proto::encode_json(&v))
            .await
            .map_err(|_| anyhow!("phone disconnected"))
    }

    pub async fn event(&self, ev: &str, data: Value) -> Result<()> {
        self.send_json(json!({"t": "ev", "ev": ev, "data": data})).await
    }

    /// Sends a request to the phone and waits for its response.
    pub async fn request(&self, op: &str, args: Value, timeout: Duration) -> Result<Value> {
        let id = self.next_id.fetch_add(1, Ordering::SeqCst);
        let (tx, rx) = oneshot::channel();
        self.pending.lock().unwrap().insert(id, tx);
        if let Err(e) = self
            .send_json(json!({"t": "req", "id": id, "op": op, "args": args}))
            .await
        {
            self.pending.lock().unwrap().remove(&id);
            return Err(e);
        }
        match tokio::time::timeout(timeout, rx).await {
            Ok(Ok(Ok(v))) => Ok(v),
            Ok(Ok(Err(e))) => Err(anyhow!(e)),
            Ok(Err(_)) => Err(anyhow!("phone disconnected")),
            Err(_) => {
                self.pending.lock().unwrap().remove(&id);
                Err(anyhow!("phone did not answer `{op}` in time"))
            }
        }
    }

    pub async fn respond(&self, id: u64, result: Result<Value>) -> Result<()> {
        let msg = match result {
            Ok(data) => json!({"t": "res", "id": id, "ok": true, "data": data}),
            Err(e) => json!({"t": "res", "id": id, "ok": false, "error": format!("{e:#}")}),
        };
        self.send_json(msg).await
    }

    /// Routes a response frame to the waiting request.
    pub fn on_response(&self, msg: &Value) {
        let Some(id) = msg.u64_of("id") else { return };
        if let Some(tx) = self.pending.lock().unwrap().remove(&id) {
            let ok = msg.get("ok").and_then(Value::as_bool).unwrap_or(false);
            let _ = tx.send(if ok {
                Ok(msg.get("data").cloned().unwrap_or(Value::Null))
            } else {
                Err(msg.str_of("error").unwrap_or("request failed").to_string())
            });
        }
    }

    pub fn alloc_xfer(&self) -> u32 {
        // Odd ids only.
        self.next_xfer.fetch_add(2, Ordering::SeqCst)
    }

    /// Prepares to receive a transfer into `dir`. Data is written to a
    /// hidden `.part` file and renamed when the sender finishes. When
    /// `name` is known it becomes the final file name; otherwise the caller
    /// renames later via the returned path.
    pub async fn expect_incoming(
        &self,
        xfer: u32,
        dir: &Path,
        name: Option<&str>,
        expected: Option<u64>,
    ) -> Result<oneshot::Receiver<XferResult>> {
        tokio::fs::create_dir_all(dir).await?;
        let part_path = dir.join(format!(".pclink-{xfer}.part"));
        let file = tokio::fs::File::create(&part_path)
            .await
            .with_context(|| format!("creating {}", part_path.display()))?;
        let final_path = name.map(|n| unique_path(dir, &sanitize_name(n)));
        let (tx, rx) = oneshot::channel();
        self.incoming.lock().unwrap().insert(
            xfer,
            Incoming {
                file: Some(file),
                part_path,
                final_path,
                received: 0,
                expected,
                done: Some(tx),
            },
        );
        Ok(rx)
    }

    /// Writes a data frame into its transfer.
    pub async fn on_data(&self, xfer: u32, bytes: &[u8]) -> Result<()> {
        // Take the file out so no lock is held across the write.
        let file = {
            let mut map = self.incoming.lock().unwrap();
            let Some(inc) = map.get_mut(&xfer) else {
                bail!("data for unknown transfer {xfer}");
            };
            inc.received += bytes.len() as u64;
            if let Some(exp) = inc.expected {
                if inc.received > exp {
                    bail!("transfer {xfer} is larger than announced");
                }
            }
            inc.file.take()
        };
        let Some(mut file) = file else { bail!("transfer {xfer} is busy") };
        let res = file.write_all(bytes).await;
        if let Some(inc) = self.incoming.lock().unwrap().get_mut(&xfer) {
            inc.file = Some(file);
        }
        res.map_err(Into::into)
    }

    /// Finishes a transfer when the sender sends `xfer_end`.
    pub async fn on_xfer_end(&self, msg: &Value) {
        let Some(xfer) = msg.u64_of("xfer").map(|x| x as u32) else { return };
        let Some(mut inc) = self.incoming.lock().unwrap().remove(&xfer) else { return };
        let ok = msg.get("ok").and_then(Value::as_bool).unwrap_or(false);
        let result: XferResult = async {
            if let Some(mut f) = inc.file.take() {
                f.flush().await.map_err(|e| e.to_string())?;
            }
            if !ok {
                return Err(msg.str_of("error").unwrap_or("sender failed").to_string());
            }
            if let Some(exp) = inc.expected {
                if inc.received != exp {
                    return Err(format!("expected {exp} bytes, got {}", inc.received));
                }
            }
            let dest = match &inc.final_path {
                Some(p) => p.clone(),
                None => {
                    let name = msg.str_of("name").unwrap_or("file");
                    let dir = inc.part_path.parent().unwrap_or(Path::new("."));
                    unique_path(dir, &sanitize_name(name))
                }
            };
            tokio::fs::rename(&inc.part_path, &dest)
                .await
                .map_err(|e| e.to_string())?;
            Ok(dest)
        }
        .await;
        if result.is_err() {
            let _ = tokio::fs::remove_file(&inc.part_path).await;
        }
        self.finished.lock().unwrap().insert(xfer, result.clone());
        if let Some(done) = inc.done.take() {
            let _ = done.send(result);
        }
    }

    /// Result of a finished incoming transfer (`xfer.status` request).
    pub fn finished_status(&self, xfer: u32) -> Option<XferResult> {
        self.finished.lock().unwrap().remove(&xfer)
    }

    /// Streams a local file to the phone under transfer id `xfer`.
    pub async fn send_file(&self, xfer: u32, path: &Path, offset: u64) -> Result<u64> {
        let res: Result<u64> = async {
            let mut f = tokio::fs::File::open(path).await?;
            if offset > 0 {
                f.seek(std::io::SeekFrom::Start(offset)).await?;
            }
            let mut buf = vec![0u8; CHUNK];
            let mut sent = 0u64;
            loop {
                let n = f.read(&mut buf).await?;
                if n == 0 {
                    break;
                }
                self.tx
                    .send(proto::encode_data(xfer, &buf[..n]))
                    .await
                    .map_err(|_| anyhow!("phone disconnected"))?;
                sent += n as u64;
            }
            Ok(sent)
        }
        .await;
        let end = match &res {
            Ok(sent) => json!({"t": "xfer_end", "xfer": xfer, "ok": true, "size": sent}),
            Err(e) => json!({"t": "xfer_end", "xfer": xfer, "ok": false, "error": e.to_string()}),
        };
        let _ = self.send_json(end).await;
        res
    }

    /// Downloads a file from the phone into `dest_dir`.
    pub async fn pull(&self, phone_path: &str, dest_dir: &Path) -> Result<PathBuf> {
        let xfer = self.alloc_xfer();
        let done = self.expect_incoming(xfer, dest_dir, None, None).await?;
        let meta = match self
            .request(
                "fs.read",
                json!({"path": phone_path, "offset": 0, "xfer": xfer}),
                Duration::from_secs(30),
            )
            .await
        {
            Ok(m) => m,
            Err(e) => {
                self.abandon_incoming(xfer);
                return Err(e);
            }
        };
        let path = done
            .await
            .map_err(|_| anyhow!("phone disconnected"))?
            .map_err(|e| anyhow!(e))?;
        // The sender's res carries the real name; rename if it differs.
        if let Some(name) = meta.str_of("name") {
            let want = sanitize_name(name);
            if path.file_name().and_then(|n| n.to_str()) != Some(want.as_str()) {
                let dest = unique_path(dest_dir, &want);
                tokio::fs::rename(&path, &dest).await?;
                return Ok(dest);
            }
        }
        Ok(path)
    }

    /// Uploads a local file to the phone. Returns the path on the phone.
    pub async fn push(&self, local: &Path, phone_dir: Option<&str>) -> Result<String> {
        let meta = tokio::fs::metadata(local)
            .await
            .with_context(|| format!("{} not found", local.display()))?;
        if !meta.is_file() {
            bail!("{} is not a file", local.display());
        }
        let name = local
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or("file")
            .to_string();
        let res = self
            .request(
                "fs.write",
                json!({"dir": phone_dir, "name": name, "size": meta.len()}),
                Duration::from_secs(30),
            )
            .await?;
        let xfer = res
            .u64_of("xfer")
            .ok_or_else(|| anyhow!("phone did not give a transfer id"))? as u32;
        self.send_file(xfer, local, 0).await?;
        let status = self
            .request("xfer.status", json!({"xfer": xfer}), Duration::from_secs(60))
            .await?;
        Ok(status.str_of("path").unwrap_or("").to_string())
    }

    fn abandon_incoming(&self, xfer: u32) {
        if let Some(inc) = self.incoming.lock().unwrap().remove(&xfer) {
            let _ = std::fs::remove_file(&inc.part_path);
        }
    }
}

/// Keeps only the final path component and strips characters Windows rejects.
pub fn sanitize_name(name: &str) -> String {
    let base = name.rsplit(['/', '\\']).next().unwrap_or("");
    let cleaned: String = base
        .chars()
        .map(|c| match c {
            '<' | '>' | ':' | '"' | '|' | '?' | '*' => '_',
            c if c.is_control() => '_',
            c => c,
        })
        .collect();
    let cleaned = cleaned.trim().trim_matches('.').to_string();
    if cleaned.is_empty() {
        "file".into()
    } else {
        cleaned
    }
}

/// `dir/name`, or `dir/name (2).ext` etc. if it already exists.
pub fn unique_path(dir: &Path, name: &str) -> PathBuf {
    let first = dir.join(name);
    if !first.exists() {
        return first;
    }
    let p = Path::new(name);
    let stem = p.file_stem().and_then(|s| s.to_str()).unwrap_or(name);
    let ext = p.extension().and_then(|s| s.to_str());
    for i in 2.. {
        let candidate = match ext {
            Some(e) => dir.join(format!("{stem} ({i}).{e}")),
            None => dir.join(format!("{stem} ({i})")),
        };
        if !candidate.exists() {
            return candidate;
        }
    }
    unreachable!()
}

/// Spawns the task that drains a link's outgoing queue into the socket.
pub fn spawn_writer<W>(mut w: W, mut rx: mpsc::Receiver<Vec<u8>>, mut close: watch::Receiver<bool>)
where
    W: tokio::io::AsyncWrite + Unpin + Send + 'static,
{
    tokio::spawn(async move {
        loop {
            tokio::select! {
                msg = rx.recv() => {
                    let Some(bytes) = msg else { break };
                    if w.write_all(&bytes).await.is_err() {
                        break;
                    }
                    // Flush when the queue is drained so small messages go out promptly.
                    if rx.is_empty() && w.flush().await.is_err() {
                        break;
                    }
                }
                _ = close.changed() => break,
            }
        }
        let _ = w.shutdown().await;
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sanitizes_names() {
        assert_eq!(sanitize_name("../../etc/passwd"), "passwd");
        assert_eq!(sanitize_name("C:\\Windows\\a.txt"), "a.txt");
        assert_eq!(sanitize_name("what?.txt"), "what_.txt");
        assert_eq!(sanitize_name(".."), "file");
        assert_eq!(sanitize_name(""), "file");
    }

    #[test]
    fn unique_paths() {
        let d = tempfile::tempdir().unwrap();
        let a = unique_path(d.path(), "a.txt");
        std::fs::write(&a, "x").unwrap();
        assert_eq!(unique_path(d.path(), "a.txt").file_name().unwrap(), "a (2).txt");
    }
}

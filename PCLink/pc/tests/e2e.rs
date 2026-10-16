//! End-to-end test: start a real agent, connect a simulated phone over TLS,
//! and exercise pairing, requests in both directions, file transfers and
//! the local API that the CLI, hooks and MCP server use.

use pclink::config::{Action, Config};
use pclink::proto::{self, Frame, MsgExt};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tokio::io::{AsyncWriteExt, BufReader};
use tokio::net::TcpStream;
use tokio::sync::{mpsc, oneshot};

type Pending = Arc<Mutex<HashMap<u64, oneshot::Sender<Value>>>>;

/// A tiny stand-in for the Android app.
struct FakePhone {
    tx: mpsc::Sender<Vec<u8>>,
    pending: Pending,
    next_id: Mutex<u64>,
    /// Files "stored on the phone".
    files: Arc<Mutex<HashMap<String, Vec<u8>>>>,
    /// Data the PC sent us, keyed by transfer id.
    received: Arc<Mutex<HashMap<u32, Vec<u8>>>>,
    events: Arc<Mutex<Vec<Value>>>,
}

impl FakePhone {
    async fn connect(addr: std::net::SocketAddr, fp: &str, first: Value) -> (Arc<FakePhone>, Value) {
        let cfg = pclink::tls::pinned_client_config(fp);
        let connector = tokio_rustls::TlsConnector::from(cfg);
        let tcp = TcpStream::connect(addr).await.unwrap();
        let name = rustls::pki_types::ServerName::try_from("pclink").unwrap();
        let tls = connector.connect(name, tcp).await.unwrap();
        let (r, mut w) = tokio::io::split(tls);
        let mut r = BufReader::new(r);
        w.write_all(&proto::encode_json(&first)).await.unwrap();
        w.flush().await.unwrap();
        let Some(Frame::Json(reply)) = proto::read_frame(&mut r).await.unwrap() else { panic!() };

        let (tx, mut rx) = mpsc::channel::<Vec<u8>>(64);
        tokio::spawn(async move {
            while let Some(b) = rx.recv().await {
                if w.write_all(&b).await.is_err() {
                    break;
                }
                let _ = w.flush().await;
            }
        });
        let phone = Arc::new(FakePhone {
            tx,
            pending: Default::default(),
            next_id: Mutex::new(1000),
            files: Arc::new(Mutex::new(HashMap::from([(
                "/sdcard/DCIM/photo.jpg".to_string(),
                b"JPEGDATA".repeat(20_000),
            )]))),
            received: Default::default(),
            events: Default::default(),
        });
        let p = phone.clone();
        tokio::spawn(async move {
            while let Ok(Some(frame)) = proto::read_frame(&mut r).await {
                p.clone().on_frame(frame).await;
            }
        });
        (phone, reply)
    }

    async fn send(&self, v: Value) {
        self.tx.send(proto::encode_json(&v)).await.unwrap();
    }

    async fn request(&self, op: &str, args: Value) -> Value {
        let id = {
            let mut n = self.next_id.lock().unwrap();
            *n += 1;
            *n
        };
        let (tx, rx) = oneshot::channel();
        self.pending.lock().unwrap().insert(id, tx);
        self.send(json!({"t": "req", "id": id, "op": op, "args": args})).await;
        tokio::time::timeout(Duration::from_secs(10), rx).await.unwrap().unwrap()
    }

    async fn on_frame(self: Arc<Self>, frame: Frame) {
        match frame {
            Frame::Data { xfer, bytes } => {
                self.received.lock().unwrap().entry(xfer).or_default().extend(bytes);
            }
            Frame::Json(m) => match m.t() {
                "res" => {
                    if let Some(tx) = self.pending.lock().unwrap().remove(&m.u64_of("id").unwrap()) {
                        let _ = tx.send(m);
                    }
                }
                "ev" | "xfer_end" => self.events.lock().unwrap().push(m),
                "req" => {
                    let id = m.u64_of("id").unwrap();
                    let args = m["args"].clone();
                    let data = match m.str_of("op").unwrap() {
                        "notify" => json!({}),
                        "ask" => json!({"answer": args["options"][0]}),
                        "fs.write" => json!({"xfer": 2}),
                        "xfer.status" => {
                            // Give the data frames a moment to land.
                            tokio::time::sleep(Duration::from_millis(100)).await;
                            json!({"path": "/sdcard/Download/PCLink/notes.txt"})
                        }
                        "fs.read" => {
                            let path = args["path"].as_str().unwrap().to_string();
                            let xfer = args["xfer"].as_u64().unwrap() as u32;
                            let content = self.files.lock().unwrap().get(&path).cloned().unwrap();
                            let me = self.clone();
                            let size = content.len();
                            tokio::spawn(async move {
                                for chunk in content.chunks(proto::CHUNK) {
                                    me.tx.send(proto::encode_data(xfer, chunk)).await.unwrap();
                                }
                                me.send(json!({"t": "xfer_end", "xfer": xfer, "ok": true, "name": "photo.jpg"})).await;
                            });
                            json!({"name": "photo.jpg", "size": size})
                        }
                        _ => {
                            self.send(json!({"t": "res", "id": id, "ok": false, "error": "unsupported"})).await;
                            return;
                        }
                    };
                    self.send(json!({"t": "res", "id": id, "ok": true, "data": data})).await;
                }
                _ => {}
            },
        }
    }
}

fn api(port: u16, token: &str, method: &str, path: &str, body: Value) -> Result<Value, String> {
    let url = format!("http://127.0.0.1:{port}{path}");
    let req = match method {
        "GET" => ureq::get(&url),
        _ => ureq::post(&url),
    }
    .set("Authorization", &format!("Bearer {token}"));
    let res = if method == "GET" { req.call() } else { req.send_json(body) };
    match res {
        Ok(r) => Ok(r.into_json().unwrap()),
        Err(ureq::Error::Status(code, r)) => Err(format!("{code}: {}", r.into_string().unwrap_or_default())),
        Err(e) => Err(e.to_string()),
    }
}

async fn api_async(port: u16, token: String, method: &'static str, path: &'static str, body: Value) -> Result<Value, String> {
    tokio::task::spawn_blocking(move || api(port, &token, method, path, body)).await.unwrap()
}

#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn phone_and_pc_end_to_end() {
    let home = tempfile::tempdir().unwrap();
    std::env::set_var("PCLINK_HOME", home.path());
    let shared = home.path().join("shared");
    std::fs::create_dir_all(&shared).unwrap();
    std::fs::write(shared.join("notes.txt"), "hello from the PC").unwrap();
    let cfg = Config {
        port: 0,
        api_port: 0,
        shared_folders: vec![shared.clone()],
        inbox: home.path().join("inbox"),
        clipboard_sync: false,
        mirror_notifications: false,
        actions: vec![Action {
            name: "echo".into(),
            command: "echo built ok".into(),
            cwd: None,
            icon: None,
        }],
        ..Config::default()
    };
    let running = pclink::run::start_with(cfg, false).await.unwrap();
    let agent = running.agent.clone();
    let fp = agent.identity.fingerprint.clone();
    let addr: std::net::SocketAddr = format!("127.0.0.1:{}", running.phone_addr.port()).parse().unwrap();
    let port = running.api_addr.port();
    let token = agent.local_token.clone();

    // --- Local API refuses requests without the token.
    assert!(api(port, "wrong", "GET", "/api/status", json!({})).unwrap_err().starts_with("401"));

    // --- Pairing: wrong code is refused, right code yields a token.
    let (_, reply) = FakePhone::connect(addr, &fp, json!({"t": "pair", "code": "NOPE", "device_id": "p1", "device_name": "Pixel"})).await;
    assert_eq!(reply.t(), "error");
    let offer = agent.start_pairing();
    assert!(offer["url"].as_str().unwrap().starts_with("pclink://pair?"));
    assert!(offer["svg"].as_str().unwrap().contains("<svg"));
    let code = offer["code"].as_str().unwrap().to_lowercase(); // case-insensitive
    let (phone, reply) = FakePhone::connect(addr, &fp, json!({"t": "pair", "code": code, "device_id": "p1", "device_name": "Pixel"})).await;
    assert_eq!(reply.t(), "paired", "{reply}");
    let device_token = reply["token"].as_str().unwrap().to_string();
    // The code is single-use.
    let (_, again) = FakePhone::connect(addr, &fp, json!({"t": "pair", "code": code, "device_id": "p2", "device_name": "X"})).await;
    assert_eq!(again.t(), "error");
    drop(phone);

    // --- Reconnect with the token (a bad token is refused).
    let (_, bad) = FakePhone::connect(addr, &fp, json!({"t": "hello", "device_id": "p1", "token": "00"})).await;
    assert_eq!(bad.t(), "error");
    let (phone, welcome) = FakePhone::connect(addr, &fp, json!({"t": "hello", "device_id": "p1", "token": device_token, "device_name": "Pixel"})).await;
    assert_eq!(welcome.t(), "welcome");
    tokio::time::sleep(Duration::from_millis(200)).await;
    let status = api_async(port, token.clone(), "GET", "/api/status", json!({})).await.unwrap();
    assert_eq!(status["connected"], true);
    assert_eq!(status["phone"]["name"], "Pixel");

    // --- A wrong fingerprint must fail the TLS handshake.
    let bad_cfg = pclink::tls::pinned_client_config(&"0".repeat(64));
    let tcp = TcpStream::connect(addr).await.unwrap();
    let name = rustls::pki_types::ServerName::try_from("pclink").unwrap();
    assert!(tokio_rustls::TlsConnector::from(bad_cfg).connect(name, tcp).await.is_err());

    // --- Phone → PC requests.
    let roots = phone.request("fs.roots", json!({})).await;
    let root_path = roots["data"][0]["path"].as_str().unwrap().to_string();
    let listing = phone.request("fs.list", json!({"path": root_path})).await;
    assert_eq!(listing["data"]["entries"][0]["name"], "notes.txt");
    let denied = phone.request("fs.list", json!({"path": home.path().to_str().unwrap()})).await;
    assert_eq!(denied["ok"], false);

    // Download notes.txt from the PC.
    let file_path = listing["data"]["entries"][0]["path"].as_str().unwrap().to_string();
    let res = phone.request("fs.read", json!({"path": file_path, "xfer": 4, "offset": 0})).await;
    assert_eq!(res["data"]["size"], 17);
    tokio::time::sleep(Duration::from_millis(200)).await;
    assert_eq!(phone.received.lock().unwrap().get(&4).unwrap(), b"hello from the PC");
    assert!(phone.events.lock().unwrap().iter().any(|e| e.t() == "xfer_end" && e["xfer"] == 4 && e["ok"] == true));

    // Upload a file to the PC inbox.
    let body = b"photo bytes".to_vec();
    let res = phone.request("fs.write", json!({"name": "../../evil/shot.png", "size": body.len()})).await;
    let xfer = res["data"]["xfer"].as_u64().unwrap() as u32;
    assert_eq!(xfer % 2, 1, "PC allocates odd transfer ids");
    phone.tx.send(proto::encode_data(xfer, &body)).await.unwrap();
    phone.send(json!({"t": "xfer_end", "xfer": xfer, "ok": true})).await;
    let st = phone.request("xfer.status", json!({"xfer": xfer})).await;
    let saved = st["data"]["path"].as_str().unwrap().to_string();
    assert!(saved.ends_with("shot.png"), "{saved}");
    assert!(std::path::Path::new(&saved).starts_with(home.path().join("inbox")), "sanitized into the inbox");
    assert_eq!(std::fs::read(&saved).unwrap(), body);

    // Announced size is enforced.
    let res = phone.request("fs.write", json!({"name": "small.bin", "size": 2})).await;
    let x2 = res["data"]["xfer"].as_u64().unwrap() as u32;
    phone.tx.send(proto::encode_data(x2, b"too long")).await.unwrap();
    phone.send(json!({"t": "xfer_end", "xfer": x2, "ok": true})).await;
    let st = phone.request("xfer.status", json!({"xfer": x2})).await;
    assert_eq!(st["ok"], false);

    // Run a control-panel action.
    let res = phone.request("actions.run", json!({"name": "echo", "wait": true})).await;
    assert_eq!(res["data"]["code"], 0);
    assert!(res["data"]["output"].as_str().unwrap().contains("built ok"));
    let res = phone.request("actions.run", json!({"name": "rm -rf /"})).await;
    assert_eq!(res["ok"], false);

    // Events update PC state.
    phone.send(json!({"t": "ev", "ev": "battery", "data": {"battery": 81, "charging": true}})).await;
    phone.send(json!({"t": "ev", "ev": "notif.posted", "data": {"key": "k1", "app": "Signal", "title": "Sam", "text": "hey", "can_reply": true}})).await;
    tokio::time::sleep(Duration::from_millis(200)).await;
    let status = api_async(port, token.clone(), "GET", "/api/status", json!({})).await.unwrap();
    assert_eq!(status["phone"]["status"]["battery"], 81);
    let notifs = api_async(port, token.clone(), "GET", "/api/notifications", json!({})).await.unwrap();
    assert_eq!(notifs[0]["title"], "Sam");

    // --- PC → phone through the local API (what the CLI, hooks and MCP use).
    let r = api_async(port, token.clone(), "POST", "/api/notify", json!({"title": "Claude", "text": "done", "channel": "claude"})).await.unwrap();
    assert_eq!(r["sent"], true);
    let r = api_async(port, token.clone(), "POST", "/api/ask", json!({"question": "Allow Bash?", "options": ["Allow", "Deny"]})).await.unwrap();
    assert_eq!(r["answer"], "Allow");

    let pulled = api_async(port, token.clone(), "POST", "/api/pull", json!({"path": "/sdcard/DCIM/photo.jpg"})).await.unwrap();
    let local = pulled["path"].as_str().unwrap().to_string();
    assert!(local.ends_with("photo.jpg"), "{local}");
    assert_eq!(std::fs::read(&local).unwrap().len(), 8 * 20_000);

    let pushed = tokio::task::spawn_blocking({
        let token = token.clone();
        let p = shared.join("notes.txt");
        move || api(port, &token, "POST", "/api/push", json!({"path": p}))
    })
    .await
    .unwrap()
    .unwrap();
    assert_eq!(pushed["path"], "/sdcard/Download/PCLink/notes.txt");
    assert_eq!(phone.received.lock().unwrap().get(&2).unwrap(), b"hello from the PC");

    // Unknown ops surface the phone's error.
    let err = api_async(port, token.clone(), "POST", "/api/phone", json!({"op": "bogus"})).await.unwrap_err();
    assert!(err.contains("unsupported"), "{err}");

    // --- Forgetting the device drops the link.
    api_async(port, token.clone(), "POST", "/api/devices/forget", json!({"id": "p1"})).await.unwrap();
    tokio::time::sleep(Duration::from_millis(200)).await;
    let status = api_async(port, token.clone(), "GET", "/api/status", json!({})).await.unwrap();
    assert_eq!(status["connected"], false);
    let err = api_async(port, token, "POST", "/api/notify", json!({"text": "x"})).await.unwrap_err();
    assert!(err.starts_with("503"), "{err}");
}

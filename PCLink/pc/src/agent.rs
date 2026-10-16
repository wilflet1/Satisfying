//! The running agent: owns configuration, the paired-phone connection and
//! all features that span the phone link and the local API.

use crate::audio::{self, AudioFormat};
use crate::config::{Config, PairedDevice, State};
use crate::files::SharedFs;
use crate::link::Link;
use crate::platform;
use crate::tls::Identity;
use crate::util;
use anyhow::{anyhow, bail, Result};
use base64::Engine;
use serde::Serialize;
use serde_json::{json, Value};
use std::collections::VecDeque;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, RwLock};
use std::time::{Duration, Instant};

pub const VERSION: &str = env!("CARGO_PKG_VERSION");
const PAIRING_TTL: Duration = Duration::from_secs(10 * 60);
const MAX_NOTIFICATIONS: usize = 200;

pub struct PairingOffer {
    pub code: String,
    pub expires: Instant,
}

#[derive(Debug, Clone, Serialize)]
pub struct PhoneNotification {
    pub key: String,
    pub app: String,
    pub package: String,
    pub title: String,
    pub text: String,
    pub time: u64,
    pub can_reply: bool,
}

#[derive(Debug, Clone, Serialize)]
pub struct ActivityItem {
    pub time: u64,
    pub kind: String,
    pub text: String,
}

pub struct SpeakerSession {
    stop: Arc<AtomicBool>,
    pub restore_default: Option<String>,
    pub stats: Arc<audio::SendStats>,
    pub device: String,
    pub format: AudioFormat,
}

impl Drop for SpeakerSession {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
        if let Some(id) = self.restore_default.take() {
            if let Err(e) = platform::set_default_output(&id) {
                tracing::warn!("could not restore default output: {e:#}");
            }
        }
    }
}

pub struct Agent {
    pub cfg: Config,
    pub fs: SharedFs,
    pub state: Mutex<State>,
    pub identity: Identity,
    pub local_token: String,
    pub link: RwLock<Option<Arc<Link>>>,
    pub pairing: Mutex<Option<PairingOffer>>,
    pub notifications: Mutex<VecDeque<PhoneNotification>>,
    pub activity: Mutex<VecDeque<ActivityItem>>,
    pub phone_status: Mutex<Value>,
    pub speaker: Mutex<Option<SpeakerSession>>,
    pub mic: Mutex<Option<crate::mic::MicSession>>,
    /// Last clipboard text we set or sent, to avoid echoing it back.
    pub clip_last: Mutex<String>,
    pub mac: Option<String>,
    job_counter: AtomicU64,
    pub tasks: crate::tasks::Tasks,
    /// Called whenever something the tray cares about changes.
    pub on_change: Mutex<Option<Box<dyn Fn() + Send + Sync>>>,
}

impl Agent {
    pub fn new(cfg: Config, state: State, identity: Identity, local_token: String) -> Arc<Agent> {
        let fs = SharedFs::new(&cfg.shared_folders);
        let mac = mac_address::get_mac_address()
            .ok()
            .flatten()
            .map(|m| m.to_string());
        Arc::new(Agent {
            cfg,
            fs,
            state: Mutex::new(state),
            identity,
            local_token,
            link: RwLock::new(None),
            pairing: Mutex::new(None),
            notifications: Mutex::new(VecDeque::new()),
            activity: Mutex::new(VecDeque::new()),
            phone_status: Mutex::new(json!({})),
            speaker: Mutex::new(None),
            mic: Mutex::new(None),
            clip_last: Mutex::new(String::new()),
            mac,
            job_counter: AtomicU64::new(1),
            tasks: Default::default(),
            on_change: Mutex::new(None),
        })
    }

    pub fn changed(&self) {
        if let Some(f) = self.on_change.lock().unwrap().as_ref() {
            f();
        }
    }

    pub fn log(&self, kind: &str, text: impl Into<String>) {
        let text = text.into();
        tracing::info!("[{kind}] {text}");
        let mut a = self.activity.lock().unwrap();
        a.push_front(ActivityItem { time: util::now_millis(), kind: kind.into(), text });
        a.truncate(100);
    }

    pub fn current_link(&self) -> Option<Arc<Link>> {
        self.link
            .read()
            .unwrap()
            .clone()
            .filter(|l| !l.is_closed())
    }

    pub fn phone(&self) -> Result<Arc<Link>> {
        self.current_link()
            .ok_or_else(|| anyhow!("phone is not connected"))
    }

    pub fn inbox(&self) -> PathBuf {
        self.cfg.inbox.clone()
    }

    // ------------------------------------------------------------------
    // Updates

    /// Checks for an update; with `install`, installs it and restarts.
    /// Returns a message for the user. Blocking.
    pub fn check_update(&self, install: bool) -> Result<String> {
        use crate::update::{check, Check, BUILD};
        Ok(match check()? {
            Check::UpToDate { latest } => format!("PCLink is up to date (build {BUILD}, latest {latest})."),
            Check::LocalBuild { latest } => format!(
                "This is a local build, which doesn't update itself. Build {latest} is available at https://github.com/{}/releases/tag/pclink-latest",
                crate::update::repo()
            ),
            Check::Available(m) if install => {
                let build = m.build;
                self.install_update(&m);
                // Only reached if installing failed.
                format!("Build {build} is available but could not be installed; see agent.log.")
            }
            Check::Available(m) => format!("Build {} is available.", m.build),
        })
    }

    /// Downloads and installs `m`, then restarts the agent. Returns only on failure.
    pub fn install_update(&self, m: &crate::update::Manifest) {
        self.log("update", format!("installing build {}", m.build));
        platform::notify("Updating PCLink", &format!("Installing build {}. PCLink will restart.", m.build));
        let result = crate::update::agent_path().and_then(|exe| {
            crate::update::apply(m, exe.parent().unwrap_or(std::path::Path::new(".")))?;
            Ok(exe)
        });
        match result {
            Ok(exe) => {
                self.shutdown();
                crate::update::restart_with(&exe, Some(m.build));
            }
            Err(e) => {
                tracing::error!("update failed: {e:#}");
                platform::notify("PCLink update failed", &format!("{e:#}"));
            }
        }
    }

    /// Releases things the next process needs (default audio device, phone link).
    pub fn shutdown(&self) {
        *self.speaker.lock().unwrap() = None;
        *self.mic.lock().unwrap() = None;
        if let Some(l) = self.current_link() {
            l.close();
        }
    }

    // ------------------------------------------------------------------
    // Pairing

    pub fn start_pairing(&self) -> Value {
        let code = util::pairing_code();
        *self.pairing.lock().unwrap() = Some(PairingOffer {
            code: code.clone(),
            expires: Instant::now() + PAIRING_TTL,
        });
        let hosts: Vec<String> = util::local_ipv4s().iter().map(|ip| ip.to_string()).collect();
        let url = format!(
            "pclink://pair?h={}&p={}&fp={}&c={}&n={}&id={}",
            hosts.join(","),
            self.cfg.port,
            self.identity.fingerprint,
            code,
            url_encode(&self.cfg.pc_name),
            self.state.lock().unwrap().pc_id,
        );
        let svg = qrcode::QrCode::new(url.as_bytes())
            .map(|q| {
                q.render::<qrcode::render::svg::Color>()
                    .min_dimensions(240, 240)
                    .quiet_zone(true)
                    .build()
            })
            .unwrap_or_default();
        self.log("pair", "pairing code created");
        json!({
            "code": code,
            "url": url,
            "svg": svg,
            "hosts": hosts,
            "port": self.cfg.port,
            "fingerprint": self.identity.fingerprint,
            "expires_in": PAIRING_TTL.as_secs(),
        })
    }

    /// Checks a pairing code and, if valid, registers the device and returns
    /// its new secret token.
    pub fn complete_pairing(&self, code: &str, device_id: &str, device_name: &str) -> Result<String> {
        {
            let mut offer = self.pairing.lock().unwrap();
            let valid = match offer.as_ref() {
                Some(o) => {
                    o.expires > Instant::now()
                        && util::ct_eq(o.code.as_bytes(), code.trim().to_uppercase().as_bytes())
                }
                None => false,
            };
            if !valid {
                bail!("wrong or expired pairing code");
            }
            *offer = None; // one use only
        }
        if device_id.is_empty() || device_id.len() > 128 {
            bail!("bad device id");
        }
        let token = util::random_hex(32);
        let mut st = self.state.lock().unwrap();
        st.devices.retain(|d| d.id != device_id);
        st.devices.push(PairedDevice {
            id: device_id.into(),
            name: device_name.chars().take(80).collect(),
            token_sha256: util::sha256_hex(token.as_bytes()),
            paired_at: util::now_secs(),
            last_seen: util::now_secs(),
        });
        st.save()?;
        drop(st);
        self.log("pair", format!("paired with {device_name}"));
        Ok(token)
    }

    pub fn authenticate(&self, device_id: &str, token: &str) -> Option<PairedDevice> {
        let hash = util::sha256_hex(token.as_bytes());
        let mut st = self.state.lock().unwrap();
        let dev = st
            .devices
            .iter_mut()
            .find(|d| d.id == device_id && util::ct_eq(d.token_sha256.as_bytes(), hash.as_bytes()))?;
        dev.last_seen = util::now_secs();
        let found = dev.clone();
        let _ = st.save();
        Some(found)
    }

    pub fn forget_device(&self, id: &str) -> Result<()> {
        let mut st = self.state.lock().unwrap();
        st.devices.retain(|d| d.id != id);
        st.save()?;
        drop(st);
        if let Some(l) = self.current_link() {
            if l.device_id == id {
                l.close();
            }
        }
        Ok(())
    }

    pub fn hello_info(&self) -> Value {
        json!({
            "pc_id": self.state.lock().unwrap().pc_id,
            "pc_name": self.cfg.pc_name,
            "mac": self.mac,
            "version": VERSION,
            "build": crate::update::BUILD,
            "protocol": crate::proto::PROTOCOL_VERSION,
            "features": features(),
        })
    }

    // ------------------------------------------------------------------
    // Connection lifecycle

    pub fn attach(&self, link: Arc<Link>) {
        if let Some(old) = self.link.write().unwrap().replace(link.clone()) {
            old.close();
        }
        self.log("link", format!("{} connected from {}", link.device_name, link.peer.ip()));
        self.changed();
    }

    pub fn detach(&self, link: &Arc<Link>) {
        let mut cur = self.link.write().unwrap();
        if cur.as_ref().map(|c| Arc::ptr_eq(c, link)).unwrap_or(false) {
            *cur = None;
            drop(cur);
            *self.speaker.lock().unwrap() = None;
            *self.mic.lock().unwrap() = None;
            self.log("link", format!("{} disconnected", link.device_name));
            self.changed();
        }
    }

    // ------------------------------------------------------------------
    // Events from the phone

    pub fn on_event(&self, ev: &str, data: &Value) {
        match ev {
            "notif.posted" => {
                let n = PhoneNotification {
                    key: s(data, "key"),
                    app: s(data, "app"),
                    package: s(data, "package"),
                    title: s(data, "title"),
                    text: s(data, "text"),
                    time: data["time"].as_u64().unwrap_or_else(util::now_millis),
                    can_reply: data["can_reply"].as_bool().unwrap_or(false),
                };
                let blocked = self.cfg.notification_blocklist.contains(&n.package);
                if self.cfg.mirror_notifications && !blocked && !data["silent"].as_bool().unwrap_or(false) {
                    let title = if n.title.is_empty() { n.app.clone() } else { format!("{} · {}", n.app, n.title) };
                    platform::notify(&title, &n.text);
                }
                let mut list = self.notifications.lock().unwrap();
                list.retain(|x| x.key != n.key);
                list.push_front(n);
                list.truncate(MAX_NOTIFICATIONS);
            }
            "notif.removed" => {
                let key = s(data, "key");
                self.notifications.lock().unwrap().retain(|x| x.key != key);
            }
            "clip.changed" => {
                if let Some(text) = data["text"].as_str() {
                    *self.clip_last.lock().unwrap() = text.to_string();
                    if let Err(e) = platform::clipboard_set_text(text) {
                        tracing::warn!("clipboard: {e:#}");
                    } else {
                        self.log("clipboard", "phone → PC");
                    }
                }
            }
            "status" | "battery" => {
                let mut st = self.phone_status.lock().unwrap();
                if let (Some(dst), Some(src)) = (st.as_object_mut(), data.as_object()) {
                    for (k, v) in src {
                        dst.insert(k.clone(), v.clone());
                    }
                }
                drop(st);
                self.changed();
            }
            "mic.stopped" => {
                self.close_mic();
            }
            "speaker.stopped" => {
                *self.speaker.lock().unwrap() = None;
                self.log("speaker", "phone stopped playback");
                self.changed();
            }
            other => tracing::debug!("unhandled event {other}"),
        }
    }

    // ------------------------------------------------------------------
    // Requests from the phone

    /// Requests that must run in arrival order (typed text then Enter,
    /// mouse moves). They're queued to the input thread without waiting,
    /// so the touchpad never stalls on a round trip.
    pub fn handle_inline(&self, op: &str, args: &Value) -> Option<Result<Value>> {
        let cmd = match op {
            "input" => parse_input(args),
            "media" => match args["cmd"].as_str() {
                Some(c @ ("play_pause" | "next" | "prev" | "vol_up" | "vol_down" | "mute")) => {
                    Ok(platform::InputCmd::Key { key: c.to_string(), modifiers: vec![] })
                }
                other => Err(anyhow!("unknown media command {other:?}")),
            },
            _ => return None,
        };
        Some(cmd.and_then(platform::input_queue).map(|_| json!({})))
    }

    pub async fn handle_request(self: &Arc<Self>, link: &Arc<Link>, op: &str, args: Value) -> Result<Value> {
        match op {
            "ping" => Ok(json!({"pong": true})),
            "status" => {
                let me = self.clone();
                Ok(tokio::task::spawn_blocking(move || me.pc_status()).await?)
            }
            "fs.roots" => Ok(self.fs.roots_json()),
            "fs.list" => self.fs.list(&req_str(&args, "path")?),
            "fs.read" => {
                let path = self.fs.readable_file(&req_str(&args, "path")?)?;
                let xfer = args["xfer"].as_u64().ok_or_else(|| anyhow!("missing xfer"))? as u32;
                let offset = args["offset"].as_u64().unwrap_or(0);
                let size = std::fs::metadata(&path)?.len();
                let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("file").to_string();
                let l = link.clone();
                let p = path.clone();
                // The phone registered `xfer` before asking, so data frames
                // may safely arrive before or after this response.
                tokio::spawn(async move {
                    if let Err(e) = l.send_file(xfer, &p, offset).await {
                        tracing::warn!("send {}: {e:#}", p.display());
                    }
                });
                self.log("files", format!("PC → phone: {name}"));
                Ok(json!({"name": name, "size": size}))
            }
            "fs.write" => {
                let dir = match args["dir"].as_str() {
                    Some(d) if !d.is_empty() => self.fs.writable_dir(d)?,
                    _ => self.inbox(),
                };
                let name = req_str(&args, "name")?;
                let size = args["size"].as_u64();
                let kind = args["kind"].as_str().unwrap_or("").to_string();
                let xfer = link.alloc_xfer();
                let done = link.expect_incoming(xfer, &dir, Some(&name), size).await?;
                let me = self.clone();
                tokio::spawn(async move {
                    if let Ok(Ok(path)) = done.await {
                        me.on_file_received(&path, &kind);
                    }
                });
                Ok(json!({"xfer": xfer}))
            }
            "xfer.status" => {
                let xfer = args["xfer"].as_u64().ok_or_else(|| anyhow!("missing xfer"))? as u32;
                // The end-of-transfer frame arrives before this request, but
                // finishing the file is async; give it a moment.
                for _ in 0..100 {
                    if let Some(r) = link.finished_status(xfer) {
                        return r
                            .map(|p| json!({"path": crate::files::display(&p)}))
                            .map_err(|e| anyhow!(e));
                    }
                    tokio::time::sleep(Duration::from_millis(50)).await;
                }
                bail!("transfer {xfer} not finished")
            }
            "clip.get" => Ok(json!({"text": platform::clipboard_get_text().unwrap_or_default()})),
            "clip.set" => {
                let text = req_str(&args, "text")?;
                *self.clip_last.lock().unwrap() = text.clone();
                platform::clipboard_set_text(&text)?;
                self.log("clipboard", "phone → PC");
                Ok(json!({}))
            }
            "open_url" => {
                let url = req_str(&args, "url")?;
                platform::open_url(&url)?;
                self.log("link", format!("opened {url}"));
                Ok(json!({}))
            }
            "open_inbox" => {
                platform::open_path(&self.inbox())?;
                Ok(json!({}))
            }
            "sys" => {
                let cmd = req_str(&args, "cmd")?;
                self.log("system", format!("phone asked to {cmd}"));
                tokio::task::spawn_blocking(move || platform::sys(&cmd)).await??;
                Ok(json!({}))
            }
            "actions.list" => Ok(Value::Array(
                self.cfg
                    .actions
                    .iter()
                    .map(|a| json!({"name": a.name, "icon": a.icon}))
                    .collect(),
            )),
            "actions.run" => {
                let name = req_str(&args, "name")?;
                let wait = args["wait"].as_bool().unwrap_or(false);
                self.run_action(&name, wait).await
            }
            "mic.open" => self.open_mic().await,
            "cable.open" => Ok(json!({"message": crate::cable::open_download_page()?})),
            "mic.close" => {
                self.close_mic();
                Ok(json!({}))
            }
            "speaker.set" => {
                let on = args["on"].as_bool().unwrap_or(true);
                self.set_speaker(on).await.map(|on| json!({"on": on}))
            }
            "tasks.list" => Ok(json!(self.tasks.summaries())),
            "tasks.get" => {
                let id = args["id"].as_u64().ok_or_else(|| anyhow!("missing id"))?;
                let since = args["since"].as_u64().unwrap_or(0) as usize;
                self.tasks.get(id, since).ok_or_else(|| anyhow!("no task {id}"))
            }
            "tasks.start" => {
                let id = self.start_task(
                    &req_str(&args, "prompt")?,
                    args["resume"].as_u64(),
                    args["ask"].as_bool().unwrap_or(true),
                )?;
                Ok(json!({"id": id}))
            }
            "tasks.stop" => {
                let id = args["id"].as_u64().ok_or_else(|| anyhow!("missing id"))?;
                Ok(json!({"stopped": self.tasks.stop(id)}))
            }
            "notify" => {
                platform::notify(
                    args["title"].as_str().unwrap_or("Phone"),
                    args["text"].as_str().unwrap_or(""),
                );
                Ok(json!({}))
            }
            other => bail!("unknown request {other}"),
        }
    }

    fn on_file_received(&self, path: &std::path::Path, kind: &str) {
        let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("file").to_string();
        self.log("files", format!("phone → PC: {name}"));
        let mut note = format!("Saved to {}", crate::files::display(path.parent().unwrap_or(path)));
        if kind == "share" && self.cfg.image_to_clipboard && platform::is_image(path) {
            match platform::clipboard_set_image(path) {
                Ok(()) => note = "Image copied to clipboard. Paste with Ctrl+V.".into(),
                Err(e) => tracing::warn!("{e:#}"),
            }
        }
        platform::notify(&format!("Received {name}"), &note);
    }

    pub fn pc_status(&self) -> Value {
        let mut sys = sysinfo::System::new();
        sys.refresh_memory();
        sys.refresh_cpu_usage();
        std::thread::sleep(sysinfo::MINIMUM_CPU_UPDATE_INTERVAL);
        sys.refresh_cpu_usage();
        json!({
            "pc_name": self.cfg.pc_name,
            "cpu": sys.global_cpu_usage().round(),
            "mem_used": sys.used_memory(),
            "mem_total": sys.total_memory(),
            "uptime": sysinfo::System::uptime(),
            "speaker": self.speaker.lock().unwrap().is_some(),
            "mic": self.mic_on(),
        })
    }

    // ------------------------------------------------------------------
    // Actions (the phone's dev control panel)

    pub async fn run_action(self: &Arc<Self>, name: &str, wait: bool) -> Result<Value> {
        let action = self
            .cfg
            .actions
            .iter()
            .find(|a| a.name == name)
            .cloned()
            .ok_or_else(|| anyhow!("no action named {name}"))?;
        let job = self.job_counter.fetch_add(1, Ordering::SeqCst);
        self.log("action", format!("running {name}"));
        let me = self.clone();
        let task = tokio::spawn(async move {
            let started = Instant::now();
            let mut cmd = shell_command(&action.command);
            if let Some(cwd) = &action.cwd {
                cmd.current_dir(cwd);
            }
            let (code, output) = match cmd.output().await {
                Ok(out) => {
                    let mut text = String::from_utf8_lossy(&out.stdout).to_string();
                    text.push_str(&String::from_utf8_lossy(&out.stderr));
                    (out.status.code().unwrap_or(-1), tail(&text, 4000))
                }
                Err(e) => (-1, format!("failed to start: {e}")),
            };
            let result = json!({
                "job": job,
                "name": action.name,
                "code": code,
                "ok": code == 0,
                "secs": started.elapsed().as_secs(),
                "output": output,
            });
            me.log("action", format!("{} finished with code {code}", action.name));
            if let Some(l) = me.current_link() {
                let _ = l.event("action.done", result.clone()).await;
            }
            result
        });
        if wait {
            Ok(task.await?)
        } else {
            Ok(json!({"job": job, "started": true}))
        }
    }

    // ------------------------------------------------------------------
    // Phone as speaker

    pub fn speaker_on(&self) -> bool {
        self.speaker.lock().unwrap().is_some()
    }

    pub async fn toggle_speaker(&self) -> Result<bool> {
        let on = !self.speaker_on();
        self.set_speaker(on).await
    }

    pub async fn set_speaker(&self, on: bool) -> Result<bool> {
        if !on {
            let had = self.speaker.lock().unwrap().take().is_some();
            if had {
                if let Some(l) = self.current_link() {
                    let _ = l.request("audio.stop", json!({}), Duration::from_secs(5)).await;
                }
                self.log("speaker", "stopped");
            }
            self.changed();
            return Ok(false);
        }
        if self.speaker_on() {
            return Ok(true);
        }
        let link = self.phone()?;

        // Decide which output device to capture, switching Windows' default
        // to a silent virtual device when configured.
        let mode = self.cfg.speaker.pc_output.as_str();
        let previous = platform::default_output();
        let mut restore = None;
        let mut capture_name = previous.as_ref().map(|(_, n)| n.clone());
        // If Windows' default output is the cable the phone mic plays into,
        // capturing it would send the mic back to the phone instead of PC
        // sound: capture a real output instead.
        let mic_cable_now = self.mic.lock().unwrap().as_ref().map(|m| m.device.clone());
        if capture_name.is_some() && capture_name == mic_cable_now {
            capture_name = platform::output_devices()
                .into_iter()
                .map(|(_, n)| n)
                .find(|n| !crate::cable::is_virtual_cable(n));
        }
        if mode == "auto" || mode == "switch" {
            let want = self.cfg.speaker.silent_device.to_lowercase();
            // Never silence the PC through the cable the phone mic uses, or
            // PC audio would be heard through the mic.
            let mic_cable = self.mic.lock().unwrap().as_ref().map(|m| m.device.clone());
            let target = platform::output_devices()
                .into_iter()
                .find(|(_, name)| !want.is_empty() && name.to_lowercase().contains(&want) && Some(name) != mic_cable.as_ref());
            match target {
                Some((id, name)) => {
                    if previous.as_ref().map(|(pid, _)| pid != &id).unwrap_or(true) {
                        platform::set_default_output(&id)?;
                        restore = previous.as_ref().map(|(pid, _)| pid.clone());
                    }
                    capture_name = Some(name);
                }
                None if mode == "switch" => {
                    bail!("no output device matching `{}` (install VB-Cable or change speaker.silent_device)", self.cfg.speaker.silent_device)
                }
                None => {}
            }
        }

        let stop = Arc::new(AtomicBool::new(false));
        let (tx, rx) = std::sync::mpsc::channel();
        let restore_on_err = restore.clone();
        let fail = |e: anyhow::Error| {
            stop.store(true, Ordering::Relaxed);
            if let Some(id) = &restore_on_err {
                let _ = platform::set_default_output(id);
            }
            e
        };
        let fmt = platform::start_capture(capture_name.clone(), tx, stop.clone()).map_err(fail)?;
        let key = util::random_bytes::<32>();
        let frame_ms = self.cfg.speaker.frame_ms;
        let res = link
            .request(
                "audio.start",
                json!({
                    "key": base64::engine::general_purpose::STANDARD.encode(key),
                    "rate": fmt.rate,
                    "channels": fmt.channels,
                    "frame_ms": frame_ms,
                }),
                Duration::from_secs(10),
            )
            .await
            .map_err(fail)?;
        let port = res["port"].as_u64().ok_or_else(|| fail(anyhow!("phone gave no audio port")))? as u16;
        let dest = std::net::SocketAddr::new(link.peer.ip(), port);
        let stats = Arc::new(audio::SendStats::default());
        audio::spawn_sender_with_stats(rx, dest, key, fmt, frame_ms, stop.clone(), stats.clone()).map_err(fail)?;
        let device = capture_name.unwrap_or_else(|| "default output".into());
        self.log("speaker", format!("streaming {} Hz from {device} to the phone", fmt.rate));
        *self.speaker.lock().unwrap() = Some(SpeakerSession {
            stop,
            restore_default: restore,
            stats,
            device,
            format: fmt,
        });
        self.changed();
        Ok(true)
    }

    // ------------------------------------------------------------------
    // Clipboard watcher (PC → phone)

    pub fn spawn_clipboard_watch(self: &Arc<Self>) {
        if !self.cfg.clipboard_sync {
            return;
        }
        let me = self.clone();
        let rt = tokio::runtime::Handle::current();
        std::thread::Builder::new()
            .name("pclink-clipboard".into())
            .spawn(move || {
                let mut last_seen = platform::clipboard_get_text().unwrap_or_default();
                loop {
                    std::thread::sleep(Duration::from_millis(700));
                    let Some(text) = platform::clipboard_get_text() else { continue };
                    if text == last_seen {
                        continue;
                    }
                    last_seen = text.clone();
                    if *me.clip_last.lock().unwrap() == text || text.len() > 1_000_000 {
                        continue;
                    }
                    *me.clip_last.lock().unwrap() = text.clone();
                    if let Some(l) = me.current_link() {
                        rt.spawn(async move {
                            let _ = l.event("clip.changed", json!({"text": text})).await;
                        });
                    }
                }
            })
            .expect("clipboard thread");
    }

    pub fn status_json(&self) -> Value {
        let link = self.current_link();
        let st = self.state.lock().unwrap();
        let speaker = self.speaker.lock().unwrap();
        let pairing = self.pairing.lock().unwrap();
        json!({
            "pc_name": self.cfg.pc_name,
            "pc_id": st.pc_id,
            "version": crate::update::version_string(),
            "build": crate::update::BUILD,
            "auto_update": self.cfg.auto_update,
            "fingerprint": self.identity.fingerprint,
            "port": self.cfg.port,
            "connected": link.is_some(),
            "phone": link.as_ref().map(|l| json!({
                "id": l.device_id,
                "name": l.device_name,
                "ip": l.peer.ip().to_string(),
                "status": self.phone_status.lock().unwrap().clone(),
            })),
            "devices": st.devices,
            "speaker": speaker.as_ref().map(|s| json!({
                "device": s.device,
                "rate": s.format.rate,
                "channels": s.format.channels,
                "packets": s.stats.packets.load(Ordering::Relaxed),
                "level": s.stats.level.load(Ordering::Relaxed),
            })),
            "idle_secs": platform::idle_secs(),
            "mic": self.mic_status(),
            "cable_present": crate::mic::cable_present(),
            "claude": self.cfg.claude,
            "pairing_active": pairing.as_ref().map(|p| p.expires > Instant::now()).unwrap_or(false),
            "inbox": crate::files::display(&self.cfg.inbox),
            "shared_folders": self.fs.roots_json(),
            "actions": self.cfg.actions.iter().map(|a| json!({"name": a.name, "icon": a.icon})).collect::<Vec<_>>(),
        })
    }
}

pub fn features() -> Vec<&'static str> {
    let mut f = vec!["files", "clipboard", "notifications", "actions", "url", "claude"];
    if cfg!(windows) {
        f.extend(["speaker", "input", "media", "sys"]);
    }
    f
}

fn s(v: &Value, k: &str) -> String {
    v[k].as_str().unwrap_or("").to_string()
}

fn req_str(args: &Value, key: &str) -> Result<String> {
    args[key]
        .as_str()
        .map(str::to_string)
        .ok_or_else(|| anyhow!("missing `{key}`"))
}

fn parse_input(args: &Value) -> Result<platform::InputCmd> {
    use platform::InputCmd::*;
    let i = |k: &str| args[k].as_f64().unwrap_or(0.0).round() as i32;
    Ok(match args["kind"].as_str().unwrap_or("") {
        "move" => Move { dx: i("dx"), dy: i("dy") },
        "click" => Click {
            button: args["button"].as_str().unwrap_or("left").into(),
            double: args["double"].as_bool().unwrap_or(false),
        },
        "button" => Button {
            button: args["button"].as_str().unwrap_or("left").into(),
            down: args["down"].as_bool().unwrap_or(false),
        },
        "scroll" => Scroll { dx: i("dx"), dy: i("dy") },
        "text" => Text(req_str(args, "text")?),
        "key" => Key {
            key: req_str(args, "key")?,
            modifiers: args["modifiers"]
                .as_array()
                .map(|a| a.iter().filter_map(|m| m.as_str().map(str::to_string)).collect())
                .unwrap_or_default(),
        },
        other => bail!("unknown input kind {other}"),
    })
}

pub fn shell_command(command: &str) -> tokio::process::Command {
    #[cfg(windows)]
    {
        let mut c = tokio::process::Command::new("cmd.exe");
        c.arg("/C").raw_arg(command);
        // Don't flash a console window from the tray app.
        c.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
        c
    }
    #[cfg(not(windows))]
    {
        let mut c = tokio::process::Command::new("sh");
        c.arg("-c").arg(command);
        c
    }
}

pub fn tail(text: &str, max: usize) -> String {
    if text.len() <= max {
        return text.to_string();
    }
    let mut start = text.len() - max;
    while !text.is_char_boundary(start) {
        start += 1;
    }
    format!("…{}", &text[start..])
}

/// The first `max` bytes of `text` (on a char boundary), with "…" if cut.
pub fn head(text: &str, max: usize) -> String {
    if text.len() <= max {
        return text.to_string();
    }
    let mut end = max;
    while !text.is_char_boundary(end) {
        end -= 1;
    }
    format!("{}…", &text[..end])
}

pub fn url_encode(s: &str) -> String {
    let mut out = String::new();
    for b in s.bytes() {
        match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => out.push(b as char),
            _ => out.push_str(&format!("%{b:02X}")),
        }
    }
    out
}

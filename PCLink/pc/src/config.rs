//! On-disk configuration (`config.toml`, edited by the user) and state
//! (`state.json`, written by the agent: paired devices, local API token).

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

pub const DEFAULT_PORT: u16 = 47810;
pub const DISCOVERY_PORT: u16 = 47811;
pub const API_PORT: u16 = 47812;

/// Where PCLink keeps its files. `PCLINK_HOME` overrides it (used by tests and
/// for running several agents on one machine).
pub fn home_dir() -> PathBuf {
    if let Ok(dir) = std::env::var("PCLINK_HOME") {
        return PathBuf::from(dir);
    }
    dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("PCLink")
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct Config {
    /// Name the phone shows for this PC.
    pub pc_name: String,
    /// TLS port the phone connects to.
    pub port: u16,
    /// Port of the local HTTP API and dashboard (bound to 127.0.0.1 only).
    pub api_port: u16,
    /// Folders the phone may browse, download from and upload to.
    /// Nothing outside these folders is reachable from the phone.
    pub shared_folders: Vec<PathBuf>,
    /// Where files sent from the phone land.
    pub inbox: PathBuf,
    /// Keep the PC and phone clipboards in sync automatically.
    pub clipboard_sync: bool,
    /// When the phone shares an image, also put it on the PC clipboard
    /// (handy for pasting a photo of an error straight into Claude).
    pub image_to_clipboard: bool,
    /// Show phone notifications as Windows notifications.
    pub mirror_notifications: bool,
    /// Apps whose notifications are not mirrored (Android package names).
    pub notification_blocklist: Vec<String>,
    /// Check GitHub for a newer build at launch and install it.
    pub auto_update: bool,
    /// WebSocket relay (relay/server.js) the phone uses when it can't reach
    /// this PC directly, e.g. on mobile data. Empty turns it off. The relay
    /// only passes along the phone's end-to-end encrypted connection.
    pub relay: String,
    pub speaker: SpeakerConfig,
    pub mic: MicConfig,
    pub claude: ClaudeConfig,
    /// Buttons shown on the phone's control panel.
    pub actions: Vec<Action>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct SpeakerConfig {
    /// What happens to the PC's own speakers while the phone plays:
    /// "auto"   - mute them (Windows' default output and apps stay as they
    ///            are); if muting also silences the capture on this PC, fall
    ///            back to "switch", or play on both if there's no spare cable.
    /// "mute"   - always mute.
    /// "switch" - make `silent_device` (a virtual cable) the default output.
    /// "keep"   - leave the PC alone; sound plays on both.
    pub pc_output: String,
    /// Part of the name of the output device to switch to, e.g. "CABLE Input".
    pub silent_device: String,
    /// Audio frame length sent per packet, in milliseconds.
    pub frame_ms: u32,
}

impl Default for SpeakerConfig {
    fn default() -> Self {
        Self {
            pc_output: "auto".into(),
            silent_device: "CABLE Input".into(),
            frame_ms: 5,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct MicConfig {
    /// Output device the phone's mic is played into: "auto" picks a virtual
    /// cable (VB-Cable), preferring one the speaker feature isn't using; or
    /// part of a device name, e.g. "CABLE-A Input".
    pub device: String,
    /// While the mic is on, make the cable's recording side Windows' default
    /// microphone (restored afterwards), so apps pick it up automatically.
    pub set_default: bool,
}

impl Default for MicConfig {
    fn default() -> Self {
        Self { device: "auto".into(), set_default: true }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct ClaudeConfig {
    /// When Claude Code's permission prompts go to the phone:
    /// "away" (only when you haven't touched the PC for `away_after_secs`),
    /// "always", or "off".
    pub approvals: String,
    /// When "Claude finished / needs you" notifications go to the phone:
    /// "away", "always" or "off".
    pub notify: String,
    pub away_after_secs: u64,
    /// How long a permission prompt waits for an answer on the phone before
    /// falling back to the normal prompt on the PC.
    pub ask_timeout_secs: u64,
    /// Tasks sent from the phone ("Claude, do X on my PC"):
    /// model alias or full model name.
    pub task_model: String,
    /// Folder tasks run in (default: your home folder).
    pub task_dir: Option<PathBuf>,
    /// Path to the `claude` CLI if it isn't found automatically.
    pub cli: Option<PathBuf>,
}

impl Default for ClaudeConfig {
    fn default() -> Self {
        Self {
            approvals: "away".into(),
            notify: "away".into(),
            away_after_secs: 60,
            ask_timeout_secs: 300,
            task_model: "opus".into(),
            task_dir: None,
            cli: None,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Action {
    pub name: String,
    /// Run through `cmd /C` on Windows, `sh -c` elsewhere.
    pub command: String,
    #[serde(default)]
    pub cwd: Option<PathBuf>,
    /// Optional emoji or short label shown on the button.
    #[serde(default)]
    pub icon: Option<String>,
}

impl Default for Config {
    fn default() -> Self {
        let home = dirs::home_dir().unwrap_or_else(|| PathBuf::from("."));
        let mut shared = Vec::new();
        for d in [dirs::download_dir(), dirs::document_dir(), dirs::desktop_dir()]
            .into_iter()
            .flatten()
        {
            shared.push(d);
        }
        if shared.is_empty() {
            shared.push(home.clone());
        }
        Self {
            pc_name: hostname(),
            port: DEFAULT_PORT,
            api_port: API_PORT,
            shared_folders: shared,
            inbox: dirs::download_dir()
                .unwrap_or(home)
                .join("PCLink"),
            clipboard_sync: true,
            image_to_clipboard: true,
            mirror_notifications: true,
            notification_blocklist: vec![],
            auto_update: true,
            relay: DEFAULT_RELAY.into(),
            speaker: SpeakerConfig::default(),
            mic: MicConfig::default(),
            claude: ClaudeConfig::default(),
            actions: vec![
                Action {
                    name: "Git status".into(),
                    command: "git status --short --branch".into(),
                    cwd: None,
                    icon: Some("🌿".into()),
                },
                Action {
                    name: "IP config".into(),
                    command: if cfg!(windows) { "ipconfig".into() } else { "ip addr".into() },
                    cwd: None,
                    icon: Some("🌐".into()),
                },
            ],
        }
    }
}

pub fn hostname() -> String {
    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .ok()
        .filter(|s| !s.is_empty())
        .or_else(|| {
            std::fs::read_to_string("/etc/hostname")
                .ok()
                .map(|s| s.trim().to_string())
        })
        .unwrap_or_else(|| "My PC".into())
}

pub const DEFAULT_RELAY: &str = "wss://agentrelay.arketek.co.za";

impl Config {
    pub fn path() -> PathBuf {
        home_dir().join("config.toml")
    }

    /// Loads `config.toml`, writing a commented default the first time.
    pub fn load_or_create() -> Result<Config> {
        let path = Self::path();
        if path.exists() {
            let text = std::fs::read_to_string(&path)
                .with_context(|| format!("reading {}", path.display()))?;
            return toml::from_str(&text).with_context(|| format!("parsing {}", path.display()));
        }
        let cfg = Config::default();
        std::fs::create_dir_all(home_dir())?;
        let body = toml::to_string_pretty(&cfg)?;
        std::fs::write(
            &path,
            format!(
                "# PCLink configuration. Restart the agent after editing.\n\
                 # shared_folders: the only folders your phone can see.\n\
                 # [[actions]]: buttons on the phone's control panel.\n\n{body}"
            ),
        )?;
        Ok(cfg)
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct PairedDevice {
    pub id: String,
    pub name: String,
    /// SHA-256 of the device token, hex. The token itself only lives on the phone.
    pub token_sha256: String,
    pub paired_at: u64,
    #[serde(default)]
    pub last_seen: u64,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct State {
    pub pc_id: String,
    pub devices: Vec<PairedDevice>,
    /// Secret room id on the relay; only this PC and its paired phones know it.
    #[serde(default)]
    pub relay_room: String,
}

impl State {
    pub fn path() -> PathBuf {
        home_dir().join("state.json")
    }

    pub fn load_or_create() -> Result<State> {
        let path = Self::path();
        if path.exists() {
            let text = std::fs::read_to_string(&path)?;
            let mut state: State = serde_json::from_str(&text)?;
            if state.relay_room.len() != 64 {
                state.relay_room = crate::util::random_hex(32);
                state.save()?;
            }
            return Ok(state);
        }
        let state = State {
            pc_id: crate::util::random_hex(8),
            devices: vec![],
            relay_room: crate::util::random_hex(32),
        };
        state.save()?;
        Ok(state)
    }

    pub fn save(&self) -> Result<()> {
        std::fs::create_dir_all(home_dir())?;
        write_private(&Self::path(), serde_json::to_string_pretty(self)?.as_bytes())
    }
}

/// Token the CLI, MCP server and dashboard use for the local API.
pub fn local_token_path() -> PathBuf {
    home_dir().join("local-token")
}

pub fn load_or_create_local_token() -> Result<String> {
    let path = local_token_path();
    if let Ok(t) = std::fs::read_to_string(&path) {
        let t = t.trim().to_string();
        if t.len() >= 32 {
            return Ok(t);
        }
    }
    let token = crate::util::random_hex(32);
    std::fs::create_dir_all(home_dir())?;
    write_private(&path, token.as_bytes())?;
    Ok(token)
}

/// Writes a file readable only by the current user (on Unix; on Windows the
/// per-user AppData folder already restricts access).
pub fn write_private(path: &Path, data: &[u8]) -> Result<()> {
    let tmp = path.with_extension("tmp");
    std::fs::write(&tmp, data)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600))?;
    }
    std::fs::rename(&tmp, path)?;
    Ok(())
}

//! OS integration. Windows gets the full implementation; other systems get
//! working clipboard/URL support and clear "not supported" errors for the
//! rest, so the agent still runs (and is testable) on Linux.

use anyhow::{bail, Context, Result};

#[cfg(windows)]
mod windows;
#[cfg(windows)]
pub use self::windows::*;

#[cfg(not(windows))]
mod other;
#[cfg(not(windows))]
pub use other::*;

/// Input events sent from the phone's touchpad and keyboard.
#[derive(Debug, Clone)]
pub enum InputCmd {
    Move { dx: i32, dy: i32 },
    Click { button: String, double: bool },
    Button { button: String, down: bool },
    Scroll { dx: i32, dy: i32 },
    Text(String),
    /// A named key ("enter", "backspace", "esc", "up", ...) with optional
    /// modifiers ("ctrl", "shift", "alt", "win").
    Key { key: String, modifiers: Vec<String> },
}

/// A display, in virtual-desktop pixels.
#[derive(Debug, Clone, serde::Serialize)]
pub struct Monitor {
    pub index: usize,
    pub primary: bool,
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
}

/// A top-level window on the desktop.
#[derive(Debug, Clone, serde::Serialize)]
pub struct WindowInfo {
    /// Opaque id to pass back to window actions.
    pub id: u64,
    pub title: String,
    pub process: String,
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
    pub monitor: usize,
    /// "normal", "maximized" or "minimized".
    pub state: String,
}

pub fn clipboard_get_text() -> Option<String> {
    arboard::Clipboard::new().ok()?.get_text().ok()
}

pub fn clipboard_set_text(text: &str) -> Result<()> {
    arboard::Clipboard::new()
        .context("opening clipboard")?
        .set_text(text.to_string())
        .context("setting clipboard")
}

/// Decodes a PNG/JPEG and places it on the clipboard as a bitmap, so it can
/// be pasted into apps (including Claude Code) with Ctrl+V.
pub fn clipboard_set_image(path: &std::path::Path) -> Result<()> {
    let img = image::open(path)
        .with_context(|| format!("decoding {}", path.display()))?
        .to_rgba8();
    let (w, h) = img.dimensions();
    arboard::Clipboard::new()?
        .set_image(arboard::ImageData {
            width: w as usize,
            height: h as usize,
            bytes: std::borrow::Cow::Owned(img.into_raw()),
        })
        .context("setting clipboard image")
}

pub fn is_image(path: &std::path::Path) -> bool {
    matches!(
        path.extension()
            .and_then(|e| e.to_str())
            .map(|e| e.to_ascii_lowercase())
            .as_deref(),
        Some("png" | "jpg" | "jpeg")
    )
}

/// Opens an http(s) URL in the default browser.
pub fn open_url(url: &str) -> Result<()> {
    if !(url.starts_with("http://") || url.starts_with("https://")) {
        bail!("only http(s) links can be opened");
    }
    if url.chars().any(|c| c.is_control() || c == '"') {
        bail!("invalid URL");
    }
    open_with_shell(url)
}

/// Opens a local folder or file with its default handler.
pub fn open_path(path: &std::path::Path) -> Result<()> {
    open_with_shell(&path.to_string_lossy())
}

#[cfg(windows)]
fn open_with_shell(target: &str) -> Result<()> {
    // ShellExecuteW opens URLs in the default browser and folders in
    // Explorer. (Passing a URL to explorer.exe breaks on `#` and `&` and
    // just opens the Documents folder.)
    shell_execute(target)
}

#[cfg(not(windows))]
fn open_with_shell(target: &str) -> Result<()> {
    #[cfg(target_os = "macos")]
    let mut cmd = {
        let mut c = std::process::Command::new("open");
        c.arg(target);
        c
    };
    #[cfg(all(unix, not(target_os = "macos")))]
    let mut cmd = {
        let mut c = std::process::Command::new("xdg-open");
        c.arg(target);
        c
    };
    cmd.spawn().context("opening")?;
    Ok(())
}

use super::InputCmd;
use crate::audio::AudioFormat;
use anyhow::{bail, Result};
use std::sync::atomic::AtomicBool;
use std::sync::mpsc::Sender;
use std::sync::Arc;

pub fn notify(title: &str, body: &str) {
    tracing::info!(target: "notify", "{title}: {body}");
}

pub fn input(_cmd: InputCmd) -> Result<()> {
    bail!("remote input is only supported on Windows")
}

pub fn media(_cmd: &str) -> Result<()> {
    bail!("media keys are only supported on Windows")
}

pub fn sys(cmd: &str) -> Result<()> {
    bail!("`{cmd}` is only supported on Windows")
}

pub fn output_devices() -> Vec<(String, String)> {
    vec![]
}

pub fn default_output() -> Option<(String, String)> {
    None
}

pub fn set_default_output(_id: &str) -> Result<()> {
    bail!("switching audio devices is only supported on Windows")
}

pub fn start_capture(
    _device_name: Option<String>,
    _sink: Sender<Vec<i16>>,
    _stop: Arc<AtomicBool>,
) -> Result<AudioFormat> {
    bail!("capturing PC audio is only supported on Windows")
}

/// Seconds since the last keyboard/mouse input, if the OS can tell us.
pub fn idle_secs() -> Option<u64> {
    None
}

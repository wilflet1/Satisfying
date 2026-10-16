use super::InputCmd;
use crate::audio::AudioFormat;
use anyhow::{bail, Result};
use std::sync::atomic::AtomicBool;
use std::sync::mpsc::Sender;
use std::sync::Arc;

pub fn notify(title: &str, body: &str) {
    tracing::info!(target: "notify", "{title}: {body}");
}

pub fn input_queue(_cmd: InputCmd) -> Result<()> {
    bail!("remote input is only supported on Windows")
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

/// Capturing PC audio needs Windows. For testing the speaker pipeline
/// elsewhere, `PCLINK_TEST_TONE=1` streams a 440 Hz test tone instead.
pub fn start_capture(
    _device_name: Option<String>,
    sink: Sender<Vec<i16>>,
    stop: Arc<AtomicBool>,
) -> Result<AudioFormat> {
    if std::env::var_os("PCLINK_TEST_TONE").is_none() {
        bail!("capturing PC audio is only supported on Windows");
    }
    let fmt = AudioFormat { rate: 48_000, channels: 2 };
    std::thread::spawn(move || {
        use std::sync::atomic::Ordering;
        let block = 480; // 10 ms
        let mut t = 0u64;
        let start = std::time::Instant::now();
        while !stop.load(Ordering::Relaxed) {
            let mut out = Vec::with_capacity(block * 2);
            for _ in 0..block {
                let x = (t as f64 * 440.0 * std::f64::consts::TAU / 48_000.0).sin();
                let v = (x * 0.3 * 32767.0) as i16;
                out.extend_from_slice(&[v, v]);
                t += 1;
            }
            if sink.send(out).is_err() {
                break;
            }
            // Pace to real time.
            let due = std::time::Duration::from_micros(t * 1_000_000 / 48_000);
            if let Some(wait) = due.checked_sub(start.elapsed()) {
                std::thread::sleep(wait);
            }
        }
    });
    Ok(fmt)
}

/// Seconds since the last keyboard/mouse input, if the OS can tell us.
pub fn idle_secs() -> Option<u64> {
    None
}

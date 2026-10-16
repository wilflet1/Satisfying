//! Phone mic → PC: the phone records (e.g. its headset mic) and streams
//! encrypted 16-bit mono PCM over UDP, in the same packet format as the
//! speaker stream. The agent plays it into a virtual cable's input; apps
//! use the cable's output as their microphone.

use crate::agent::Agent;
use crate::audio;
use crate::platform;
use anyhow::{anyhow, bail, Result};
use base64::Engine;
use serde_json::{json, Value};
use std::collections::VecDeque;
use std::net::UdpSocket;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

pub const MIC_RATE: u32 = 48_000;
/// Error text the apps recognise to offer installing VB-Cable.
pub const NO_CABLE: &str = "No virtual audio cable on the PC: install VB-Cable from VB-Audio, then restart the PC.";
/// Audio older than this is dropped so the mic stays live, not delayed.
const MAX_BUFFER_MS: usize = 160;

#[derive(Default)]
pub struct MicStats {
    pub packets: AtomicU64,
    /// RMS level of the last packets, 0..1000.
    pub level: AtomicU32,
}

pub struct MicSession {
    stop: Arc<AtomicBool>,
    restore_input: Option<String>,
    pub device: String,
    pub recording_device: Option<String>,
    pub stats: Arc<MicStats>,
}

impl Drop for MicSession {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
        if let Some(id) = self.restore_input.take() {
            if let Err(e) = platform::set_default_output(&id) {
                tracing::warn!("could not restore the default microphone: {e:#}");
            }
        }
    }
}

/// Picks the output device for the mic: a configured name, or in "auto"
/// mode a virtual cable, preferring one the speaker isn't using.
pub fn choose_device(outputs: &[(String, String)], wanted: &str, speaker_cable: &str) -> Result<String> {
    let names = outputs.iter().map(|(_, n)| n.clone());
    if wanted != "auto" {
        let w = wanted.to_lowercase();
        return names
            .clone()
            .find(|n| n.to_lowercase().contains(&w))
            .ok_or_else(|| anyhow!("no output device matching `{wanted}` (mic.device in config.toml)"));
    }
    let cables: Vec<String> = names.filter(|n| crate::cable::is_virtual_cable(n)).collect();
    let speaker = speaker_cable.to_lowercase();
    cables
        .iter()
        .find(|n| speaker.is_empty() || !n.to_lowercase().contains(&speaker))
        .or_else(|| cables.first())
        .cloned()
        .ok_or_else(|| anyhow!("{NO_CABLE}"))
}

/// The recording side of a cable: "CABLE-A Input (…)" → "CABLE-A Output (…)".
pub fn recording_side(output_name: &str) -> String {
    output_name.replacen("Input", "Output", 1)
}

/// Whether this PC has a virtual cable the mic can use.
pub fn cable_present() -> bool {
    platform::output_devices().iter().any(|(_, n)| crate::cable::is_virtual_cable(n))
}

/// Decrypts packets from the phone into the playback buffer.
fn spawn_receiver(
    sock: UdpSocket,
    key: [u8; 32],
    buffer: Arc<Mutex<VecDeque<i16>>>,
    stats: Arc<MicStats>,
    stop: Arc<AtomicBool>,
) {
    std::thread::Builder::new()
        .name("pclink-mic-receive".into())
        .spawn(move || {
            let key = audio::make_key(&key);
            let _ = sock.set_read_timeout(Some(Duration::from_millis(200)));
            let mut buf = [0u8; 4096];
            let mut last_seq: Option<u64> = None;
            let max = MIC_RATE as usize * MAX_BUFFER_MS / 1000;
            while !stop.load(Ordering::Relaxed) {
                let Ok(n) = sock.recv(&mut buf) else { continue };
                let Some((seq, pcm)) = audio::open(&key, &buf[..n]) else { continue };
                if last_seq.map(|l| seq <= l).unwrap_or(false) {
                    continue; // late or duplicate
                }
                last_seq = Some(seq);
                let mut sum = 0f64;
                let mut q = buffer.lock().unwrap();
                for s in pcm.chunks_exact(2) {
                    let v = i16::from_le_bytes([s[0], s[1]]);
                    sum += (v as f64 / 32768.0).powi(2);
                    q.push_back(v);
                }
                let excess = q.len().saturating_sub(max);
                q.drain(..excess);
                drop(q);
                let rms = (sum / (pcm.len() / 2).max(1) as f64).sqrt();
                stats.level.store((rms * 1000.0) as u32, Ordering::Relaxed);
                stats.packets.fetch_add(1, Ordering::Relaxed);
            }
        })
        .expect("mic receive thread");
}

/// Consumes the buffer in real time without playing it (tests).
fn null_playback(buffer: Arc<Mutex<VecDeque<i16>>>, stop: Arc<AtomicBool>) {
    std::thread::spawn(move || {
        while !stop.load(Ordering::Relaxed) {
            std::thread::sleep(Duration::from_millis(10));
            let mut b = buffer.lock().unwrap();
            let n = (MIC_RATE as usize / 100).min(b.len());
            b.drain(..n);
        }
    });
}

impl Agent {
    pub fn mic_on(&self) -> bool {
        self.mic.lock().unwrap().is_some()
    }

    /// Prepares to receive the phone's mic. Returns what the phone needs to
    /// stream to us: UDP port, key and format.
    pub async fn open_mic(self: &Arc<Self>) -> Result<Value> {
        *self.mic.lock().unwrap() = None;
        self.phone()?;
        // Tests (PCLINK_TEST_TONE) use a null output instead of a real device.
        let test_mode = std::env::var_os("PCLINK_TEST_TONE").is_some();
        let device = if test_mode {
            "test output".to_string()
        } else {
            choose_device(&platform::output_devices(), &self.cfg.mic.device, &self.cfg.speaker.silent_device)?
        };

        // If the speaker is using this cable (to silence the PC, or because
        // it was Windows' default), PC audio and the mic would mix. Stop it
        // first; it restarts below on a real output.
        let speaker_shares = self
            .speaker
            .lock()
            .unwrap()
            .as_ref()
            .map(|s| s.device == device)
            .unwrap_or(false);
        if speaker_shares {
            let _ = self.set_speaker(false).await;
        }
        // Windows often makes a newly installed cable the default playback
        // device: then PC sound goes silent and leaks into the mic. Put the
        // default back on a real output.
        if !test_mode {
            self.fix_default_output(&device);
        }

        let stop = Arc::new(AtomicBool::new(false));
        let buffer = Arc::new(Mutex::new(VecDeque::new()));
        let stats = Arc::new(MicStats::default());
        if test_mode {
            null_playback(buffer.clone(), stop.clone());
        } else {
            platform::start_playback(device.clone(), buffer.clone(), MIC_RATE, stop.clone())?;
        }
        let sock = UdpSocket::bind("0.0.0.0:0")?;
        let port = sock.local_addr()?.port();
        let key = crate::util::random_bytes::<32>();
        spawn_receiver(sock, key, buffer, stats.clone(), stop.clone());

        let mut restore_input = None;
        let recording = recording_side(&device);
        let recording_found = platform::input_devices().into_iter().find(|(_, n)| *n == recording);
        if self.cfg.mic.set_default {
            if let Some((id, _)) = &recording_found {
                let previous = platform::default_input();
                if previous.as_ref().map(|(pid, _)| pid != id).unwrap_or(true) {
                    match platform::set_default_output(id) {
                        Ok(()) => restore_input = previous.map(|(pid, _)| pid),
                        Err(e) => tracing::warn!("could not set the default microphone: {e:#}"),
                    }
                }
            }
        }
        *self.mic.lock().unwrap() = Some(MicSession {
            stop,
            restore_input,
            device: device.clone(),
            recording_device: recording_found.map(|(_, n)| n),
            stats,
        });
        if speaker_shares {
            if let Err(e) = self.set_speaker(true).await {
                tracing::warn!("restarting the speaker: {e:#}");
            }
        }
        self.log("mic", format!("phone mic → {device}"));
        self.changed();
        Ok(json!({
            "port": port,
            "key": base64::engine::general_purpose::STANDARD.encode(key),
            "rate": MIC_RATE,
            "channels": 1,
            "device": device,
            "recording_device": recording,
        }))
    }

    /// If Windows plays PC sound into `cable`, switch the default output to
    /// a real device (when the speaker isn't deliberately silencing the PC).
    fn fix_default_output(&self, cable: &str) {
        let Some((_, default_name)) = platform::default_output() else { return };
        if default_name != cable {
            return;
        }
        let real = platform::output_devices()
            .into_iter()
            .find(|(_, n)| !crate::cable::is_virtual_cable(n));
        if let Some((id, name)) = real {
            if platform::set_default_output(&id).is_ok() {
                self.log("mic", format!("PC sound was going into {cable}; switched it back to {name}"));
                platform::notify(
                    "PCLink fixed your PC sound",
                    &format!("Windows was playing PC sound into {cable} (common after installing VB-Cable), so you couldn't hear it and it went into the mic. It now plays on {name}."),
                );
            }
        }
    }

    pub fn close_mic(&self) {
        if self.mic.lock().unwrap().take().is_some() {
            self.log("mic", "phone mic off");
            self.changed();
        }
    }

    /// From the dashboard/tray: asks the phone to start or stop its mic
    /// (the phone then calls `mic.open`).
    pub async fn set_mic(self: &Arc<Self>, on: bool) -> Result<bool> {
        let link = self.phone()?;
        if on {
            link.request("mic.start", json!({}), Duration::from_secs(15)).await?;
            if !self.mic_on() {
                bail!("the phone didn't start its mic");
            }
            Ok(true)
        } else {
            let _ = link.request("mic.stop", json!({}), Duration::from_secs(10)).await;
            self.close_mic();
            Ok(false)
        }
    }

    pub fn mic_status(&self) -> Value {
        match self.mic.lock().unwrap().as_ref() {
            Some(m) => json!({
                "device": m.device,
                "recording_device": m.recording_device,
                "packets": m.stats.packets.load(Ordering::Relaxed),
                "level": m.stats.level.load(Ordering::Relaxed),
            }),
            None => Value::Null,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn outs(names: &[&str]) -> Vec<(String, String)> {
        names.iter().map(|n| (format!("id-{n}"), n.to_string())).collect()
    }

    #[test]
    fn picks_a_cable_the_speaker_isnt_using() {
        let devices = outs(&["Speakers (Realtek)", "CABLE Input (VB-Audio Virtual Cable)", "CABLE-A Input (VB-Audio Cable A)"]);
        assert_eq!(choose_device(&devices, "auto", "CABLE Input").unwrap(), "CABLE-A Input (VB-Audio Cable A)");
        // Only one cable: share it (the speaker is moved off it).
        let one = outs(&["Speakers", "CABLE Input (VB-Audio Virtual Cable)"]);
        assert_eq!(choose_device(&one, "auto", "CABLE Input").unwrap(), "CABLE Input (VB-Audio Virtual Cable)");
        assert!(choose_device(&outs(&["Speakers"]), "auto", "CABLE Input").unwrap_err().to_string().contains("VB-Cable"));
        assert_eq!(choose_device(&devices, "realtek", "").unwrap(), "Speakers (Realtek)");
        assert_eq!(recording_side("CABLE-A Input (VB-Audio Cable A)"), "CABLE-A Output (VB-Audio Cable A)");
    }

    #[test]
    fn receiver_buffers_and_measures() {
        let sock = UdpSocket::bind("127.0.0.1:0").unwrap();
        let addr = sock.local_addr().unwrap();
        let key = crate::util::random_bytes::<32>();
        let buffer = Arc::new(Mutex::new(VecDeque::new()));
        let stats = Arc::new(MicStats::default());
        let stop = Arc::new(AtomicBool::new(false));
        spawn_receiver(sock, key, buffer.clone(), stats.clone(), stop.clone());
        let sender = UdpSocket::bind("127.0.0.1:0").unwrap();
        let k = audio::make_key(&key);
        let pcm: Vec<u8> = std::iter::repeat_n(8000i16.to_le_bytes(), 480).flatten().collect();
        for seq in [0u64, 1, 1, 0, 2] {
            sender.send_to(&audio::seal(&k, seq, &pcm), addr).unwrap();
        }
        sender.send_to(b"garbage packet that is long enough to parse", addr).unwrap();
        std::thread::sleep(Duration::from_millis(300));
        // Duplicates and late packets dropped, garbage ignored.
        assert_eq!(stats.packets.load(Ordering::Relaxed), 3);
        assert_eq!(buffer.lock().unwrap().len(), 3 * 480);
        assert!(stats.level.load(Ordering::Relaxed) > 200);
        stop.store(true, Ordering::Relaxed);
    }
}

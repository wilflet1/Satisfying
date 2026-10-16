//! Phone-as-speaker streaming: interleaved 16-bit PCM, cut into short
//! frames, sealed with AES-256-GCM and sent over UDP.
//!
//! Packet layout: `u64 big-endian sequence | ciphertext | 16-byte tag`.
//! The GCM nonce is 4 zero bytes followed by the sequence number. A fresh
//! key is sent to the phone over the TLS link for every session, so
//! sequence numbers never repeat under one key.

use anyhow::Result;
use ring::aead::{Aad, LessSafeKey, Nonce, UnboundKey, AES_256_GCM};
use std::net::{SocketAddr, UdpSocket};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, RecvTimeoutError};
use std::sync::Arc;
use std::time::Duration;

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct AudioFormat {
    pub rate: u32,
    /// 1 or 2; capture code downmixes anything wider.
    pub channels: u16,
}

fn nonce(seq: u64) -> Nonce {
    let mut n = [0u8; 12];
    n[4..].copy_from_slice(&seq.to_be_bytes());
    Nonce::assume_unique_for_key(n)
}

pub fn seal(key: &LessSafeKey, seq: u64, pcm: &[u8]) -> Vec<u8> {
    let mut buf = Vec::with_capacity(8 + pcm.len() + 16);
    buf.extend_from_slice(&seq.to_be_bytes());
    let mut body = pcm.to_vec();
    key.seal_in_place_append_tag(nonce(seq), Aad::empty(), &mut body)
        .expect("seal");
    buf.extend_from_slice(&body);
    buf
}

pub fn open(key: &LessSafeKey, packet: &[u8]) -> Option<(u64, Vec<u8>)> {
    if packet.len() < 8 + 16 {
        return None;
    }
    let seq = u64::from_be_bytes(packet[..8].try_into().ok()?);
    let mut body = packet[8..].to_vec();
    let plain = key.open_in_place(nonce(seq), Aad::empty(), &mut body).ok()?;
    Some((seq, plain.to_vec()))
}

pub fn make_key(raw: &[u8; 32]) -> LessSafeKey {
    LessSafeKey::new(UnboundKey::new(&AES_256_GCM, raw).expect("key"))
}

/// Samples per packet (all channels).
pub fn samples_per_frame(fmt: AudioFormat, frame_ms: u32) -> usize {
    ((fmt.rate as usize * frame_ms.clamp(2, 20) as usize) / 1000).max(1) * fmt.channels as usize
}

/// Reads PCM blocks from `rx`, and sends them to `dest` until `stop` is set
/// or the producer goes away. Runs on its own thread.
/// What the sender has done, for the dashboard.
#[derive(Default)]
pub struct SendStats {
    pub packets: std::sync::atomic::AtomicU64,
    /// RMS level of recent audio, 0..1000 (0 = the PC is playing silence).
    pub level: std::sync::atomic::AtomicU32,
}

pub fn spawn_sender(
    rx: Receiver<Vec<i16>>,
    dest: SocketAddr,
    key: [u8; 32],
    fmt: AudioFormat,
    frame_ms: u32,
    stop: Arc<AtomicBool>,
) -> Result<std::thread::JoinHandle<()>> {
    spawn_sender_with_stats(rx, dest, key, fmt, frame_ms, stop, Arc::new(SendStats::default()))
}

pub fn spawn_sender_with_stats(
    rx: Receiver<Vec<i16>>,
    dest: SocketAddr,
    key: [u8; 32],
    fmt: AudioFormat,
    frame_ms: u32,
    stop: Arc<AtomicBool>,
    stats: Arc<SendStats>,
) -> Result<std::thread::JoinHandle<()>> {
    let sock = UdpSocket::bind(if dest.is_ipv4() { "0.0.0.0:0" } else { "[::]:0" })?;
    let handle = std::thread::Builder::new()
        .name("pclink-audio-send".into())
        .spawn(move || {
            let key = make_key(&key);
            let per = samples_per_frame(fmt, frame_ms);
            let mut pending: Vec<i16> = Vec::with_capacity(per * 4);
            let mut bytes = Vec::with_capacity(per * 2);
            let mut seq = 0u64;
            while !stop.load(Ordering::Relaxed) {
                match rx.recv_timeout(Duration::from_millis(200)) {
                    Ok(block) => pending.extend_from_slice(&block),
                    Err(RecvTimeoutError::Timeout) => continue,
                    Err(RecvTimeoutError::Disconnected) => break,
                }
                while pending.len() >= per {
                    bytes.clear();
                    let mut sum = 0f64;
                    for s in pending.drain(..per) {
                        sum += (s as f64 / 32768.0).powi(2);
                        bytes.extend_from_slice(&s.to_le_bytes());
                    }
                    let rms = (sum / per as f64).sqrt();
                    stats.level.store((rms * 1000.0) as u32, Ordering::Relaxed);
                    stats.packets.fetch_add(1, Ordering::Relaxed);
                    let _ = sock.send_to(&seal(&key, seq, &bytes), dest);
                    seq += 1;
                }
            }
        })?;
    Ok(handle)
}

/// Converts captured f32 samples to i16, keeping at most two channels.
pub fn to_i16_stereo(input: &[f32], in_channels: usize, out: &mut Vec<i16>) {
    let in_channels = in_channels.max(1);
    let keep = in_channels.min(2);
    for frame in input.chunks_exact(in_channels) {
        for s in &frame[..keep] {
            out.push((s.clamp(-1.0, 1.0) * 32767.0) as i16);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn seal_open_roundtrip_and_tamper() {
        let raw = crate::util::random_bytes::<32>();
        let k = make_key(&raw);
        let pkt = seal(&k, 42, b"hello audio");
        assert_eq!(open(&k, &pkt), Some((42, b"hello audio".to_vec())));
        let mut bad = pkt.clone();
        bad[10] ^= 1;
        assert_eq!(open(&k, &bad), None);
        let other = make_key(&crate::util::random_bytes::<32>());
        assert_eq!(open(&other, &pkt), None);
    }

    #[test]
    fn downmix() {
        let mut out = vec![];
        to_i16_stereo(&[0.5, -0.5, 1.0, 0.0, 2.0, -2.0, 0.0, 0.0], 4, &mut out);
        assert_eq!(out, vec![16383, -16383, 32767, -32767]);
        let fmt = AudioFormat { rate: 48000, channels: 2 };
        assert_eq!(samples_per_frame(fmt, 5), 480);
    }

    #[test]
    fn sender_delivers_packets() {
        let recv = UdpSocket::bind("127.0.0.1:0").unwrap();
        recv.set_read_timeout(Some(Duration::from_secs(2))).unwrap();
        let raw = crate::util::random_bytes::<32>();
        let (tx, rx) = std::sync::mpsc::channel();
        let stop = Arc::new(AtomicBool::new(false));
        let fmt = AudioFormat { rate: 48000, channels: 2 };
        let h = spawn_sender(rx, recv.local_addr().unwrap(), raw, fmt, 5, stop.clone()).unwrap();
        tx.send(vec![7i16; 480 * 2 + 10]).unwrap();
        let mut buf = [0u8; 2048];
        let k = make_key(&raw);
        for want in 0..2u64 {
            let n = recv.recv(&mut buf).unwrap();
            let (seq, pcm) = open(&k, &buf[..n]).unwrap();
            assert_eq!(seq, want);
            assert_eq!(pcm.len(), 960);
            assert_eq!(&pcm[..2], &7i16.to_le_bytes());
        }
        stop.store(true, Ordering::Relaxed);
        drop(tx);
        h.join().unwrap();
    }
}

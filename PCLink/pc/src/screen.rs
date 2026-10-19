//! Remote desktop for the phone: JPEG frames on request, and pointer input
//! in frame coordinates (0..1), so the phone needn't know the PC's layout.

use crate::platform::{self, InputCmd};
use anyhow::{anyhow, Result};
use base64::Engine;
use serde_json::{json, Value};

fn monitor(args: &Value) -> Result<platform::Monitor> {
    let mons = platform::monitors();
    let i = args["monitor"].as_u64().unwrap_or(0) as usize;
    mons.get(i)
        .or_else(|| mons.first())
        .cloned()
        .ok_or_else(|| anyhow!("no monitors"))
}

/// `{monitor, max_w, quality}` -> `{jpeg (base64), w, h, t, monitors}`.
/// Several can run at once (the phone pipelines requests); `t` orders them.
pub fn frame(args: &Value) -> Result<Value> {
    let t = crate::util::now_millis();
    let m = monitor(args)?;
    let max_w = args["max_w"].as_u64().unwrap_or(1280).clamp(320, 3840) as i32;
    let (dw, dh) = if m.width > max_w { (max_w, (m.height as i64 * max_w as i64 / m.width as i64) as i32) } else { (m.width, m.height) };
    let bgra = platform::capture_bgra(m.x, m.y, m.width, m.height, dw, dh)?;
    let q = args["quality"].as_u64().unwrap_or(50).clamp(20, 95) as u8;
    let mut jpeg = Vec::with_capacity(256 * 1024);
    jpeg_encoder::Encoder::new(&mut jpeg, q).encode(&bgra, dw as u16, dh as u16, jpeg_encoder::ColorType::Bgra)?;
    Ok(json!({
        "jpeg": base64::engine::general_purpose::STANDARD.encode(jpeg),
        "w": dw,
        "h": dh,
        "t": t,
        "monitors": platform::monitors().len(),
    }))
}

/// `{monitor, x, y (0..1), action: move|click|right|double|down|up|scroll, dy}`.
pub fn pointer(args: &Value) -> Result<Value> {
    let m = monitor(args)?;
    let f = |k: &str| args[k].as_f64().unwrap_or(0.5).clamp(0.0, 1.0);
    let x = m.x + (f("x") * (m.width - 1) as f64).round() as i32;
    let y = m.y + (f("y") * (m.height - 1) as f64).round() as i32;
    platform::set_cursor(x, y)?;
    let click = |button: &str, double| platform::input_blocking(InputCmd::Click { button: button.into(), double });
    match args["action"].as_str().unwrap_or("move") {
        "move" => Ok(()),
        "click" => click("left", false),
        "double" => click("left", true),
        "right" => click("right", false),
        "down" => platform::input_blocking(InputCmd::Button { button: "left".into(), down: true }),
        "up" => platform::input_blocking(InputCmd::Button { button: "left".into(), down: false }),
        "scroll" => platform::input_blocking(InputCmd::Scroll { dx: 0, dy: args["dy"].as_i64().unwrap_or(0) as i32 }),
        other => Err(anyhow!("unknown pointer action {other}")),
    }?;
    Ok(json!({}))
}

// ---------------------------------------------------------------- video

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

/// A running H.264 stream to the phone. Frames go out as `screen.video`
/// events; the phone acks each one, and the encoder waits when too many are
/// unacked, so a slow link (the relay) lowers the frame rate instead of
/// piling up latency.
struct Video {
    stop: AtomicBool,
    acked: AtomicU64,
    keyframe: AtomicBool,
}

static VIDEO: Mutex<Option<Arc<Video>>> = Mutex::new(None);
const MAX_IN_FLIGHT: u64 = 3;

pub fn stop_video() {
    if let Some(v) = VIDEO.lock().unwrap().take() {
        v.stop.store(true, Ordering::Relaxed);
    }
}

pub fn ack(seq: u64) {
    if let Some(v) = VIDEO.lock().unwrap().as_ref() {
        v.acked.fetch_max(seq, Ordering::Relaxed);
    }
}

pub fn request_keyframe() {
    if let Some(v) = VIDEO.lock().unwrap().as_ref() {
        v.keyframe.store(true, Ordering::Relaxed);
    }
}

/// `{monitor, max_w, fps, bitrate}` -> `{w, h}`; replaces any running stream.
pub fn start_video(link: Arc<crate::link::Link>, args: &Value) -> Result<Value> {
    use openh264::encoder::{BitRate, Encoder, EncoderConfig, FrameRate, UsageType};
    stop_video();
    let m = monitor(args)?;
    let max_w = args["max_w"].as_u64().unwrap_or(1600).clamp(320, 3840) as i32;
    let (mut dw, mut dh) = if m.width > max_w { (max_w, (m.height as i64 * max_w as i64 / m.width as i64) as i32) } else { (m.width, m.height) };
    // 4:2:0 needs even sizes.
    dw &= !1;
    dh &= !1;
    let fps = args["fps"].as_f64().unwrap_or(30.0).clamp(5.0, 60.0);
    let bitrate = args["bitrate"].as_u64().unwrap_or(4_000_000).clamp(300_000, 20_000_000) as u32;
    let config = EncoderConfig::new()
        .bitrate(BitRate::from_bps(bitrate))
        .max_frame_rate(FrameRate::from_hz(fps as f32))
        .usage_type(UsageType::ScreenContentRealTime)
        .skip_frames(true);
    let mut enc = Encoder::with_api_config(openh264::OpenH264API::from_source(), config)?;
    let v = Arc::new(Video { stop: AtomicBool::new(false), acked: AtomicU64::new(0), keyframe: AtomicBool::new(false) });
    *VIDEO.lock().unwrap() = Some(v.clone());
    let rt = tokio::runtime::Handle::current();
    std::thread::Builder::new().name("pclink-video".into()).spawn(move || {
        let period = std::time::Duration::from_secs_f64(1.0 / fps);
        let mut seq = 0u64;
        let mut next = std::time::Instant::now();
        while !v.stop.load(Ordering::Relaxed) && !link.is_closed() {
            let now = std::time::Instant::now();
            if now < next {
                std::thread::sleep(next - now);
            }
            next = std::time::Instant::now().max(next) + period;
            if seq.saturating_sub(v.acked.load(Ordering::Relaxed)) >= MAX_IN_FLIGHT {
                continue;
            }
            let bgra = match platform::capture_bgra(m.x, m.y, m.width, m.height, dw, dh) {
                Ok(b) => b,
                Err(e) => {
                    tracing::debug!("video capture: {e:#}");
                    std::thread::sleep(std::time::Duration::from_millis(200));
                    continue;
                }
            };
            let yuv = openh264::formats::YUVBuffer::from_rgb_source(openh264::formats::BgraSliceU8::new(&bgra, (dw as usize, dh as usize)));
            if v.keyframe.swap(false, Ordering::Relaxed) || seq == 0 {
                enc.force_intra_frame();
            }
            let data = match enc.encode(&yuv) {
                Ok(b) => b.to_vec(),
                Err(e) => {
                    tracing::warn!("video encode: {e}");
                    break;
                }
            };
            if data.is_empty() {
                continue; // skipped by rate control
            }
            seq += 1;
            let ev = json!({"seq": seq, "w": dw, "h": dh, "data": base64::engine::general_purpose::STANDARD.encode(&data)});
            if rt.block_on(link.event("screen.video", ev)).is_err() {
                break;
            }
        }
        let mut cur = VIDEO.lock().unwrap();
        if cur.as_ref().is_some_and(|c| Arc::ptr_eq(c, &v)) {
            *cur = None;
        }
    })?;
    Ok(json!({"w": dw, "h": dh, "monitors": platform::monitors().len()}))
}

#[cfg(test)]
mod tests {
    #[test]
    fn encodes_a_frame_quickly() {
        let (w, h) = (1600u16, 900u16);
        let bgra: Vec<u8> = (0..w as usize * h as usize * 4).map(|i| ((i / 7) ^ (i / 3001)) as u8).collect();
        let t = std::time::Instant::now();
        let mut jpeg = Vec::new();
        jpeg_encoder::Encoder::new(&mut jpeg, 50).encode(&bgra, w, h, jpeg_encoder::ColorType::Bgra).unwrap();
        eprintln!("1600x900 JPEG: {} KB in {:?}", jpeg.len() / 1024, t.elapsed());
        assert!(jpeg.starts_with(&[0xFF, 0xD8]));
    }

    #[test]
    fn encodes_h264() {
        use openh264::encoder::{Encoder, EncoderConfig, UsageType};
        let (w, h) = (1600usize, 900usize);
        let bgra: Vec<u8> = (0..w * h * 4).map(|i| ((i / 7) ^ (i / 3001)) as u8).collect();
        let cfg = EncoderConfig::new().usage_type(UsageType::ScreenContentRealTime);
        let mut enc = Encoder::with_api_config(openh264::OpenH264API::from_source(), cfg).unwrap();
        let t = std::time::Instant::now();
        let yuv = openh264::formats::YUVBuffer::from_rgb_source(openh264::formats::BgraSliceU8::new(&bgra, (w, h)));
        let first = enc.encode(&yuv).unwrap().to_vec();
        let second = enc.encode(&yuv).unwrap().to_vec();
        eprintln!("H.264 1600x900: key {} KB, next {} B, {:?} for both", first.len() / 1024, second.len(), t.elapsed());
        // Annex B with SPS (type 7) up front, so the phone's decoder can start.
        assert_eq!(&first[..4], &[0, 0, 0, 1]);
        assert_eq!(first[4] & 0x1F, 7);
    }
}

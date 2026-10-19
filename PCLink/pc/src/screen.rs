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
}

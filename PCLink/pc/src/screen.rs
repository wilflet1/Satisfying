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

/// `{monitor, max_w, quality}` -> `{jpeg (base64), w, h, monitors}`.
pub fn frame(args: &Value) -> Result<Value> {
    let m = monitor(args)?;
    let img = platform::capture(m.x, m.y, m.width, m.height)?;
    let max_w = args["max_w"].as_u64().unwrap_or(1280).clamp(320, 3840) as u32;
    let img = if img.width() > max_w {
        let h = (img.height() as u64 * max_w as u64 / img.width() as u64) as u32;
        image::imageops::resize(&img, max_w, h, image::imageops::FilterType::Triangle)
    } else {
        img
    };
    let rgb = image::DynamicImage::ImageRgba8(img).to_rgb8();
    let mut jpeg = Vec::new();
    let q = args["quality"].as_u64().unwrap_or(60).clamp(20, 95) as u8;
    image::codecs::jpeg::JpegEncoder::new_with_quality(&mut jpeg, q).encode_image(&rgb)?;
    Ok(json!({
        "jpeg": base64::engine::general_purpose::STANDARD.encode(jpeg),
        "w": rgb.width(),
        "h": rgb.height(),
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

//! `pclink mcp`: a Model Context Protocol server over stdio that gives
//! Claude tools which act through your phone (via the running agent).

use crate::client::Client;
use anyhow::{anyhow, bail, Result};
use base64::Engine;
use serde_json::{json, Value};
use std::io::{BufRead, Write};
use std::time::Duration;

const DEFAULT_PROTOCOL: &str = "2025-06-18";

pub fn tools() -> Value {
    let obj = |props: Value, required: &[&str]| {
        json!({"type": "object", "properties": props, "required": required})
    };
    json!([
        {
            "name": "notify_phone",
            "description": "Send a push notification to the user's phone. Use it to report that a long task finished or that you need them.",
            "inputSchema": obj(json!({
                "title": {"type": "string"},
                "message": {"type": "string"}
            }), &["message"])
        },
        {
            "name": "ask_user_on_phone",
            "description": "Ask the user a multiple-choice question on their phone and wait for the answer. Use when the user may be away from the PC. Returns the chosen option, or null on timeout.",
            "inputSchema": obj(json!({
                "question": {"type": "string"},
                "options": {"type": "array", "items": {"type": "string"}, "minItems": 1, "maxItems": 6},
                "detail": {"type": "string", "description": "Optional extra context shown under the question"},
                "timeout_seconds": {"type": "integer", "default": 300}
            }), &["question", "options"])
        },
        {
            "name": "send_file_to_phone",
            "description": "Copy a file from this PC to the phone (default folder: Download/PCLink).",
            "inputSchema": obj(json!({
                "path": {"type": "string", "description": "Absolute path of the file on the PC"},
                "phone_folder": {"type": "string", "description": "Optional destination folder on the phone"}
            }), &["path"])
        },
        {
            "name": "get_file_from_phone",
            "description": "Copy a file from the phone to this PC. Returns the local path.",
            "inputSchema": obj(json!({
                "phone_path": {"type": "string"},
                "save_dir": {"type": "string", "description": "Optional folder on the PC (default: the PCLink inbox)"}
            }), &["phone_path"])
        },
        {
            "name": "list_phone_files",
            "description": "List a folder on the phone. Start with no path to see the storage root.",
            "inputSchema": obj(json!({"path": {"type": "string"}}), &[])
        },
        {
            "name": "get_latest_phone_photo",
            "description": "Fetch the newest photo or screenshot from the phone's gallery, e.g. after asking the user to photograph a screen, whiteboard or device. Returns the image and its local path.",
            "inputSchema": obj(json!({
                "screenshots_only": {"type": "boolean", "default": false}
            }), &[])
        },
        {
            "name": "phone_clipboard",
            "description": "Read or set the phone's clipboard.",
            "inputSchema": obj(json!({
                "action": {"type": "string", "enum": ["get", "set"]},
                "text": {"type": "string"}
            }), &["action"])
        },
        {
            "name": "get_phone_sms_code",
            "description": "Read the most recent one-time code (2FA / verification code) received by SMS in the last 10 minutes. Ask the user before using it.",
            "inputSchema": obj(json!({}), &[])
        },
        {
            "name": "phone_status",
            "description": "Battery level, charging state, network and other basic phone status.",
            "inputSchema": obj(json!({}), &[])
        },
        {
            "name": "recent_phone_notifications",
            "description": "Recent notifications mirrored from the phone.",
            "inputSchema": obj(json!({"limit": {"type": "integer", "default": 20}}), &[])
        },
        {
            "name": "open_url_on_phone",
            "description": "Open a link on the phone, e.g. a local dev server URL for mobile testing.",
            "inputSchema": obj(json!({"url": {"type": "string"}}), &["url"])
        },
        {
            "name": "ring_phone",
            "description": "Make the phone ring loudly so the user can find it.",
            "inputSchema": obj(json!({}), &[])
        },
        // ---- Controlling this PC's desktop (Windows) ----
        {
            "name": "screenshot",
            "description": "Take a screenshot of one monitor of this PC (default: the main monitor, 0). Coordinates for click, move_mouse, drag and scroll are pixels in the most recent screenshot. Take a new screenshot after acting to check the result.",
            "inputSchema": obj(json!({"monitor": {"type": "integer", "description": "Monitor index from list_monitors; 0 is the main screen"}}), &[])
        },
        {
            "name": "list_monitors",
            "description": "List this PC's monitors. Index 0 is the main (primary) screen.",
            "inputSchema": obj(json!({}), &[])
        },
        {
            "name": "list_windows",
            "description": "List open application windows (front to back) with id, title, process name, monitor and state. Use the id with window_action.",
            "inputSchema": obj(json!({}), &[])
        },
        {
            "name": "window_action",
            "description": "Act on a window from list_windows: focus, maximize, minimize, restore, close, or move (to `monitor`, maximized).",
            "inputSchema": obj(json!({
                "id": {"type": "integer"},
                "action": {"type": "string", "enum": ["focus", "maximize", "minimize", "restore", "close", "move"]},
                "monitor": {"type": "integer", "description": "For move: target monitor index"}
            }), &["id", "action"])
        },
        {
            "name": "click",
            "description": "Click at a point in the most recent screenshot.",
            "inputSchema": obj(json!({
                "x": {"type": "number"}, "y": {"type": "number"},
                "button": {"type": "string", "enum": ["left", "right", "middle"], "default": "left"},
                "double": {"type": "boolean", "default": false}
            }), &["x", "y"])
        },
        {
            "name": "move_mouse",
            "description": "Move the pointer to a point in the most recent screenshot (e.g. to hover).",
            "inputSchema": obj(json!({"x": {"type": "number"}, "y": {"type": "number"}}), &["x", "y"])
        },
        {
            "name": "drag",
            "description": "Drag with the left button from one point to another (screenshot coordinates), e.g. to move a browser tab or a file.",
            "inputSchema": obj(json!({
                "from_x": {"type": "number"}, "from_y": {"type": "number"},
                "to_x": {"type": "number"}, "to_y": {"type": "number"}
            }), &["from_x", "from_y", "to_x", "to_y"])
        },
        {
            "name": "scroll",
            "description": "Scroll at a point (screenshot coordinates). Positive amount scrolls down.",
            "inputSchema": obj(json!({"x": {"type": "number"}, "y": {"type": "number"}, "amount": {"type": "integer", "default": 3}}), &["x", "y"])
        },
        {
            "name": "type_text",
            "description": "Type text into the focused window.",
            "inputSchema": obj(json!({"text": {"type": "string"}}), &["text"])
        },
        {
            "name": "key",
            "description": "Press a key or shortcut in the focused window, e.g. \"enter\", \"f\", \"ctrl+l\", \"alt+tab\", \"win+up\", \"f11\".",
            "inputSchema": obj(json!({"keys": {"type": "string"}}), &["keys"])
        },
        {
            "name": "launch",
            "description": "Open an app, file, folder or URL on this PC (like double-clicking it or the Run box), e.g. \"blender\", \"C:\\Program Files\\Blender Foundation\\Blender 4.2\\blender.exe\", \"https://youtube.com\".",
            "inputSchema": obj(json!({"target": {"type": "string"}, "args": {"type": "string"}}), &["target"])
        },
        {
            "name": "wait",
            "description": "Wait a few seconds (e.g. for an app to open), then continue.",
            "inputSchema": obj(json!({"seconds": {"type": "number", "default": 2}}), &[])
        }
    ])
}

/// Maps screenshot pixels back to desktop pixels: (offset x, offset y, scale).
static LAST_SHOT: std::sync::Mutex<Option<(i32, i32, f64)>> = std::sync::Mutex::new(None);

fn to_screen(args: &Value, kx: &str, ky: &str) -> Result<(i32, i32)> {
    let x = args[kx].as_f64().ok_or_else(|| anyhow!("missing {kx}"))?;
    let y = args[ky].as_f64().ok_or_else(|| anyhow!("missing {ky}"))?;
    let (ox, oy, scale) = LAST_SHOT
        .lock()
        .unwrap()
        .ok_or_else(|| anyhow!("take a screenshot first, so coordinates have a reference"))?;
    Ok((ox + (x / scale).round() as i32, oy + (y / scale).round() as i32))
}

/// Parses "ctrl+shift+t" into a key and its modifiers.
pub fn parse_keys(keys: &str) -> Result<crate::platform::InputCmd> {
    let parts: Vec<String> = keys
        .split('+')
        .map(|p| p.trim().to_lowercase())
        .filter(|p| !p.is_empty())
        .collect();
    let (key, mods) = parts.split_last().ok_or_else(|| anyhow!("no key given"))?;
    Ok(crate::platform::InputCmd::Key { key: key.clone(), modifiers: mods.to_vec() })
}

/// Desktop-control tools. They run in this process (no agent needed).
fn pc_tool(name: &str, args: &Value) -> Option<Result<Vec<Value>>> {
    use crate::platform::{self, InputCmd};
    let text = |t: String| vec![json!({"type": "text", "text": t})];
    let done = |r: Result<()>, msg: &str| r.map(|_| text(msg.to_string()));
    let result = match name {
        "list_monitors" => Ok(text(serde_json::to_string_pretty(&platform::monitors()).unwrap_or_default())),
        "list_windows" => Ok(text(serde_json::to_string_pretty(&platform::list_windows()).unwrap_or_default())),
        "screenshot" => screenshot(args["monitor"].as_u64().unwrap_or(0) as usize),
        "window_action" => (|| {
            let id = args["id"].as_u64().ok_or_else(|| anyhow!("missing id"))?;
            let action = args["action"].as_str().ok_or_else(|| anyhow!("missing action"))?;
            platform::window_action(id, action, args["monitor"].as_u64().map(|m| m as usize))?;
            std::thread::sleep(std::time::Duration::from_millis(400));
            Ok(text(format!("{action} done")))
        })(),
        "click" => (|| {
            let (x, y) = to_screen(args, "x", "y")?;
            platform::set_cursor(x, y)?;
            platform::input_blocking(InputCmd::Click {
                button: args["button"].as_str().unwrap_or("left").into(),
                double: args["double"].as_bool().unwrap_or(false),
            })?;
            Ok(text(format!("clicked at ({x}, {y}) on the desktop")))
        })(),
        "move_mouse" => (|| {
            let (x, y) = to_screen(args, "x", "y")?;
            platform::set_cursor(x, y)?;
            Ok(text("moved".into()))
        })(),
        "drag" => (|| {
            let (x1, y1) = to_screen(args, "from_x", "from_y")?;
            let (x2, y2) = to_screen(args, "to_x", "to_y")?;
            platform::set_cursor(x1, y1)?;
            platform::input_blocking(InputCmd::Button { button: "left".into(), down: true })?;
            // Move in steps so apps register a drag, not a jump.
            for i in 1..=12 {
                std::thread::sleep(std::time::Duration::from_millis(25));
                platform::set_cursor(x1 + (x2 - x1) * i / 12, y1 + (y2 - y1) * i / 12)?;
            }
            std::thread::sleep(std::time::Duration::from_millis(150));
            platform::input_blocking(InputCmd::Button { button: "left".into(), down: false })?;
            Ok(text("dragged".into()))
        })(),
        "scroll" => (|| {
            let (x, y) = to_screen(args, "x", "y")?;
            platform::set_cursor(x, y)?;
            let amount = args["amount"].as_i64().unwrap_or(3) as i32;
            platform::input_blocking(InputCmd::Scroll { dx: 0, dy: amount })?;
            Ok(text("scrolled".into()))
        })(),
        "type_text" => done(
            args["text"]
                .as_str()
                .ok_or_else(|| anyhow!("missing text"))
                .and_then(|t| platform::input_blocking(InputCmd::Text(t.into()))),
            "typed",
        ),
        "key" => done(
            args["keys"]
                .as_str()
                .ok_or_else(|| anyhow!("missing keys"))
                .and_then(parse_keys)
                .and_then(platform::input_blocking),
            "pressed",
        ),
        "launch" => done(
            args["target"]
                .as_str()
                .ok_or_else(|| anyhow!("missing target"))
                .and_then(|t| platform::launch(t, args["args"].as_str())),
            "launched",
        ),
        "wait" => {
            let secs = args["seconds"].as_f64().unwrap_or(2.0).clamp(0.1, 60.0);
            std::thread::sleep(std::time::Duration::from_secs_f64(secs));
            Ok(text(format!("waited {secs}s")))
        }
        _ => return None,
    };
    Some(result)
}

fn screenshot(monitor: usize) -> Result<Vec<Value>> {
    let mons = crate::platform::monitors();
    let m = mons
        .get(monitor)
        .ok_or_else(|| anyhow!("no monitor {monitor} (this PC has {})", mons.len()))?;
    let img = crate::platform::capture(m.x, m.y, m.width, m.height)?;
    // Keep within what the model reads well: long edge 1568, about 1.15 MP.
    let (w, h) = (img.width() as f64, img.height() as f64);
    let scale = (1568.0 / w.max(h)).min((1_150_000.0 / (w * h)).sqrt()).min(1.0);
    let (sw, sh) = ((w * scale).round() as u32, (h * scale).round() as u32);
    let small = image::imageops::resize(&img, sw, sh, image::imageops::FilterType::Triangle);
    let rgb = image::DynamicImage::ImageRgba8(small).to_rgb8();
    let mut jpeg = Vec::new();
    image::codecs::jpeg::JpegEncoder::new_with_quality(&mut jpeg, 80).encode_image(&rgb)?;
    *LAST_SHOT.lock().unwrap() = Some((m.x, m.y, scale));
    Ok(vec![
        json!({"type": "text", "text": format!(
            "Monitor {monitor}{} is {}x{}; this image is {sw}x{sh}. Use this image's pixel coordinates for click, move_mouse, drag and scroll.",
            if m.primary { " (main)" } else { "" }, m.width, m.height
        )}),
        json!({"type": "image", "data": base64::engine::general_purpose::STANDARD.encode(jpeg), "mimeType": "image/jpeg"}),
    ])
}

/// Runs one tool call. Returns MCP `content` items.
pub fn call_tool(client: &Client, name: &str, args: &Value) -> Result<Vec<Value>> {
    let s = |k: &str| args[k].as_str().map(str::to_string);
    let text = |t: String| vec![json!({"type": "text", "text": t})];
    let short = Duration::from_secs(30);
    match name {
        "notify_phone" => {
            client.post(
                "/api/notify",
                json!({"title": s("title").unwrap_or_else(|| "Claude".into()), "text": s("message").unwrap_or_default(), "channel": "claude"}),
                short,
            )?;
            Ok(text("Notification sent.".into()))
        }
        "ask_user_on_phone" => {
            let timeout = args["timeout_seconds"].as_u64().unwrap_or(300);
            let res = client.post(
                "/api/ask",
                json!({
                    "title": "Claude asks",
                    "question": s("question").unwrap_or_default(),
                    "detail": s("detail"),
                    "options": args["options"],
                    "timeout_s": timeout,
                }),
                Duration::from_secs(timeout + 15),
            )?;
            Ok(text(match res["answer"].as_str() {
                Some(a) => format!("The user answered: {a}"),
                None => "No answer (timed out).".into(),
            }))
        }
        "send_file_to_phone" => {
            let res = client.post(
                "/api/push",
                json!({"path": s("path").ok_or_else(|| anyhow!("path is required"))?, "dest_dir": s("phone_folder")}),
                Duration::from_secs(3600),
            )?;
            Ok(text(format!("Saved on the phone at {}", res["path"].as_str().unwrap_or("?"))))
        }
        "get_file_from_phone" => {
            let res = client.post(
                "/api/pull",
                json!({"path": s("phone_path").ok_or_else(|| anyhow!("phone_path is required"))?, "dest": s("save_dir")}),
                Duration::from_secs(3600),
            )?;
            Ok(text(format!("Saved on the PC at {}", res["path"].as_str().unwrap_or("?"))))
        }
        "list_phone_files" => {
            let res = client.phone("fs.list", json!({"path": s("path")}), short)?;
            let mut out = format!("{}\n", res["path"].as_str().unwrap_or(""));
            for e in res["entries"].as_array().into_iter().flatten() {
                let dir = e["dir"].as_bool().unwrap_or(false);
                out.push_str(&format!(
                    "{} {}{}\n",
                    if dir { "📁" } else { "  " },
                    e["name"].as_str().unwrap_or(""),
                    if dir { String::new() } else { format!("  ({} bytes)", e["size"]) }
                ));
            }
            Ok(text(out))
        }
        "get_latest_phone_photo" => {
            let info = client.phone(
                "media.latest",
                json!({"screenshots_only": args["screenshots_only"].as_bool().unwrap_or(false)}),
                short,
            )?;
            let phone_path = info["path"].as_str().ok_or_else(|| anyhow!("no photos found"))?;
            let res = client.post("/api/pull", json!({"path": phone_path}), Duration::from_secs(600))?;
            let local = res["path"].as_str().unwrap_or("").to_string();
            let mut content = text(format!("Photo saved at {local} (taken {})", info["taken"].as_str().unwrap_or("recently")));
            if let Ok(jpeg) = shrink_image(std::path::Path::new(&local)) {
                content.push(json!({
                    "type": "image",
                    "data": base64::engine::general_purpose::STANDARD.encode(jpeg),
                    "mimeType": "image/jpeg",
                }));
            }
            Ok(content)
        }
        "phone_clipboard" => match s("action").as_deref() {
            Some("get") => {
                let res = client.phone("clip.get", json!({}), short)?;
                Ok(text(res["text"].as_str().unwrap_or("").to_string()))
            }
            Some("set") => {
                client.phone("clip.set", json!({"text": s("text").unwrap_or_default()}), short)?;
                Ok(text("Phone clipboard set.".into()))
            }
            _ => bail!("action must be get or set"),
        },
        "get_phone_sms_code" => {
            let res = client.phone("sms.latest_code", json!({}), short)?;
            Ok(text(match res["code"].as_str() {
                Some(c) => format!("Code {c} from {}", res["from"].as_str().unwrap_or("?")),
                None => "No recent code found.".into(),
            }))
        }
        "phone_status" => {
            let res = client.phone("status", json!({}), short)?;
            Ok(text(serde_json::to_string_pretty(&res)?))
        }
        "recent_phone_notifications" => {
            let limit = args["limit"].as_u64().unwrap_or(20) as usize;
            let res = client.get("/api/notifications")?;
            let list: Vec<Value> = res.as_array().cloned().unwrap_or_default().into_iter().take(limit).collect();
            Ok(text(serde_json::to_string_pretty(&list)?))
        }
        "open_url_on_phone" => {
            client.phone("open_url", json!({"url": s("url").unwrap_or_default()}), short)?;
            Ok(text("Opened on the phone.".into()))
        }
        "ring_phone" => {
            client.phone("find_phone", json!({}), short)?;
            Ok(text("The phone is ringing.".into()))
        }
        other => bail!("unknown tool {other}"),
    }
}

/// Downscales a photo so it fits comfortably in a model's context.
fn shrink_image(path: &std::path::Path) -> Result<Vec<u8>> {
    let img = image::open(path)?;
    let img = if img.width().max(img.height()) > 1568 {
        img.resize(1568, 1568, image::imageops::FilterType::Triangle)
    } else {
        img
    };
    let mut out = std::io::Cursor::new(Vec::new());
    image::DynamicImage::ImageRgb8(img.to_rgb8()).write_to(&mut out, image::ImageFormat::Jpeg)?;
    Ok(out.into_inner())
}

/// Handles one JSON-RPC message. Returns the response, if any.
pub fn handle(client: Option<&Client>, msg: &Value) -> Option<Value> {
    let id = msg.get("id").cloned();
    let method = msg["method"].as_str().unwrap_or("");
    let result: Result<Value, (i64, String)> = match method {
        "initialize" => Ok(json!({
            "protocolVersion": msg["params"]["protocolVersion"].as_str().unwrap_or(DEFAULT_PROTOCOL),
            "capabilities": {"tools": {}},
            "serverInfo": {"name": "pclink", "version": crate::agent::VERSION},
            "instructions": "PCLink tools. Phone: notify_phone when a long task ends, ask_user_on_phone for decisions when the user may be away, get_latest_phone_photo when the user photographs something for you. PC desktop: screenshot, list_monitors, list_windows, window_action, click, move_mouse, drag, scroll, type_text, key, launch and wait let you see and operate this Windows PC; take a screenshot after acting to check the result.",
        })),
        "ping" => Ok(json!({})),
        "tools/list" => Ok(json!({"tools": tools()})),
        "tools/call" => {
            let name = msg["params"]["name"].as_str().unwrap_or("");
            let args = msg["params"]["arguments"].clone();
            let outcome = match pc_tool(name, &args) {
                Some(r) => r,
                None => match client {
                    Some(c) => call_tool(c, name, &args),
                    None => Err(anyhow!("PCLink agent has never run on this PC")),
                },
            };
            Ok(match outcome {
                Ok(content) => json!({"content": content, "isError": false}),
                Err(e) => json!({"content": [{"type": "text", "text": format!("{e:#}")}], "isError": true}),
            })
        }
        m if m.starts_with("notifications/") => return None,
        _ => Err((-32601, format!("method not found: {method}"))),
    };
    let id = id?;
    Some(match result {
        Ok(r) => json!({"jsonrpc": "2.0", "id": id, "result": r}),
        Err((code, message)) => json!({"jsonrpc": "2.0", "id": id, "error": {"code": code, "message": message}}),
    })
}

pub fn serve_stdio() -> Result<()> {
    crate::platform::dpi_aware();
    let client = Client::new().ok();
    let stdin = std::io::stdin();
    let mut stdout = std::io::stdout();
    for line in stdin.lock().lines() {
        let line = line?;
        if line.trim().is_empty() {
            continue;
        }
        let response = match serde_json::from_str::<Value>(&line) {
            Ok(msg) => handle(client.as_ref(), &msg),
            Err(_) => Some(json!({"jsonrpc": "2.0", "id": null, "error": {"code": -32700, "message": "parse error"}})),
        };
        if let Some(r) = response {
            writeln!(stdout, "{r}")?;
            stdout.flush()?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn protocol_basics() {
        let init = handle(None, &json!({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-03-26"}})).unwrap();
        assert_eq!(init["result"]["protocolVersion"], "2025-03-26");
        assert!(handle(None, &json!({"jsonrpc": "2.0", "method": "notifications/initialized"})).is_none());
        let list = handle(None, &json!({"jsonrpc": "2.0", "id": 2, "method": "tools/list"})).unwrap();
        let tools = list["result"]["tools"].as_array().unwrap();
        assert!(tools.len() >= 22);
        for t in tools {
            assert_eq!(t["inputSchema"]["type"], "object");
        }
        let call = handle(None, &json!({"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "ring_phone", "arguments": {}}})).unwrap();
        assert_eq!(call["result"]["isError"], true);
        let unknown = handle(None, &json!({"jsonrpc": "2.0", "id": 4, "method": "bogus"})).unwrap();
        assert_eq!(unknown["error"]["code"], -32601);
    }
}

#[cfg(test)]
mod pc_tests {
    use super::*;

    #[test]
    fn key_parsing() {
        match parse_keys("Ctrl+Shift+T").unwrap() {
            crate::platform::InputCmd::Key { key, modifiers } => {
                assert_eq!(key, "t");
                assert_eq!(modifiers, vec!["ctrl", "shift"]);
            }
            other => panic!("{other:?}"),
        }
        assert!(parse_keys("").is_err());
    }

    #[test]
    fn coordinates_need_a_screenshot_then_scale() {
        *LAST_SHOT.lock().unwrap() = None;
        assert!(to_screen(&json!({"x": 1, "y": 1}), "x", "y").is_err());
        *LAST_SHOT.lock().unwrap() = Some((1920, 0, 0.5));
        assert_eq!(to_screen(&json!({"x": 100, "y": 50}), "x", "y").unwrap(), (2120, 100));
    }

    #[test]
    fn pc_tools_answer_without_the_agent() {
        let wait = handle(None, &json!({"jsonrpc": "2.0", "id": 9, "method": "tools/call", "params": {"name": "wait", "arguments": {"seconds": 0.1}}})).unwrap();
        assert_eq!(wait["result"]["isError"], false);
        let mons = handle(None, &json!({"jsonrpc": "2.0", "id": 10, "method": "tools/call", "params": {"name": "list_monitors", "arguments": {}}})).unwrap();
        assert_eq!(mons["result"]["isError"], false);
    }
}

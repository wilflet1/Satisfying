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
        }
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
            "instructions": "Tools that reach the user's Android phone through PCLink. Use notify_phone when a long task ends, ask_user_on_phone for decisions when the user may be away, and get_latest_phone_photo when the user photographs something for you.",
        })),
        "ping" => Ok(json!({})),
        "tools/list" => Ok(json!({"tools": tools()})),
        "tools/call" => {
            let name = msg["params"]["name"].as_str().unwrap_or("");
            let args = msg["params"]["arguments"].clone();
            let outcome = match client {
                Some(c) => call_tool(c, name, &args),
                None => Err(anyhow!("PCLink agent has never run on this PC")),
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
        assert!(tools.len() >= 10);
        for t in tools {
            assert_eq!(t["inputSchema"]["type"], "object");
        }
        let call = handle(None, &json!({"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "ring_phone", "arguments": {}}})).unwrap();
        assert_eq!(call["result"]["isError"], true);
        let unknown = handle(None, &json!({"jsonrpc": "2.0", "id": 4, "method": "bogus"})).unwrap();
        assert_eq!(unknown["error"]["code"], -32601);
    }
}

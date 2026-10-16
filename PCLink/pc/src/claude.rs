//! Claude Code integration: hook handlers and installing them.
//!
//! Hooks never fail loudly: if the agent isn't running or the phone isn't
//! connected they exit quietly so Claude Code carries on as normal.

use crate::client::Client;
use crate::config::Config;
use anyhow::{Context, Result};
use serde_json::{json, Value};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::time::Duration;

fn read_stdin_json() -> Value {
    let mut s = String::new();
    let _ = std::io::stdin().read_to_string(&mut s);
    serde_json::from_str(&s).unwrap_or(json!({}))
}

/// Whether the phone should be involved, given a mode of "always", "away" or "off".
fn should_use_phone(mode: &str, away_after: u64, status: &Value) -> bool {
    if status["connected"] != json!(true) {
        return false;
    }
    match mode {
        "always" => true,
        "away" => match status["idle_secs"].as_u64() {
            Some(idle) => idle >= away_after,
            // Can't tell (not Windows): treat as away.
            None => true,
        },
        _ => false,
    }
}

fn project_name(input: &Value) -> String {
    input["cwd"]
        .as_str()
        .and_then(|c| Path::new(c).file_name())
        .and_then(|n| n.to_str())
        .unwrap_or("Claude Code")
        .to_string()
}

pub fn run_hook(event: &str) -> Result<()> {
    let input = read_stdin_json();
    let task = std::env::var(crate::tasks::TASK_ENV).ok().and_then(|v| v.parse::<u64>().ok());
    let Ok(client) = Client::new() else { return Ok(()) };
    if event == "task-permission" {
        let Some(task) = task else { return Ok(()) };
        let res = client.post(
            "/api/tasks/approve",
            json!({"task": task, "tool_name": input["tool_name"], "tool_input": input["tool_input"]}),
            Duration::from_secs(3600),
        );
        if let Ok(out) = res {
            println!("{out}");
        }
        return Ok(());
    }
    // Inside a phone task, the task's own hook and feed handle everything.
    if task.is_some() {
        return Ok(());
    }
    let Ok(status) = client.get("/api/status") else { return Ok(()) };
    let cfg = Config::load_or_create().unwrap_or_default();
    let c = &cfg.claude;
    match event {
        "notification" => {
            // Permission prompts are handled by the permission hook.
            if input["notification_type"] == "permission_prompt" {
                return Ok(());
            }
            if !should_use_phone(&c.notify, c.away_after_secs, &status) {
                return Ok(());
            }
            let msg = input["message"].as_str().unwrap_or("Claude Code needs your attention");
            let _ = client.post(
                "/api/notify",
                json!({"title": format!("Claude · {}", project_name(&input)), "text": msg, "channel": "claude"}),
                Duration::from_secs(10),
            );
        }
        "stop" => {
            if input["stop_hook_active"] == json!(true) {
                return Ok(());
            }
            if !should_use_phone(&c.notify, c.away_after_secs, &status) {
                return Ok(());
            }
            let summary = input["last_assistant_message"]
                .as_str()
                .filter(|m| !m.trim().is_empty())
                .map(str::to_string)
                .or_else(|| {
                    input["transcript_path"]
                        .as_str()
                        .and_then(|p| last_assistant_text(Path::new(p)))
                })
                .unwrap_or_else(|| "Finished its turn.".into());
            let _ = client.post(
                "/api/notify",
                json!({
                    "title": format!("✅ Claude finished · {}", project_name(&input)),
                    "text": crate::agent::tail(&summary, 600),
                    "channel": "claude",
                }),
                Duration::from_secs(10),
            );
        }
        "permission" => {
            if !should_use_phone(&c.approvals, c.away_after_secs, &status) {
                return Ok(());
            }
            let tool = input["tool_name"].as_str().unwrap_or("a tool");
            let detail = describe_tool_input(tool, &input["tool_input"]);
            let res = client.post(
                "/api/ask",
                json!({
                    "title": format!("Claude · {}", project_name(&input)),
                    "question": format!("Allow {tool}?"),
                    "detail": detail,
                    "options": ["Allow", "Deny", "Decide on PC"],
                    "timeout_s": c.ask_timeout_secs,
                    "channel": "claude",
                }),
                Duration::from_secs(c.ask_timeout_secs + 15),
            );
            let answer = res.ok().and_then(|v| v["answer"].as_str().map(str::to_string));
            if let Some(out) = permission_output(answer.as_deref()) {
                println!("{out}");
            }
        }
        other => anyhow::bail!("unknown hook event {other} (use notification, stop or permission)"),
    }
    Ok(())
}

/// The JSON a PermissionRequest hook prints for an answer from the phone.
/// `None` means "no decision": Claude Code shows its usual prompt.
pub fn permission_output(answer: Option<&str>) -> Option<Value> {
    let decision = match answer? {
        "Allow" => json!({"behavior": "allow"}),
        "Deny" => json!({"behavior": "deny", "message": "Denied from your phone via PCLink."}),
        _ => return None,
    };
    Some(json!({
        "hookSpecificOutput": {
            "hookEventName": "PermissionRequest",
            "decision": decision,
        }
    }))
}

pub fn describe_tool_input(tool: &str, input: &Value) -> String {
    let s = |k: &str| input[k].as_str().unwrap_or("").to_string();
    let text = match tool {
        "Bash" => {
            let d = s("description");
            if d.is_empty() { s("command") } else { format!("{}\n$ {}", d, s("command")) }
        }
        "Write" | "Edit" | "MultiEdit" | "Read" | "NotebookEdit" => {
            let p = s("file_path");
            if p.is_empty() { s("notebook_path") } else { p }
        }
        "WebFetch" => s("url"),
        "WebSearch" => s("query"),
        _ => serde_json::to_string_pretty(input).unwrap_or_default(),
    };
    crate::agent::tail(&text, 800)
}

/// The most recent assistant text in a Claude Code transcript (JSONL).
pub fn last_assistant_text(path: &Path) -> Option<String> {
    use std::io::{Seek, SeekFrom};
    let mut f = std::fs::File::open(path).ok()?;
    let len = f.metadata().ok()?.len();
    let start = len.saturating_sub(512 * 1024);
    f.seek(SeekFrom::Start(start)).ok()?;
    let mut buf = Vec::new();
    f.read_to_end(&mut buf).ok()?;
    let text = String::from_utf8_lossy(&buf);
    for line in text.lines().rev() {
        let Ok(v) = serde_json::from_str::<Value>(line) else { continue };
        if v["type"] != "assistant" {
            continue;
        }
        let Some(parts) = v["message"]["content"].as_array() else { continue };
        let joined: Vec<&str> = parts
            .iter()
            .filter(|p| p["type"] == "text")
            .filter_map(|p| p["text"].as_str())
            .collect();
        if !joined.is_empty() {
            return Some(joined.join("\n").trim().to_string());
        }
    }
    None
}

fn claude_settings_path() -> PathBuf {
    dirs::home_dir().unwrap_or_default().join(".claude").join("settings.json")
}

/// Adds PCLink's hooks to ~/.claude/settings.json (keeping everything else)
/// and registers the MCP server with the `claude` CLI if it's installed.
pub fn install() -> Result<()> {
    let exe = std::env::current_exe()?;
    let exe_str = exe.to_string_lossy().to_string();
    let cmd = |event: &str| format!("\"{}\" hook {event}", exe_str);

    let path = claude_settings_path();
    let mut settings: Value = if path.exists() {
        let text = std::fs::read_to_string(&path)?;
        serde_json::from_str(&text).with_context(|| format!("{} is not valid JSON", path.display()))?
    } else {
        json!({})
    };
    if path.exists() {
        std::fs::copy(&path, path.with_extension("json.pclink-backup"))?;
    }
    merge_hooks(&mut settings, &cmd);
    std::fs::create_dir_all(path.parent().unwrap())?;
    std::fs::write(&path, serde_json::to_string_pretty(&settings)?)?;
    println!("Added PCLink hooks to {}", path.display());

    let mcp_args = ["mcp", "add", "--scope", "user", "pclink", "--", &exe_str, "mcp"];
    let claude = if cfg!(windows) { "claude.cmd" } else { "claude" };
    let ok = std::process::Command::new(claude)
        .args(mcp_args)
        .status()
        .map(|s| s.success())
        .unwrap_or(false);
    if ok {
        println!("Registered the PCLink MCP server with Claude Code.");
    } else {
        println!(
            "Could not run `claude`. To give Claude phone tools, run:\n  claude mcp add --scope user pclink -- \"{exe_str}\" mcp"
        );
    }
    Ok(())
}

/// Puts PCLink's hook entries into a settings object, replacing older
/// PCLink entries and leaving the user's other hooks alone.
pub fn merge_hooks(settings: &mut Value, cmd: &dyn Fn(&str) -> String) {
    if !settings.is_object() {
        *settings = json!({});
    }
    if !settings["hooks"].is_object() {
        settings["hooks"] = json!({});
    }
    let wanted = [
        ("Notification", "notification", None, 30),
        ("Stop", "stop", None, 30),
        ("PermissionRequest", "permission", Some("*"), 3600),
    ];
    for (event, arg, matcher, timeout) in wanted {
        let list = settings["hooks"][event].as_array().cloned().unwrap_or_default();
        let mut kept: Vec<Value> = list
            .into_iter()
            .filter_map(|mut group| {
                if let Some(hooks) = group["hooks"].as_array() {
                    let filtered: Vec<Value> = hooks
                        .iter()
                        .filter(|h| {
                            !h["command"].as_str().map(is_pclink_command).unwrap_or(false)
                        })
                        .cloned()
                        .collect();
                    if filtered.is_empty() {
                        return None;
                    }
                    group["hooks"] = Value::Array(filtered);
                }
                Some(group)
            })
            .collect();
        let mut group = json!({"hooks": [{"type": "command", "command": cmd(arg), "timeout": timeout}]});
        if let Some(m) = matcher {
            group["matcher"] = json!(m);
        }
        kept.push(group);
        settings["hooks"][event] = Value::Array(kept);
    }
}

fn is_pclink_command(c: &str) -> bool {
    let lower = c.to_lowercase();
    (lower.contains("pclink.exe") || lower.contains("pclink\"") || lower.contains("/pclink ") || lower.starts_with("pclink "))
        && lower.contains(" hook ")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn permission_answers() {
        let allow = permission_output(Some("Allow")).unwrap();
        assert_eq!(allow["hookSpecificOutput"]["decision"]["behavior"], "allow");
        assert_eq!(allow["hookSpecificOutput"]["hookEventName"], "PermissionRequest");
        let deny = permission_output(Some("Deny")).unwrap();
        assert_eq!(deny["hookSpecificOutput"]["decision"]["behavior"], "deny");
        assert!(permission_output(Some("Decide on PC")).is_none());
        assert!(permission_output(None).is_none());
    }

    #[test]
    fn away_logic() {
        let st = |idle: Value| json!({"connected": true, "idle_secs": idle});
        assert!(should_use_phone("always", 60, &st(json!(0))));
        assert!(!should_use_phone("away", 60, &st(json!(5))));
        assert!(should_use_phone("away", 60, &st(json!(61))));
        assert!(should_use_phone("away", 60, &st(Value::Null)));
        assert!(!should_use_phone("off", 60, &st(json!(999))));
        assert!(!should_use_phone("always", 60, &json!({"connected": false})));
    }

    #[test]
    fn merging_keeps_user_hooks_and_is_idempotent() {
        let mut s = json!({
            "model": "x",
            "hooks": {"Stop": [{"hooks": [{"type": "command", "command": "say done"}]}]}
        });
        let cmd = |e: &str| format!("\"C:\\\\tools\\\\pclink.exe\" hook {e}");
        merge_hooks(&mut s, &cmd);
        merge_hooks(&mut s, &cmd);
        assert_eq!(s["model"], "x");
        let stop = s["hooks"]["Stop"].as_array().unwrap();
        assert_eq!(stop.len(), 2);
        assert_eq!(stop[0]["hooks"][0]["command"], "say done");
        assert_eq!(s["hooks"]["PermissionRequest"].as_array().unwrap().len(), 1);
        assert_eq!(s["hooks"]["PermissionRequest"][0]["matcher"], "*");
    }

    #[test]
    fn transcript_tail() {
        let d = tempfile::tempdir().unwrap();
        let p = d.path().join("t.jsonl");
        std::fs::write(
            &p,
            concat!(
                r#"{"type":"user","message":{"content":"hi"}}"#, "\n",
                r#"{"type":"assistant","message":{"content":[{"type":"text","text":"First"}]}}"#, "\n",
                r#"{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash"}]}}"#, "\n",
                r#"{"type":"assistant","message":{"content":[{"type":"text","text":"All tests pass."}]}}"#, "\n",
                r#"{"type":"system"}"#, "\n",
            ),
        )
        .unwrap();
        assert_eq!(last_assistant_text(&p).as_deref(), Some("All tests pass."));
    }
}

//! Tasks: "Claude, do X on my PC", sent from the phone or dashboard.
//!
//! Each task runs Claude Code headless (`claude -p`) on this PC with the
//! PCLink MCP server (desktop control + phone tools). Its stream-json
//! output becomes a progress feed for the phone. Anything Claude Code would
//! ask permission for goes through a PermissionRequest hook to the phone;
//! with `--permission-prompts none`, unanswered requests are denied rather
//! than hanging.

use crate::agent::Agent;
use anyhow::{anyhow, bail, Context, Result};
use serde::Serialize;
use serde_json::{json, Value};
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};

const MAX_TASKS: usize = 30;
const MAX_EVENTS: usize = 400;

/// Env var telling hooks and the MCP server which task they belong to.
pub const TASK_ENV: &str = "PCLINK_TASK_ID";

#[derive(Debug, Clone, Serialize)]
pub struct TaskEvent {
    pub seq: usize,
    pub time: u64,
    /// "status", "text", "tool", "error", "approval" or "result".
    pub kind: String,
    pub text: String,
}

#[derive(Debug, Clone, Serialize)]
pub struct TaskSummary {
    pub id: u64,
    pub prompt: String,
    pub status: String,
    pub created: u64,
    pub ask: bool,
    pub cost_usd: f64,
    pub session_id: Option<String>,
    pub last: Option<String>,
    /// Permission the task is waiting for (the oldest, if several).
    pub pending: Option<Pending>,
}

#[derive(Debug, Clone, Serialize)]
pub struct Pending {
    pub approval: u64,
    pub tool: String,
    pub question: String,
    pub detail: String,
    pub options: Vec<String>,
}

pub const APPROVAL_OPTIONS: [&str; 3] = ["Allow", "Allow all for this task", "Deny"];

struct Task {
    summary: TaskSummary,
    events: Vec<TaskEvent>,
    /// Tools the user allowed for the rest of this task.
    allow_all: bool,
    kill: Option<tokio::sync::oneshot::Sender<()>>,
    /// Open approvals (Claude can ask several at once), oldest first.
    approvals: Vec<(Pending, tokio::sync::oneshot::Sender<String>)>,
}

#[derive(Default)]
pub struct Tasks {
    list: Mutex<Vec<Task>>,
    next: AtomicU64,
    next_approval: AtomicU64,
}

impl Tasks {
    pub fn summaries(&self) -> Vec<TaskSummary> {
        self.list.lock().unwrap().iter().rev().map(|t| t.summary.clone()).collect()
    }

    pub fn get(&self, id: u64, since: usize) -> Option<Value> {
        let list = self.list.lock().unwrap();
        let t = list.iter().find(|t| t.summary.id == id)?;
        let events: Vec<&TaskEvent> = t.events.iter().filter(|e| e.seq >= since).collect();
        Some(json!({"task": t.summary, "events": events}))
    }

    fn with<R>(&self, id: u64, f: impl FnOnce(&mut Task) -> R) -> Option<R> {
        self.list.lock().unwrap().iter_mut().find(|t| t.summary.id == id).map(f)
    }

    pub fn stop(&self, id: u64) -> bool {
        self.with(id, |t| t.kill.take().map(|k| k.send(()).is_ok()).unwrap_or(false))
            .unwrap_or(false)
    }

    /// Answers one of the task's approvals (the oldest if `approval` is
    /// None), from the dashboard or the phone's chat. False if none matched.
    pub fn answer(&self, id: u64, approval: Option<u64>, option: &str) -> bool {
        self.with(id, |t| {
            let i = t.approvals.iter().position(|(p, _)| approval.is_none_or(|a| a == p.approval))?;
            let (_, tx) = t.approvals.remove(i);
            t.summary.pending = t.approvals.first().map(|(p, _)| p.clone());
            Some(tx.send(option.to_string()).is_ok())
        })
        .flatten()
        .unwrap_or(false)
    }

    /// Allows every open approval of the task (after "Allow all").
    fn allow_open(&self, id: u64) {
        let open = self.with(id, |t| {
            t.summary.pending = None;
            std::mem::take(&mut t.approvals)
        });
        for (_, tx) in open.unwrap_or_default() {
            let _ = tx.send("Allow".into());
        }
    }

    /// Drops an approval that was answered some other way.
    fn close_approval(&self, id: u64, approval: u64) {
        self.with(id, |t| {
            t.approvals.retain(|(p, _)| p.approval != approval);
            t.summary.pending = t.approvals.first().map(|(p, _)| p.clone());
        });
    }

    pub fn allow_all(&self, id: u64) -> bool {
        self.with(id, |t| t.allow_all).unwrap_or(false)
    }

    pub fn set_allow_all(&self, id: u64) {
        self.with(id, |t| t.allow_all = true);
    }

    pub fn session_of(&self, id: u64) -> Option<String> {
        self.with(id, |t| t.summary.session_id.clone()).flatten()
    }
}

/// Finds the Claude Code CLI.
pub fn find_cli(configured: Option<&PathBuf>) -> Result<PathBuf> {
    if let Some(p) = configured {
        if p.exists() {
            return Ok(p.clone());
        }
        bail!("claude CLI not found at {}", p.display());
    }
    let names: &[&str] = if cfg!(windows) { &["claude.exe", "claude.cmd"] } else { &["claude"] };
    let mut dirs: Vec<PathBuf> = std::env::var_os("PATH")
        .map(|p| std::env::split_paths(&p).collect())
        .unwrap_or_default();
    // Default install locations, in case PATH (e.g. at Windows startup) lacks them.
    if let Some(home) = dirs::home_dir() {
        dirs.push(home.join(".local").join("bin"));
        dirs.push(home.join(".claude").join("local"));
    }
    if let Some(appdata) = std::env::var_os("APPDATA") {
        dirs.push(PathBuf::from(appdata).join("npm"));
    }
    for dir in dirs {
        for name in names {
            let candidate = dir.join(name);
            if candidate.is_file() {
                return Ok(candidate);
            }
        }
    }
    bail!("Claude Code isn't installed on this PC (no `claude` command found). Install it from https://claude.com/claude-code, or set claude.cli in config.toml.")
}

const SYSTEM_PROMPT: &str = "\
You are running on the user's Windows PC, on a task they sent from their phone through PCLink. \
They are probably away from the PC, so don't stop to ask clarifying questions: make sensible choices and finish the job. \
If a decision really needs them, use the ask_user_on_phone tool.

To see and operate the desktop, use the pclink tools: screenshot (then click/move_mouse/drag/scroll with that image's coordinates), \
list_monitors (0 is the main screen), list_windows and window_action (focus, maximize, move to a monitor), type_text, key (shortcuts such as \"f\" for YouTube fullscreen, \"f11\", \"win+up\"), launch (apps, files, URLs) and wait. \
Prefer reliable methods (window_action, keyboard shortcuts, the command line) over guessing click positions, and take a screenshot after GUI actions to check the result. \
Use the shell for installs and setup work, as usual.

When you finish, reply with a short summary of what you did and anything the user must do.";

impl Agent {
    /// Starts a task, or continues task `resume` with a follow-up prompt.
    pub fn start_task(self: &Arc<Self>, prompt: &str, resume: Option<u64>, ask: bool) -> Result<u64> {
        let prompt = prompt.trim();
        if prompt.is_empty() {
            bail!("the prompt is empty");
        }
        let cli = find_cli(self.cfg.claude.cli.as_ref())?;
        let resume_session = match resume {
            Some(id) => Some(
                self.tasks
                    .session_of(id)
                    .ok_or_else(|| anyhow!("that task can't be continued (it never started a session)"))?,
            ),
            None => None,
        };
        let id = self.tasks.next.fetch_add(1, Ordering::SeqCst) + 1;
        let files = write_task_files(id)?;

        let mut cmd = tokio::process::Command::new(&cli);
        cmd.args(["-p", "--output-format", "stream-json", "--verbose"])
            .args(["--model", &self.cfg.claude.task_model])
            .arg("--mcp-config")
            .arg(&files.mcp)
            .arg("--settings")
            .arg(&files.settings)
            .arg("--append-system-prompt-file")
            .arg(&files.system_prompt)
            // Desktop and phone tools run without asking; everything else
            // that needs approval goes to the phone through the hook.
            .args(["--allowedTools", "mcp__pclink"])
            .args(["--permission-prompts", "none"]);
        if let Some(session) = &resume_session {
            cmd.args(["--resume", session]);
        }
        let dir = self
            .cfg
            .claude
            .task_dir
            .clone()
            .or_else(dirs::home_dir)
            .unwrap_or_else(|| PathBuf::from("."));
        cmd.current_dir(dir)
            .env(TASK_ENV, id.to_string())
            .env("PCLINK_HOME", crate::config::home_dir())
            .stdin(std::process::Stdio::piped())
            .stdout(std::process::Stdio::piped())
            .stderr(std::process::Stdio::piped())
            .kill_on_drop(true);
        #[cfg(windows)]
        cmd.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
        let mut child = cmd.spawn().with_context(|| format!("starting {}", cli.display()))?;

        let (kill_tx, kill_rx) = tokio::sync::oneshot::channel();
        {
            let mut list = self.tasks.list.lock().unwrap();
            list.push(Task {
                summary: TaskSummary {
                    id,
                    prompt: prompt.to_string(),
                    status: "running".into(),
                    created: crate::util::now_millis(),
                    ask,
                    cost_usd: 0.0,
                    session_id: resume_session.clone(),
                    last: None,
                    pending: None,
                },
                events: vec![],
                allow_all: !ask,
                kill: Some(kill_tx),
                approvals: vec![],
            });
            let excess = list.len().saturating_sub(MAX_TASKS);
            list.drain(..excess);
        }
        self.log("claude", format!("task {id}: {}", crate::agent::head(prompt, 80)));
        self.task_event(id, "status", if resume.is_some() { "Continuing…" } else { "Starting Claude…" });

        let mut stdin = child.stdin.take().expect("stdin");
        let stdout = child.stdout.take().expect("stdout");
        let stderr = child.stderr.take().expect("stderr");
        let text = prompt.to_string();
        tokio::spawn(async move {
            let _ = stdin.write_all(text.as_bytes()).await;
            let _ = stdin.shutdown().await;
        });
        let stderr_tail = Arc::new(Mutex::new(String::new()));
        {
            let tail = stderr_tail.clone();
            tokio::spawn(async move {
                let mut lines = BufReader::new(stderr).lines();
                while let Ok(Some(line)) = lines.next_line().await {
                    let mut t = tail.lock().unwrap();
                    t.push_str(&line);
                    t.push('\n');
                    let keep = crate::agent::tail(&t, 2000);
                    *t = keep;
                }
            });
        }
        let me = self.clone();
        tokio::spawn(async move {
            let mut lines = BufReader::new(stdout).lines();
            let mut finished = false;
            // Polled only until it resolves: polling a finished oneshot panics.
            let mut kill_rx = Some(kill_rx);
            loop {
                tokio::select! {
                    line = lines.next_line() => match line {
                        Ok(Some(line)) => {
                            if let Ok(msg) = serde_json::from_str::<Value>(&line) {
                                finished |= me.on_stream_message(id, &msg);
                            }
                        }
                        _ => break,
                    },
                    stop = async { kill_rx.as_mut().expect("guarded").await }, if kill_rx.is_some() => {
                        kill_rx = None;
                        // The sender is dropped (not used) when the task finishes.
                        if stop.is_err() {
                            continue;
                        }
                        let _ = child.start_kill();
                        me.finish_task(id, "stopped", "Stopped from the phone.".into());
                        let _ = child.wait().await;
                        let _ = std::fs::remove_dir_all(&files.dir);
                        return;
                    }
                }
            }
            let status = child.wait().await;
            if !finished {
                let err = stderr_tail.lock().unwrap().trim().to_string();
                let msg = if err.is_empty() {
                    format!("Claude Code exited ({})", status.map(|s| s.to_string()).unwrap_or_default())
                } else {
                    err
                };
                me.finish_task(id, "failed", msg);
            }
            let _ = std::fs::remove_dir_all(&files.dir);
        });
        Ok(id)
    }

    /// Turns one stream-json message into feed events. Returns true on the final result.
    fn on_stream_message(self: &Arc<Self>, id: u64, msg: &Value) -> bool {
        // Subagent internals stay out of the feed.
        if !msg["parent_tool_use_id"].is_null() {
            return false;
        }
        match msg["type"].as_str().unwrap_or("") {
            "system" if msg["subtype"] == "init" => {
                let session = msg["session_id"].as_str().map(str::to_string);
                self.tasks.with(id, |t| t.summary.session_id = session.or(t.summary.session_id.take()));
                let model = msg["model"].as_str().unwrap_or("Claude");
                self.task_event(id, "status", &format!("Working ({model})"));
            }
            "system" if msg["subtype"] == "permission_denied" => {
                let tool = msg["tool_name"].as_str().unwrap_or("a tool");
                self.task_event(id, "error", &format!("Not allowed: {tool}"));
            }
            "assistant" => {
                for part in msg["message"]["content"].as_array().into_iter().flatten() {
                    match part["type"].as_str() {
                        Some("text") => {
                            let t = part["text"].as_str().unwrap_or("").trim();
                            if !t.is_empty() {
                                self.task_event(id, "text", t);
                            }
                        }
                        Some("tool_use") => {
                            let name = part["name"].as_str().unwrap_or("tool");
                            let short = name.strip_prefix("mcp__pclink__").unwrap_or(name);
                            let detail = tool_summary(name, &part["input"]);
                            let line = if detail.is_empty() { short.to_string() } else { format!("{short}: {detail}") };
                            self.task_event(id, "tool", &line);
                        }
                        _ => {}
                    }
                }
            }
            "result" => {
                let cost = msg["total_cost_usd"].as_f64().unwrap_or(0.0);
                let session = msg["session_id"].as_str().map(str::to_string);
                self.tasks.with(id, |t| {
                    t.summary.cost_usd = cost;
                    if session.is_some() {
                        t.summary.session_id = session;
                    }
                });
                let failed = msg["is_error"].as_bool().unwrap_or(false) || msg["subtype"] != "success";
                let text = msg["result"].as_str().unwrap_or("").trim().to_string();
                let text = if text.is_empty() {
                    format!("Finished ({})", msg["subtype"].as_str().unwrap_or("done"))
                } else {
                    text
                };
                self.finish_task(id, if failed { "failed" } else { "done" }, text);
                return true;
            }
            _ => {}
        }
        false
    }

    fn finish_task(self: &Arc<Self>, id: u64, status: &str, text: String) {
        let prompt = self
            .tasks
            .with(id, |t| {
                t.summary.status = status.to_string();
                t.kill = None;
                t.approvals.clear();
                t.summary.pending = None;
                t.summary.prompt.clone()
            })
            .unwrap_or_default();
        // Claude's last message usually is the result; don't show it twice.
        let repeat = self
            .tasks
            .with(id, |t| t.events.iter().rev().find(|e| e.kind == "text").map(|e| e.text == text))
            .flatten()
            .unwrap_or(false);
        if repeat {
            self.task_event(id, "status", "Finished");
        } else {
            self.task_event(id, "result", &text);
        }
        self.log("claude", format!("task {id} {status}"));
        let title = match status {
            "done" => "✅ Claude finished",
            "stopped" => "⏹ Claude stopped",
            _ => "❌ Claude couldn't finish",
        };
        let body = format!("{}\n{}", crate::agent::head(&prompt, 80), crate::agent::head(&text, 400));
        if let Some(l) = self.current_link() {
            tokio::spawn(async move {
                let _ = l
                    .request("notify", json!({"title": title, "text": body, "channel": "claude", "task": id}), Duration::from_secs(10))
                    .await;
            });
        }
    }

    pub fn task_event(self: &Arc<Self>, id: u64, kind: &str, text: &str) {
        let ev = self.tasks.with(id, |t| {
            let ev = TaskEvent {
                seq: t.events.last().map(|e| e.seq + 1).unwrap_or(0),
                time: crate::util::now_millis(),
                kind: kind.to_string(),
                text: crate::agent::tail(text, 4000),
            };
            if kind != "status" {
                t.summary.last = Some(crate::agent::head(text, 200));
            }
            t.events.push(ev.clone());
            let excess = t.events.len().saturating_sub(MAX_EVENTS);
            t.events.drain(..excess);
            (ev, t.summary.status.clone())
        });
        if let (Some((ev, status)), Some(l)) = (ev, self.current_link()) {
            tokio::spawn(async move {
                let _ = l.event("task.event", json!({"id": id, "status": status, "event": ev})).await;
            });
        }
    }

    /// Called by the PermissionRequest hook of a running task. Asks the
    /// phone unless the user already allowed everything for this task.
    pub async fn approve_task_tool(self: &Arc<Self>, id: u64, tool: &str, input: &Value) -> Value {
        let detail = crate::claude::describe_tool_input(tool, input);
        if self.tasks.allow_all(id) {
            return crate::claude::permission_output(Some("Allow")).unwrap_or_default();
        }
        let question = format!("Allow {tool}?");
        let (tx, rx) = tokio::sync::oneshot::channel::<String>();
        let approval = self.tasks.next_approval.fetch_add(1, Ordering::Relaxed) + 1;
        let running = self
            .tasks
            .with(id, |t| {
                let pending = Pending {
                    approval,
                    tool: tool.to_string(),
                    question: question.clone(),
                    detail: detail.clone(),
                    options: APPROVAL_OPTIONS.iter().map(|s| s.to_string()).collect(),
                };
                t.approvals.push((pending, tx));
                t.summary.pending = t.approvals.first().map(|(p, _)| p.clone());
                t.summary.status == "running"
            })
            .unwrap_or(false);
        if !running {
            return deny("The task is no longer running.");
        }
        let link = self.current_link();
        self.task_event(
            id,
            "approval",
            &if link.is_some() {
                format!("Waiting for your OK: {tool}")
            } else {
                format!("Waiting for your OK on the PC dashboard: {tool} (phone not connected)")
            },
        );
        let prompt = self.tasks.with(id, |t| t.summary.prompt.clone()).unwrap_or_default();
        let timeout = self.cfg.claude.ask_timeout_secs.max(30);
        // Asked on the phone (notification + the task's chat) and on the
        // dashboard at once; whichever answers first wins.
        let phone = async {
            let Some(link) = &link else { return std::future::pending().await };
            let res = link
                .request(
                    "ask",
                    json!({
                        "title": format!("Claude task: {}", crate::agent::head(&prompt, 60)),
                        "question": question,
                        "detail": detail,
                        "options": APPROVAL_OPTIONS,
                        "timeout_ms": timeout * 1000,
                        "channel": "claude",
                        "task": id,
                        "approval": approval,
                    }),
                    Duration::from_secs(timeout + 5),
                )
                .await;
            res.ok().and_then(|v| v["answer"].as_str().map(str::to_string))
        };
        let (answer, from_phone) = tokio::select! {
            a = phone => (a, true),
            a = rx => (a.ok(), false),
            _ = tokio::time::sleep(Duration::from_secs(timeout + 5)) => (None, false),
        };
        self.tasks.close_approval(id, approval);
        if !from_phone {
            // Take the question off the phone.
            if let Some(l) = link {
                tokio::spawn(async move {
                    let _ = l.event("ask.cancel", json!({"task": id, "approval": approval})).await;
                });
            }
        }
        let still_running = self.tasks.with(id, |t| t.summary.status == "running").unwrap_or(false);
        if !still_running {
            return deny("The task was stopped.");
        }
        match answer.as_deref() {
            Some("Allow") => {
                self.task_event(id, "approval", &format!("You allowed {tool}"));
                crate::claude::permission_output(Some("Allow")).unwrap_or_default()
            }
            Some("Allow all for this task") => {
                self.tasks.set_allow_all(id);
                self.tasks.allow_open(id);
                self.task_event(id, "approval", "You allowed everything for this task");
                crate::claude::permission_output(Some("Allow")).unwrap_or_default()
            }
            Some(_) => {
                self.task_event(id, "approval", &format!("You denied {tool}"));
                deny("The user denied this from their phone.")
            }
            None => {
                self.task_event(id, "approval", &format!("No answer about {tool}; skipped"));
                deny("The user didn't answer in time.")
            }
        }
    }
}

/// One line describing a tool call for the phone's feed.
pub fn tool_summary(name: &str, input: &Value) -> String {
    let s = |k: &str| input[k].as_str().unwrap_or("").to_string();
    let text = match name {
        "Bash" | "PowerShell" => s("command"),
        "Write" | "Edit" | "MultiEdit" | "Read" | "NotebookEdit" => s("file_path"),
        "WebFetch" => s("url"),
        "WebSearch" | "ToolSearch" => s("query"),
        "Glob" | "Grep" => s("pattern"),
        _ => match input.as_object() {
            Some(o) if o.is_empty() => String::new(),
            Some(o) => o
                .iter()
                .map(|(k, v)| match v {
                    Value::String(t) => format!("{k}={t}"),
                    other => format!("{k}={other}"),
                })
                .collect::<Vec<_>>()
                .join(", "),
            None => String::new(),
        },
    };
    crate::agent::head(text.lines().next().unwrap_or("").trim(), 160)
}

fn deny(message: &str) -> Value {
    json!({
        "hookSpecificOutput": {
            "hookEventName": "PermissionRequest",
            "decision": {"behavior": "deny", "message": message},
        }
    })
}

struct TaskFiles {
    dir: PathBuf,
    mcp: PathBuf,
    settings: PathBuf,
    system_prompt: PathBuf,
}

/// Writes the per-task MCP config, hook settings and system prompt to
/// files (passing paths keeps the command line free of quoting issues).
fn write_task_files(id: u64) -> Result<TaskFiles> {
    let dir = crate::config::home_dir().join("tasks").join(format!("{id}-{}", crate::util::random_hex(4)));
    std::fs::create_dir_all(&dir)?;
    let cli = crate::update::install_dir()?.join(if cfg!(windows) { "pclink.exe" } else { "pclink" });
    let cli_str = cli.to_string_lossy().to_string();
    let mcp = dir.join("mcp.json");
    std::fs::write(
        &mcp,
        serde_json::to_string_pretty(&json!({
            "mcpServers": {"pclink": {"type": "stdio", "command": cli_str, "args": ["mcp"]}}
        }))?,
    )?;
    let settings = dir.join("settings.json");
    std::fs::write(
        &settings,
        serde_json::to_string_pretty(&json!({
            "hooks": {
                "PermissionRequest": [{
                    "matcher": "*",
                    "hooks": [{"type": "command", "command": format!("\"{cli_str}\" hook task-permission"), "timeout": 3600}]
                }]
            }
        }))?,
    )?;
    let system_prompt = dir.join("system-prompt.txt");
    std::fs::write(&system_prompt, SYSTEM_PROMPT)?;
    Ok(TaskFiles { dir, mcp, settings, system_prompt })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tool_summaries() {
        assert_eq!(tool_summary("Bash", &json!({"command": "npm i\nnpm test"})), "npm i");
        assert_eq!(tool_summary("mcp__pclink__wait", &json!({"seconds": 1})), "seconds=1");
        assert_eq!(tool_summary("mcp__pclink__list_monitors", &json!({})), "");
        assert_eq!(tool_summary("mcp__pclink__window_action", &json!({"id": 5, "action": "move"})), "action=move, id=5");
    }

    #[test]
    fn finds_configured_cli_or_explains() {
        let d = tempfile::tempdir().unwrap();
        let fake = d.path().join("claude");
        std::fs::write(&fake, "").unwrap();
        assert_eq!(find_cli(Some(&fake)).unwrap(), fake);
        assert!(find_cli(Some(&d.path().join("missing"))).is_err());
    }
}

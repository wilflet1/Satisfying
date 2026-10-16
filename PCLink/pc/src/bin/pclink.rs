//! `pclink`: command-line companion to the PCLink agent.

use anyhow::Result;
use clap::{Parser, Subcommand};
use pclink::client::Client;
use serde_json::json;
use std::path::PathBuf;
use std::time::{Duration, Instant};

#[derive(Parser)]
#[command(name = "pclink", version, about = "Talk to your phone from the terminal (needs the PCLink agent running)")]
struct Cli {
    #[command(subcommand)]
    cmd: Cmd,
}

#[derive(Subcommand)]
enum Cmd {
    /// Show agent and phone status.
    Status,
    /// Open the dashboard in the browser.
    Dashboard,
    /// Open the dashboard on the pairing page.
    Pair,
    /// Send a notification to the phone. With a command after `--`, run it
    /// and notify when it finishes: `pclink notify -- npm test`.
    Notify {
        #[arg(short, long)]
        title: Option<String>,
        /// Message (when not running a command).
        #[arg(short, long)]
        message: Option<String>,
        #[arg(last = true)]
        command: Vec<String>,
    },
    /// Send files to the phone.
    Send {
        files: Vec<PathBuf>,
        /// Destination folder on the phone (default Download/PCLink).
        #[arg(long)]
        to: Option<String>,
    },
    /// Copy a file from the phone to the PC.
    Get {
        phone_path: String,
        #[arg(long)]
        to: Option<PathBuf>,
    },
    /// Fetch the newest photo from the phone; prints its local path.
    Photo {
        #[arg(long)]
        screenshot: bool,
    },
    /// Clipboard: `send` (PC → phone) or `get` (print phone's clipboard).
    Clip { action: String },
    /// Ask a question on the phone and print the answer.
    Ask {
        question: String,
        #[arg(short, long = "option", required = true)]
        options: Vec<String>,
        #[arg(long, default_value_t = 300)]
        timeout: u64,
    },
    /// Phone as PC speaker: on, off or toggle (default).
    Speaker { state: Option<String> },
    /// Make the phone ring.
    Ring,
    /// Open a URL on the phone.
    Open { url: String },
    /// Print the newest SMS verification code.
    SmsCode,
    /// Run a control-panel action defined in config.toml.
    Action { name: String },
    /// Claude Code hook handler (reads hook JSON on stdin).
    Hook { event: String },
    /// Run the MCP server on stdio (for Claude Code).
    Mcp,
    /// Install the Claude Code hooks and MCP server.
    InstallClaude,
}

fn main() {
    let cli = Cli::parse();
    // Hooks must never break Claude Code.
    if let Cmd::Hook { event } = &cli.cmd {
        let _ = pclink::claude::run_hook(event);
        return;
    }
    if let Err(e) = run(cli) {
        eprintln!("pclink: {e:#}");
        std::process::exit(1);
    }
}

fn run(cli: Cli) -> Result<()> {
    let long = Duration::from_secs(3600);
    let short = Duration::from_secs(30);
    match cli.cmd {
        Cmd::Hook { .. } => unreachable!(),
        Cmd::Mcp => return pclink::mcp::serve_stdio(),
        Cmd::InstallClaude => return pclink::claude::install(),
        _ => {}
    }
    let c = Client::new()?;
    match cli.cmd {
        Cmd::Status => {
            let s = c.get("/api/status")?;
            println!("PC:       {} (PCLink {})", s["pc_name"].as_str().unwrap_or(""), s["version"].as_str().unwrap_or(""));
            match s["phone"].as_object() {
                Some(p) => {
                    let st = &p["status"];
                    println!("Phone:    {} at {}", p["name"].as_str().unwrap_or(""), p["ip"].as_str().unwrap_or(""));
                    if let Some(b) = st["battery"].as_u64() {
                        println!("Battery:  {b}%{}", if st["charging"] == true { " (charging)" } else { "" });
                    }
                }
                None => println!("Phone:    not connected"),
            }
            println!("Speaker:  {}", if s["speaker"].is_null() { "off" } else { "on" });
            println!("Inbox:    {}", s["inbox"].as_str().unwrap_or(""));
        }
        Cmd::Dashboard | Cmd::Pair => {
            let page = if matches!(cli.cmd, Cmd::Pair) { "pair" } else { "home" };
            let url = pclink::run::dashboard_url(c.api_port(), c.token(), page);
            pclink::platform::open_url(&url)?;
            println!("Opened the dashboard in your browser.");
        }
        Cmd::Notify { title, message, command } => {
            if command.is_empty() {
                c.post(
                    "/api/notify",
                    json!({"title": title, "text": message.unwrap_or_else(|| "Ping from your PC".into()), "channel": "build"}),
                    short,
                )?;
            } else {
                let started = Instant::now();
                let status = std::process::Command::new(&command[0]).args(&command[1..]).status();
                let secs = started.elapsed().as_secs();
                let line = command.join(" ");
                let (code, text) = match &status {
                    Ok(s) if s.success() => (0, format!("✅ `{line}` finished in {}", fmt_secs(secs))),
                    Ok(s) => (s.code().unwrap_or(1), format!("❌ `{line}` failed (exit {}) after {}", s.code().unwrap_or(-1), fmt_secs(secs))),
                    Err(e) => (127, format!("❌ could not start `{line}`: {e}")),
                };
                let _ = c.post(
                    "/api/notify",
                    json!({"title": title.unwrap_or_else(|| "Command finished".into()), "text": text, "channel": "build"}),
                    short,
                );
                std::process::exit(code);
            }
        }
        Cmd::Send { files, to } => {
            for f in files {
                let abs = std::fs::canonicalize(&f)?;
                let r = c.post("/api/push", json!({"path": abs, "dest_dir": to}), long)?;
                println!("{} → {}", f.display(), r["path"].as_str().unwrap_or("?"));
            }
        }
        Cmd::Get { phone_path, to } => {
            let dest = to.map(|p| std::fs::canonicalize(&p).unwrap_or(p));
            let r = c.post("/api/pull", json!({"path": phone_path, "dest": dest}), long)?;
            println!("{}", r["path"].as_str().unwrap_or("?"));
        }
        Cmd::Photo { screenshot } => {
            let info = c.phone("media.latest", json!({"screenshots_only": screenshot}), short)?;
            let p = info["path"].as_str().ok_or_else(|| anyhow::anyhow!("no photos found"))?;
            let r = c.post("/api/pull", json!({"path": p}), long)?;
            println!("{}", r["path"].as_str().unwrap_or("?"));
        }
        Cmd::Clip { action } => match action.as_str() {
            "send" => {
                let r = c.post("/api/clipboard/send", json!({}), short)?;
                println!("Sent {} characters.", r["chars"]);
            }
            "get" => println!("{}", c.phone("clip.get", json!({}), short)?["text"].as_str().unwrap_or("")),
            other => anyhow::bail!("unknown clip action {other} (send or get)"),
        },
        Cmd::Ask { question, options, timeout } => {
            let r = c.post(
                "/api/ask",
                json!({"question": question, "options": options, "timeout_s": timeout, "channel": "general"}),
                Duration::from_secs(timeout + 15),
            )?;
            match r["answer"].as_str() {
                Some(a) => println!("{a}"),
                None => {
                    eprintln!("no answer");
                    std::process::exit(2);
                }
            }
        }
        Cmd::Speaker { state } => {
            let on = match state.as_deref() {
                Some("on") => Some(true),
                Some("off") => Some(false),
                None | Some("toggle") => None,
                Some(o) => anyhow::bail!("use on, off or toggle (got {o})"),
            };
            let r = c.post("/api/speaker", json!({"on": on}), short)?;
            println!("Phone speaker {}", if r["on"] == true { "on" } else { "off" });
        }
        Cmd::Ring => {
            c.phone("find_phone", json!({}), short)?;
        }
        Cmd::Open { url } => {
            c.phone("open_url", json!({"url": url}), short)?;
        }
        Cmd::SmsCode => {
            let r = c.phone("sms.latest_code", json!({}), short)?;
            match r["code"].as_str() {
                Some(code) => println!("{code}"),
                None => {
                    eprintln!("no recent code");
                    std::process::exit(2);
                }
            }
        }
        Cmd::Action { name } => {
            let r = c.post("/api/actions/run", json!({"name": name, "wait": true}), long)?;
            print!("{}", r["output"].as_str().unwrap_or(""));
            std::process::exit(r["code"].as_i64().unwrap_or(0) as i32);
        }
        Cmd::Hook { .. } | Cmd::Mcp | Cmd::InstallClaude => unreachable!(),
    }
    Ok(())
}

fn fmt_secs(s: u64) -> String {
    if s >= 60 {
        format!("{}m {}s", s / 60, s % 60)
    } else {
        format!("{s}s")
    }
}

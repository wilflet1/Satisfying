//! PCLink agent: runs in the background (system tray on Windows), keeps the
//! phone connected and serves the local dashboard.

#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

use anyhow::Result;
use pclink::config::{self, Config};

fn init_logging() {
    use tracing_subscriber::EnvFilter;
    let filter = EnvFilter::try_from_env("PCLINK_LOG").unwrap_or_else(|_| EnvFilter::new("info"));
    let _ = std::fs::create_dir_all(config::home_dir());
    let log_path = config::home_dir().join("agent.log");
    // Keep the log from growing forever.
    if std::fs::metadata(&log_path).map(|m| m.len() > 5_000_000).unwrap_or(false) {
        let _ = std::fs::rename(&log_path, log_path.with_extension("log.old"));
    }
    match std::fs::OpenOptions::new().create(true).append(true).open(&log_path) {
        Ok(file) if cfg!(windows) => tracing_subscriber::fmt()
            .with_env_filter(filter)
            .with_ansi(false)
            .with_writer(std::sync::Mutex::new(file))
            .init(),
        _ => tracing_subscriber::fmt().with_env_filter(filter).init(),
    }
}

fn main() {
    init_logging();
    if let Err(e) = run() {
        tracing::error!("{e:#}");
        pclink::platform::notify("PCLink could not start", &format!("{e:#}"));
        eprintln!("pclink-agent: {e:#}");
        std::process::exit(1);
    }
}

fn run() -> Result<()> {
    let rt = tokio::runtime::Runtime::new()?;
    let running = match rt.block_on(pclink::run::start()) {
        Ok(r) => r,
        Err(e) if format!("{e:#}").contains("already running") => {
            // Second launch: just show the dashboard of the running agent.
            let cfg = Config::load_or_create()?;
            let token = config::load_or_create_local_token()?;
            let _ = pclink::platform::open_url(&pclink::run::dashboard_url(cfg.api_port, &token, "home"));
            return Ok(());
        }
        Err(e) => return Err(e),
    };
    pclink::run::spawn_update_check(running.agent.clone());
    if running.agent.state.lock().unwrap().devices.is_empty() {
        let url = pclink::run::dashboard_url(running.api_addr.port(), &running.agent.local_token, "pair");
        let _ = pclink::platform::open_url(&url);
    }

    #[cfg(windows)]
    {
        tray::run(rt, running)
    }
    #[cfg(not(windows))]
    {
        println!(
            "Dashboard: {}",
            pclink::run::dashboard_url(running.api_addr.port(), &running.agent.local_token, "home")
        );
        rt.block_on(async {
            let _ = tokio::signal::ctrl_c().await;
        });
        Ok(())
    }
}

#[cfg(windows)]
mod tray {
    use anyhow::Result;
    use pclink::agent::Agent;
    use pclink::run::{dashboard_url, Running};
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::Arc;
    use tray_icon::menu::{CheckMenuItem, Menu, MenuEvent, MenuItem, PredefinedMenuItem};
    use tray_icon::{Icon, TrayIconBuilder, TrayIconEvent};
    use windows::Win32::UI::Input::KeyboardAndMouse::{RegisterHotKey, MOD_ALT, MOD_CONTROL, MOD_NOREPEAT};
    use windows::Win32::UI::WindowsAndMessaging::{
        DispatchMessageW, MsgWaitForMultipleObjects, PeekMessageW, TranslateMessage, MSG, PM_REMOVE,
        QS_ALLINPUT, WM_HOTKEY, WM_QUIT,
    };

    const HOTKEY_SPEAKER: i32 = 1;

    /// A filled circle; the colour shows the link state at a glance.
    fn icon(rgb: [u8; 3]) -> Icon {
        let size = 32u32;
        let mut px = Vec::with_capacity((size * size * 4) as usize);
        let c = (size as f32 - 1.0) / 2.0;
        for y in 0..size {
            for x in 0..size {
                let d = ((x as f32 - c).powi(2) + (y as f32 - c).powi(2)).sqrt();
                let inner = d < 9.0;
                let ring = (11.5..15.0).contains(&d);
                let a = if inner || ring { 255 } else { 0 };
                let col = if inner { [255, 255, 255] } else { rgb };
                px.extend_from_slice(&[col[0], col[1], col[2], a]);
            }
        }
        // Centre dot in the state colour.
        for y in 0..size {
            for x in 0..size {
                let d = ((x as f32 - c).powi(2) + (y as f32 - c).powi(2)).sqrt();
                if d < 5.5 {
                    let i = ((y * size + x) * 4) as usize;
                    px[i..i + 3].copy_from_slice(&rgb);
                }
            }
        }
        Icon::from_rgba(px, size, size).expect("icon")
    }

    fn startup_enabled() -> bool {
        std::process::Command::new("reg")
            .args(["query", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "/v", "PCLink"])
            .output()
            .map(|o| o.status.success())
            .unwrap_or(false)
    }

    fn set_startup(on: bool) {
        let key = r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
        let _ = if on {
            let exe = std::env::current_exe().unwrap_or_default();
            std::process::Command::new("reg")
                .args(["add", key, "/v", "PCLink", "/t", "REG_SZ", "/d", &format!("\"{}\"", exe.display()), "/f"])
                .output()
        } else {
            std::process::Command::new("reg").args(["delete", key, "/v", "PCLink", "/f"]).output()
        };
    }

    pub fn run(rt: tokio::runtime::Runtime, running: Running) -> Result<()> {
        let agent: Arc<Agent> = running.agent.clone();
        let port = running.api_addr.port();

        let status = MenuItem::new("PCLink: phone not connected", false, None);
        let version = MenuItem::new(format!("Version {}", pclink::update::version_string()), false, None);
        let open = MenuItem::new("Open dashboard", true, None);
        let pair = MenuItem::new("Pair a phone…", true, None);
        let speaker = CheckMenuItem::new("Phone as speaker\tCtrl+Alt+P", true, false, None);
        let mic = CheckMenuItem::new("Phone as microphone", true, false, None);
        let clip = MenuItem::new("Send clipboard to phone", true, None);
        let ring = MenuItem::new("Find my phone", true, None);
        let inbox = MenuItem::new("Open received files", true, None);
        let settings = MenuItem::new("Edit settings…", true, None);
        let restart = MenuItem::new("Restart PCLink (apply settings)", true, None);
        let startup = CheckMenuItem::new("Start with Windows", true, startup_enabled(), None);
        let updates = MenuItem::new("Check for updates", true, None);
        let quit = MenuItem::new("Quit PCLink", true, None);
        let menu = Menu::new();
        menu.append_items(&[
            &status,
            &PredefinedMenuItem::separator(),
            &open,
            &pair,
            &PredefinedMenuItem::separator(),
            &speaker,
            &mic,
            &clip,
            &ring,
            &inbox,
            &PredefinedMenuItem::separator(),
            &settings,
            &restart,
            &startup,
            &PredefinedMenuItem::separator(),
            &version,
            &updates,
            &quit,
        ])?;

        let grey = [128, 128, 128];
        let green = [46, 160, 67];
        let blue = [56, 132, 255];
        let tray = TrayIconBuilder::new()
            .with_menu(Box::new(menu))
            .with_tooltip("PCLink")
            .with_icon(icon(grey))
            .build()?;

        let dirty = Arc::new(AtomicBool::new(true));
        {
            let dirty = dirty.clone();
            *agent.on_change.lock().unwrap() = Some(Box::new(move || dirty.store(true, Ordering::SeqCst)));
        }
        unsafe {
            let _ = RegisterHotKey(None, HOTKEY_SPEAKER, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'P' as u32);
        }

        let toggle_speaker = |agent: Arc<Agent>| {
            rt.spawn(async move {
                if let Err(e) = agent.toggle_speaker().await {
                    pclink::platform::notify("Phone speaker", &format!("{e:#}"));
                }
            });
        };
        let open_dash = |page: &str| {
            let _ = pclink::platform::open_url(&dashboard_url(port, &agent.local_token, page));
        };

        loop {
            unsafe {
                MsgWaitForMultipleObjects(None, false, 250, QS_ALLINPUT);
                let mut msg = MSG::default();
                while PeekMessageW(&mut msg, None, 0, 0, PM_REMOVE).as_bool() {
                    if msg.message == WM_QUIT {
                        return Ok(());
                    }
                    if msg.message == WM_HOTKEY && msg.wParam.0 as i32 == HOTKEY_SPEAKER {
                        toggle_speaker(agent.clone());
                    }
                    let _ = TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }

            while let Ok(ev) = MenuEvent::receiver().try_recv() {
                let id = ev.id;
                if id == open.id().clone() {
                    open_dash("home");
                } else if id == pair.id().clone() {
                    open_dash("pair");
                } else if id == speaker.id().clone() {
                    toggle_speaker(agent.clone());
                    dirty.store(true, Ordering::SeqCst);
                } else if id == mic.id().clone() {
                    let a = agent.clone();
                    let on = !agent.mic_on();
                    rt.spawn(async move {
                        if let Err(e) = a.set_mic(on).await {
                            pclink::platform::notify("Phone mic", &format!("{e:#}"));
                        }
                    });
                    dirty.store(true, Ordering::SeqCst);
                } else if id == clip.id().clone() {
                    let a = agent.clone();
                    rt.spawn(async move {
                        let text = pclink::platform::clipboard_get_text().unwrap_or_default();
                        if text.is_empty() {
                            pclink::platform::notify("Send clipboard", "The clipboard has no text.");
                            return;
                        }
                        *a.clip_last.lock().unwrap() = text.clone();
                        let res = match a.phone() {
                            Ok(l) => l.request("clip.set", serde_json::json!({"text": text}), std::time::Duration::from_secs(10)).await,
                            Err(e) => Err(e),
                        };
                        if let Err(e) = res {
                            pclink::platform::notify("Send clipboard", &format!("{e:#}"));
                        }
                    });
                } else if id == ring.id().clone() {
                    let a = agent.clone();
                    rt.spawn(async move {
                        let res = match a.phone() {
                            Ok(l) => l.request("find_phone", serde_json::json!({}), std::time::Duration::from_secs(10)).await,
                            Err(e) => Err(e),
                        };
                        if let Err(e) = res {
                            pclink::platform::notify("Find my phone", &format!("{e:#}"));
                        }
                    });
                } else if id == inbox.id().clone() {
                    let _ = std::fs::create_dir_all(agent.inbox());
                    let _ = pclink::platform::open_path(&agent.inbox());
                } else if id == settings.id().clone() {
                    let _ = std::process::Command::new("notepad.exe").arg(pclink::config::Config::path()).spawn();
                } else if id == restart.id().clone() {
                    agent.shutdown();
                    pclink::update::restart();
                } else if id == updates.id().clone() {
                    let a = agent.clone();
                    std::thread::spawn(move || {
                        let msg = a.check_update(true).unwrap_or_else(|e| format!("{e:#}"));
                        pclink::platform::notify("PCLink updates", &msg);
                    });
                } else if id == startup.id().clone() {
                    set_startup(startup.is_checked());
                } else if id == quit.id().clone() {
                    // Restores the default audio device if the speaker was on.
                    agent.shutdown();
                    return Ok(());
                }
            }

            while let Ok(ev) = TrayIconEvent::receiver().try_recv() {
                if let TrayIconEvent::DoubleClick { .. } = ev {
                    open_dash("home");
                }
            }

            if dirty.swap(false, Ordering::SeqCst) {
                let link = agent.current_link();
                let on = agent.speaker_on();
                speaker.set_checked(on);
                mic.set_checked(agent.mic_on());
                let (text, colour) = match &link {
                    Some(l) if on => (format!("PCLink: {} (speaker on)", l.device_name), blue),
                    Some(l) => (format!("PCLink: {} connected", l.device_name), green),
                    None => ("PCLink: phone not connected".to_string(), grey),
                };
                status.set_text(&text);
                let _ = tray.set_tooltip(Some(&text));
                let _ = tray.set_icon(Some(icon(colour)));
            }
        }
    }
}

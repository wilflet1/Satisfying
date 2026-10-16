//! Starting the agent: load everything, bind sockets, spawn tasks.

use crate::agent::Agent;
use crate::config::{self, Config, State};
use crate::tls::Identity;
use anyhow::{Context, Result};
use std::net::SocketAddr;
use std::sync::Arc;

pub struct Running {
    pub agent: Arc<Agent>,
    pub phone_addr: SocketAddr,
    pub api_addr: SocketAddr,
}

/// Loads config from `PCLINK_HOME` (or the default folder) and starts.
pub async fn start() -> Result<Running> {
    let cfg = Config::load_or_create()?;
    start_with(cfg, true).await
}

pub async fn start_with(cfg: Config, background_tasks: bool) -> Result<Running> {
    let home = config::home_dir();
    let state = State::load_or_create()?;
    let identity = Identity::load_or_create(&home)?;
    let token = config::load_or_create_local_token()?;
    let _ = std::fs::create_dir_all(&cfg.inbox);

    // Bind the API first: if it's taken, another agent is already running.
    // After an update or restart the old agent may still be exiting.
    let restarted = std::env::var_os(crate::update::RESTART_ENV).is_some();
    let mut attempts = if restarted { 40 } else { 1 };
    let api_listener = loop {
        match tokio::net::TcpListener::bind(("127.0.0.1", cfg.api_port)).await {
            Ok(l) => break l,
            Err(_) if attempts > 1 => {
                attempts -= 1;
                tokio::time::sleep(std::time::Duration::from_millis(250)).await;
            }
            Err(e) => {
                return Err(e).with_context(|| {
                    format!("port {} is busy (is PCLink already running?)", cfg.api_port)
                })
            }
        }
    };
    let phone_listener = tokio::net::TcpListener::bind(("0.0.0.0", cfg.port))
        .await
        .with_context(|| format!("binding phone port {}", cfg.port))?;
    let api_addr = api_listener.local_addr()?;
    let phone_addr = phone_listener.local_addr()?;

    crate::agent::unmute_after_crash();
    let agent = Agent::new(cfg, state, identity, token);
    tokio::spawn(crate::server::run(agent.clone(), phone_listener));
    let router = crate::api::router(agent.clone());
    tokio::spawn(async move {
        if let Err(e) = axum::serve(api_listener, router).await {
            tracing::error!("local API stopped: {e}");
        }
    });
    if background_tasks {
        tokio::spawn(crate::server::beacon(agent.clone()));
        agent.spawn_clipboard_watch();
    }
    tracing::info!(
        "PCLink {} listening for the phone on {phone_addr}, dashboard on http://{api_addr}",
        crate::agent::VERSION
    );
    Ok(Running { agent, phone_addr, api_addr })
}

pub fn dashboard_url(api_port: u16, token: &str, page: &str) -> String {
    // The token goes in the query string: some ways of launching a browser
    // drop the `#fragment`.
    format!("http://127.0.0.1:{api_port}/?t={token}#{page}")
}

/// Starts the background update check (CI builds only).
pub fn spawn_update_check(agent: std::sync::Arc<crate::agent::Agent>) {
    if let Ok(dir) = crate::update::install_dir() {
        crate::update::cleanup(&dir);
    }
    if !agent.cfg.auto_update || !crate::update::auto_update_allowed() {
        return;
    }
    std::thread::spawn(move || {
        // Let the agent finish starting first.
        std::thread::sleep(std::time::Duration::from_secs(5));
        match crate::update::check() {
            Ok(crate::update::Check::Available(m)) => agent.install_update(&m),
            Ok(_) => {}
            Err(e) => tracing::info!("update check: {e:#}"),
        }
    });
}

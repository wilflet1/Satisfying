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
    let api_listener = tokio::net::TcpListener::bind(("127.0.0.1", cfg.api_port))
        .await
        .with_context(|| format!("port {} is busy (is PCLink already running?)", cfg.api_port))?;
    let phone_listener = tokio::net::TcpListener::bind(("0.0.0.0", cfg.port))
        .await
        .with_context(|| format!("binding phone port {}", cfg.port))?;
    let api_addr = api_listener.local_addr()?;
    let phone_addr = phone_listener.local_addr()?;

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

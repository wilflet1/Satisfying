//! Small blocking client for the agent's local API (used by the CLI, the
//! Claude Code hooks and the MCP server).

use crate::config::{self, Config};
use anyhow::{anyhow, Result};
use serde_json::{json, Value};
use std::time::Duration;

pub struct Client {
    base: String,
    token: String,
    agent: ureq::Agent,
}

impl Client {
    pub fn new() -> Result<Client> {
        let cfg = Config::load_or_create()?;
        let token = std::fs::read_to_string(config::local_token_path())
            .map_err(|_| anyhow!("PCLink agent has never run on this PC (no local token)"))?
            .trim()
            .to_string();
        Ok(Client {
            base: format!("http://127.0.0.1:{}", cfg.api_port),
            token,
            agent: ureq::AgentBuilder::new()
                .timeout_connect(Duration::from_secs(2))
                .build(),
        })
    }

    pub fn api_port(&self) -> u16 {
        self.base.rsplit(':').next().and_then(|p| p.parse().ok()).unwrap_or(0)
    }

    pub fn token(&self) -> &str {
        &self.token
    }

    fn handle(res: Result<ureq::Response, ureq::Error>) -> Result<Value> {
        match res {
            Ok(r) => Ok(r.into_json()?),
            Err(ureq::Error::Status(_, r)) => {
                let v: Value = r.into_json().unwrap_or(json!({}));
                Err(anyhow!(v["error"].as_str().unwrap_or("request failed").to_string()))
            }
            Err(ureq::Error::Transport(_)) => Err(anyhow!("PCLink agent is not running")),
        }
    }

    pub fn get(&self, path: &str) -> Result<Value> {
        Self::handle(
            self.agent
                .get(&format!("{}{}", self.base, path))
                .set("Authorization", &format!("Bearer {}", self.token))
                .timeout(Duration::from_secs(30))
                .call(),
        )
    }

    pub fn post(&self, path: &str, body: Value, timeout: Duration) -> Result<Value> {
        Self::handle(
            self.agent
                .post(&format!("{}{}", self.base, path))
                .set("Authorization", &format!("Bearer {}", self.token))
                .timeout(timeout)
                .send_json(body),
        )
    }

    /// Sends an arbitrary request to the phone through the agent.
    pub fn phone(&self, op: &str, args: Value, timeout: Duration) -> Result<Value> {
        self.post(
            "/api/phone",
            json!({"op": op, "args": args, "timeout_ms": timeout.as_millis() as u64}),
            timeout + Duration::from_secs(5),
        )
    }
}

//! Reaching the phone from anywhere through the HTTPS relay
//! (`relay/relay.php`, which runs on ordinary PHP hosting).
//!
//! The PC long-polls the relay in its secret room. When a phone opens a
//! session, the PC turns that session into a byte pipe made of HTTP requests
//! (POST to send, long-polled GET to receive) and runs the normal phone
//! connection over it: TLS pinned to this PC's certificate, then the usual
//! protocol. The relay only stores and forwards ciphertext.

use crate::agent::Agent;
use anyhow::{bail, Context, Result};
use serde_json::Value;
use std::io::Read;
use std::sync::Arc;
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};

/// Bytes sent per request (the relay accepts up to 2 MB).
const SEND_MAX: usize = 512 * 1024;
const LEGACY_DEFAULT: &str = "wss://agentrelay.arketek.co.za";

/// The relay endpoint to use, or None if the relay is off.
pub fn endpoint(configured: &str) -> Option<String> {
    let url = configured.trim();
    if url.is_empty() {
        return None;
    }
    // Early builds wrote the WebSocket address of the default relay.
    if url.trim_end_matches('/') == LEGACY_DEFAULT {
        return Some(crate::config::DEFAULT_RELAY.to_string());
    }
    Some(url.to_string())
}

fn http() -> &'static ureq::Agent {
    static AGENT: std::sync::OnceLock<ureq::Agent> = std::sync::OnceLock::new();
    AGENT.get_or_init(|| {
        ureq::AgentBuilder::new()
            .timeout_connect(Duration::from_secs(15))
            // Long polls last 20 s on the relay.
            .timeout_read(Duration::from_secs(45))
            .timeout_write(Duration::from_secs(60))
            .user_agent(&format!("PCLink/{}", crate::agent::VERSION))
            // Networks that only allow the web through a proxy.
            .try_proxy_from_env(true)
            .build()
    })
}

fn url(base: &str, query: &str) -> String {
    format!("{base}?{query}")
}

async fn blocking<T: Send + 'static>(f: impl FnOnce() -> Result<T> + Send + 'static) -> Result<T> {
    tokio::task::spawn_blocking(f).await.context("relay worker")?
}

fn status_error(e: ureq::Error) -> anyhow::Error {
    match e {
        ureq::Error::Status(code, resp) => {
            let body = resp.into_string().unwrap_or_default();
            let msg = serde_json::from_str::<Value>(&body)
                .ok()
                .and_then(|v| v["error"].as_str().map(str::to_string))
                .unwrap_or_else(|| {
                    // A web page instead of the relay's JSON: the host's firewall
                    // or bot protection answered, not relay.php.
                    if body.trim_start().starts_with('<') {
                        "the web host's firewall blocked the request (not the relay itself)".into()
                    } else {
                        crate::agent::head(body.trim(), 200)
                    }
                });
            anyhow::anyhow!("relay answered {code}: {}", msg.trim())
        }
        other => anyhow::anyhow!("can't reach the relay: {other}"),
    }
}

/// Long-polls for a phone; Some(session) when one opened a session.
fn wait(base: &str, room: &str) -> Result<Option<String>> {
    let resp = http()
        .post(&url(base, &format!("a=wait&room={room}")))
        .send_bytes(&[])
        .map_err(status_error)?;
    if resp.status() == 204 {
        return Ok(None);
    }
    let v: Value = resp.into_json().context("relay reply")?;
    match v["session"].as_str() {
        Some(s) if s.len() == 32 && s.bytes().all(|b| b.is_ascii_hexdigit()) => Ok(Some(s.to_string())),
        _ => bail!("relay sent no session"),
    }
}

fn send(base: &str, session: &str, data: &[u8]) -> Result<()> {
    let u = url(base, &format!("a=send&s={session}&d=down"));
    loop {
        match http().post(&u).send_bytes(data) {
            Ok(_) => return Ok(()),
            // The phone is behind; nothing was stored, so try again.
            Err(ureq::Error::Status(429, _)) => std::thread::sleep(Duration::from_millis(250)),
            Err(e) => return Err(status_error(e)),
        }
    }
}

enum Recv {
    Data(Vec<u8>, u64),
    Empty(u64),
    Closed,
}

fn recv(base: &str, session: &str, from: u64) -> Result<Recv> {
    let resp = match http().get(&url(base, &format!("a=recv&s={session}&d=up&from={from}"))).call() {
        Ok(r) => r,
        Err(ureq::Error::Status(410, _)) => return Ok(Recv::Closed),
        Err(e) => return Err(status_error(e)),
    };
    let next = resp.header("X-Next").and_then(|v| v.parse().ok()).unwrap_or(from);
    if resp.status() == 204 {
        return Ok(Recv::Empty(next));
    }
    let mut body = Vec::new();
    resp.into_reader().take(4 * 1024 * 1024).read_to_end(&mut body)?;
    Ok(Recv::Data(body, next))
}

fn close(base: &str, session: &str) {
    let _ = http().post(&url(base, &format!("a=close&s={session}"))).send_bytes(&[]);
}

/// Keeps a long poll waiting on the relay and hands each phone that opens a
/// session to the normal connection handler.
pub async fn run(agent: Arc<Agent>) {
    let Some(base) = endpoint(&agent.cfg.relay) else { return };
    let room = agent.state.lock().unwrap().relay_room.clone();
    let mut backoff = Duration::from_secs(2);
    loop {
        let (b, r) = (base.clone(), room.clone());
        match blocking(move || wait(&b, &r)).await {
            Ok(found) => {
                backoff = Duration::from_secs(2);
                agent.set_relay_status(true, "Waiting on the relay");
                if let Some(session) = found {
                    let stream = tunnel(base.clone(), session);
                    let agent = agent.clone();
                    tokio::spawn(async move {
                        if let Err(e) = crate::server::handle_relayed(agent, stream).await {
                            tracing::info!("relayed connection: {e:#}");
                        }
                    });
                }
            }
            Err(e) => {
                tracing::debug!("relay: {e:#}");
                agent.set_relay_status(false, &format!("{e:#}"));
                tokio::time::sleep(backoff).await;
                backoff = (backoff * 2).min(Duration::from_secs(60));
            }
        }
    }
}

/// Turns a relay session into a byte stream for the connection handler.
fn tunnel(base: String, session: String) -> tokio::io::DuplexStream {
    let (ours, theirs) = tokio::io::duplex(SEND_MAX);
    let (mut pipe_r, mut pipe_w) = tokio::io::split(ours);
    let (b, s) = (base.clone(), session.clone());
    // Handler -> relay. While one request is in flight the next batch
    // collects in the pipe, so busy transfers go out in big requests.
    tokio::spawn(async move {
        let mut buf = vec![0u8; SEND_MAX];
        loop {
            let n = match pipe_r.read(&mut buf).await {
                Ok(0) | Err(_) => break,
                Ok(n) => n,
            };
            let data = buf[..n].to_vec();
            let (b2, s2) = (b.clone(), s.clone());
            if let Err(e) = blocking(move || send(&b2, &s2, &data)).await {
                tracing::info!("relay send: {e:#}");
                break;
            }
        }
        let _ = blocking(move || {
            close(&b, &s);
            Ok(())
        })
        .await;
    });
    // Relay -> handler.
    tokio::spawn(async move {
        let mut next = 0u64;
        let mut failures = 0;
        loop {
            let (b, s) = (base.clone(), session.clone());
            match blocking(move || recv(&b, &s, next)).await {
                Ok(Recv::Data(bytes, n)) => {
                    failures = 0;
                    next = n;
                    if pipe_w.write_all(&bytes).await.is_err() {
                        break;
                    }
                }
                Ok(Recv::Empty(n)) => {
                    failures = 0;
                    next = n;
                }
                Ok(Recv::Closed) => break,
                // Receiving is idempotent: retry a flaky network a few times.
                Err(e) if failures < 3 => {
                    failures += 1;
                    tracing::debug!("relay recv: {e:#}");
                    tokio::time::sleep(Duration::from_secs(1)).await;
                }
                Err(_) => break,
            }
        }
        let _ = pipe_w.shutdown().await;
    });
    theirs
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn endpoints() {
        assert_eq!(endpoint(""), None);
        assert_eq!(endpoint("wss://agentrelay.arketek.co.za/").as_deref(), Some(crate::config::DEFAULT_RELAY));
        assert_eq!(endpoint(" https://r.example/relay.php ").as_deref(), Some("https://r.example/relay.php"));
    }
}

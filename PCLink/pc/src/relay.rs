//! Reaching the phone from anywhere through a WebSocket relay
//! (`relay/server.js`).
//!
//! The PC keeps one WebSocket open to the relay in its secret room. When a
//! phone joins, the relay sends a text "open" and from then on pipes bytes
//! between the two. That socket then carries the normal phone connection
//! (TLS pinned to this PC's certificate, then the usual protocol), and the PC
//! opens a fresh socket for the next phone. The relay sees only ciphertext.

use crate::agent::Agent;
use anyhow::{anyhow, bail, Context, Result};
use std::sync::Arc;
use std::time::Duration;
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};
use tokio::sync::mpsc;

/// The relay pings every 25 s; nothing for this long means the socket died.
const READ_TIMEOUT: Duration = Duration::from_secs(80);
const MAX_FRAME: usize = 4 * 1024 * 1024;

trait Io: AsyncRead + AsyncWrite + Unpin + Send {}
impl<T: AsyncRead + AsyncWrite + Unpin + Send> Io for T {}

/// Address of `room` on the relay at `base` ("wss://host[/path]").
pub fn room_url(base: &str, room: &str, role: &str) -> String {
    format!("{}/{room}?role={role}", base.trim_end_matches('/'))
}

/// Keeps a socket waiting on the relay and hands each phone that joins to
/// the normal connection handler.
pub async fn run(agent: Arc<Agent>) {
    let base = agent.cfg.relay.trim().to_string();
    if base.is_empty() {
        return;
    }
    let room = agent.state.lock().unwrap().relay_room.clone();
    let url = room_url(&base, &room, "pc");
    let mut backoff = Duration::from_secs(2);
    let mut was_ok = false;
    loop {
        let res = async {
            let ws = WsConn::connect(&url).await?;
            agent.set_relay_status(true, "Waiting on the relay");
            was_ok = true;
            ws.wait_for_open().await
        }
        .await;
        match res {
            Ok(ws) => {
                backoff = Duration::from_secs(2);
                let agent = agent.clone();
                tokio::spawn(async move {
                    let stream = ws.into_stream();
                    if let Err(e) = crate::server::handle_relayed(agent, stream).await {
                        tracing::info!("relayed connection: {e:#}");
                    }
                });
            }
            Err(e) => {
                if was_ok {
                    tracing::info!("relay: {e:#}");
                } else {
                    tracing::debug!("relay: {e:#}");
                }
                agent.set_relay_status(false, &format!("{e:#}"));
                // A socket that worked for a while and then dropped is
                // normal (proxies time out); come back quickly.
                let wait = if was_ok { Duration::from_secs(1) } else { backoff };
                was_ok = false;
                tokio::time::sleep(wait).await;
                backoff = (backoff * 2).min(Duration::from_secs(60));
            }
        }
    }
}

/// A minimal WebSocket client: enough of RFC 6455 to pipe bytes.
pub struct WsConn {
    io: Box<dyn Io>,
}

impl WsConn {
    pub async fn connect(url: &str) -> Result<WsConn> {
        let (tls, rest) = if let Some(r) = url.strip_prefix("wss://") {
            (true, r)
        } else if let Some(r) = url.strip_prefix("ws://") {
            (false, r)
        } else {
            bail!("relay address must start with wss:// or ws://");
        };
        let (authority, path) = match rest.find('/') {
            Some(i) => (&rest[..i], &rest[i..]),
            None => (rest, "/"),
        };
        let (host, port) = match authority.rsplit_once(':') {
            Some((h, p)) if p.parse::<u16>().is_ok() => (h.to_string(), p.parse::<u16>()?),
            _ => (authority.to_string(), if tls { 443 } else { 80 }),
        };
        let tcp = tokio::time::timeout(Duration::from_secs(15), tokio::net::TcpStream::connect((host.as_str(), port)))
            .await
            .context("relay connect timed out")?
            .with_context(|| format!("can't reach relay {host}:{port}"))?;
        let _ = tcp.set_nodelay(true);
        let mut io: Box<dyn Io> = if tls {
            let mut roots = rustls::RootCertStore::empty();
            roots.extend(webpki_roots::TLS_SERVER_ROOTS.iter().cloned());
            let cfg = rustls::ClientConfig::builder_with_provider(Arc::new(rustls::crypto::ring::default_provider()))
                .with_safe_default_protocol_versions()?
                .with_root_certificates(roots)
                .with_no_client_auth();
            let name = rustls::pki_types::ServerName::try_from(host.clone())?;
            let stream = tokio_rustls::TlsConnector::from(Arc::new(cfg))
                .connect(name, tcp)
                .await
                .context("relay TLS")?;
            Box::new(stream)
        } else {
            Box::new(tcp)
        };
        use base64::Engine;
        let key = base64::engine::general_purpose::STANDARD.encode(crate::util::random_bytes::<16>());
        let host_header = if (tls && port == 443) || (!tls && port == 80) { host.clone() } else { format!("{host}:{port}") };
        let req = format!(
            "GET {path} HTTP/1.1\r\nHost: {host_header}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\
             Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\nUser-Agent: PCLink/{}\r\n\r\n",
            crate::agent::VERSION
        );
        io.write_all(req.as_bytes()).await?;
        io.flush().await?;
        // Read the response head byte by byte so nothing after it is lost.
        let mut head = Vec::new();
        let read_head = async {
            while !head.ends_with(b"\r\n\r\n") {
                if head.len() > 16 * 1024 {
                    bail!("relay sent an oversized response");
                }
                let b = io.read_u8().await.context("relay closed during handshake")?;
                head.push(b);
            }
            Ok(())
        };
        tokio::time::timeout(Duration::from_secs(15), read_head).await.context("relay handshake timed out")??;
        let head = String::from_utf8_lossy(&head);
        let status = head.lines().next().unwrap_or("");
        if !status.contains(" 101") {
            bail!("relay refused the connection: {}", status.trim());
        }
        Ok(WsConn { io })
    }

    /// Waits (answering pings) until the relay says a phone joined.
    async fn wait_for_open(mut self) -> Result<WsConn> {
        loop {
            let (op, payload) = tokio::time::timeout(READ_TIMEOUT, read_frame(&mut self.io))
                .await
                .map_err(|_| anyhow!("relay went quiet"))??;
            match op {
                0x1 if payload == b"open" => return Ok(self),
                0x8 => bail!("relay closed: {}", close_reason(&payload)),
                0x9 => write_frame(&mut self.io, 0xA, &payload).await?,
                _ => {}
            }
        }
    }

    /// Turns the socket into a byte stream (the other end of a duplex pipe).
    fn into_stream(self) -> tokio::io::DuplexStream {
        let (ours, theirs) = tokio::io::duplex(256 * 1024);
        let (mut ws_r, mut ws_w) = tokio::io::split(self.io);
        let (mut pipe_r, mut pipe_w) = tokio::io::split(ours);
        let (tx, mut rx) = mpsc::channel::<(u8, Vec<u8>)>(64);
        // Writer: everything to the relay goes through here.
        tokio::spawn(async move {
            while let Some((op, data)) = rx.recv().await {
                if write_frame(&mut ws_w, op, &data).await.is_err() || op == 0x8 {
                    break;
                }
            }
            let _ = ws_w.shutdown().await;
        });
        // Pipe -> relay.
        let tx2 = tx.clone();
        tokio::spawn(async move {
            let mut buf = vec![0u8; 64 * 1024];
            loop {
                match pipe_r.read(&mut buf).await {
                    Ok(0) | Err(_) => {
                        let _ = tx2.send((0x8, 1000u16.to_be_bytes().to_vec())).await;
                        break;
                    }
                    Ok(n) => {
                        if tx2.send((0x2, buf[..n].to_vec())).await.is_err() {
                            break;
                        }
                    }
                }
            }
        });
        // Relay -> pipe.
        tokio::spawn(async move {
            loop {
                let frame = tokio::time::timeout(READ_TIMEOUT, read_frame(&mut ws_r)).await;
                let Ok(Ok((op, payload))) = frame else { break };
                match op {
                    0x0..=0x2 => {
                        if pipe_w.write_all(&payload).await.is_err() {
                            break;
                        }
                    }
                    0x8 => break,
                    0x9 => {
                        let _ = tx.send((0xA, payload)).await;
                    }
                    _ => {}
                }
            }
            let _ = pipe_w.shutdown().await;
        });
        theirs
    }
}

fn close_reason(payload: &[u8]) -> String {
    if payload.len() > 2 {
        String::from_utf8_lossy(&payload[2..]).into_owned()
    } else {
        "no reason given".into()
    }
}

async fn read_frame<R: AsyncRead + Unpin>(r: &mut R) -> Result<(u8, Vec<u8>)> {
    let mut h = [0u8; 2];
    r.read_exact(&mut h).await?;
    let op = h[0] & 0x0F;
    let masked = h[1] & 0x80 != 0;
    let len = match h[1] & 0x7F {
        126 => r.read_u16().await? as usize,
        127 => r.read_u64().await? as usize,
        n => n as usize,
    };
    if len > MAX_FRAME {
        bail!("relay frame too big");
    }
    let mut mask = [0u8; 4];
    if masked {
        r.read_exact(&mut mask).await?;
    }
    let mut payload = vec![0u8; len];
    r.read_exact(&mut payload).await?;
    if masked {
        for (i, b) in payload.iter_mut().enumerate() {
            *b ^= mask[i & 3];
        }
    }
    Ok((op, payload))
}

/// Client frames must be masked.
async fn write_frame<W: AsyncWrite + Unpin>(w: &mut W, op: u8, payload: &[u8]) -> Result<()> {
    let mut out = Vec::with_capacity(payload.len() + 14);
    out.push(0x80 | op);
    let n = payload.len();
    if n < 126 {
        out.push(0x80 | n as u8);
    } else if n < 65536 {
        out.push(0x80 | 126);
        out.extend_from_slice(&(n as u16).to_be_bytes());
    } else {
        out.push(0x80 | 127);
        out.extend_from_slice(&(n as u64).to_be_bytes());
    }
    let mask = crate::util::random_bytes::<4>();
    out.extend_from_slice(&mask);
    out.extend(payload.iter().enumerate().map(|(i, b)| b ^ mask[i & 3]));
    w.write_all(&out).await?;
    w.flush().await?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn urls() {
        assert_eq!(room_url("wss://r.example/", "ab", "pc"), "wss://r.example/ab?role=pc");
        assert_eq!(room_url("wss://r.example/relay", "ab", "phone"), "wss://r.example/relay/ab?role=phone");
    }

    #[tokio::test]
    async fn frames_round_trip() {
        let (mut a, mut b) = tokio::io::duplex(1 << 20);
        for len in [0usize, 5, 125, 126, 300, 70_000] {
            let data: Vec<u8> = (0..len).map(|i| i as u8).collect();
            write_frame(&mut a, 0x2, &data).await.unwrap();
            let (op, got) = read_frame(&mut b).await.unwrap();
            assert_eq!(op, 0x2);
            assert_eq!(got, data);
        }
    }
}

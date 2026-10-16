//! TLS listener for the phone, and the UDP discovery beacon.

use crate::agent::Agent;
use crate::link::{spawn_writer, Link};
use crate::proto::{self, Frame, MsgExt};
use anyhow::{bail, Context, Result};
use serde_json::{json, Value};
use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;
use tokio::io::{AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::mpsc;
use tokio_rustls::TlsAcceptor;

/// The phone pings every 20 s; a link silent for this long is dead.
const IDLE_TIMEOUT: Duration = Duration::from_secs(70);
const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(15);

pub async fn run(agent: Arc<Agent>, listener: TcpListener) -> Result<()> {
    let acceptor = TlsAcceptor::from(agent.identity.server_config()?);
    loop {
        let (tcp, peer) = listener.accept().await?;
        let _ = tcp.set_nodelay(true);
        let agent = agent.clone();
        let acceptor = acceptor.clone();
        tokio::spawn(async move {
            if let Err(e) = handle_conn(agent, acceptor, tcp, peer).await {
                tracing::info!("connection from {peer}: {e:#}");
            }
        });
    }
}

async fn handle_conn(
    agent: Arc<Agent>,
    acceptor: TlsAcceptor,
    tcp: TcpStream,
    peer: SocketAddr,
) -> Result<()> {
    let tls = tokio::time::timeout(HANDSHAKE_TIMEOUT, acceptor.accept(tcp))
        .await
        .context("TLS handshake timed out")??;
    let (r, mut w) = tokio::io::split(tls);
    let mut r = BufReader::with_capacity(128 * 1024, r);

    let first = match tokio::time::timeout(HANDSHAKE_TIMEOUT, proto::read_frame(&mut r)).await {
        Ok(Ok(Some(Frame::Json(v)))) => v,
        _ => bail!("no hello"),
    };
    let device_id = first.str_of("device_id").unwrap_or("").to_string();
    let device_name = first.str_of("device_name").unwrap_or("Phone").to_string();

    let reply_err = |msg: &str| proto::encode_json(&json!({"t": "error", "error": msg}));
    let (device_id, device_name) = match first.t() {
        "pair" => {
            let code = first.str_of("code").unwrap_or("");
            match agent.complete_pairing(code, &device_id, &device_name) {
                Ok(token) => {
                    let mut info = agent.hello_info();
                    info["t"] = json!("paired");
                    info["token"] = json!(token);
                    w.write_all(&proto::encode_json(&info)).await?;
                    (device_id, device_name)
                }
                Err(e) => {
                    w.write_all(&reply_err(&e.to_string())).await?;
                    w.flush().await?;
                    bail!("pairing refused: {e}");
                }
            }
        }
        "hello" => {
            let token = first.str_of("token").unwrap_or("");
            match agent.authenticate(&device_id, token) {
                Some(dev) => {
                    let mut info = agent.hello_info();
                    info["t"] = json!("welcome");
                    w.write_all(&proto::encode_json(&info)).await?;
                    (dev.id, device_name)
                }
                None => {
                    w.write_all(&reply_err("unknown device: pair again")).await?;
                    w.flush().await?;
                    bail!("unknown device {device_id}");
                }
            }
        }
        other => bail!("unexpected first message {other}"),
    };
    w.flush().await?;

    let (tx, rx) = mpsc::channel::<Vec<u8>>(256);
    let link = Arc::new(Link::new(device_id, device_name, peer, tx));
    spawn_writer(w, rx, link.close_signal());
    agent.attach(link.clone());

    let result = read_loop(&agent, &link, &mut r).await;
    link.close();
    agent.detach(&link);
    result
}

async fn read_loop<R: tokio::io::AsyncRead + Unpin>(
    agent: &Arc<Agent>,
    link: &Arc<Link>,
    r: &mut R,
) -> Result<()> {
    let mut closed = link.close_signal();
    loop {
        let frame = tokio::select! {
            f = tokio::time::timeout(IDLE_TIMEOUT, proto::read_frame(r)) => match f {
                Ok(f) => f?,
                Err(_) => bail!("link idle, dropping"),
            },
            _ = closed.changed() => return Ok(()),
        };
        let Some(frame) = frame else { return Ok(()) };
        match frame {
            Frame::Data { xfer, bytes } => {
                if let Err(e) = link.on_data(xfer, &bytes).await {
                    tracing::warn!("{e:#}");
                }
            }
            Frame::Json(msg) => dispatch(agent, link, msg).await,
        }
    }
}

async fn dispatch(agent: &Arc<Agent>, link: &Arc<Link>, msg: Value) {
    match msg.t() {
        "res" => link.on_response(&msg),
        "req" => {
            let id = msg.u64_of("id").unwrap_or(0);
            let op = msg.str_of("op").unwrap_or("").to_string();
            let args = msg.get("args").cloned().unwrap_or(Value::Null);
            let agent = agent.clone();
            let link = link.clone();
            tokio::spawn(async move {
                let res = agent.handle_request(&link, &op, args).await;
                if let Err(e) = &res {
                    tracing::debug!("request {op} failed: {e:#}");
                }
                let _ = link.respond(id, res).await;
            });
        }
        "ev" => {
            let ev = msg.str_of("ev").unwrap_or("");
            agent.on_event(ev, msg.get("data").unwrap_or(&Value::Null));
        }
        "xfer_end" => link.on_xfer_end(&msg).await,
        "ping" => {
            let _ = link.send_json(json!({"t": "pong"})).await;
        }
        "pong" => {}
        other => tracing::debug!("unknown message type {other}"),
    }
}

/// Broadcasts a small JSON beacon so the phone can find the PC on the LAN
/// (including after its IP address changes).
pub async fn beacon(agent: Arc<Agent>) {
    let sock = match tokio::net::UdpSocket::bind("0.0.0.0:0").await {
        Ok(s) => s,
        Err(e) => {
            tracing::warn!("beacon disabled: {e}");
            return;
        }
    };
    let _ = sock.set_broadcast(true);
    let msg = json!({
        "pclink": proto::PROTOCOL_VERSION,
        "id": agent.state.lock().unwrap().pc_id,
        "name": agent.cfg.pc_name,
        "port": agent.cfg.port,
        "fp": agent.identity.fingerprint,
    })
    .to_string();
    let dest = format!("255.255.255.255:{}", crate::config::DISCOVERY_PORT);
    loop {
        let _ = sock.send_to(msg.as_bytes(), &dest).await;
        tokio::time::sleep(Duration::from_secs(3)).await;
    }
}

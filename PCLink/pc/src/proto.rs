//! Wire format shared by the PC agent and the Android app. See docs/PROTOCOL.md.
//!
//! Every frame is `u32 big-endian length | u8 kind | payload`, where the
//! length counts the kind byte and payload.
//!  - kind 0: UTF-8 JSON object with a `"t"` (type) field.
//!  - kind 1: file data: `u32 big-endian transfer id | bytes`.

use anyhow::{bail, Result};
use serde_json::Value;
use tokio::io::{AsyncRead, AsyncReadExt};

pub const KIND_JSON: u8 = 0;
pub const KIND_DATA: u8 = 1;
/// Largest frame either side accepts. Chunks are 64 KiB; JSON is small.
pub const MAX_FRAME: usize = 4 * 1024 * 1024;
pub const CHUNK: usize = 64 * 1024;
pub const PROTOCOL_VERSION: u32 = 1;

#[derive(Debug, Clone, PartialEq)]
pub enum Frame {
    Json(Value),
    Data { xfer: u32, bytes: Vec<u8> },
}

impl Frame {
    pub fn encode(&self) -> Vec<u8> {
        match self {
            Frame::Json(v) => {
                let body = serde_json::to_vec(v).expect("serializable");
                let mut out = Vec::with_capacity(5 + body.len());
                out.extend_from_slice(&((body.len() + 1) as u32).to_be_bytes());
                out.push(KIND_JSON);
                out.extend_from_slice(&body);
                out
            }
            Frame::Data { xfer, bytes } => encode_data(*xfer, bytes),
        }
    }
}

pub fn encode_data(xfer: u32, bytes: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(9 + bytes.len());
    out.extend_from_slice(&((bytes.len() + 5) as u32).to_be_bytes());
    out.push(KIND_DATA);
    out.extend_from_slice(&xfer.to_be_bytes());
    out.extend_from_slice(bytes);
    out
}

pub fn encode_json(v: &Value) -> Vec<u8> {
    Frame::Json(v.clone()).encode()
}

/// Reads one frame. Returns `Ok(None)` on a clean end of stream.
pub async fn read_frame<R: AsyncRead + Unpin>(r: &mut R) -> Result<Option<Frame>> {
    let mut len_buf = [0u8; 4];
    match r.read_exact(&mut len_buf).await {
        Ok(_) => {}
        Err(e) if e.kind() == std::io::ErrorKind::UnexpectedEof => return Ok(None),
        Err(e) => return Err(e.into()),
    }
    let len = u32::from_be_bytes(len_buf) as usize;
    if len == 0 || len > MAX_FRAME {
        bail!("bad frame length {len}");
    }
    let mut buf = vec![0u8; len];
    r.read_exact(&mut buf).await?;
    match buf[0] {
        KIND_JSON => {
            let v: Value = serde_json::from_slice(&buf[1..])?;
            if !v.is_object() {
                bail!("JSON frame is not an object");
            }
            Ok(Some(Frame::Json(v)))
        }
        KIND_DATA => {
            if buf.len() < 5 {
                bail!("short data frame");
            }
            let xfer = u32::from_be_bytes([buf[1], buf[2], buf[3], buf[4]]);
            buf.drain(..5);
            Ok(Some(Frame::Data { xfer, bytes: buf }))
        }
        k => bail!("unknown frame kind {k}"),
    }
}

/// Convenience accessors for JSON messages.
pub trait MsgExt {
    fn t(&self) -> &str;
    fn str_of(&self, key: &str) -> Option<&str>;
    fn u64_of(&self, key: &str) -> Option<u64>;
}

impl MsgExt for Value {
    fn t(&self) -> &str {
        self.get("t").and_then(Value::as_str).unwrap_or("")
    }
    fn str_of(&self, key: &str) -> Option<&str> {
        self.get(key).and_then(Value::as_str)
    }
    fn u64_of(&self, key: &str) -> Option<u64> {
        self.get(key).and_then(Value::as_u64)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[tokio::test]
    async fn roundtrip() {
        let frames = vec![
            Frame::Json(json!({"t": "req", "id": 1, "op": "fs.list", "args": {"path": "/"}})),
            Frame::Data { xfer: 7, bytes: vec![1, 2, 3, 4, 5] },
            Frame::Data { xfer: u32::MAX, bytes: vec![] },
        ];
        let mut wire = Vec::new();
        for f in &frames {
            wire.extend(f.encode());
        }
        let mut r = &wire[..];
        for f in &frames {
            assert_eq!(read_frame(&mut r).await.unwrap().as_ref(), Some(f));
        }
        assert_eq!(read_frame(&mut r).await.unwrap(), None);
    }

    #[tokio::test]
    async fn rejects_oversized_and_unknown() {
        let mut big = ((MAX_FRAME + 1) as u32).to_be_bytes().to_vec();
        big.push(0);
        assert!(read_frame(&mut &big[..]).await.is_err());
        let unknown = [0u8, 0, 0, 1, 9];
        assert!(read_frame(&mut &unknown[..]).await.is_err());
        let not_object = Frame::Json(json!([1, 2])).encode();
        assert!(read_frame(&mut &not_object[..]).await.is_err());
    }
}

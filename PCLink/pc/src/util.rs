use rand::RngCore;
use sha2::{Digest, Sha256};

pub fn random_bytes<const N: usize>() -> [u8; N] {
    let mut b = [0u8; N];
    rand::thread_rng().fill_bytes(&mut b);
    b
}

pub fn random_hex(n_bytes: usize) -> String {
    let mut b = vec![0u8; n_bytes];
    rand::thread_rng().fill_bytes(&mut b);
    hex::encode(b)
}

pub fn sha256_hex(data: &[u8]) -> String {
    hex::encode(Sha256::digest(data))
}

pub fn now_secs() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

pub fn now_millis() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// Constant-time comparison for secrets.
pub fn ct_eq(a: &[u8], b: &[u8]) -> bool {
    a.len() == b.len() && a.iter().zip(b).fold(0u8, |acc, (x, y)| acc | (x ^ y)) == 0
}

/// Human-friendly pairing code: 8 characters without look-alikes (0/O, 1/I/L).
pub fn pairing_code() -> String {
    const ALPHABET: &[u8] = b"ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    let bytes = random_bytes::<8>();
    bytes
        .iter()
        .map(|b| ALPHABET[*b as usize % ALPHABET.len()] as char)
        .collect()
}

/// Local IPv4 addresses worth putting in a pairing QR code, best first.
pub fn local_ipv4s() -> Vec<std::net::Ipv4Addr> {
    // Asking the OS which source address it would use for an outbound UDP
    // "connection" finds the primary interface without any extra crates.
    let mut out = Vec::new();
    if let Ok(sock) = std::net::UdpSocket::bind("0.0.0.0:0") {
        if sock.connect("192.168.255.255:9").is_ok() || sock.connect("10.255.255.255:9").is_ok() {
            if let Ok(std::net::SocketAddr::V4(a)) = sock.local_addr() {
                if !a.ip().is_unspecified() {
                    out.push(*a.ip());
                }
            }
        }
    }
    out
}

//! Self-update from the repository's `pclink-latest` GitHub release.
//!
//! CI publishes `latest.json` next to the binaries:
//! `{"build": 57, "commit": "…", "files": {"pclink-agent.exe": {"sha256": "…", "size": 123}, …}}`.
//! Builds made by CI carry their build number (PCLINK_BUILD); local
//! builds are build 0 and never update themselves.

use anyhow::{anyhow, bail, Context, Result};
use serde::Deserialize;
use sha2::{Digest, Sha256};
use std::collections::HashMap;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;

/// This binary's CI build number (0 for local builds).
pub const BUILD: u32 = parse_build(option_env!("PCLINK_BUILD"));

const fn parse_build(s: Option<&str>) -> u32 {
    let Some(s) = s else { return 0 };
    let b = s.as_bytes();
    let mut n = 0u32;
    let mut i = 0;
    while i < b.len() {
        if b[i] < b'0' || b[i] > b'9' {
            return 0;
        }
        n = n * 10 + (b[i] - b'0') as u32;
        i += 1;
    }
    n
}

/// GitHub repository the release lives in.
pub fn repo() -> &'static str {
    option_env!("PCLINK_REPO").unwrap_or("wilflet1/Satisfying")
}

/// Where `latest.json` and the binaries are downloaded from.
/// `PCLINK_UPDATE_URL` overrides it (used by tests).
pub fn base_url() -> String {
    std::env::var("PCLINK_UPDATE_URL")
        .unwrap_or_else(|_| format!("https://github.com/{}/releases/download/pclink-latest", repo()))
}

pub fn version_string() -> String {
    if BUILD != 0 {
        format!("{} (build {BUILD})", crate::agent::VERSION)
    } else {
        format!("{} (local build)", crate::agent::VERSION)
    }
}

#[derive(Debug, Clone, Deserialize)]
pub struct FileInfo {
    pub sha256: String,
    pub size: u64,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Manifest {
    pub build: u32,
    #[serde(default)]
    pub commit: String,
    pub files: HashMap<String, FileInfo>,
}

/// Files the PC side replaces when it updates.
pub fn pc_files() -> Vec<&'static str> {
    if cfg!(windows) {
        vec!["pclink-agent.exe", "pclink.exe"]
    } else {
        vec!["pclink-agent", "pclink"]
    }
}

fn agent() -> ureq::Agent {
    ureq::AgentBuilder::new()
        .timeout_connect(Duration::from_secs(10))
        .timeout_read(Duration::from_secs(60))
        // Honour HTTPS_PROXY for GitHub; local (test) URLs go direct.
        .try_proxy_from_env(base_url().starts_with("https://"))
        .build()
}

pub fn fetch_manifest() -> Result<Manifest> {
    let url = format!("{}/latest.json", base_url());
    let res = agent()
        .get(&url)
        // Never accept a cached copy: it hides a build that was just released.
        .set("Cache-Control", "no-cache")
        .call()
        .map_err(|e| anyhow!("checking for updates: {e}"))?;
    Ok(res.into_json()?)
}

/// Outcome of a check.
#[derive(Debug)]
pub enum Check {
    UpToDate { latest: u32 },
    Available(Manifest),
    /// Local builds don't auto-update.
    LocalBuild { latest: u32 },
}

pub fn check() -> Result<Check> {
    let m = fetch_manifest()?;
    Ok(if BUILD == 0 {
        Check::LocalBuild { latest: m.build }
    } else if m.build > BUILD {
        Check::Available(m)
    } else {
        Check::UpToDate { latest: m.build }
    })
}

/// Downloads `name`, checking its size and SHA-256, to `dest`.
pub fn download(name: &str, info: &FileInfo, dest: &Path) -> Result<()> {
    let url = format!("{}/{}", base_url(), name);
    let res = agent()
        .get(&url)
        .call()
        .map_err(|e| anyhow!("downloading {name}: {e}"))?;
    let mut reader = res.into_reader().take(info.size + 1);
    let mut file = std::fs::File::create(dest).with_context(|| format!("creating {}", dest.display()))?;
    let mut hasher = Sha256::new();
    let mut buf = vec![0u8; 64 * 1024];
    let mut total = 0u64;
    loop {
        let n = reader.read(&mut buf)?;
        if n == 0 {
            break;
        }
        hasher.update(&buf[..n]);
        file.write_all(&buf[..n])?;
        total += n as u64;
    }
    file.sync_all()?;
    drop(file);
    let digest = hex::encode(hasher.finalize());
    if total != info.size || !digest.eq_ignore_ascii_case(&info.sha256) {
        let _ = std::fs::remove_file(dest);
        bail!("{name} failed verification (size {total}, sha256 {digest})");
    }
    Ok(())
}

/// Folder holding the running binaries.
pub fn install_dir() -> Result<PathBuf> {
    let exe = std::env::current_exe()?;
    Ok(exe.parent().ok_or_else(|| anyhow!("no install folder"))?.to_path_buf())
}

/// Downloads every PC file in the manifest and swaps them in. A running
/// .exe can't be overwritten on Windows but can be renamed, so each current
/// file moves aside to `*.old` (removed on the next start).
pub fn apply(m: &Manifest, dir: &Path) -> Result<Vec<String>> {
    let names = pc_files();
    // Download and verify everything before touching the installed files.
    let mut staged = Vec::new();
    for name in &names {
        let info = m
            .files
            .get(*name)
            .ok_or_else(|| anyhow!("release has no {name}"))?;
        let new = dir.join(format!("{name}.new"));
        download(name, info, &new)?;
        staged.push((name.to_string(), new));
    }
    let mut replaced = Vec::new();
    for (name, new) in staged {
        let cur = dir.join(&name);
        let old = dir.join(format!("{name}.old"));
        let _ = std::fs::remove_file(&old);
        if cur.exists() {
            std::fs::rename(&cur, &old).with_context(|| format!("moving {} aside", cur.display()))?;
        }
        if let Err(e) = std::fs::rename(&new, &cur) {
            // Put the old file back so the install keeps working.
            let _ = std::fs::rename(&old, &cur);
            return Err(e).with_context(|| format!("installing {name}"));
        }
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            let _ = std::fs::set_permissions(&cur, std::fs::Permissions::from_mode(0o755));
        }
        replaced.push(name);
    }
    Ok(replaced)
}

/// Removes files left behind by the previous update.
pub fn cleanup(dir: &Path) {
    for name in pc_files() {
        let _ = std::fs::remove_file(dir.join(format!("{name}.old")));
        let _ = std::fs::remove_file(dir.join(format!("{name}.new")));
    }
}

/// Path of the installed agent binary. Captured up front: once an update
/// renames the running file, `current_exe()` points at the `.old` copy.
pub fn agent_path() -> Result<PathBuf> {
    Ok(install_dir()?.join(pc_files()[0]))
}

/// Starts `exe` and exits this process. The new process waits for this
/// one to release its ports. `updated_to` marks a restart after an update.
pub fn restart_with(exe: &Path, updated_to: Option<u32>) -> ! {
    let mut cmd = std::process::Command::new(exe);
    cmd.env(RESTART_ENV, "1");
    match updated_to {
        Some(b) => cmd.env(UPDATED_TO_ENV, b.to_string()),
        None => cmd.env_remove(UPDATED_TO_ENV),
    };
    if let Err(e) = cmd.spawn() {
        tracing::error!("could not restart {}: {e}", exe.display());
    }
    std::process::exit(0);
}

/// Restarts the installed agent (e.g. after editing settings).
pub fn restart() -> ! {
    let exe = agent_path().or_else(|_| std::env::current_exe()).unwrap_or_default();
    restart_with(&exe, None)
}

/// Set on a restarted agent so it waits for the old one's ports.
pub const RESTART_ENV: &str = "PCLINK_RESTARTED";
/// Build an update just installed. If the restarted agent is older than
/// this, the swap didn't take; it must not try again (no update loop).
pub const UPDATED_TO_ENV: &str = "PCLINK_UPDATED_TO";

/// Whether automatic updates should run in this process.
pub fn auto_update_allowed() -> bool {
    if BUILD == 0 {
        return false;
    }
    match std::env::var(UPDATED_TO_ENV).ok().and_then(|v| v.parse::<u32>().ok()) {
        Some(target) if target > BUILD => {
            tracing::error!("update to build {target} did not take effect; not retrying");
            false
        }
        _ => true,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn build_numbers() {
        assert_eq!(parse_build(None), 0);
        assert_eq!(parse_build(Some("57")), 57);
        assert_eq!(parse_build(Some("x1")), 0);
    }

    /// Serves files over plain HTTP for the update tests.
    fn serve(files: Vec<(&'static str, Vec<u8>)>) -> String {
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let addr = listener.local_addr().unwrap();
        std::thread::spawn(move || {
            for stream in listener.incoming() {
                let Ok(mut s) = stream else { continue };
                let mut req = [0u8; 2048];
                let n = s.read(&mut req).unwrap_or(0);
                let line = String::from_utf8_lossy(&req[..n]).to_string();
                let path = line.split_whitespace().nth(1).unwrap_or("/").trim_start_matches('/').to_string();
                match files.iter().find(|(name, _)| *name == path) {
                    Some((_, body)) => {
                        let _ = write!(s, "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", body.len());
                        let _ = s.write_all(body);
                    }
                    None => {
                        let _ = write!(s, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    }
                }
            }
        });
        format!("http://{addr}")
    }

    #[test]
    fn downloads_verifies_and_swaps_files() {
        let names = pc_files();
        let new_agent = b"new agent binary".to_vec();
        let new_cli = b"new cli binary".to_vec();
        let info = |b: &[u8]| serde_json::json!({"sha256": hex::encode(Sha256::digest(b)), "size": b.len()});
        let manifest = serde_json::json!({
            "build": 4_000_000_000u32,
            "commit": "abc",
            "files": { names[0]: info(&new_agent), names[1]: info(&new_cli) },
        });
        let url = serve(vec![
            ("latest.json", manifest.to_string().into_bytes()),
            (names[0], new_agent.clone()),
            (names[1], new_cli.clone()),
            ("tampered", b"evil".to_vec()),
        ]);
        std::env::set_var("PCLINK_UPDATE_URL", &url);

        let m = fetch_manifest().unwrap();
        assert_eq!(m.build, 4_000_000_000);
        // Local test builds report that; CI builds (PCLINK_BUILD set) see an update.
        match check().unwrap() {
            Check::LocalBuild { latest } => assert!(BUILD == 0 && latest == 4_000_000_000),
            Check::Available(a) => assert!(BUILD != 0 && a.build == 4_000_000_000),
            other => panic!("unexpected {other:?}"),
        }

        let dir = tempfile::tempdir().unwrap();
        std::fs::write(dir.path().join(names[0]), b"old agent").unwrap();
        std::fs::write(dir.path().join(names[1]), b"old cli").unwrap();
        let replaced = apply(&m, dir.path()).unwrap();
        assert_eq!(replaced.len(), 2);
        assert_eq!(std::fs::read(dir.path().join(names[0])).unwrap(), new_agent);
        assert_eq!(std::fs::read(dir.path().join(names[1])).unwrap(), new_cli);
        assert_eq!(std::fs::read(dir.path().join(format!("{}.old", names[0]))).unwrap(), b"old agent");
        cleanup(dir.path());
        assert!(!dir.path().join(format!("{}.old", names[0])).exists());

        // A file whose hash doesn't match is rejected and leaves nothing behind.
        let bad = FileInfo { sha256: hex::encode(Sha256::digest(b"good")), size: 4 };
        let dest = dir.path().join("t.new");
        assert!(download("tampered", &bad, &dest).is_err());
        assert!(!dest.exists());

        // A failed download leaves the installed files untouched.
        let mut broken = m.clone();
        broken.files.get_mut(names[1]).unwrap().sha256 = "00".repeat(32);
        assert!(apply(&broken, dir.path()).is_err());
        assert_eq!(std::fs::read(dir.path().join(names[0])).unwrap(), new_agent);
        std::env::remove_var("PCLINK_UPDATE_URL");
    }
}

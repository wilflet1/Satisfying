//! The PC's file system as the phone sees it: only the configured shared
//! folders, with every path checked against them.

use anyhow::{anyhow, bail, Result};
use serde_json::{json, Value};
use std::path::{Path, PathBuf};

pub struct SharedFs {
    roots: Vec<PathBuf>,
}

impl SharedFs {
    pub fn new(folders: &[PathBuf]) -> Self {
        let roots = folders
            .iter()
            .filter_map(|f| std::fs::canonicalize(f).ok())
            .collect();
        Self { roots }
    }

    pub fn roots_json(&self) -> Value {
        Value::Array(
            self.roots
                .iter()
                .map(|r| {
                    let name = r
                        .file_name()
                        .and_then(|n| n.to_str())
                        .map(str::to_string)
                        .unwrap_or_else(|| display(r));
                    json!({"name": name, "path": display(r)})
                })
                .collect(),
        )
    }

    /// Resolves a path from the phone, refusing anything outside the roots
    /// (including through `..` or symlinks, since the path is canonicalized).
    pub fn resolve(&self, path: &str) -> Result<PathBuf> {
        let canon = std::fs::canonicalize(path).map_err(|_| anyhow!("not found: {path}"))?;
        if self.roots.iter().any(|r| canon.starts_with(r)) {
            Ok(canon)
        } else {
            bail!("access denied: {path} is outside the shared folders")
        }
    }

    pub fn list(&self, path: &str) -> Result<Value> {
        let dir = self.resolve(path)?;
        let mut entries = Vec::new();
        for e in std::fs::read_dir(&dir)? {
            let Ok(e) = e else { continue };
            let Ok(meta) = e.metadata() else { continue };
            let name = e.file_name().to_string_lossy().to_string();
            if is_hidden(&name, &meta) {
                continue;
            }
            let mtime = meta
                .modified()
                .ok()
                .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
                .map(|d| d.as_millis() as u64)
                .unwrap_or(0);
            entries.push(json!({
                "name": name,
                "path": display(&e.path()),
                "dir": meta.is_dir(),
                "size": if meta.is_dir() { 0 } else { meta.len() },
                "mtime": mtime,
            }));
        }
        entries.sort_by(|a, b| {
            let ad = a["dir"].as_bool().unwrap_or(false);
            let bd = b["dir"].as_bool().unwrap_or(false);
            bd.cmp(&ad).then_with(|| {
                a["name"]
                    .as_str()
                    .unwrap_or("")
                    .to_lowercase()
                    .cmp(&b["name"].as_str().unwrap_or("").to_lowercase())
            })
        });
        let parent = dir
            .parent()
            .filter(|p| self.roots.iter().any(|r| p.starts_with(r)))
            .map(display);
        Ok(json!({"path": display(&dir), "parent": parent, "entries": entries}))
    }

    /// A file the phone may download.
    pub fn readable_file(&self, path: &str) -> Result<PathBuf> {
        let p = self.resolve(path)?;
        if !p.is_file() {
            bail!("{path} is not a file");
        }
        Ok(p)
    }

    /// A folder the phone may upload into.
    pub fn writable_dir(&self, path: &str) -> Result<PathBuf> {
        let p = self.resolve(path)?;
        if !p.is_dir() {
            bail!("{path} is not a folder");
        }
        Ok(p)
    }
}

fn is_hidden(name: &str, _meta: &std::fs::Metadata) -> bool {
    if name.starts_with(".pclink-") {
        return true;
    }
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        const HIDDEN: u32 = 0x2;
        const SYSTEM: u32 = 0x4;
        if _meta.file_attributes() & (HIDDEN | SYSTEM) != 0 {
            return true;
        }
    }
    false
}

/// Path as shown to the user: without Windows' `\\?\` verbatim prefix.
pub fn display(p: &Path) -> String {
    let s = p.to_string_lossy();
    if let Some(rest) = s.strip_prefix(r"\\?\UNC\") {
        return format!(r"\\{rest}");
    }
    if let Some(rest) = s.strip_prefix(r"\\?\") {
        return rest.to_string();
    }
    s.to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sandbox() {
        let base = tempfile::tempdir().unwrap();
        let shared = base.path().join("shared");
        let secret = base.path().join("secret");
        std::fs::create_dir_all(shared.join("sub")).unwrap();
        std::fs::create_dir_all(&secret).unwrap();
        std::fs::write(shared.join("sub/a.txt"), "hi").unwrap();
        std::fs::write(secret.join("key"), "nope").unwrap();

        let fs = SharedFs::new(std::slice::from_ref(&shared));
        assert!(fs.readable_file(shared.join("sub/a.txt").to_str().unwrap()).is_ok());
        let escape = shared.join("sub/../../secret/key");
        assert!(fs.readable_file(escape.to_str().unwrap()).is_err());
        assert!(fs.list(secret.to_str().unwrap()).is_err());

        #[cfg(unix)]
        {
            std::os::unix::fs::symlink(&secret, shared.join("link")).unwrap();
            assert!(fs.readable_file(shared.join("link/key").to_str().unwrap()).is_err());
        }

        let listing = fs.list(shared.to_str().unwrap()).unwrap();
        assert_eq!(listing["parent"], Value::Null);
        assert_eq!(listing["entries"][0]["name"], "sub");
        let sub = fs.list(shared.join("sub").to_str().unwrap()).unwrap();
        assert!(sub["parent"].is_string());
    }
}

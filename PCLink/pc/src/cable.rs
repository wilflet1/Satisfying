//! VB-Cable (VB-Audio's free virtual audio cable), which the phone mic and
//! the speaker's "silence the PC" mode need. PCLink doesn't install drivers
//! itself: it opens VB-Audio's official page and explains the steps.

/// VB-Audio's official VB-Cable page.
pub const DOWNLOAD_PAGE: &str = "https://vb-audio.com/Cable/";

pub const STEPS: &str = "On VB-Audio's page (now open on the PC), download the VB-CABLE driver pack, extract it, right-click VBCABLE_Setup_x64.exe and choose Run as administrator, click Install Driver, then restart the PC.";

/// Whether an output device looks like a virtual cable's input side.
pub fn is_virtual_cable(name: &str) -> bool {
    let l = name.to_lowercase();
    (l.contains("cable") && l.contains("input"))
        || (l.contains("voicemeeter") && l.contains("input"))
        || l.contains("virtual audio cable")
}

/// Opens VB-Audio's download page on the PC. Returns the steps to follow.
pub fn open_download_page() -> anyhow::Result<String> {
    crate::platform::open_url(DOWNLOAD_PAGE)?;
    Ok(STEPS.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn recognises_virtual_cables() {
        assert!(is_virtual_cable("CABLE Input (VB-Audio Virtual Cable)"));
        assert!(is_virtual_cable("CABLE-A Input (VB-Audio Cable A)"));
        assert!(is_virtual_cable("Voicemeeter Input (VB-Audio Voicemeeter VAIO)"));
        assert!(is_virtual_cable("Line 1 (Virtual Audio Cable)"));
        assert!(!is_virtual_cable("Speakers (Realtek(R) Audio)"));
        assert!(!is_virtual_cable("CABLE Output (VB-Audio Virtual Cable)"));
    }
}

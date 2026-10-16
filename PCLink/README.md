# PCLink

Link your Android phone and your Windows PC: files both ways, shared clipboard,
notifications, a one-tap **phone-as-speaker** for PC audio, a touchpad and keyboard,
a dev control panel, and a Claude Code integration that lets Claude reach you (and
your phone) while you're away from the desk.

Microsoft's Phone Link has no public API, so PCLink is its own system: a small
Rust agent on the PC and a Kotlin app on the phone, talking over TLS on your LAN.

```
 Android app (Kotlin)                         PC agent (Rust, tray app)
 ┌──────────────────────┐   TLS, pinned cert  ┌─────────────────────────┐
 │ foreground service   │◄───────────────────►│ phone link  :47810      │
 │ notification access  │   UDP beacon :47811 │ dashboard   127.0.0.1:47812
 │ share target, tiles  │◄────────────────────│ pclink CLI, hooks, MCP  │
 │ AudioTrack speaker   │◄── UDP AES-GCM PCM ─│ WASAPI loopback capture │
 └──────────────────────┘                     └─────────────────────────┘
```

## Features

### Files
- Browse the phone's storage from the PC dashboard; download anything, upload into any folder.
- Browse the PC's **shared folders** from the phone; download, or upload into them.
- **Share → Send to PC** from any Android app. Photos shared this way are also put on the
  PC clipboard, so you can photograph an error and paste it straight into Claude.
- `pclink send <files>` / `pclink get <phone path>` from the terminal; drag and drop on the dashboard.
- Progress notifications on the phone; received files land in `Downloads\PCLink` (PC) and
  `Download/PCLink` (phone).

### Phone as PC speaker (one button)
- Tray menu, **Ctrl+Alt+P**, the dashboard, the phone's notification, or a Quick Settings tile.
- Captures everything the PC plays (WASAPI loopback), streams 16-bit PCM over UDP,
  encrypted with a fresh AES-256-GCM key per session, and plays it with a low-latency
  `AudioTrack` that drops late packets to keep delay around 50–100 ms on Wi-Fi.
- **Silence the PC while the phone plays:** install [VB-Cable](https://vb-audio.com/Cable/).
  With `speaker.pc_output = "auto"` PCLink switches Windows' default output to the cable
  while streaming and switches it back afterwards. Without a virtual device, audio plays
  on both.

### Claude Code
Run `pclink install-claude` once. It adds hooks to `~/.claude/settings.json` (keeping
your existing ones, with a backup) and registers the MCP server.
- **Done / needs you pings:** when Claude finishes a turn or waits for you, your phone
  gets a notification with Claude's last message.
- **Approve from your phone:** permission prompts become a phone notification with
  *Allow / Deny / Decide on PC*. By default this only happens when you've been away from
  the keyboard for 60 s (`[claude] approvals = "away"`); at your desk you get the normal
  prompt. While a question is on the phone, the PC prompt waits for it; answer
  *Decide on PC* (or let it time out) to get the normal prompt back.
- **Phone tools for Claude (MCP):** `notify_phone`, `ask_user_on_phone`,
  `send_file_to_phone`, `get_file_from_phone`, `list_phone_files`,
  `get_latest_phone_photo` (returns the image to Claude), `phone_clipboard`,
  `get_phone_sms_code`, `phone_status`, `recent_phone_notifications`,
  `open_url_on_phone`, `ring_phone`.
- **Dictate prompts:** open *Touchpad & keys* on the phone, tap the keyboard's mic, then
  *Send + Enter*; the text is typed into whatever window has focus (e.g. Claude Code).

### Dev control panel
- Define buttons in `config.toml` under `[[actions]]`; they show on the phone and the
  dashboard. Output comes back as a phone notification.
- `pclink notify -- npm test` runs a command and pings your phone with the result and duration.
- `pclink ask "Deploy?" -o Yes -o No` asks on the phone from any script.
- `pclink open http://192.168.1.20:5173` opens your dev server on the phone.

### Everything else
- Clipboard sync: PC → phone automatically; phone → PC via a Quick Settings tile, the
  notification button or sharing text (Android blocks background clipboard reads).
- Phone notifications appear as Windows notifications; reply or dismiss from the dashboard.
- Touchpad (tap, two-finger scroll, right-click), keyboard keys and shortcuts, media keys and volume.
- Lock, sleep, shut down the PC; Wake-on-LAN from the phone.
- Find my phone (rings at alarm volume even on silent), phone battery on the PC,
  newest-photo fetch, latest SMS verification code (optional permission).
- Starts with Windows (tray toggle) and reconnects automatically on the phone, following
  the PC if its IP changes.

## Install

### PC (Windows 10/11)
1. Build: `cd PCLink/pc && cargo build --release`. That gives you
   `target\release\pclink-agent.exe` (tray app) and `target\release\pclink.exe` (CLI).
   Every push also builds both in GitHub Actions (*PCLink* workflow → `pclink-windows` artifact).
2. Put both in one folder (e.g. `C:\Tools\PCLink`) and add it to `PATH` for the CLI.
3. Run `pclink-agent.exe`. Allow it through Windows Firewall on **private networks**
   when asked; the phone must reach TCP port 47810.
4. The dashboard opens on the pairing page. Tray icon → *Start with Windows* to keep it running.

### Phone (Android 11+)
1. Build: open `PCLink/android` in Android Studio and run it, or
   `./gradlew assembleRelease` (output in `app/build/outputs/apk/release/`).
   GitHub Actions also builds it (`pclink-android` artifact).
2. Install the APK (sideloaded; it uses permissions the Play Store restricts).
3. Pair: scan the QR code on the dashboard with your camera, or open the app, pick your
   PC from the list and type the 8-letter code.
4. Work through *Finish setup* in the app: all-files access, notification access,
   unrestricted battery, and (optionally) SMS for 2FA codes.

## Configuration

`%APPDATA%\PCLink\config.toml` (tray → *Edit settings*, restart the agent after editing):

```toml
pc_name = "DESKTOP"
shared_folders = ['C:\Users\me\Downloads', 'C:\Users\me\Documents', 'C:\code']
inbox = 'C:\Users\me\Downloads\PCLink'
clipboard_sync = true
image_to_clipboard = true
mirror_notifications = true
notification_blocklist = ["com.whatsapp"]

[speaker]
pc_output = "auto"          # auto | switch | keep
silent_device = "CABLE Input"
frame_ms = 5

[claude]
approvals = "away"          # away | always | off
notify = "away"             # away | always | off
away_after_secs = 60
ask_timeout_secs = 300

[[actions]]
name = "Run tests"
command = "npm test"
cwd = 'C:\code\my-app'
icon = "🧪"
```

## Security

- The phone pins the PC's self-signed certificate (SHA-256 fingerprint from the QR code
  or beacon). Pairing codes are random, single-use and expire after 10 minutes; each
  phone then gets its own 256-bit token (the PC stores only its hash).
- The phone can only reach the PC folders listed in `shared_folders`; paths are
  canonicalized so `..` and symlinks can't escape. Uploads are name-sanitized.
- The dashboard and local API listen on 127.0.0.1 only and require the token in
  `%APPDATA%\PCLink\local-token`.
- Remote control (input, power, actions) is available to the paired phone by design.
  *Forget* it on the dashboard's Settings page if the phone is lost.
- Pairing **by IP address** trusts whichever certificate answers (trust on first use);
  prefer the QR code or the discovered-PC list.
- Away from home: put both devices on Tailscale/WireGuard and pair using the PC's tailnet IP.

## Layout

```
PCLink/
  pc/                 Rust agent, CLI, MCP server (cargo test runs unit + end-to-end tests)
    src/agent.rs      features and phone request handling
    src/server.rs     TLS listener, discovery beacon
    src/link.rs       request/response matching and file transfers
    src/api.rs        local HTTP API; src/dashboard.html is the UI
    src/claude.rs     Claude Code hooks and installer; src/mcp.rs MCP server
    src/platform/     Windows: toasts, input, power, audio capture, default-device switching
  android/            Kotlin app, no third-party dependencies
  docs/PROTOCOL.md    wire protocol
```

## Not built yet

Ideas from the original list still to do: screen mirroring and control (MediaProjection),
phone as webcam/microphone, phone as gamepad (ViGEm), the phone as a drive in File
Explorer, SMS and calls UI, auto-lock/unlock by proximity, hotspot toggle, handoff of
tabs/media, ADB/logcat helpers, and Opus compression for the speaker stream.

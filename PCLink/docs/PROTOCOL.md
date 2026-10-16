# PCLink protocol (version 1)

## Transport

- The PC listens on **TCP 47810** with TLS (1.2/1.3). Its certificate is self-signed and
  long-lived; the phone pins its SHA-256 fingerprint (lowercase hex of the DER).
- The PC broadcasts a **UDP beacon** to `255.255.255.255:47811` every 3 s:
  `{"pclink":1,"id":"<pc id>","name":"DESKTOP","port":47810,"fp":"<fingerprint>"}`.
- Pairing QR codes encode `pclink://pair?h=<ip,ip>&p=<port>&fp=<fingerprint>&c=<code>&n=<name>&id=<pc id>`.

## Frames

```
u32 big-endian length (of everything after it) | u8 kind | payload
kind 0: UTF-8 JSON object with a "t" field
kind 1: u32 big-endian transfer id | raw file bytes (≤ 64 KiB per frame)
```
Frames larger than 4 MiB are rejected.

## Handshake

The phone's first frame is one of:
- `{"t":"pair","code":"ABCD2345","device_id":"<uuid>","device_name":"Pixel 9"}`
  → `{"t":"paired","token":"<64 hex>","pc_id":…,"pc_name":…,"mac":…,"version":…,"features":[…]}`
- `{"t":"hello","device_id":"<uuid>","token":"<token>","device_name":"Pixel 9"}`
  → `{"t":"welcome", …same fields minus token}`

Failure: `{"t":"error","error":"…"}` and the PC closes the connection.

## Messages

| Type | Fields | Meaning |
|---|---|---|
| `req` | `id`, `op`, `args` | request; ids are per-sender |
| `res` | `id`, `ok`, `data` or `error` | response |
| `ev` | `ev`, `data` | event, no response |
| `xfer_end` | `xfer`, `ok`, `size`, `name`, `error` | sender finished a transfer |
| `ping` / `pong` | | keepalive; the phone pings every 20 s, either side drops a link silent for 70 s |

### Transfers

Whoever **receives** data allocates the transfer id and registers it before data can
arrive: the PC uses odd ids, the phone even ids.
- Download: `req fs.read {path, offset, xfer}` → `res {name, size}`; the other side streams
  kind-1 frames for `xfer`, then `xfer_end`.
- Upload: `req fs.write {dir|null, name, size, kind}` → `res {xfer}`; the requester streams
  the data and `xfer_end`, then `req xfer.status {xfer}` → `res {path}`.

### Phone → PC requests
`status`, `fs.roots`, `fs.list {path}`, `fs.read`, `fs.write`, `xfer.status`,
`clip.get`, `clip.set {text}`, `open_url {url}`, `open_inbox`,
`input {kind: move|click|button|scroll|text|key, …}`, `media {cmd}`,
`sys {cmd: lock|sleep|hibernate|shutdown|restart|signout|display_off}`,
`actions.list`, `actions.run {name, wait}`, `speaker.set {on}`, `notify {title, text}`.

### PC → phone requests
`status`, `battery`, `fs.list {path|null}`, `fs.read`, `fs.write`, `xfer.status`,
`clip.get`, `clip.set {text}`, `notify {title, text, channel, url}`,
`ask {title, question, detail, options[], timeout_ms, channel}` → `{answer|null}`,
`find_phone`, `open_url {url}`, `sms.latest_code` → `{code, from}`,
`media.latest {screenshots_only}` → `{path, taken}`, `notif.reply {key, text}`,
`notif.dismiss {key}`, `audio.start {key, rate, channels, frame_ms}` → `{port}`, `audio.stop`.

### Events
Phone → PC: `battery`, `status`, `notif.posted {key, package, app, title, text, time, can_reply, silent}`,
`notif.removed {key}`, `clip.changed {text}`, `speaker.stopped`.
PC → phone: `clip.changed {text}`, `action.done {job, name, code, ok, secs, output}`.

## Speaker stream

UDP from the PC to the port returned by `audio.start`. Each packet:
`u64 big-endian sequence | AES-256-GCM ciphertext | 16-byte tag`, nonce = 4 zero bytes
followed by the sequence number, no AAD. The plaintext is interleaved little-endian
16-bit PCM at the announced rate and channel count, `frame_ms` long (5 ms by default).
The key is random per session and sent only over the TLS link.

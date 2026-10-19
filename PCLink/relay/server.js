// PCLink relay: lets the phone reach the PC from anywhere.
//
// Both sides connect out to this server over WebSocket and are joined by a
// shared random room id. The relay only pipes bytes: the phone and PC run
// their usual TLS session (pinned to the PC's certificate) through the pipe,
// so the relay never sees anything but ciphertext.
//
//   PC:    GET /<anything>/<room>?role=pc     (waits; gets a text "open" when a phone joins)
//   Phone: GET /<anything>/<room>?role=phone  (joined to a waiting PC socket, or closed)
//
// No dependencies; runs as a cPanel "Node.js App" (Passenger sets PORT).

"use strict";
const http = require("http");
const crypto = require("crypto");

const PORT = process.env.PORT || 8787;
const MAX_WAITING_PER_ROOM = 4;
const MAX_ROOMS = 2000;
const MAX_FRAME = 4 * 1024 * 1024;
const PING_MS = 25_000;

/** room -> PC sockets waiting for a phone */
const waiting = new Map();
let piped = 0;

const server = http.createServer((req, res) => {
  res.writeHead(200, { "content-type": "text/plain" });
  res.end(`PCLink relay OK\nrooms waiting: ${waiting.size}, live links: ${piped}\n`);
});

server.on("upgrade", (req, socket) => {
  socket.on("error", () => {});
  const url = new URL(req.url, "http://relay");
  const room = url.pathname.split("/").filter(Boolean).pop() || "";
  const role = url.searchParams.get("role");
  const key = req.headers["sec-websocket-key"];
  if (!/^[0-9a-f]{64}$/.test(room) || !["pc", "phone"].includes(role) || !key) {
    socket.end("HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n");
    return;
  }
  const accept = crypto.createHash("sha1").update(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
  socket.write(
    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
      `Sec-WebSocket-Accept: ${accept}\r\n\r\n`,
  );
  socket.setNoDelay(true);
  const ws = new Ws(socket);

  if (role === "pc") {
    if (!waiting.has(room) && waiting.size >= MAX_ROOMS) return ws.close(1013, "relay full");
    const list = waiting.get(room) || [];
    while (list.length >= MAX_WAITING_PER_ROOM) list.shift().close(1000, "replaced");
    list.push(ws);
    waiting.set(room, list);
    const unlist = () => {
      const l = waiting.get(room);
      if (!l) return;
      const i = l.indexOf(ws);
      if (i >= 0) l.splice(i, 1);
      if (!l.length) waiting.delete(room);
    };
    ws.onclose = unlist;
    ws.ondata = () => {}; // ignored until paired
    ws.unlist = unlist;
  } else {
    const list = waiting.get(room);
    const pc = list && list.shift();
    if (list && !list.length) waiting.delete(room);
    if (!pc) return ws.close(4004, "PC is not connected to the relay");
    pc.unlist = null;
    pipe(pc, ws);
  }
});

function pipe(pc, phone) {
  piped++;
  let done = false;
  const end = () => {
    if (done) return;
    done = true;
    piped--;
    pc.close(1000, "peer left");
    phone.close(1000, "peer left");
  };
  pc.ondata = (d) => phone.send(d);
  phone.ondata = (d) => pc.send(d);
  pc.onclose = end;
  phone.onclose = end;
  pc.sendText("open");
}

/** Just enough of RFC 6455 for binary piping. */
class Ws {
  constructor(socket) {
    this.socket = socket;
    this.buf = Buffer.alloc(0);
    this.closed = false;
    this.ondata = null;
    this.onclose = null;
    this.alive = true;
    socket.on("data", (d) => this.onBytes(d));
    socket.on("close", () => this.finish());
    socket.on("end", () => this.finish());
    this.timer = setInterval(() => {
      if (!this.alive) return this.close(1001, "timeout");
      this.alive = false;
      this.frame(0x9, Buffer.alloc(0));
    }, PING_MS);
  }

  onBytes(d) {
    this.alive = true;
    this.buf = this.buf.length ? Buffer.concat([this.buf, d]) : d;
    for (;;) {
      const b = this.buf;
      if (b.length < 2) return;
      const op = b[0] & 0x0f;
      const masked = (b[1] & 0x80) !== 0;
      let len = b[1] & 0x7f;
      let off = 2;
      if (len === 126) {
        if (b.length < 4) return;
        len = b.readUInt16BE(2);
        off = 4;
      } else if (len === 127) {
        if (b.length < 10) return;
        len = Number(b.readBigUInt64BE(2));
        off = 10;
      }
      if (len > MAX_FRAME) return this.close(1009, "frame too big");
      const maskOff = off;
      if (masked) off += 4;
      if (b.length < off + len) return;
      let payload = b.subarray(off, off + len);
      if (masked) {
        const mask = b.subarray(maskOff, maskOff + 4);
        payload = Buffer.from(payload);
        for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i & 3];
      }
      this.buf = b.subarray(off + len);
      if (op === 0x8) return this.close(1000, "");
      if (op === 0x9) this.frame(0xa, payload);
      else if ((op === 0x0 || op === 0x1 || op === 0x2) && payload.length && this.ondata) this.ondata(payload);
    }
  }

  frame(op, payload) {
    if (this.closed) return;
    const n = payload.length;
    let head;
    if (n < 126) head = Buffer.from([0x80 | op, n]);
    else if (n < 65536) {
      head = Buffer.alloc(4);
      head[0] = 0x80 | op;
      head[1] = 126;
      head.writeUInt16BE(n, 2);
    } else {
      head = Buffer.alloc(10);
      head[0] = 0x80 | op;
      head[1] = 127;
      head.writeBigUInt64BE(BigInt(n), 2);
    }
    this.socket.write(Buffer.concat([head, payload]));
  }

  send(data) {
    this.frame(0x2, data);
  }

  sendText(text) {
    this.frame(0x1, Buffer.from(text));
  }

  close(code, reason) {
    if (this.closed) return;
    const p = Buffer.alloc(2 + Buffer.byteLength(reason || ""));
    p.writeUInt16BE(code, 0);
    p.write(reason || "", 2);
    this.frame(0x8, p);
    this.closed = true;
    this.socket.end();
    this.finish();
  }

  finish() {
    clearInterval(this.timer);
    this.closed = true;
    const cb = this.onclose;
    this.onclose = null;
    if (cb) cb();
  }
}

server.listen(PORT, () => console.log(`PCLink relay listening on ${PORT}`));

// Phone Paddle demo server.
// - Serves the 3D scene (desktop) and the controller page (phone).
// - Relays phone orientation to the scene over WebSockets with no buffering.
// - HTTPS (self-signed) is required because phones only expose motion
//   sensors to secure pages.
const http = require('http');
const https = require('https');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { WebSocketServer } = require('ws');
const selfsigned = require('selfsigned');
const QRCode = require('qrcode');

const HTTP_PORT = Number(process.env.HTTP_PORT) || 8080;
const HTTPS_PORT = Number(process.env.HTTPS_PORT) || 8443;
const PUBLIC = path.join(__dirname, 'public');
const THREE = path.join(__dirname, 'node_modules', 'three', 'build');
const CERT_DIR = path.join(__dirname, '.cert');

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
};

function lanAddress() {
  for (const list of Object.values(os.networkInterfaces())) {
    for (const i of list || []) {
      if (i.family === 'IPv4' && !i.internal) return i.address;
    }
  }
  return 'localhost';
}

const LAN_IP = lanAddress();
const PHONE_URL = `https://${LAN_IP}:${HTTPS_PORT}/phone.html`;

function sendFile(res, file) {
  fs.readFile(file, (err, data) => {
    if (err) {
      res.writeHead(404);
      return res.end('Not found');
    }
    res.writeHead(200, {
      'Content-Type': MIME[path.extname(file)] || 'application/octet-stream',
      'Cache-Control': 'no-cache',
    });
    res.end(data);
  });
}

async function handler(req, res) {
  const url = new URL(req.url, 'http://x');
  let p = decodeURIComponent(url.pathname);

  if (p === '/info') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify({ phoneUrl: PHONE_URL }));
  }
  if (p === '/qr.svg') {
    const svg = await QRCode.toString(PHONE_URL, { type: 'svg', margin: 1 });
    res.writeHead(200, { 'Content-Type': MIME['.svg'] });
    return res.end(svg);
  }
  if (p === '/' || p === '') p = '/index.html';

  let base = PUBLIC;
  if (p.startsWith('/vendor/three/')) {
    base = THREE;
    p = p.slice('/vendor/three'.length);
  }
  const file = path.normalize(path.join(base, p));
  if (!file.startsWith(base)) {
    res.writeHead(403);
    return res.end();
  }
  sendFile(res, file);
}

// ---- WebSocket relay -------------------------------------------------------
const screens = new Set();
const phones = new Set();

function broadcast(set, data) {
  for (const ws of set) {
    // Never queue stale poses: if a socket is backed up, drop the frame.
    if (ws.readyState === 1 && ws.bufferedAmount < 4096) ws.send(data);
  }
}

function status() {
  broadcast(screens, JSON.stringify({ t: 'status', phones: phones.size }));
}

function attachWs(server) {
  const wss = new WebSocketServer({ server, perMessageDeflate: false });
  wss.on('connection', (ws, req) => {
    ws._socket.setNoDelay(true);
    const role = new URL(req.url, 'http://x').searchParams.get('role');
    if (role === 'phone') {
      phones.add(ws);
      console.log('Phone connected');
      ws.on('message', (data, isBinary) => broadcast(screens, isBinary ? data : data.toString()));
      ws.on('close', () => {
        phones.delete(ws);
        console.log('Phone disconnected');
        status();
      });
    } else {
      screens.add(ws);
      ws.on('message', (data) => broadcast(phones, data.toString()));
      ws.on('close', () => screens.delete(ws));
    }
    status();
  });
}

async function getCert() {
  const keyFile = path.join(CERT_DIR, `${LAN_IP}.key`);
  const certFile = path.join(CERT_DIR, `${LAN_IP}.crt`);
  if (fs.existsSync(keyFile) && fs.existsSync(certFile)) {
    return { key: fs.readFileSync(keyFile), cert: fs.readFileSync(certFile) };
  }
  const notAfter = new Date();
  notAfter.setFullYear(notAfter.getFullYear() + 1);
  const pems = await selfsigned.generate([{ name: 'commonName', value: LAN_IP }], {
    keySize: 2048,
    algorithm: 'sha256',
    notAfterDate: notAfter,
    extensions: [
      {
        name: 'subjectAltName',
        altNames: [
          { type: 7, ip: LAN_IP },
          { type: 7, ip: '127.0.0.1' },
          { type: 2, value: 'localhost' },
        ],
      },
    ],
  });
  fs.mkdirSync(CERT_DIR, { recursive: true });
  fs.writeFileSync(keyFile, pems.private);
  fs.writeFileSync(certFile, pems.cert);
  return { key: pems.private, cert: pems.cert };
}

(async () => {
  const httpServer = http.createServer(handler);
  attachWs(httpServer);
  httpServer.listen(HTTP_PORT, '0.0.0.0');

  const httpsServer = https.createServer(await getCert(), handler);
  attachWs(httpsServer);
  httpsServer.listen(HTTPS_PORT, '0.0.0.0');

  console.log('\n  Phone Paddle is running\n');
  console.log(`  1. On this computer open:  http://localhost:${HTTP_PORT}`);
  console.log(`  2. On your phone open:     ${PHONE_URL}`);
  console.log('     (or scan the QR code shown on the computer screen)');
  console.log('     Your phone will warn about the certificate - tap "Advanced" -> "Proceed".\n');
})();

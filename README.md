# Phone Paddle 🏓

A small demo: a 3D ping-pong scene on your monitor, with your phone as the paddle.
Tilt and swing the phone and the paddle follows straight away. Press **DROP BALL** on the phone
to drop a ball onto your side of the table, where you can reach it.

No app to install. The phone controller is a web page.

## Run it

Requires [Node.js](https://nodejs.org) 18+.

```bash
npm install
npm start
```

1. On the computer, open **http://localhost:4180**.
   If port 4180 is already in use, the server picks the next free one. Use the address it prints.
2. Connect the phone to the **same Wi-Fi**, then scan the QR code on screen (or type the `https://…:4443/phone.html` URL printed in the terminal).
3. The phone warns about the certificate. It is a self-signed certificate made on your machine. Tap *Advanced → Proceed* (Android) or *Show Details → visit this website* (iOS). Phones only give motion sensors to `https` pages, so this step is needed.
4. Tap **TAP TO START**. On iOS, allow motion access.
5. Hold the phone like a paddle handle: the bottom of the phone is the grip and the screen is the red face. Point it at the monitor and tap **RE-CENTER**.
6. Tap **DROP BALL** and hit it.

Turn off auto-rotate on the phone so the page doesn't flip while you swing.

## Android Chrome says the sensors are blocked

1. Tap the icon left of the address bar → *Permissions* / *Site settings* → **Motion sensors → Allow**, then reload.
2. If that doesn't fix it (Chrome often blocks sensors on pages with a certificate warning), skip the certificate entirely:
   open `chrome://flags`, search **Insecure origins treated as secure**, enter `http://<your-PC-IP>:4180`,
   set it to *Enabled*, tap *Relaunch*, then open `http://<your-PC-IP>:4180/phone.html`.
   The terminal and the phone screen both show the exact address.

## Controls

| Where    | Action                                |
|----------|---------------------------------------|
| Phone    | Rotate: paddle rotates 1:1 around its grip (rotation never moves it) |
| Phone    | Move your hand: paddle moves 1:1 left/right/up/down/forward |
| Phone    | Curved slider (thumb): walk left/right, camera and paddle move together |
| Phone    | DROP BALL / RE-CENTER buttons         |
| Computer | `Space`: drop a ball, `C`: re-center, `H`: hide help |
| Computer | Mouse moves the paddle when no phone is connected |

Click the 3D page once to turn on sound. Browsers block audio until you click.

## How it stays fast

- The phone reads orientation from the Generic Sensor API (`RelativeOrientationSensor`, about 60–120 Hz) when the browser has it. Otherwise it uses `deviceorientation`.
- Each reading goes out right away over a LAN WebSocket. Nagle and compression are off, and a frame is dropped rather than queued if the socket is busy, so the screen always shows the newest pose.
- The screen draws the latest pose on the next frame with no smoothing or buffering. Physics runs 16 sub-steps per frame against the interpolated paddle, so fast swings don't pass through the ball.

### Hand movement tracking

- **AR camera tracking (Android Chrome):** when you tap Start, the phone asks for camera access and starts a WebXR AR session (needs *Google Play Services for AR*, which Chrome installs if missing). The camera tracks the phone's real position, so moving your hand 10 cm moves the paddle 10 cm. The phone reports the position of its *bottom edge* (the grip), so turning the phone in your hand doesn't move the paddle. Keep the back camera uncovered and point it at a room with some visible detail.
- **Fallback (no AR, e.g. iPhone):** the movement is estimated from the accelerometer. Quick movements come through, but accelerometers drift, so the paddle eases back to its home position when you hold still.

Tap **RE-CENTER** with your hand in a comfortable spot. That spot becomes the paddle's home position.

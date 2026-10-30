# MotionLink VR

MotionLink VR is a mobile-first gyroscope controller and Windows companion app for camera movement in PC games. The phone uses the browser's motion sensors and sends a live orientation stream to the Windows app over WebRTC. The desktop app converts the motion into relative mouse movement using Windows input APIs so it can work like a head-tracked camera controller without injecting code or installing a driver.

## What this project contains

- `mobile/` — the Android-friendly controller website
- `desktop/` — the .NET desktop app and reusable motion-processing library
- `supabase/` — the pairing and signaling schema plus setup notes
- `.github/workflows/` — the GitHub Actions build and release pipelines

## Mobile controller features

- Responsive HTML app that runs in Android Chrome
- Device Orientation permission flow
- Start Motion, Pause, Recenter, Disconnect controls
- Live pitch, yaw, roll display
- Visual orientation indicator
- Sensitivity, smoothing, and deadzone controls
- Connection status and sensor status panels
- Device support checks for unsupported browsers
- Pairing code metadata and local WebRTC-ready flow
- Works as a static site for HTTPS hosting
- PWA install support when served over HTTPS

## Windows companion features

- .NET 10 desktop app targeting Windows x64
- Self-contained portable Windows build
- Relative mouse movement using `SendInput`
- Sensitivity, smoothing, deadzone, inversion, recenter, pause, stop controls
- Live connection and motion status display
- Emergency stop and pause hotkey support
- Portable app output that runs without admin rights for normal use
- User settings outside the repo path

## Communication architecture

The design follows the requested flow:

1. The phone browser reads orientation data.
2. It sends real-time motion packets over a WebRTC DataChannel.
3. The Windows app receives those packets.
4. The app converts relative yaw and pitch into mouse deltas.
5. Supabase is used for pairing and signaling, not for the main motion stream.

## Important limitations

- This is not full VR or full 6-DoF tracking. It is rotational camera control.
- Sensor accuracy depends on the phone, browser, and device calibration.
- Some games block synthetic mouse input or restrict it through anti-cheat software.
- WebRTC connectivity depends on network conditions and STUN/TURN availability.
- Magnetic heading, orientation drift, and browser suspension can affect readings.

## Project layout

```text
.
├── .env.example
├── .github/
│   └── workflows/
│       ├── build-windows.yml
│       └── release.yml
├── README.md
├── desktop/
│   ├── MotionLinkVR.App/
│   ├── MotionLinkVR.Core/
│   ├── MotionLinkVR.Tests/
│   └── MotionLinkVR.sln
├── mobile/
│   ├── app.js
│   ├── index.html
│   ├── manifest.json
│   ├── motion.js
│   ├── styles.css
│   ├── supabase.js
│   ├── webrtc.js
│   └── icons/
├── supabase/
│   ├── README.md
│   └── schema.sql
└── .gitignore
```

## Supabase setup

1. Create a Supabase project.
2. Open the SQL editor.
3. Run the SQL in `supabase/schema.sql`.
4. Copy `.env.example` to `.env`.
5. Fill in your values.

Example:

```bash
cp .env.example .env
```

Then edit `.env`:

```env
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_PUBLISHABLE_KEY=your-publishable-anon-key
```

Do not use a secret or service-role key in browser files or in the public repo.

## Host the mobile app over HTTPS

The mobile controller needs HTTPS because motion sensor permission requires secure origins in modern browsers.

Recommended options:

- GitHub Pages with a static site
- Cloudflare Pages
- Netlify
- Any HTTPS static host
- A local HTTPS reverse proxy for development

For local testing, use a trusted certificate and browser access to `https://localhost` or a local LAN address.

Example GitHub Pages flow:

```bash
cd mobile
python3 -m http.server 8000
```

Then serve it via GitHub Pages or another HTTPS host. Do not rely on plain HTTP for the actual motion permission flow.

## Build the Windows app

This repository contains a .NET 10 desktop project that can be published for Windows x64.

```bash
cd /workspaces/Gyroscope_Windows

dotnet restore ./desktop/MotionLinkVR.sln
dotnet build ./desktop/MotionLinkVR.sln --configuration Release

dotnet publish ./desktop/MotionLinkVR.App/MotionLinkVR.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:UseAppHost=true
```

The generated executable is placed under:

```text
desktop/MotionLinkVR.App/bin/Release/net10.0/win-x64/publish/
```

## GitHub Actions build and download

### Build workflow

The repository includes `build-windows.yml`, which runs on push, pull request, and manual dispatch.

### Release workflow

The repository also includes `release.yml`, which creates a release asset when a tag like `v1.0.0` is pushed.

### Where to download the EXE

After a successful GitHub Action run:

1. Open GitHub.
2. Open the repository.
3. Click the `Actions` tab.
4. Open the latest successful `Build Windows Portable App` or `Release MotionLink VR` run.
5. Click `Artifacts`.
6. Download `MotionLinkVR-Windows-x64.zip`.
7. Extract it and run `MotionLinkVR.App.exe`.

## Pair the phone with the PC

1. Launch the Windows app on your PC.
2. Host the mobile HTML over HTTPS.
3. Open the controller in Android Chrome.
4. On the phone, tap `Start Motion` and allow motion permissions.
5. Use the pairing code shown in the mobile app or connect through the configured Supabase signaling flow.
6. Confirm the desktop app shows the connection as active.

## Calibration and recentering

- Tap `Recenter` before using the controller.
- Hold the phone still in the neutral position, then recenter.
- Adjust sensitivity and deadzone if motion is too jumpy or too slow.
- Use the pause control when you need to stop movement without disconnecting.

## Emergency stop and hotkeys

The desktop app includes a pause and emergency stop flow. The default emergency stop is designed to stop all movement immediately even if the app is minimized.

## Test and debugging checklist

### Mobile checks

- Android Chrome asks for motion permission.
- Orientation reading appears in pitch/yaw/roll fields.
- Movement indicator behaves smoothly.
- Recenter resets motion to neutral.
- Pause prevents accidental motion.
- Disconnect stops the session cleanly.

### PC checks

- Connection status shows ready.
- Movement updates appear in debug output.
- Mouse movement is relative.
- Pause removes all motion.
- Emergency stop stops movement instantly.
- System tray behavior works while minimized.
- The app exits cleanly.

### Game testing

- Start with a normal Windows mouse test first.
- Confirm the app moves the cursor only when motion is active.
- Try a game that allows mouse camera control.
- Confirm sensitivity and inversion match the game camera behavior.
- Expect some games to ignore synthetic input or block it via anti-cheat.

## Automated tests

The project includes automated tests for:

- angle normalization
- angle wrapping
- recenter behavior
- sensitivity scaling
- deadzone behavior
- smoothing behavior
- invalid sensor data rejection
- stale packet rejection
- mouse delta conversion
- pairing session expiry logic

Run:

```bash
cd /workspaces/Gyroscope_Windows/desktop
dotnet test MotionLinkVR.sln --nologo
```

## Security notes

- No Supabase secret key is stored in browser or repo files.
- Motion data is not stored permanently in the database schema.
- Sessions expire automatically.
- The app avoids sending every motion update through Supabase. WebRTC handles the live motion stream.

## Troubleshooting

### Browser does not detect motion

- Confirm HTTPS is being used.
- Confirm Device Orientation is supported in the browser.
- Check that the page is not running in an insecure context.
- Test on a modern Android browser, especially Chrome.

### No camera movement

- Confirm the phone is connected and the motion stream is active.
- Check the deadzone and sensitivity settings.
- Try a manual recenter.
- Confirm the Windows app is not paused or emergency-stopped.

### WebRTC connection issues

- Confirm that the phone and PC are on the same network or have reachable ICE/STUN endpoints.
- Test TURN servers if direct connectivity fails.
- Confirm Supabase configuration values are correct.
- Check browser console and desktop logs for connection errors.

## Known limitations

- The app is not a full VR system and does not provide true positional tracking.
- Phone sensors can drift, especially on magnetic heading and motion smoothing.
- Some browsers throttle background tabs and may delay motion events.
- Direct peer-to-peer connectivity is not guaranteed on all networks.
- Game compatibility varies by title and anti-cheat restrictions.

## What works in this repo

The project includes a real working codebase for:

- the mobile HTML controller,
- the reusable motion processing logic,
- the .NET desktop app shell,
- the Windows publish pipeline,
- the Supabase schema and setup notes,
- the automated core tests,
- the GitHub Actions build and release pipelines.

## What still requires physical validation

The following need a real Android device, a real Windows environment, and WebRTC connectivity testing:

- sensor permission behavior on actual hardware,
- phone-to-PC pairing over the real network,
- live motion streaming under field conditions,
- mouse control behavior in real games,
- emergency stop and tray behavior under actual Windows usage.

This repository is structured to support real testing, but the final end-to-end hardware validation must be performed on the target devices.

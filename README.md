# MotionLink VR

MotionLink VR is a phone gyroscope controller and Windows companion app for camera movement in PC games. When both devices are on the same Wi-Fi, the Windows app hosts the mobile controller and receives motion directly over a secure local WebSocket. The desktop app converts relative orientation changes into mouse movement using Windows `SendInput`; it does not inject code or install a driver.

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
- Temporary pairing code and direct same-Wi-Fi connection
- Native Android APK with automatic PC discovery and secure pairing
- Controller site served by the Windows app over local HTTPS
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

## Connection architecture

1. The Windows app starts a local HTTPS site and WebSocket on the PC's Wi-Fi address, plus a small UDP listener for local discovery.
2. The PC displays a temporary pairing URL, code, and certificate fingerprint.
3. The Android APK searches the same Wi-Fi for the PC, verifies its certificate fingerprint with the user, and sends orientation packets directly to the PC.
4. The desktop app converts the latest yaw and pitch changes into relative mouse deltas.

This direct local connection does not use Supabase, WebRTC, a public website, or a cloud relay. It is intended for devices on the same Wi-Fi. A router setting called client isolation or guest isolation can block devices from reaching each other.

## Important limitations

- This is not full VR or full 6-DoF tracking. It is rotational camera control.
- Sensor accuracy depends on the phone, browser, and device calibration.
- Some games block synthetic mouse input or restrict it through anti-cheat software.
- Local connectivity depends on the Wi-Fi allowing devices to communicate and the Windows firewall allowing MotionLink VR on private networks.
- Magnetic heading, orientation drift, and browser suspension can affect readings.

## Project layout

```text
.
├── .env.example
├── .github/
│   └── workflows/
│       ├── build-windows.yml
│       ├── build-android.yml
│       └── release.yml
├── README.md
├── desktop/
│   ├── MotionLinkVR.App/
│   ├── MotionLinkVR.Core/
│   ├── MotionLinkVR.Tests/
│   └── MotionLinkVR.sln
├── android/
│   └── app/
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

## Cloud services

No Supabase account, public website, or cloud credentials are required for same-Wi-Fi use. `supabase/schema.sql` is a leftover signaling prototype and is not used by the local LAN connection. Do not configure it for this workflow.

## Connect with the Android APK

1. Download the `MotionLinkVR-Android` artifact from the latest successful **Build Android Controller** workflow run and install its APK on the phone. Android may ask you to allow installation from the browser or file manager used to download it.
2. Start the Windows app and click **Start LAN**. Allow MotionLink VR through Windows Defender Firewall for **Private networks**.
3. Open the Android app and tap **Search for PCs**. Choose the PC found on the same Wi-Fi and enter the temporary pairing code displayed by the Windows app.
4. Compare the certificate SHA-256 fingerprint in the Android confirmation with the Windows app. Only tap **Matches — connect** if they are identical.
5. Tap **Start motion**, hold the phone still briefly to set its neutral position, and use **Pause**, **Recenter**, **Disconnect**, or the sensitivity controls as needed.

Automatic discovery uses UDP port `47822`, with a same-subnet TCP health-check fallback on port `47820`; the control connection uses TCP port `47821` and the local certificate download uses TCP port `47820`. If automatic discovery still fails, enter the PC's Wi-Fi IPv4 address (shown by the Windows app) in **PC address**; this bypasses discovery but still validates the certificate fingerprint and pairing code. Both devices must be on the same non-guest Wi-Fi, and the router must allow device-to-device traffic. Allow MotionLink VR through the Windows firewall on Private networks. Discovery does not transmit the pairing code; enter it from the Windows screen.

## Connect with Android Chrome

1. Install and extract the latest Windows ZIP. Keep every file from the ZIP together in one folder.
2. Start `MotionLinkVR.App.exe` on the PC and click **Start LAN**.
3. Allow MotionLink VR through the Windows Defender Firewall for **Private networks** if Windows asks. Do not enable it for public networks.
4. On the phone, open the **Certificate setup link** shown in the PC app. Android requires trusting the local certificate so Chrome can use motion sensors on this local HTTPS page.
5. Install the downloaded certificate as a **CA certificate** in Android Settings. Android menu names vary; Settings search for “CA certificate” or “Install a certificate” is usually quickest. Verify the SHA-256 fingerprint shown in the PC app if Android displays it.
6. Open the **Controller URL** shown in the PC app in Android Chrome. The URL includes a temporary pairing code.
7. Tap **Start Motion** and allow the sensor permission. The PC should change to “Phone connected” and then “Motion active”.
8. Hold the phone in a comfortable neutral position; the first sensor reading is automatically recentered. Use **Recenter** later as needed.

Both devices must be on the same non-guest Wi-Fi. Turn off VPNs temporarily if they route local traffic away from the LAN. If the certificate or connection fails, stop and start the LAN server to generate a fresh URL and pairing code. The app listens on TCP ports `47820` (certificate download and health check) and `47821` (HTTPS/WebSocket), plus UDP port `47822` for Android APK discovery.

The phone can send motion only while the Windows app is running and **Start LAN** is active. The server stops when you click **Stop** or exit the app.

Build the Android debug APK locally with:

```bash
cd android
./gradlew testDebugUnitTest lintDebug assembleDebug
```

The installable APK is written to `android/app/build/outputs/apk/debug/app-debug.apk`.

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

### Phone cannot reach the PC

- Confirm both devices are on the same Wi-Fi, not a guest network.
- Allow the app through Windows Firewall on private networks.
- Confirm the PC's Wi-Fi is not using client isolation.
- Install the local CA certificate on Android and reopen the exact HTTPS URL shown by the PC app.
- Keep both displayed ports available; another program using either port will prevent startup.
- Stop and restart the LAN server to refresh its pairing URL and code.

## Known limitations

- The app is not a full VR system and does not provide true positional tracking.
- Phone sensors can drift, especially on magnetic heading and motion smoothing.
- Some browsers throttle background tabs and may delay motion events.
- Some Wi-Fi networks block communication between clients, even when both devices show the same network name.
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

# SUDOCLIPPER for Android (prototype)

A thin Android app that opens the whole SUDOCLIPPER studio (Clipper, Chat, Memory, Config) in a
WebView and talks to a SUDOCLIPPER server over your Wi-Fi. This is a trial build kept outside the
dotnet tool packaging; nothing in `install.ps1` / `install.sh` touches it.

## What it is

- Native launcher screen: enter the server address (`http://192.168.x.x:4319`) once, stored on the phone.
- Full studio UI, including drag/drop replacement (file picker), video playback and downloads.
- Long-press anywhere without a link reopens the address screen; a failed load shows it too.
- Cleartext HTTP is allowed for LAN development; the app has the `INTERNET` permission only.

## Requirements on the computer

The studio binds to `127.0.0.1` only by default, so the phone cannot reach it. Start the server with
`SUDOCLIPPER_HOST=0.0.0.0` to accept LAN connections (nothing else changes — the desktop still opens
`http://127.0.0.1:4319`):

```powershell
$env:SUDOCLIPPER_HOST="0.0.0.0"; sudoclipper --no-open
```

```bash
SUDOCLIPPER_HOST=0.0.0.0 sudoclipper --no-open
```

Then find the computer's LAN address (`ipconfig` on Windows, `ifconfig`/`ip addr` elsewhere), allow
port 4319 through the firewall once, and type `http://<that-ip>:4319` on the phone. Without the
variable the server stays loopback-only.

## Build

Needs the .NET Android workload (`dotnet workload install android`) plus an Android SDK and JDK.
The build can fetch them itself:

```bash
cd android/SudoClipperMobile
dotnet build -f net10.0-android -c Release \
  -p:AndroidSdkDirectory="$PWD/../../.tools/android-sdk" \
  -p:JavaHome="$PWD/../../.tools/jdk" \
  -p:AcceptAndroidSDKLicenses=True -t:InstallAndroidDependencies
dotnet publish -f net10.0-android -c Release \
  -p:AndroidSdkDirectory="$PWD/../../.tools/android-sdk" \
  -p:JavaHome="$PWD/../../.tools/jdk"
```

The APK lands in `bin/Release/net10.0-android/publish/` (signed with the standard debug/trial key,
so any Android phone can sideload it with "install unknown apps" enabled).

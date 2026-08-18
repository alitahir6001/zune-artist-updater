# Building and testing on Windows 10

The project README predates the vendored XuneSyncLibrary gaining Windows support. The
vendored copy in `vendor/XuneSyncLibrary` **does** build on Windows: the CMake config
links WinUSB/SetupAPI, the AFTL dependency has a `mtp/backend/windows` USB backend, and
a libwdi-based driver helper (`xune-driver-setup.exe`) is included. The C# side probes
for `xune_sync.dll` and handles the Windows CRT memory contract.

## 1. Prerequisites

Install (winget names shown, installers work too):

```powershell
winget install Microsoft.DotNet.SDK.8
winget install Kitware.CMake
winget install Git.Git
# Visual Studio 2022 Build Tools with the "Desktop development with C++" workload:
winget install Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
```

Then vcpkg for the native dependencies:

```powershell
git clone https://github.com/microsoft/vcpkg C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat
C:\vcpkg\vcpkg install openssl curl taglib --triplet x64-windows
```

You also need the MTPZ key blob at `%USERPROFILE%\.mtpz-data` (same well-known leaked
`.mtpz-data` file used by libmtp/AFTL — not distributed with this repo), or pass
`--mtpz PATH` to `serve`.

## 2. Clone and build

Run from a **"Developer PowerShell for VS 2022"** (or any shell after VsDevCmd):

```powershell
git clone <this-repo> zunehd-artist-updater
cd zunehd-artist-updater

# Fetch the native library (not included in this repo; LGPL-2.1) at the pinned commit.
# This is a fork pinned for reproducibility, unmodified from xune-software/XuneSyncLibrary.
git clone https://github.com/alitahir6001/XuneSyncLibrary.git vendor\XuneSyncLibrary
git -C vendor\XuneSyncLibrary checkout c2a6eeb

# Native library — tests off (some don't compile against the current API)
cd vendor\XuneSyncLibrary
cmake -S . -B build -DZUNE_BUILD_TESTS=OFF `
      -DCMAKE_TOOLCHAIN_FILE=C:\vcpkg\scripts\buildsystems\vcpkg.cmake
cmake --build build --config Release
cd ..\..

# CLI — picks up build\Release\xune_sync.dll (plus the vcpkg runtime DLLs that
# vcpkg's applocal deployment copies next to it) automatically
dotnet build
```

Notes:
- Keep the default dynamic CRT (`/MD`). The C# resolver callback allocates strings on
  the ucrtbase heap for the DLL to `free()`; a static-CRT build would crash there.
- **Build the native library `--config Release`, not Debug.** Debug implies `/MDd`, which
  routes the DLL's `free()` to `ucrtbased.dll` while C# still allocates from
  `ucrtbase.dll` — a different heap, so the first artist request corrupts it. Use
  `zunemd serve --verbose` to debug instead of a Debug native build.
- If `libwdi.lib` is present under `vendor/XuneSyncLibrary/vendor/libwdi/lib/x64`
  (it is vendored), the build also produces `build\Release\xune-driver-setup.exe`.

## 3. Driver: WinUSB instead of the Zune driver

**What this means in plain terms:** a driver is the piece of Windows software that lets
programs talk to a USB device, and only one driver can own a device at a time. When you
plug the Zune in, Windows normally attaches Microsoft's *Zune driver* — that's what the
Zune desktop app uses. This tool instead talks to the device directly through a generic
Microsoft driver called **WinUSB**. So before running `serve` you tell Windows "use
WinUSB for this device", and when you want the Zune desktop app to sync music again,
you switch it back. The swap happens entirely **on your PC** — nothing is installed on
the Zune itself, and switching back and forth is safe and repeatable.

Option A — bundled helper (needs the device instance ID from Device Manager →
Zune device → Details → *Device instance path*, e.g. `USB\VID_045E&PID_0710\...`):

```powershell
# from an elevated prompt
vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe status  "<instance-id>"
vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe install "<instance-id>" --backup
# ... later, to give the device back to the Zune desktop app:
vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe restore "<instance-id>"
```

Option B — [Zadig](https://zadig.akeo.ie): pick the Zune (VID `045E`, PID `0710` for
Zune HD), install **WinUSB**. To restore, open Device Manager → the device →
Update driver → "Browse my computer" → "Let me pick" → choose the Zune driver.

## Is this safe for the device?

Short version: the bricking risk is essentially nil, because nothing here writes to the
Zune's firmware or storage.

- The **driver swap** only changes a setting on your PC. The Zune is untouched.
- **`serve` is read-only from the device's point of view.** The Zune asks for artist
  bios/images over USB and the tool answers with files — the same conversation it used
  to have with Microsoft's servers. A malformed answer just means a failed fetch or a
  blank artist page; if the device ever seems stuck, reboot it (hold power + the media
  button) and it's back to normal.
- **`update` modifies files on your PC, not the device** — it rewrites ID3 tags in your
  MP3s to add MusicBrainz IDs. **Back up your music folder before the first `update`
  run.** The device needs no backup; its music all came from your PC anyway.
- Bricking a Zune requires a failed *firmware flash*; this tool has no firmware code
  path at all. "Untested on Windows" means the USB connection might fail with an error,
  not that it might damage the device.

## 4. Test flow

```powershell
# 1. Tag MBIDs and download bios/images into .\artist_data
dotnet run -- update C:\Users\you\Music

# 2. Sync the music itself with the Zune desktop app (original Zune driver active)

# 3. Swap to WinUSB (step 3 above), plug the Zune HD in, then:
dotnet run -- serve --verbose
```

Watch for `GET biography/images/... -> <file>` lines; `progress=100 phase=4 status=3`
means the device finished fetching. Disconnect and open the artist on the device.

## Known unknowns

The upstream XuneSyncLibrary README still says Windows is "not integrated or tested",
so treat the first `serve` run on Windows as a shakedown: the WinUSB backend code all
exists, but you may be the first to exercise the full PPP-over-USB path on this OS.
If `zune_device_connect_usb` fails, check the driver swap first (`status` command or
Zadig), then MTPZ keys.

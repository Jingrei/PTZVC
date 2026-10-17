# PTZVC — PTZ Virtual Controller

A Windows desktop remote for PTZ cameras, styled like a physical PTZ remote (black/grey).

**Run:** `dist\PTZVC.exe` — a single file, no install. Needs .NET Framework 4.8, which ships with Windows 10/11.

## Using it

1. Pick a camera from the **CAMERA** list (top). It connects right away.
2. **Arrows** (or the arrow keys) pan/tilt while held. **CENTER** returns home.
3. **ZOOM −/+** and **FOCUS FAR/NEAR** while held, or drag their sliders.
4. **AUTO FOCUS** toggle: while it's ON, the FOCUS controls are disabled and the camera focuses itself.
5. **Presets:** click **SAVE PRESET**, then a number (1–12). This stores pan, tilt, zoom and focus.
   Click a number to recall it. Pan/tilt/zoom are always restored. The saved focus is applied
   only when AUTO FOCUS is OFF. Use **CLEAR**, then a number, or right-click a number, to erase it.

6. **Snapshot:** click the camera button next to the CAMERA list (or press **P**) to save a PNG of what the
   camera sees, at the camera's own resolution, to `Pictures\PTZVC`. The frame around the video flashes to confirm.
   **⋯ → Open snapshots folder** opens that folder. Snapshots work for USB cameras with live video and the built-in
   simulator. VISCA cameras send their video over HDMI/SDI/NDI, so PTZVC has no picture to save from them.

Presets are stored per camera in `%APPDATA%\PTZVC\presets.txt`.

| Key | Action |
|---|---|
| Arrow keys | Pan / tilt (hold; combine two for diagonals) |
| H / Home | Center |
| Page Up / Page Down | Zoom in / out |
| `[` / `]` | Focus far / near |
| F | Toggle auto focus |
| P | Snapshot |
| S, then 1–9, 0, -, = | Save preset 1–12 |
| 1–9, 0, -, = | Recall preset 1–12 |
| Esc | Cancel save/clear |

## Supported cameras

| Type | How | Video in app |
|---|---|---|
| USB / UVC PTZ cameras (Logitech PTZ Pro, OBSBOT, AVer, …) | DirectShow `IAMCameraControl` | Yes (live) |
| VISCA over IP — Sony style, UDP 52381 | **⋯ → Add VISCA camera** | No (position map) |
| VISCA raw TCP 5678 / raw UDP 1259 (PTZOptics, Lumens, BirdDog, …) | **⋯ → Add VISCA camera** | No (position map) |
| VISCA serial RS-232/RS-485 (COM port, 9600 baud) | **⋯ → Add VISCA camera** | No (position map) |

The **⋯** menu also has *Invert pan / tilt* for ceiling-mounted cameras.

## Testing without a PTZ camera

- **Virtual PTZ Camera (built-in simulator)** is always first in the camera list. It's a 360° virtual
  studio with a live clock, focus chart, color bars, numbered preset targets and fine print that's
  only readable at full zoom. Pan, tilt, 1–20x zoom, focus blur and auto focus all behave like a real camera.
- **⋯ → Launch virtual VISCA camera** starts a second window (`PTZVC.exe --virtual-camera`). That window
  behaves like a network PTZ camera: it listens on UDP 52381 (Sony), TCP 5678 and UDP 1259, and
  shows every VISCA command it receives. PTZVC connects to it at 127.0.0.1, so this tests the real
  network-camera code path end to end.

## Building and tests

```powershell
.\build.ps1             # -> dist\PTZVC.exe   (uses the C# compiler built into Windows; C# 5 syntax only)
.\tests\run-tests.ps1   # 88 checks: VISCA encoding, VISCA client <-> virtual camera over UDP/TCP,
                        # simulator physics, preset storage, render speed,
                        # snapshots, embedded logos
```

Logos are in `src/assets/`. The one-color ones are embedded in the exe and tinted for the dark UI (`Brand.cs`).
`logo-mark-color.png` is used to make the app icon: `build.ps1` regenerates `src/ptzvc.ico` when that file changes.

Source is in `src/`. Start with `Camera.cs` (the camera interface), then `MainForm*.cs` (UI and logic),
`SimCamera.cs`/`SimScene.cs` (simulator), `Visca.cs` + `ViscaServer.cs` (protocol), and `UvcCamera.cs` (USB).

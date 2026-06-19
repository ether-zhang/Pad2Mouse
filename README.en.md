# Pad2Mouse

**English** | [简体中文](README.md)

Use a PlayStation DualSense or Xbox controller as a mouse and keyboard on Windows. A desktop tool that lives in the system tray, automatically steps aside in fullscreen games, ships as a single dependency-free executable — double-click and go.

Great for browsing from the couch, watching videos, driving an HTPC, or operating Windows when a mouse and keyboard aren't within reach.

## Out of the box (OOTB)

1. Download the latest `Pad2Mouse.exe` from [Releases](https://github.com/ether-zhang/Pad2Mouse/releases).
2. Double-click to run — **no installation**, no .NET runtime, no Visual C++ redistributable, no drivers, and no administrator rights required.
3. It lives in the system tray; plug in a DualSense or Xbox controller and the left stick drives the mouse.

> The released EXE is a self-contained single file. On first launch it extracts the runtime under `%LOCALAPPDATA%\Microsoft\.NET`, so the very first start takes 1–2 seconds; subsequent launches are instant.
>
> Because the binary isn't code-signed, Windows SmartScreen may show "Windows protected your PC" on first run — click **More info → Run anyway**.

## Features

### Pointer & scrolling
- **Left stick → mouse movement**: radial deadzone + exponential curve + time-based linear acceleration. The longer you hold, the faster it tracks; return to the deadzone and it stops instantly.
- **Right stick → scroll wheel**: vertical scrolling with adjustable speed and sub-pixel accumulation to avoid jitter.

### Button mapping
Defaults below — **all eight buttons are reassignable from the main window** (and can also be bound to a keyboard key as a "hold" mapping):

| Button (PS / Xbox) | Default action |
| --- | --- |
| L2 / LT | Ctrl |
| R2 / RT | Mouse left hold |
| ✕ Cross / A | Mouse left hold |
| ○ Circle / B | Mouse right click |
| □ Square / X | Mouse middle click |
| △ Triangle / Y | Enter |
| L3 / LSB | Tab |
| R3 / RSB | Mouse middle click |
| D-Pad | Arrow keys (fixed) |

> Left-hold supports dragging; R2 and ✕ both trigger left-hold, and releasing one doesn't affect the other.

Combos (fixed):
- **L1 + R1 / LB + RB**: toggle the built-in on-screen keyboard
- **Create + Options / View + Menu**: snap the cursor to the center of the primary display (a lifesaver when it gets lost across monitors)
- **L3 + R3 / LSB + RSB**: enable / pause mapping anytime without opening the UI

### Concurrent multi-controller
- DualSense (incl. DualSense Edge) via HID, Xbox (incl. XInput-compatible third-party pads) via XInput.
- USB + Bluetooth, PlayStation + Xbox can be connected at the same time — all of them drive the same cursor.
- Connections show in a compact form like `DualSense Edge(Bluetooth)`, clear at a glance with multiple devices.

### Built-in on-screen keyboard
- L1 + R1 brings up a translucent QWERTY keyboard; move the cursor with the stick to pick keys — no need to reach for a physical keyboard.

### Win11 shell gamepad-nav suppression
- Suppresses the built-in controller focus navigation in Windows 11 shell apps (Settings, Start menu, etc.) so it doesn't double up with this tool's mapping.
- Implemented as a low-level keyboard hook that only drops the OS-injected navigation keys (arrows / Tab / Enter / Esc / Space / Page Up·Down / Home / End), and **only while the controller is actually active** — so it doesn't interfere with keystrokes injected by remote-streaming hosts (Sunshine / Parsec / RDP), Logitech side buttons, AutoHotkey, or any other injection source.

### Fullscreen auto-yield
- When any foreground window covers the entire monitor (exclusive fullscreen / borderless / D3D / presentation mode / PowerPoint, etc.) mapping disables itself; a process whitelist allows exceptions. Maximized windows and the empty desktop don't count as fullscreen.

### UI & system integration
- Dark, modern interface; the accent color follows the controller: DualSense blue, Xbox green, a blue→green gradient when both are connected.
- Simplified Chinese / English, switchable at runtime (English by default); the tray menu follows.
- Lives in the tray: right-click to enable/disable, show window, or exit; double-click to open. Windows toast on enable/disable.
- Optional start with Windows (writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- All settings are saved to `%LOCALAPPDATA%\Pad2Mouse\config.json` and persisted on every change; configs from older versions (next to the EXE) are migrated automatically on first launch.

## Requirements

- Windows 10 (1809 or newer) / Windows 11, x64
- Runtime: self-contained single-file EXE — no .NET runtime, Visual C++ redistributable, or drivers required
- Build: .NET 9 SDK
- A DualSense or Xbox controller (USB or Bluetooth)

## Connecting

- **USB wired**: plug in and it's recognized; lowest latency.
- **Bluetooth**: DualSense — hold PS + Create to enter pairing; Xbox — hold the pair button; then connect from Windows Bluetooth settings.

> On first DualSense Bluetooth connection, the app requests feature report 0x05 to switch the pad into the full report mode (0x31).

## Build & run

```powershell
# Debug build (for IDE debugging)
dotnet build src/DS2Mouse/DS2Mouse.csproj

# Run directly
dotnet run --project src/DS2Mouse/DS2Mouse.csproj

# Publish: self-contained single-file EXE (zero dependencies on the target)
# The Release config in the csproj already enables PublishSingleFile / embedded
# PDB / DeterministicSourcePaths, so the command line only adds runtime +
# self-contained + native-library extraction.
dotnet publish src/DS2Mouse/DS2Mouse.csproj `
  -c Release -r win-x64 `
  --self-contained true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src/DS2Mouse/bin/Release/net9.0-windows/win-x64/publish/Pad2Mouse.exe`. Copy it to the target machine and double-click; `config.json` is written to `%LOCALAPPDATA%\Pad2Mouse\`.

## Config example (`config.json`)

```json
{
  "Enabled": true,
  "Language": "en",
  "EnableNotifications": true,
  "LeftStick": {
    "Deadzone": 0.10,
    "Sensitivity": 12.0,
    "Exponent": 2.0,
    "AccelMaxFactor": 2.5,
    "AccelRampSeconds": 1.0
  },
  "RightStick": {
    "Deadzone": 0.20,
    "Speed": 8.0,
    "InvertVertical": false,
    "AccelMaxFactor": 2.5,
    "AccelRampSeconds": 1.0
  },
  "TriggerThreshold": 0.20,
  "Mappings": {
    "L2": "Ctrl",
    "R2": "LeftHold",
    "Cross": "LeftHold",
    "Circle": "RightClick",
    "Square": "MiddleClick",
    "Triangle": "Enter",
    "L3": "Tab",
    "R3": "MiddleClick"
  },
  "FullscreenWhitelist": ["chrome", "mpv", "vlc"]
}
```

- `Language`: `en` or `zh-CN`.
- Mapping actions include: `LeftHold` / `RightHold` / `LeftClick` / `RightClick` / `MiddleClick` / `Ctrl` / `Shift` / `Enter` / `Tab`, and `Key:0xNN` (a hold-style binding to a virtual key, which you can generate by picking a key on the on-screen keyboard).
- `AccelMaxFactor` = 1.0 disables acceleration; `AccelRampSeconds` is the time to ramp linearly from 1× to `AccelMaxFactor`.
- Whitelist entries match by process name (case-insensitive, `.exe` suffix optional). The "Foreground process" readout at the bottom of the main window shows the current foreground process name — add that name to make an exception.

## Known limitations

- `SendInput` cannot drive processes running at a higher integrity level (e.g. windows running as administrator). This is by Windows security design and can't be bypassed. For online competitive games with kernel-mode anti-cheat (VAC / EAC / BattlEye, etc.), close this tool or use the fullscreen whitelist so it auto-yields.
- Fullscreen detection treats "window rect == monitor rect" as exact: an auto-hidden taskbar plus a maximized window counts as fullscreen (close to fullscreen in practice — intended behavior).
- When the same DualSense is online over both USB and Bluetooth at once, the connection list occasionally still shows two entries — the existing HID-serial dedup logic misses some timing cases. To be fixed.

## Project layout

```
src/DS2Mouse/
├── App.xaml(.cs)                 # entry point, tray, lifecycle, accent theme, localization
├── MainWindow.xaml(.cs)          # Main + Settings tabs
├── OnScreenKeyboardWindow.xaml   # translucent built-in keyboard
├── Models/
│   ├── AppConfig.cs              # config data model
│   ├── ButtonActions.cs          # available mapping actions + default mappings
│   └── DualSenseState.cs         # normalized controller frame
├── Services/
│   ├── IControllerReader.cs      # reader abstraction
│   ├── ControllerSource.cs       # aggregates the DualSense + XInput readers
│   ├── DualSenseReader.cs        # HidSharp + USB/BT report parsing
│   ├── XInputReader.cs           # Xbox controller polling
│   ├── StateMerge.cs             # multi-controller frame merge
│   ├── InputSimulator.cs         # SendInput wrapper (with sentinel tag)
│   ├── MapperEngine.cs           # sticks/buttons → input actions (125Hz tick, with accel)
│   ├── ShellInputSuppressor.cs   # low-level keyboard hook; suppresses Win11 shell gamepad nav
│   ├── FullscreenGuard.cs        # window-rect + SHQuery dual detection
│   ├── LocalizationService.cs    # ResourceDictionary hot-swap
│   ├── StartupRegistration.cs    # start-with-Windows registry entry
│   └── ConfigStore.cs            # JSON load / save / migrate
└── Resources/
    ├── Theme.xaml                # dark theme + control styles
    ├── Strings.zh-CN.xaml        # Chinese resources
    ├── Strings.en.xaml           # English resources
    └── tray.ico                  # tray / taskbar / EXE icon
```

## Dependencies

- [HidSharp](https://www.nuget.org/packages/HidSharp) — DualSense HID reading
- [Hardcodet.NotifyIcon.Wpf](https://www.nuget.org/packages/Hardcodet.NotifyIcon.Wpf) — system tray icon

Xbox controllers are read through Windows' built-in XInput, with no extra dependency.

## License

MIT — see [LICENSE](LICENSE).

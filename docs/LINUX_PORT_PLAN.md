# OATControl Linux Port — Analysis & Plan

## Goal

Make the OATControl mount-control dashboard usable on Linux, so OpenAstroTracker /
OpenAstroExplorer owners can test and drive their mounts without a Windows machine.

## Current state (what blocks Linux today)

| Project | Framework | Linux-portable? |
|---|---|---|
| `OATCommunications` | .NET Standard 2.0 | **Yes** — pure protocol/socket code, no UI dependency |
| `OATCommunications.WPF` | .NET Framework 4.7.2 | Partially — contains the serial handler + factory (portable logic) mixed with WPF helpers |
| `OATControl` | .NET Framework 4.7.2, WPF | **No** — WPF does not exist on Linux |
| `OATCommunications.ASCOM` | .NET Framework, COM | **No** — ASCOM is Windows COM/Registry by design |
| `ASCOM.Driver` | .NET Framework 4.0, COM | **No** — out of scope (Linux equivalent is INDI / ASCOM Alpaca) |
| `OATSimulation` | .NET Framework 4.6, SharpDX | **No** — DirectX; out of scope |

### Detailed findings

**Portable already (the hard part is done):**
- `OATCommunications` has no real Windows dependency. The `WindowsBase` reference in
  `OATCommunications.csproj` (hard-coded `Program Files (x86)` hint path) is vestigial —
  no `System.Windows` types are used anywhere in the project. It can be deleted.
- `TcpCommunicationHandler` and `UdpClientAdapter` (WiFi + UDP discovery) use standard
  .NET sockets — fully portable.
- The threaded job queue (`CommunicationHandler`), LX200 command layer
  (`OatmealTelescopeCommandHandlers`), and parsers are UI-agnostic.
- Settings and checklist storage use `Environment.SpecialFolder.ApplicationData`
  (`AppSettings.cs:52`), which maps to `~/.config` on Linux — works as-is.
- `Fody`/`PropertyChanged.Fody` and `Newtonsoft.Json` work on modern .NET.

**Portability bugs / Windows-isms in otherwise portable code:**
- `OATCommunications/Log.cs:58` — builds the log folder with a hard-coded `"\\"`
  separator instead of `Path.Combine`.
- `SerialCommunicationHandler.cs:26` — device-string regex is hard-coded to `COM\d+`;
  Linux ports are `/dev/ttyUSB0`, `/dev/ttyACM0`, etc.
- `SerialCommunicationHandler` lives in `OATCommunications.WPF` even though it has no
  WPF dependency (only `System.IO.Ports`, available on Linux via the `System.IO.Ports`
  NuGet package on .NET 6+).
- `CommunicationHandlerFactory.cs:35-39` — marshals discovery callbacks through
  `System.Windows.Application.Current.Dispatcher`; needs an injected
  `SynchronizationContext` (or callback) instead.
- `MountVM.cs:1289` — opens the log folder with `explorer.exe`; needs `xdg-open` on
  Linux (`open` on macOS).

**The actual porting work — the UI layer:**
- OATControl is ~6,250 lines of WPF XAML + ~18,000 lines of C# (of which `MountVM.cs`
  alone is ~5,000 lines of mostly portable logic).
- `MountVM` is coupled to WPF through `DispatcherTimer`, `Application.Current`, and by
  instantiating dialog windows directly.
- Custom controls to re-template: `Joystick`, `StopButton`, `PushButton`, `ScopeCircles`,
  `ScopePointer`, `RangeSlider`, `ToggleSwitch`, `LabeledToggleSwitch`, `IconButton`,
  `SlewProgressBar`, `MotorIndicator`, `LevelDisplay`, `ThemedWindow`.
- UI package replacements exist 1:1 for Avalonia: `MdXaml` → `Markdown.Avalonia`
  (same author), `AvalonEdit` → `AvaloniaEdit`, `System.Windows.Interactivity` →
  `Avalonia.Xaml.Behaviors`.
- The custom theming system (colors-only theme files, brushes generated at runtime by
  `ThemeManager.GenerateBrushes`, `DynamicResource` lookups) maps conceptually 1:1 to
  Avalonia's `ResourceDictionary`/`DynamicResource` model.

## Technology choice

**Recommendation: Avalonia UI 11 on .NET 8.**

| Option | Verdict |
|---|---|
| **Avalonia UI** | ✅ Closest to WPF (XAML, bindings, styles, DynamicResource), first-class Linux (X11/Wayland), macOS for free, active ecosystem |
| Uno Platform | WPF-like too, but Linux desktop (Skia/X11) is less mature |
| .NET MAUI | ❌ No Linux support |
| GTK# / Qt | ❌ Full UI rewrite, loses XAML and theming system |
| Web UI (ASP.NET + browser) | Interesting long-term (control from tablet/phone) but a full rewrite; out of scope |
| Wine | Not a port, but a useful stopgap (see Phase 0) |

## Plan

### Phase 0 — Unblock axis testing now (no code)
Options available today, before any porting:
1. **INDI / KStars-Ekos**: the `indi-3rdparty` repo ships an "LX200 OpenAstroTech"
   driver; Ekos on Linux can connect to the mount over serial and slew both axes.
2. **Raw serial terminal**: `screen /dev/ttyUSB0 19200` (add yourself to the `dialout`
   group first) and type LX200 commands: `:GVP#` (product), `:GR#`/`:GD#` (position),
   `:Sr…#`+`:Sd…#`+`:MS#` (slew), `:Qq#` (stop all).
3. **Wine**: OATControl may run under Wine with .NET 4.7.2 (winetricks); map the serial
   port via `ln -s /dev/ttyUSB0 ~/.wine/dosdevices/com1`. Unverified, WPF-on-Wine is
   fragile.

### Phase 1 — Modernize the shared core (small, high value)
1. Retarget `OATCommunications` to multi-target `netstandard2.0;net8.0`; delete the
   vestigial `WindowsBase` reference; fix `Log.cs` path separator.
2. Create a UI-free **`OATCommunications.Serial`** (net8.0 + `System.IO.Ports` NuGet):
   move `SerialCommunicationHandler`, `SerialListener`, `CommunicationHandlerFactory`
   out of `OATCommunications.WPF`; keep thin forwarding shims in the WPF assembly so
   the existing Windows apps still build.
3. Generalize the device string parsing: accept `Serial: COM3@19200` **and**
   `Serial: /dev/ttyUSB0@19200`; enumerate via `SerialPort.GetPortNames()` (works on
   Linux) with a `/dev/serial/by-id` fallback for stable names.
4. Replace the WPF Dispatcher marshalling in the factory with an injected
   `SynchronizationContext`/`Action<Action>` marshaller.
5. **Deliverable: `OATConsole`** — a small net8.0 CLI that connects (serial or WiFi),
   prints firmware/position, and drives the axes (`:MG…#`, slew, stop). This validates
   the whole stack on Linux and is immediately useful for hardware bring-up testing.

### Phase 2 — Avalonia application skeleton
1. New project `OATControl.Avalonia` (net8.0, Avalonia 11), sharing
   `OATCommunications` + `OATCommunications.Serial`.
2. Port the theming engine: `ThemeColorDefinitions` unchanged; `ThemeManager` adapted
   to Avalonia resource dictionaries; theme XAML files converted (colors only, so the
   conversion is mechanical).
3. Introduce `IDialogService` + `IPlatformService` (open-folder/open-URL) abstractions
   so ViewModels stop referencing `Window`/`Application.Current`/`explorer.exe`.

### Phase 3 — Port the ViewModels
1. `MountVM`: swap WPF `DispatcherTimer` for Avalonia's `DispatcherTimer` (near-identical
   API), route dialog creation through `IDialogService`, replace `explorer.exe` with the
   platform service.
2. `AppSettings`, `PointsOfInterest`, `AstroTools`, `DayTime`, `Declination`,
   checklist logic: portable nearly as-is.
3. Value converters: mechanical rewrite to `IValueConverter` (Avalonia's interface is
   equivalent).

### Phase 4 — Port the views (priority order)
1. **`DlgChooseOat`** (connection/discovery flow) + **`MainWindow`** (RA/DEC readout,
   joystick, slew controls, tracking toggle, home/park) — this is the minimum to
   control the mount's axes from Linux.
2. `MiniController`, `SettingsDialog`, `DlgAppSettings`, `DlgMessageBox`.
3. Remaining dialogs: checklist, theme editor, axis calibration, target chooser,
   update dialog. Note: SharpCap/NINA polar-align log dialogs have limited value on
   Linux (both are Windows apps) — port last or leave Windows-only.
4. Re-template the custom controls listed above.

### Phase 5 — Linux integration & distribution
1. Docs: `dialout` group, udev notes, USB serial adapters.
2. Packaging: `dotnet publish -r linux-x64 --self-contained`; then AppImage (simplest)
   and optionally Flatpak/deb.
3. CI: GitHub Actions workflow building Linux + Windows artifacts.
4. `UpdateChecker`/`DlgUpdateAvailable`: keep the version check (portable), gate the
   auto-download of `OATControlSetup.exe` to Windows; point Linux users at the release
   page or the Linux asset.

### Out of scope / explicitly not ported
- `OATCommunications.ASCOM` and `ASCOM.Driver` (Windows COM). Linux-native mount access
  for imaging suites goes through INDI; an ASCOM **Alpaca** (HTTP) client handler could
  be added later as a cross-platform replacement.
- `OATSimulation` (SharpDX/DirectX).

## Effort estimate (rough)

| Phase | Effort |
|---|---|
| Phase 1 + CLI | 1–2 days |
| Phase 2 (skeleton + theming) | 2–4 days |
| Phase 3 (ViewModels) | 3–5 days |
| Phase 4 (core views: connection + main window) | 1–2 weeks |
| Phase 4 (full dialog parity) | +1–2 weeks |
| Phase 5 (packaging/CI) | 2–3 days |

The long-term payoff of the Avalonia route: a single UI codebase that can eventually
replace the WPF app on Windows too, instead of maintaining two front-ends.

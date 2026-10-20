# G8 Volume OSD

A small Windows 11 tray utility that shows a volume overlay when the volume of a playback
device changes. It's built for the Creative Sound Blaster G8: turning the G8's knob updates the
Windows volume, but Windows doesn't show its own volume popup for that change.

In its default mode the app **only observes**. It never changes volume, mute, default
devices, audio routing, enhancements, drivers, services, or Creative settings. The Creative
app can stay closed.

There's also an opt-in **experimental mode** that opens the built-in Windows 11 volume popup
instead of the custom overlay. It has to nudge the volume one step and back to do that (see
[Experimental: Windows volume popup](#experimental-windows-volume-popup)).

## Features

- Event-driven: subscribes to Windows Core Audio `IAudioEndpointVolumeCallback`
  notifications. It doesn't poll, inject into games, simulate media keys, or use
  undocumented flyout hacks, and it never needs admin rights.
- Compact Windows 11-style overlay with a volume glyph, an accent-colored bar, the
  percentage, and the mute state (a mute glyph and a greyed-out bar). It follows the
  system light/dark mode and accent color.
- Appears at the bottom center of the **primary** monitor's work area (above the taskbar)
  and handles per-monitor DPI.
- Hides about 2 s after the last change. Further changes update the visible overlay and
  restart the timer.
- Doesn't appear when the app starts, when a device reconnects, or when the default device
  changes. It only appears for actual volume or mute changes.
- Non-activating and click-through (`WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW`,
  `MA_NOACTIVATE`). It never takes keyboard focus and never intercepts the mouse.
- Tray menu:
  - **Show volume overlay**: enable or disable the overlay (in both modes)
  - **Use Windows volume popup (experimental)**: off by default; see below
  - **Playback device**: *Default playback device (follow Windows)*, or a specific endpoint
    (saved by endpoint ID)
  - **Preview overlay** (or left-click the tray icon)
  - **Launch at login**: opt-in, per-user, and removed again when unchecked
  - **Exit**
- Single instance (per-session named mutex).

### Device modes

| Mode | Behavior |
| --- | --- |
| Default (default) | Watches the Windows default playback device (console role) and switches automatically when the default changes. |
| Specific device | Watches only that endpoint ID. If it's unplugged or disabled, the app waits and shows "(disconnected)" in the menu and tooltip. It **never** switches to another device silently. When the device comes back, the app reattaches without showing the overlay. |

The G8 can expose several playback endpoints (for example speakers and headphones). The
device submenu lists every active playback endpoint by its Windows name.

### What it can and can't detect

The app reports **every** volume or mute change on the watched endpoint, whatever caused it:
the G8 knob, the Windows volume slider, keyboard media keys, or another app. Windows doesn't
expose which one it was, so the app doesn't try to tell knob changes apart from software
changes.

When you use media keys or the taskbar slider, Windows shows its own flyout **and** this
overlay. Turn the overlay off from the tray if that bothers you.

### Experimental: Windows volume popup

Windows has **no API** to show its own volume popup. It appears only when the shell itself
handles a volume command (a media key, or a USB device that reports itself as a volume key).
The G8 knob sets the volume directly instead, so Windows never shows the popup for it.

With **Use Windows volume popup (experimental)** checked, the app works like this:

1. On a volume change, if the app hasn't triggered the popup in the last
   `WindowsFlyoutRefreshMs` (default 1200 ms), it sends `APPCOMMAND_VOLUME_DOWN` +
   `APPCOMMAND_VOLUME_UP` (or the reverse) to **its own hidden window**. Windows passes
   unhandled app commands to the shell, which steps the volume and opens its popup. Nothing is
   injected into the input stream, so the foreground game never receives a key, and no admin
   rights are needed.
2. The popup shows the live level while it's open, but it closes about 2–3 s after the
   **shell's** last volume command. Changes from the knob update its number without keeping it
   open. So during a long turn the app re-triggers about every 1.2 s, and only when a change
   actually arrives. When you stop turning, the triggers stop and the popup closes normally.
   No timers are involved.
3. The step-and-back from a trigger arrives within milliseconds, well inside the refresh
   interval, so it can't re-trigger itself and there's no feedback loop.

Trade-offs (why it's opt-in):

- **It writes the volume.** Each trigger moves the volume one step and straight back. Windows
  snaps to its 2% grid, so an odd level ends on a neighboring even level (for example 23% →
  24%). The order is picked so the rounding goes the **same direction you're turning**. A long
  turn re-triggers about every 1.2 s, so it can snap up to once per refresh, always in your
  turning direction.
- **Mute uses the custom overlay.** Windows volume commands unmute, so mute changes, and any
  change while muted, show the custom overlay instead.
- **Popup timing is estimated.** The app can't see whether the Windows popup is open. It
  assumes the popup lasts longer than `WindowsFlyoutRefreshMs`. If the popup still flickers
  closed during long turns on your PC, lower the value in `settings.json` (for example to
  1000). Raise it to snap less often.
- Media keys and the taskbar slider show the Windows popup anyway, so in this mode they simply
  trigger it once more (no visible difference; those levels are already even).
- **Preview overlay** in this mode opens the Windows popup and rounds down, never up.

### Games and fullscreen

- **Desktop apps, browser video (including browser fullscreen), and borderless or windowed
  games**: the overlay is a topmost window and appears above them.
- **Exclusive-fullscreen games** (true D3D/Vulkan exclusive mode, which is getting rare on
  Windows 11): the overlay **may be hidden** behind the game, because the game owns the
  display output. Switch the game to *Borderless* / *Windowed fullscreen* if you want the
  overlay. The overlay never minimizes the game or takes focus in either case.

## Requirements

- Windows 10 2004+ / Windows 11, x64
- To build: .NET 10 SDK (current LTS). The project was created and tested with SDK 10.0.100.
- To run the default (framework-dependent) build: the **.NET 10 Desktop Runtime (x64)**

## Build and run

```powershell
# from the repository root
dotnet build src/G8VolumeOsd -c Release
dotnet run --project src/G8VolumeOsd -c Release
```

## Publish a Windows x64 executable

The publish defaults (`win-x64`, single file, framework-dependent) are set in the project
file, so the recommended command is just:

```powershell
dotnet publish src/G8VolumeOsd -c Release -o publish/win-x64
```

Result: `publish/win-x64/G8VolumeOSD.exe`, a single file of about **0.2 MB**. Copy it
anywhere (for example `%LOCALAPPDATA%\Programs\G8VolumeOSD\`) and run it.

Self-contained alternative (doesn't need the .NET runtime installed):

```powershell
dotnet publish src/G8VolumeOsd -c Release -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/win-x64-self-contained
```

Result: one `G8VolumeOSD.exe` of about **66 MB**.

### Framework-dependent vs. self-contained

| | Framework-dependent (recommended) | Self-contained |
| --- | --- | --- |
| Size | ~0.2 MB | ~66 MB (compressed single file) |
| Needs installed runtime | Yes: .NET 10 Desktop Runtime x64 | No |
| Runtime security updates | Through Windows Update / the runtime installer | Only when you republish |
| Startup | Fast | Slightly slower on first start (the bundle extracts native parts) |

Use framework-dependent if the .NET 10 Desktop Runtime is installed (it is on the development
machine). Use self-contained to copy the tool to a PC without it.

> Note: on SDK 10.0.100, `--self-contained false` on the command line was **ignored** for this
> WPF project and produced a 140 MB self-contained build. That's why the default is set in the
> `.csproj`, and why the self-contained command uses `-p:SelfContained=true`.

## Where it stores things

| What | Where | When written |
| --- | --- | --- |
| Settings (`OverlayEnabled`, `UseWindowsFlyout`, `WindowsFlyoutRefreshMs`, `DeviceId`, `DeviceName`) | `%LOCALAPPDATA%\G8VolumeOSD\settings.json` | Only when you change an option in the tray menu |
| Errors | `%LOCALAPPDATA%\G8VolumeOSD\error.log` | Only on unexpected errors (never per volume event); capped at 256 KB |
| Launch at login | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `G8 Volume OSD` | Only when you toggle the menu item |

To uninstall: uncheck *Launch at login*, exit, and delete the exe and
`%LOCALAPPDATA%\G8VolumeOSD`.

If you move the exe, launch at login will show as unchecked (the stored path no longer
matches). Check it again to update the path. If you turn the app off under *Settings > Apps >
Startup*, Windows keeps the Run value but doesn't start it.

## Design notes (for maintenance)

```
src/G8VolumeOsd/
  App.xaml(.cs)            startup, single instance, wiring, sleep/resume, theme changes
  Audio/VolumeMonitor.cs   Core Audio subscription, device following, coalescing, recovery
  Interop/CoreAudio.cs     minimal COM declarations (read-only; see below)
  Interop/NativeMethods.cs Win32 window styles, monitor/DPI queries
  OverlayWindow.xaml(.cs)  the reused overlay window
  ThemeColors.cs           light/dark + accent colors
  TrayIcon.cs              NotifyIcon + menu (built on open)
  WindowsFlyoutTrigger.cs  experimental: opens the built-in Windows volume popup
  AppSettings.cs, StartupRegistration.cs, ErrorLog.cs
```

- **No NuGet dependencies.** Core Audio is used through about 200 lines of direct COM interop
  instead of NAudio. NAudio's `AudioEndpointVolume` has a finalizer that calls
  `UnregisterControlChangeNotify` and throws on failure. After an audio-service crash, that
  exception could be thrown on the finalizer thread and kill the process. With direct interop,
  release order and error handling are explicit.
- **Read-only by construction.** In `Interop/CoreAudio.cs`, every vtable slot that could write
  audio state (`SetMasterVolumeLevel*`, `SetChannelVolumeLevel*`, `SetMute`) is declared as a
  parameterless `VtblGap_*` placeholder, and `IPropertyStore.SetValue/Commit` aren't declared.
  The app can't change audio settings through Core Audio, even by accident. The **only** code
  path that affects volume is the opt-in `WindowsFlyoutTrigger`, which asks the shell for one
  volume step down and one up (see *Experimental: Windows volume popup*).
- **Threading.** All COM calls run on the UI thread. Audio callbacks run on system worker
  threads and do three things: read two fields from the notification struct, write a packed
  64-bit value into a mailbox with `Interlocked.Exchange`, and post **at most one** dispatcher
  operation (guarded by an interlocked flag). However fast the knob turns, at most one UI
  update is queued, and it always shows the latest value. Device notifications are coalesced
  the same way.
- **Stale-callback guard.** Each attach increments a generation counter, and notifications
  from a previous endpoint are dropped.
- **No recurring timers.** The only timers are one-shot: the 2 s hide timer (stopped while
  hidden), a post-resume rebuild, and at most three backoff retries (2 s, 5 s, 15 s) after a
  failure. After that, the app waits for the next device event, resume, or tray-menu open.
- **Recovery.**
  - *Sleep/resume:* the subscription is rebuilt once, 2 s after resume.
  - *Unplug/replug:* handled through `IMMNotificationClient`.
  - *Audio service (Audiosrv) restart:* Windows doesn't notify about this, and polling is
    ruled out. Opening the tray menu runs a cheap health check (one `GetMasterVolumeLevelScalar`
    call) and rebuilds everything if the COM objects are dead. If the overlay stops after a
    service restart, right-click the tray icon once.
- **Startup and reconnect suppression.** The current level becomes the baseline when the app
  attaches. Notifications that match the baseline, or that arrive within 300 ms of attaching,
  don't show the overlay.

## Measured footprint

Measured on the development machine (16 logical CPUs, Windows 11 26200), Release build, no
volume changes during the window:

| Measurement | Result |
| --- | --- |
| Idle CPU, 60 s (`dotnet build` output) | **0 ms** of CPU time (below the 15.6 ms timer resolution) |
| Idle CPU, 30 s (published framework-dependent exe) | **0 ms** |
| Startup CPU | ~0.5 s once (JIT + WPF init) |
| Working set / private bytes | ~110 MB / ~65 MB (mostly the WPF + WinForms runtime) |
| Threads | 11 |

**Not measured yet:** CPU while actively turning the knob (needs the hardware). Expect a
short burst while the overlay is visible and nothing once it hides. Memory is dominated by the
WPF runtime. A native Win32 rewrite would be smaller, but these numbers have no measurable
effect on games.

## Validation

### Automated checks (done during development)

- [x] `dotnet build -c Release`: 0 warnings, 0 errors
- [x] Framework-dependent and self-contained single-file publishes succeed (0.21 MB / 66 MB)
- [x] App starts, stays running, and writes no `error.log`
- [x] A second launch shows "already running" and exits; the first instance keeps running
- [x] Idle CPU: 0 ms over 60 s and over 30 s (see table above)
- [x] Static check: the interop layer has no callable volume or mute setters (see *Read-only by
      construction*)

Experimental Windows-popup mode (run on the development machine, with the volume nudged by a
test-only knob simulator in five 1% steps up, a 3 s pause, then five 1% steps down, and then
restored):

- [x] The built-in Windows 11 popup opened at the start of each turn and followed the level
      live (screenshots showed 27% and then 22%). The custom overlay stayed hidden.
- [x] No feedback loop: the level stayed constant for 2 s or more of idle readings after each turn.
- [x] Long turn (ten 1% steps 400 ms apart, 4 s in total): the popup was still visible at
      3.9 s and 4.7 s and closed normally afterwards. Refresh triggers fired about every 1.3 s.
- [x] Even levels return exactly (22% → 22%). An odd level such as 22.37% snaps to 22% or 24%.
- [x] The app kept running and wrote no `error.log`.

Not automated: overlay appearance in default mode, tray menu interaction, and real G8 knob
input.

The default-mode checks didn't change your volume. The experimental-mode test changed it
briefly and restored it.

### Manual hardware checklist (please run)

1. **Knob:** turn the G8 knob slowly. The overlay appears bottom center, the percentage
   matches the Windows slider, and it hides about 2 s after you stop.
2. **Rapid changes:** spin the knob fast for a few seconds. The overlay follows smoothly with
   no lag that builds up, and it hides about 2 s after the last movement.
3. **Mute:** mute (G8 button if it maps to the endpoint mute, otherwise the Windows mixer). The
   mute glyph and greyed bar appear. Unmute: the normal glyph and accent bar return.
4. **No spurious popups:** start the app, resume from sleep, and replug the G8. The overlay
   should **not** appear for any of these by itself.
5. **Reconnect (specific-device mode):** select the G8 endpoint in *Playback device*, unplug
   the G8. The menu shows "(disconnected)" and the app does **not** switch to another device.
   Replug and turn the knob: the overlay works again.
6. **Default-device switching:** in *Default* mode, change the default output in Windows
   Sound settings. Changes on the new default now show, and the overlay doesn't appear just
   because of the switch.
7. **Focus during gaming:** in a borderless game (and a fullscreen browser video), turn the
   knob. The overlay appears, the game keeps keyboard and mouse input, nothing minimizes, and
   clicks pass through the overlay. Optionally, try an exclusive-fullscreen game: the overlay
   may be hidden, and the game must not minimize.
8. **Sleep/resume:** sleep the PC, wake it, wait ~3 s, turn the knob. The overlay works.
9. **CPU idle and active:** in Task Manager > Details (add the *CPU time* column), note
   `G8VolumeOSD.exe` CPU time, leave it idle 5 minutes, and confirm it barely moves. Then turn
   the knob for 30 s and compare.
10. **Never writes audio settings:** note the volume and mute of each device, run the app
    through the steps above, and confirm nothing changed except what you changed yourself. In
    default mode the app has no code path that sets volume or mute (see *Read-only by
    construction*).
11. **Experimental Windows popup:** check *Use Windows volume popup (experimental)* and turn
    the knob. The **Windows** popup opens, follows the knob, **stays open through a long
    turn**, and closes 2–3 s after you stop, with no custom overlay. Pause and turn again: it
    opens again. Mute: the custom overlay
    shows instead, and you stay muted. In a game, the game shouldn't react to the popup (no
    keys are sent to it).

## License

No license chosen yet. All rights reserved by the author until one is added.

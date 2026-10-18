# Serein Gaming Studio — WinUI 3 experiment

Separate native WinUI 3 application. No WPF, webview or browser renderer.
Main Serein is unchanged; this project is deliberately outside Serein.sln.

Public beta0.1.0-beta.1.1: English is the primary app language. Russian and German
switch immediately in Settings; independent language.json retains the choice.
App-owned text is embedded from Core/Translations. Original source/assets use
GPL-3.0-only; runtime dependency notices remain separate. User instructions are
in the repository README and START_HERE.md, which is copied to the portable package.

Device recognition now enriches exact PnP nodes with read-only disk model, capacity
and firmware. Known Crucial models and manufacturer aliases choose relevant support;
PC/board support is separate, never an unknown-device fallback. Software/Windows
records stay accessible separately. No universal latest-driver database or firmware
installation claimed. Shared Core source receives the source-selection correction;
the main WPF package remains preserved.

Build from the repository root:

```powershell
powershell -NoProfile -File experiments/Serein.Gaming.WinUI/build.ps1 -Test
artifacts/Serein-Gaming-WinUI/Serein.Gaming.WinUI.exe
```

Requires the .NET 10 SDK and Windows SDK/XAML build tools to compile (NuGet pins
Windows App SDK 2.5.1). x64 Windows 10 build 19041+ target. Only Windows 11 has
been exercised locally. Unpackaged and self-contained; does not enable Developer
Mode, install Visual Studio or install a shared Windows App Runtime.

Implemented: six native screens, real CPU/RAM/network/disk history, read-only
AMD GPU driver sensors, optional explicit CPU reader, protected process inventory,
search/sort, manual two-minute priority/freeze actions, game executable selection,
exact background-app approvals, Gaming/Extreme session controller with independent
watchdog/restoration, installed-device inventory and official manufacturer support links,
light/dark/system themes, custom color picker, three surface palettes, density,
panel shape, text size, compact navigation, dashboard modules, chart period/grid
and reduced motion. Optional Liquid Glass uses native in-app AcrylicBrush, soft
highlight edges and three independent background sources: fourteen vector
patterns, ten bundled images (five dark/light pairs), and a local PNG/JPEG.
The fifth pair is ruby red for dark mode and opalescent pearl for light mode.
Fourteen vector patterns include circuits, hexagons, neon waves and particles.
Glow has off/soft/neon levels and four palettes. Optional live backgrounds use
at most12 native compositor animation targets, with no frame timer. Motion stops
in inactive/minimized/hidden windows, during managed gaming sessions, with reduced
motion or Windows animation effects disabled. Settings previews stay static.
Native preview, background palette, tint and intensity controls persist.
Choose Settings > Material and background > Liquid Glass. Windows transparency,
energy-saving fallback and high contrast are respected. This is a WinUI glass
treatment, not Apple's optical refraction renderer. Custom input is limited to
25MiB/80MP and normalized to at most2560px; the source is never modified or sent.
Independent data directory:
`%LOCALAPPDATA%/Serein.Gaming.WinUI`.

Device selection groups processor model nodes into a non-installable summary,
reads actual cores/threads and motherboard model, categorizes components, separates
healthy system/confirmed virtual devices, and keeps every source instance available
in details. Errors always surface; same-model GPUs/adapters remain separate.
Search/category/attention filters work. Official support uses OEM/component vendor
sites only, with no generic Microsoft website fallback. Explicit "Find driver
updates" runs the existing real WUA discovery off the UI thread; results associate
by exact/compatible IDs, keep ambiguity/unknowns visible and exclude firmware.
No numeric-date claim of "newer" and no universal OEM coverage. Installation is an
explicit Windows Settings handoff, not an in-app install workflow. Scan does not
download/install/accept EULAs. One search at a time; native WUA is not cancellable
through this UI, but the window remains navigable and may close during discovery.

This is an experimental vertical slice, not a full migration. Main app tray,
driver package installation, startup/services/cleanup pages, PresentMon FPS capture,
complete profile editor, localization resources and installer are not ported.
No measured FPS benefit is claimed. UI does not invent unavailable sensor data.
AMD components remain external; no AMD SDK DLLs/drivers are shipped here.
Do not use both UIs to manage the same background app simultaneously; their
independent restoration journals cannot coordinate each other's changes.

Window size is fitted at launch, with native minimize/close and no maximize or
mouse resizing. App exit waits for restoration. Game exit and a deadline restore
temporary actions. Watchdog handles UI process death; ambiguous restoration keeps
an actionable error and persisted recovery record. Remaining power/session-lock,
tray and mixed-monitor DPI behavior needs migration work before product release.

Isolated UI check (does not freeze/change priority of desktop apps):

```powershell
artifacts/Serein-Gaming-WinUI/Serein.Gaming.WinUI.exe --smoke G:/Opmitizer/artifacts/winui-smoke
```

Design reference and comparison notes: `design/DESIGN.md`, `design/concept.png`.
Build/tests/actual screenshots are recorded in the repository docs.

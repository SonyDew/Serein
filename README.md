# Serein

**A Windows gaming companion. First public beta — 0.1.0-beta.1.**

[English](README.md) · [Русский](README.ru.md) · [Deutsch](README.de.md)

Serein shows what your PC is doing and lets you temporarily manage selected
background apps while you play. It also helps you understand your hardware and
find official driver support. There is no promised FPS increase: results depend
on your PC, game and background workload.

## Download and run

1. Open [Releases](https://github.com/SonyDew/Serein/releases).
2. Download **Serein-0.1.0-beta.1-win-x64.zip** under **Assets**. The source-code
   archives are for people who want to build the app themselves.
3. Extract the **whole ZIP** into a folder. Keep its files together.
4. Open **Serein.Gaming.WinUI.exe**. No separate .NET installation is needed.

This beta has been tested on **Windows 11, 64-bit**. Windows 10 support is a target,
but has not been validated. The app is unsigned, so Windows may show an unknown
publisher warning. The release includes a SHA-256 checksum for checking your download.

## A look inside

Actual screenshots of the beta. Readings come from the test PC and will differ on yours.

![Serein dashboard in dark mode](docs/images/dashboard-dark.png)

| Light theme | Optional Liquid Glass |
| --- | --- |
| ![Light theme](docs/images/dashboard-light.png) | ![Liquid Glass](docs/images/dashboard-glass.png) |

![Settings and language selection](docs/images/settings.png)

## What you can do

- **Watch your PC:** live CPU, GPU and memory activity, charts, disk and network
  throughput. Unsupported sensors show an unavailable value.
- **Set up a gaming session:** choose a game and background apps you allow Serein
  to manage. Gaming lowers their priority; Extreme temporarily freezes them.
- **Restore changes:** use **Restore all**, or let the session restore its changes
  after the game exits or the timer expires. A separate watchdog handles controller
  failure; recovery still has limitations described below.
- **Manage an app manually:** search the process list and apply a temporary action
  to an eligible app. Windows, games, launchers and protected dependencies are excluded.
- **Understand devices:** installed drivers, manufacturers and hardware details.
  SSD firmware is shown separately from the Windows driver.
- **Look for drivers:** explicitly search Windows Update or open relevant official
  manufacturer support. A support link does not prove a compatible update exists.
- **Make it yours:** themes, accents, panel shapes, text size, dashboard blocks,
  image backgrounds and optional animated glass patterns.
- **Choose a language:** English is the default; Russian and German are included.
  Change it in **Settings → App language**. Changes apply immediately.

## Before you test

This is **Beta 1**, not a finished production release. Bugs and missing features
are expected. Start with monitoring, then try gaming actions only on apps you can
safely pause. Freezing stops downloads and file saves and can hold shared resources.
Restoration is tested but cannot cover every crash or Windows failure.

Driver discovery does not install anything. Installation opens Windows Settings
for your own selection. Windows Update does not cover every manufacturer; an empty
result does **not** mean all drivers are newest. Device recognition has a limited
curated catalog. Serein does not install SSD firmware.

AMD GPU sensors use an existing supported AMD driver. CPU temperature needs the
separate supported AMD monitoring component and an explicit administrator-approved
connection. Vendor components are not bundled. Other hardware may have fewer readings.

This download uses **WinUI 3**. Tray, cleanup, startup/service tools, integrated FPS
capture and the complete driver installer have **not yet been moved into this
interface**. Older WPF source is included for development; its wider feature set
is not what this download ships.

## Ideas, bugs and contributions

**Suggestions for improvements and new features are very welcome.** You do not
need to be a developer. [Open an issue](https://github.com/SonyDew/Serein/issues/new/choose)
and explain what you would like to do, or what went wrong. Screenshots help; remove
personal paths, serial numbers and other private information first.

Code fixes, translations and hardware-recognition improvements are welcome too.
See [CONTRIBUTING.md](CONTRIBUTING.md).

## Build from source

You need **.NET 10 SDK**, Windows SDK and Visual Studio's Windows/WinUI build tools.
The build is for Windows x64 and uses the SDK version in `global.json`.
In PowerShell, from this repository's folder:

```powershell
powershell -NoProfile -File experiments/Serein.Gaming.WinUI/build.ps1 -Test
```

The app is written to `artifacts/Serein-Gaming-WinUI/`. Tests use mocks and their
own helper apps; they do not install drivers or tune unrelated desktop apps.
See [beta notes](docs/releases/v0.1.0-beta.1.md) for scope and verification.

## License

Original Serein source and assets: **GPL-3.0-only**, see [LICENSE](LICENSE).
Bundled runtimes retain their own notices in the application's `licenses` folder;
see [third-party notices](experiments/Serein.Gaming.WinUI/THIRD_PARTY.md).

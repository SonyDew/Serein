# Testing Beta 1

Please use Windows 11 x64 for this first beta. Windows 10 and the full hardware
matrix have not been validated.

Start with the dashboard, devices and appearance. Change language in Settings.
Report clipped text, missing data and confusing wording. Some sensor values are
unavailable on unsupported hardware; please include your hardware model in reports.

For Gaming, choose your game's EXE and explicitly select background apps. Start
with an app that has no unsaved work. Gaming lowers priority; Extreme freezes it.
The app should restore changes at game exit, timeout or Restore all. Freezing can
pause saves/downloads and hold shared resources. Recovery has failure boundaries;
see ARCHITECTURE_AND_RECOVERY.md. Do not test on work you cannot afford to interrupt.

Driver discovery is read-only. Windows Update coverage and official support pages
are not a universal latest-version guarantee. Do not install a package merely
because the manufacturer's name matches. Firmware installation is outside this beta.

## Tell us what happened

Open an issue with version, Windows build, hardware model, steps and expected/
actual result. English, Russian and German are welcome. Please remove usernames,
private paths, serial numbers and private logs/screenshots before posting.

## Verified for this release

- 122 experiment checks: translations, formatting placeholders, settings and
  controlled game/priority/freeze/recovery fixtures.
- 132 native UI checks, including actual English/Russian/German language control
  events, persisted choice, page/shell refresh and actual screenshots.
- 58 Core regression checks passed, including recovery and driver mocks.

An initial controlled-fixture run conservatively rejected a fixture after its
eligibility changed. Cleanup ran; no protection was bypassed. The next complete
122-check run passed. The exact transient cause was not established. This is not
evidence that every game/app combination is safe to suspend.

No real desktop apps were frozen, drivers installed, services stopped or security/
power settings changed during release validation. Native UI tests use isolated
preferences. Sensor data is real; screenshots are not an FPS/performance claim.
